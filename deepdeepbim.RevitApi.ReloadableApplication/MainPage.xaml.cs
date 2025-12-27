//using System.Windows.Controls;
//using Autodesk.Revit.UI;

//namespace deepdeepbim.RevitApi;

//public partial class MainPage : Page, IDockablePaneProvider
//{
//	public MainPage()
//	{
//		InitializeComponent();

//		ApplicationManager.Instance().HotswapContainer = this.DynamicUiContainer;
//	}

//	public void SetupDockablePane(DockablePaneProviderData data)
//	{
//		data.VisibleByDefault = true;

//		data.InitialState = new DockablePaneState()
//		{
//			DockPosition = DockPosition.Tabbed
//		};

//		data.FrameworkElement = this;
//	}
//}
