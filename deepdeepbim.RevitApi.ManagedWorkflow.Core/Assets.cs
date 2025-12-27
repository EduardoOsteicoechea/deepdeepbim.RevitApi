using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;

namespace deepdeepbim.RevitApi.ManagedWorkflow.Core;

public static class ManagedWorkflowHelpers
{
	public static void ManageWorkflow<T>(ManagedWorkflow<T> workflow) where T : IDto
	{
		workflow.Run();
	}

	public static IManagedWorkflowObserver Observer
	(
		string documentTitle,
		string validationName,
		string workflowName,
		string directoryPath = "",
		string fileName = ""
	)
	{
		if (string.IsNullOrEmpty(directoryPath))
		{
			directoryPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
				"MBA_Logs",
				documentTitle,
				validationName
				);
		}

		if (string.IsNullOrEmpty(fileName))
		{
			fileName = $"{workflowName}.json";
		}

		var FileSystemManager = new FileSystemManager(directoryPath, fileName);

		return new ManagedWorkflowObserver(
			FileSystemManager,
			new ExecutionTimer(),
			documentTitle,
			workflowName
			);
	}
}

public enum LogOptions
{
	Log = 0,
	DoNotLog = 1,
	LogNamesOnly = 2,
	FullLog = 3
}

public class ManagedWorkflow<T> where T : IDto
{
	public T Dto { get; private set; }
	public Document Doc { get; private set; }
	public string WorkflowName { get; set; }
	public dynamic Value { get; set; }
	public IManagedWorkflowActionResult CurrentActionResult { get; set; }
	public List<IManagedWorkflowActionResult> Results { get; set; } = new List<IManagedWorkflowActionResult>();
	public List<IManagedWorkflowActionResult> Errors { get; set; } = new List<IManagedWorkflowActionResult>();
	public List<IManagedWorkflowActionResult> Warnings { get; set; } = new List<IManagedWorkflowActionResult>();
	public List<IManagedWorkflowActionResult> DoNotReview { get; set; } = new List<IManagedWorkflowActionResult>();
	public IManagedWorkflowObserver ManagedWorkflowObserver { get; private set; }
	public TransactionModeOptions TransactionModeOption { get; private set; }
	private List<(Action action, TransactionDependencyOptions transactionDependencyOption)> WorkflowSteps { get; } = new List<(Action action, TransactionDependencyOptions transactionDependencyOption)>();

	public ManagedWorkflow
	(
		Document doc,
		T dto,
		IManagedWorkflowObserver observer = null,
		TransactionModeOptions transactionOption = TransactionModeOptions.Single
	)
	{
		Doc = doc;
		Dto = dto;
		ManagedWorkflowObserver = observer;
		TransactionModeOption = transactionOption;
		WorkflowName = this.GetType().Name;
	}

	public void Add(Action a, TransactionDependencyOptions b = TransactionDependencyOptions.None)
	{
		WorkflowSteps.Add((a, b));
	}

	public void Run()
	{
		if (ManagedWorkflowObserver is null)
		{
			RunTransactionMode();
		}
		else
		{
			using (ManagedWorkflowObserver)
			{
				RunTransactionMode();
			}
		}
	}

	public void RunTransactionMode()
	{
		if (TransactionModeOption.Equals(TransactionModeOptions.Multiple))
		{
			MultipleTransactionsWorkflow();
		}
		else if (TransactionModeOption.Equals(TransactionModeOptions.Single))
		{
			SingleTransactionWorkflow();
		}
		else
		{
			TransactionlessWorkflow();
		}
	}

	public void TransactionlessWorkflow()
	{
		int count = WorkflowSteps.Count;

		for (int i = 0; i < count; i++)
		{
			var isCurrentStepFinal = count - 1 == i;

			ManagedAction(WorkflowSteps[i].action, isCurrentStepFinal);
		}
	}

	public void SingleTransactionWorkflow()
	{
		using (var t = new Transaction(Doc, WorkflowName))
		{
			t.Start();

			int count = WorkflowSteps.Count;

			for (int i = 0; i < count; i++)
			{
				var isCurrentStepFinal = count - 1 == i;

				TransactionlessAction(WorkflowSteps[i].action, isCurrentStepFinal);
			}

			t.Commit();
		}
	}

	public void MultipleTransactionsWorkflow()
	{
		int count = WorkflowSteps.Count;

		for (int i = 0; i < count; i++)
		{
			var isCurrentStepFinal = count - 1 == i;

			if (WorkflowSteps[i].transactionDependencyOption.Equals(TransactionDependencyOptions.Dependant))
			{
				ManagedTransactionDependantAction(WorkflowSteps[i].action, isCurrentStepFinal);
			}
			else
			{
				TransactionlessAction(WorkflowSteps[i].action, isCurrentStepFinal);
			}
		}
	}

	public void TransactionlessAction(Action action, bool isFinal = true)
	{
		ManagedAction(action, isFinal);
	}

	public void ManagedTransactionDependantAction(Action action, bool isFinal = true)
	{
		using (var t = new Transaction(Doc, WorkflowName))
		{
			t.Start();

			ManagedAction(action, isFinal);

			t.Commit();
		}
	}

	public void ManagedAction(Action action, bool isFinal = true)
	{
		if (action is null) return;

		CurrentActionResult = new ManagedWorkflowActionResult { Name = action.Method.Name };

		try
		{
			action.Invoke();

			CurrentActionResult.Kind = ResultKindOptions.Success;
		}
		catch (Exception e)
		{
			var errorMessage = $"{e.Message} | {e.StackTrace}";

			CurrentActionResult.Kind = ResultKindOptions.Failure;

			CurrentActionResult.Message = errorMessage;
		}

		if (isFinal)
		{
			CurrentActionResult.Value = Dto.ToObservableObject();
		}

		if (ManagedWorkflowObserver != null)
		{
			ManagedWorkflowObserver.Add(CurrentActionResult);
		}

	}

	public void Log(string a)
	{
		if (!(CurrentActionResult.Logs is null))
		{
			CurrentActionResult.Logs.Add(a);
		}
		else
		{
			CurrentActionResult.Logs = new List<string> { a };
		}
	}
}







public class ManagedWorkflowObserver : IManagedWorkflowObserver
{
	[JsonInclude]
	public bool Succeeded { get; private set; } = false;

	[JsonInclude]
	public long ExecutionDuration { get; private set; }

	[JsonInclude]
	public string WorkflowName { get; private set; }

	[JsonInclude]
	public string RevitDocumentName { get; private set; }

	[JsonInclude]
	public string ExecutionTime { get; private set; } = DateTime.Now.ToString();

	[JsonInclude]
	public List<IManagedWorkflowActionResult> Errors { get; } = new List<IManagedWorkflowActionResult>();

	[JsonInclude]
	public List<IManagedWorkflowActionResult> Warnings { get; } = new List<IManagedWorkflowActionResult>();

	[JsonInclude]
	public List<IManagedWorkflowActionResult> Actions { get; } = new List<IManagedWorkflowActionResult>();

	[JsonIgnore]
	public IFileSystemManager FileSystemManager { get; private set; }

	[JsonIgnore]
	public IExecutionTimer ExecutionTimer { get; private set; }

	public ManagedWorkflowObserver
	(
		IFileSystemManager fileSystemManager,
		IExecutionTimer executionTimer,
		string revitDocumentName,
		string wrokflowName
	)
	{
		FileSystemManager = fileSystemManager;
		ExecutionTimer = executionTimer;
		RevitDocumentName = revitDocumentName;
		WorkflowName = wrokflowName;

		ExecutionTimer.Start();
	}

	public void Add(IManagedWorkflowActionResult a)
	{
		ExecutionTimer.MarkStep();

		a.Duration = ExecutionTimer.CurrentStepDuration;

		a.Number = Actions.Count + 1;

		Actions.Add(a);

		switch (a.Kind)
		{
			case ResultKindOptions.Failure:
				Errors.Add(a);
				break;
			case ResultKindOptions.Warning:
				Warnings.Add(a);
				break;
		}
	}

	public void Dispose()
	{
		Succeeded = Errors.Count == 0;

		ExecutionTimer.Finish();

		ExecutionDuration = ExecutionTimer.TotalMilliseconds;

		FileSystemManager.Write(JsonSerializer.Serialize(this));
	}
}

public interface IManagedWorkflowObserver : IDisposable
{
	void Add(IManagedWorkflowActionResult a);
}



public class ManagedWorkflowActionResult : IManagedWorkflowActionResult
{
	public int Number { get; set; }
	public string Name { get; set; }
	public string Kind { get; set; }
	public List<(string, object)> Value { get; set; }
	public string Message { get; set; }
	public long Duration { get; set; }
	public Int64 ElementId { get; set; }
	public List<string> Logs { get; set; }
}
public interface IManagedWorkflowActionResult
{
	int Number { get; set; }
	string Name { get; set; }
	string Kind { get; set; }
	List<(string, object)> Value { get; set; }
	long Duration { get; set; }
	string Message { get; set; }
	Int64 ElementId { get; set; }
	List<string> Logs { get; set; }
}



public interface IExecutionTimer
{
	void Start();
	void Finish();
	string StartTime { get; }
	long CurrentStepDuration { get; }
	int StepCount { get; }
	long TotalMilliseconds { get; }
	void MarkStep();
}
public class ExecutionTimer : IExecutionTimer
{
	public int StepCount { get; private set; } = 0;
	public string StartTime { get; private set; }
	public long TotalMilliseconds { get; private set; }
	public long CurrentStepDuration { get; private set; }
	private long _previousTotalTime { get; set; } = 0;
	private Stopwatch Stopwatch { get; set; }
	public void Start()
	{
		Stopwatch = Stopwatch.StartNew();

		StartTime = DateTime.Now.ToString();
	}
	public void MarkStep()
	{
		var currentTotal = Stopwatch.ElapsedMilliseconds;

		CurrentStepDuration = currentTotal - _previousTotalTime;

		_previousTotalTime = currentTotal;

		TotalMilliseconds = currentTotal;

		StepCount++;
	}
	public void Finish()
	{
		Stopwatch.Stop();

		TotalMilliseconds = Stopwatch.ElapsedMilliseconds;
	}
}


public interface IFileSystemManager
{
	string DirectoryPath { get; }
	string FileName { get; }
	void Write(string data);
}
public class FileSystemManager : IFileSystemManager
{
	public string DirectoryPath { get; }
	public string FileName { get; }
	public FileSystemManager
	(
		string directoryPath,
		string fileName
	)
	{
		DirectoryPath = directoryPath;

		FileName = fileName;
	}
	public void Write(string a)
	{
		try
		{
			if (!Directory.Exists(DirectoryPath))
			{
				Directory.CreateDirectory(DirectoryPath);
			}

			File.WriteAllText(Path.Combine(DirectoryPath, FileName), a);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to write log file: {ex.Message}");
		}
	}
}


public interface IExceptionFormatter
{
	string Format(System.Exception e);
}
public class ExceptionFormatter : IExceptionFormatter
{
	public string Format(System.Exception e)
	{
		return $"{e.Message}\n{e.StackTrace}";
	}
}



public static class ResultKindOptions
{
	public const string Success = "Success";
	public const string Warning = "Warning";
	public const string Failure = "Failure";
}



public interface IDto
{
	List<IDtoItem> DtoItems { get; set; }
	List<(string, object)> ToObservableObject();
}
public interface IDtoItem : IDto { }

public class DTOBase : IDto
{
	List<IDtoItem> IDto.DtoItems { get; set; } = new List<IDtoItem>();
	public override string ToString()
	{
		return "NOT IMPLEMENTED";
	}
	public List<(string, object)> ToObservableObject()
	{
		return DtoFormater.FormatAsObject(this);
	}
}
public class DTOItemBase : IDto
{
	List<IDtoItem> IDto.DtoItems { get; set; } = new List<IDtoItem>();
	public override string ToString()
	{
		return "NOT IMPLEMENTED";
	}
	public List<(string, object)> ToObservableObject()
	{
		return DtoFormater.FormatAsObject(this);
	}
}



public enum TransactionDependencyOptions
{
	None = 0,
	Dependant = 1
}
public enum TransactionModeOptions
{
	Transactionless,
	Single,
	Group,
	Multiple
}



[AttributeUsage(AttributeTargets.Property)]
public class Print : Attribute
{
	public Type FormatterType { get; }
	public string FormatterMethodName { get; }
	public Print(string formatterMethodName)
	{
		FormatterType = typeof(TypeFormatter);
		FormatterMethodName = formatterMethodName;
	}
	public Print(Type formatterType, string formatterMethodName)
	{
		FormatterType = formatterType;
		FormatterMethodName = formatterMethodName;
	}
}



public static class DtoFormater
{
	public static string Format<T>(T item)
	{
		var printer = new StringBuilder();

		var type = typeof(T);

		printer.Append($"{type.Name}");

		var properties = type.GetProperties().Where(a => Attribute.IsDefined(a, typeof(Print)));

		foreach (var property in properties)
		{
			var attribute = (Print)property.GetCustomAttributes(typeof(Print), false).FirstOrDefault();

			object rawValue = property.GetValue(item);

			string displayValue;

			if (
				!(attribute.FormatterType is null)
					&&
				!string.IsNullOrEmpty(attribute.FormatterMethodName)
			)
			{
				var methods = attribute.FormatterType.GetMethods(BindingFlags.Static | BindingFlags.Public)
								   .Where(m => m.Name == attribute.FormatterMethodName);

				MethodInfo method = null;

				foreach (var m in methods)
				{
					var parameters = m.GetParameters();
					if (parameters.Length == 1)
					{
						if (
							parameters[0].ParameterType.IsAssignableFrom(property.PropertyType)
								||
							parameters[0].ParameterType == typeof(object))
						{
							method = m;
							break;
						}
					}
				}

				if (!(method is null))
				{
					displayValue = (string)method.Invoke(null, new object[] { rawValue });
				}
				else
				{
					displayValue = $"[{attribute.FormatterMethodName} method not found]";
				}
			}
			else
			{
				displayValue = rawValue?.ToString() ?? "null";
			}

			printer.Append($"		{property.Name}:\n{displayValue}");
		}

		return printer.ToString();
	}

	public static List<(string, object)> FormatAsObject<T>(T item)
	{
		var printer = new List<(string, object)>();

		var type = item.GetType();
		var properties = type.GetProperties().Where(a => Attribute.IsDefined(a, typeof(Print)));

		foreach (var property in properties)
		{
			var attribute = (Print)property.GetCustomAttributes(typeof(Print), false).FirstOrDefault();
			object rawValue = property.GetValue(item);

			object displayValue;

			if (attribute.FormatterType != null && !string.IsNullOrEmpty(attribute.FormatterMethodName))
			{
				var methods = attribute.FormatterType.GetMethods(BindingFlags.Static | BindingFlags.Public)
								.Where(m => m.Name == attribute.FormatterMethodName);

				MethodInfo method = null;

				foreach (var m in methods)
				{
					var parameters = m.GetParameters();
					if (parameters.Length == 1)
					{
						if (parameters[0].ParameterType.IsAssignableFrom(property.PropertyType) ||
							parameters[0].ParameterType == typeof(object))
						{
							method = m;
							break;
						}
					}
				}

				if (method != null)
				{
					displayValue = method.Invoke(null, new object[] { rawValue });
				}
				else
				{
					displayValue = $"[{attribute.FormatterMethodName} method not found]";
				}
			}
			else
			{
				displayValue = rawValue;
			}

			printer.Add((property.Name, displayValue));
		}

		return printer;
	}
}
