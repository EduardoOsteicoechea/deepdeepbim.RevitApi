using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using deepdeepbim.RevitApi.ApplicationUpdater;

namespace deepdeepbim.RevitApi;

public class App : IExternalApplication
{
	public Result OnStartup(UIControlledApplication application)
	{
		ApplicationManager
			.Instance()
			.Initialize(application);

		return Result.Succeeded;
	}
	public Result OnShutdown(UIControlledApplication application)
	{
		return Autodesk.Revit.UI.Result.Succeeded;
	}
}

[Transaction(TransactionMode.Manual)]
public class DisplayMainDockablePaneCommand : IExternalCommand
{
	public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
	{
		ApplicationManager
			.Instance()
			.ShowMainDockablePane();

		return Result.Succeeded;
	}
}


[Transaction(TransactionMode.Manual)]
public class ReloadApplicationCommand : IExternalCommand
{
	public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
	{
		_ = RunReloadProcessAsync(commandData);

		return Result.Succeeded;
	}

	private async Task RunReloadProcessAsync(ExternalCommandData commandData)
	{
		try
		{
			await ApplicationManager
				.Instance()
				.ReloadApplication(commandData);
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Error", $"Reload Failed: {ex.Message}");
		}
	}
}



public class UpdatableApplicationButtonDataModel
{
	public string? Namespace { get; set; }
	public string? ClassName { get; set; }
	public string? InternalName { get; set; }
	public string? Text { get; set; }
	public string? Tooltip { get; set; }
}

public class UpdatableApplicationModel
{
	public string? RevitUIApplicationHostDllName { get; set; }
	public string? RevitUIApplicationCommandsNamespace { get; set; }
	public UpdatableApplicationButtonDataModel? DisplayDockablePaneUpdatableApplicationButtonData { get; set; }
	public UpdatableApplicationButtonDataModel? ReloadApplicationUpdatableApplicationButtonData { get; set; }
}

public class ApplicationManager
{
	private static ApplicationManager _instance { get; set; }
	public static ApplicationManager Instance()
	{
		if (_instance is null) _instance = new ApplicationManager();

		return _instance;
	}

	public string? _mainNamespace { get; set; }
	public string? MainNamespace { get { return _mainNamespace; } }

	private string? _applicationName { get; set; }
	public string? ApplicationName { get { return _applicationName; } }

	private string? _assemblyFullPath { get; set; }
	public string? AssemblyFullPath { get { return _assemblyFullPath; } }

	private string? _assemblyDirectoryPath { get; set; }
	public string? AssemblyDirectoryPath { get { return _assemblyDirectoryPath; } }

	public ApplicationManager()
	{
		_mainNamespace = MethodBase.GetCurrentMethod().DeclaringType.Namespace;
		_applicationName = _mainNamespace;
		_assemblyFullPath = Assembly.GetExecutingAssembly().Location;
		_assemblyDirectoryPath = Path.GetDirectoryName(_assemblyFullPath);
	}

	private JsonSerializerOptions _jsonSerializerOptions { get; set; }
	public JsonSerializerOptions JsonSerializerOptions { get { return _jsonSerializerOptions; } }

	public DockablePaneId DockablePaneId { get; } = new DockablePaneId(new Guid("E82A15A8-66A6-4B05-AAE4-B114EF9AAB14"));
	private DockablePane? _mainDockablePane { get; set; }
	public DockablePane? MainDockablePane { get { return _mainDockablePane; } }

	private UIControlledApplication _application { get; set; }
	public UIControlledApplication Application { get { return _application; } }

	private string _configurationDirectoryPath { get; set; }
	public string ConfigurationDirectoryPath { get { return _configurationDirectoryPath; } }

	private string _configurationFilePath { get; set; }
	public string ConfigurationFilePath { get { return _configurationFilePath; } }

	private UpdatableApplicationModel _applicationResourcesModel { get; set; }
	public UpdatableApplicationModel UpdatableApplication { get { return _applicationResourcesModel; } }


	private IDockablePaneProvider _paneProvider { get; set; }
	public IDockablePaneProvider PaneProvider { get { return _paneProvider; } }

	public void Initialize(UIControlledApplication application)
	{
		_application = application;

		_configurationDirectoryPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			MainNamespace
		);

		_configurationFilePath = Path.Combine(
			_configurationDirectoryPath,
			"ApplicationConfiguration.json"
		);

		SetJsonSerializerOptions();

		ValidateConfigurationDirectoryPath();

		SetDockablePaneProvider();

		GenerateMainDockablePane();

		CreateRibbonTab();

		GenerateApplicationRibbonPanel();

		GenerateApplicationRibbonTabButtons();
	}

	public void SetJsonSerializerOptions()
	{
		_jsonSerializerOptions = new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNameCaseInsensitive = true
		};
	}

	public void ValidateConfigurationDirectoryPath()
	{
		if (!Directory.Exists(ConfigurationDirectoryPath))
		{
			Directory.CreateDirectory(ConfigurationDirectoryPath);
		}
	}

	public void SetDockablePaneProvider()
	{
		_paneProvider = new MainPage();
	}

	public void GenerateMainDockablePane()
	{
		Application.RegisterDockablePane(
			DockablePaneId,
			ApplicationName,
			PaneProvider
		);
	}

	public void StoreMainDockablePane()
	{
		if (_mainDockablePane is null)
		{
			_mainDockablePane = Application.GetDockablePane(DockablePaneId);
		}
	}

	public void ShowMainDockablePane()
	{
		StoreMainDockablePane();

		MainDockablePane?.Show();
	}


	public void CreateRibbonTab()
	{
		Application.CreateRibbonTab(ApplicationName);
	}

	private RibbonPanel _applicationRibbonPanel { get; set; }
	public RibbonPanel ApplicationRibbonPanel { get { return _applicationRibbonPanel; } }
	public void GenerateApplicationRibbonPanel()
	{
		_applicationRibbonPanel = Application.CreateRibbonPanel(
			ApplicationName,
			"Start"
		);
	}
	public void GenerateApplicationRibbonTabButtons()
	{
		RibbonPanel panel = ApplicationRibbonPanel;

		string assemblyFullPath = AssemblyFullPath;

		string mainNamespace = MainNamespace;

		GenerateRibbonTabButton(
			panel,
			assemblyFullPath,
			new UpdatableApplicationButtonDataModel
			{
				Namespace = mainNamespace,
				ClassName = "DisplayMainDockablePaneCommand",
				InternalName = "DisplayMainDockablePaneButton",
				Text = "Launch",
				Tooltip = "Show the main dockable pane."
			}
		);

		GenerateRibbonTabSeparator(panel);

		GenerateRibbonTabButton(
			panel,
			assemblyFullPath,
			new UpdatableApplicationButtonDataModel
			{
				Namespace = mainNamespace,
				ClassName = "ReloadApplicationCommand",
				InternalName = "ReloadApplicationButton",
				Text = "Update",
				Tooltip = "Updates the application current features"
			}
		);
	}
	public void GenerateRibbonTabButton(RibbonPanel panel, string assemblyFullPath, UpdatableApplicationButtonDataModel buttonDataModel)
	{
		PushButtonData buttonData = new PushButtonData(
			buttonDataModel.InternalName,
			buttonDataModel.Text,
			assemblyFullPath,
			$"{buttonDataModel.Namespace}.{buttonDataModel.ClassName}");

		buttonData.ToolTip = buttonDataModel.Tooltip;

		panel.AddItem(buttonData);
	}

	public void GenerateRibbonTabSeparator(RibbonPanel panel)
	{
		panel.AddSeparator();
	}


	public System.Windows.Controls.ContentControl? HotswapContainer { get; set; }

	public async Task ReloadApplication(ExternalCommandData commandData)
	{
		var fileDownloader = new ApplicationConfigurationFilesUpdater(
			Path.Combine(
				AssemblyDirectoryPath,
				$"s3downloadCredentials.json"
			));

		await fileDownloader
			.UpdateLessThan1000AppFilesFromFlatS3BucketAsync(ConfigurationDirectoryPath);

		var dllsPaths = fileDownloader.DowloadedDlls;

		var dllsPathsCount = dllsPaths.Count;

		var applicationUiClassName = "ApplicationUI";

		for (global::System.Int32 i = 0; i < dllsPathsCount; i++)
		{
			string sourcePath = dllsPaths[i];

			try
			{
				if (!File.Exists(sourcePath))
				{
					throw new Exception($"DLL not found: {sourcePath}");
				}

				byte[] assemblyBytes = File.ReadAllBytes(sourcePath);

				Assembly loadedAssembly = Assembly.Load(assemblyBytes);

				Type? viewType = loadedAssembly
					.GetTypes()
					.FirstOrDefault(
						t =>
						t.FullName.EndsWith(applicationUiClassName)
							||
						t.Name == applicationUiClassName
					);

				if (viewType == null)
				{
					throw new Exception($"Could not find a UserControl named '{applicationUiClassName}' inside {sourcePath}");
				}

				if (HotswapContainer != null)
				{
					HotswapContainer.Dispatcher.Invoke(() =>
					{
						object newView = Activator.CreateInstance(viewType, commandData);

						HotswapContainer.Content = newView;
					});
				}
			}
			catch (Exception ex)
			{
				var message = $"ERROR. For {sourcePath} |  {ex.Message}";

				System.Windows.MessageBox.Show(message);

				throw;
			}
		}
	}
}

public partial class MainPage : Page, IDockablePaneProvider
{
	public MainPage()
	{
		InitializeComponent();

		ApplicationManager.Instance().HotswapContainer = this.DynamicUiContainer;
	}

	public void SetupDockablePane(DockablePaneProviderData data)
	{
		data.VisibleByDefault = true;

		data.InitialState = new DockablePaneState()
		{
			DockPosition = DockPosition.Tabbed
		};

		data.FrameworkElement = this;
	}
}