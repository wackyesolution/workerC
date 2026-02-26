using System;
using System.Diagnostics;
using Core.Domain.Primitives;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.ExternalProcess;
using Core.Framework.Extension.Factories;
using Core.Framework.Extension.Guid;
using Core.Framework.Extension.Tasks;
using Core.Framework.Extension.Threading;
using cTrader.Automate.Domain.Providers;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Infrastructure.Common.TargetProcess;
using cTrader.Automate.TargetProcess.Native;
using cTrader.Automate.TargetProcess.Native.ProcessFactory;
using cTrader.Domain.Application;

namespace cTrader.Automate.TargetProcess.NetCore.Unprotected;

internal sealed class NetCoreUnprotectedProcess : TargetProcessBase, INetCoreUnprotectedProcess, ITargetProcess, IDisposable
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("NetCoreUnprotectedProcess");

	private readonly AlgoId? _automateId;

	private readonly bool _isPythonOrHasDependencies;

	private readonly string? _pythonRequirements;

	private readonly IAutomatePathService _automatePathService;

	private readonly IPythonRuntimeService _pythonRuntimeService;

	private readonly IEnsureDirectoryExists _ensureDirectoryExists;

	private readonly IPythonVirtualEnvironmentCreator _pythonVirtualEnvironmentCreator;

	private readonly IBackgroundTaskExecutor _backgroundTaskExecutor;

	private readonly IApplicationDirectories _applicationDirectories;

	private readonly INativeProcessJobObject _nativeProcessJobObject;

	private readonly INativeProcessProvider _nativeProcessProvider;

	private readonly string _title;

	private readonly IWindowsNativeProcessFactory _windowsNativeProcessFactory;

	public NetCoreUnprotectedProcess(string title, AlgoId? automateId, bool isPythonOrHasDependencies, string? pythonRequirements, IGuidService guidService, INativeProcessJobObject nativeProcessJobObject, IMainThreadDispatcher mainThreadDispatcher, ITargetProcessHubFactory targetProcessHubFactory, IAutomatePathService automatePathService, IPythonRuntimeService pythonRuntimeService, IEnsureDirectoryExists ensureDirectoryExists, IPythonVirtualEnvironmentCreator pythonVirtualEnvironmentCreator, INativeProcessProvider nativeProcessProvider, INativeProcessRegistry nativeProcessRegistry, IWindowsNativeProcessFactory windowsNativeProcessFactory, IFactory<IBackgroundTaskExecutor> backgroundTaskExecutorFactory, ITargetProcessUnhandledExceptionHandler unhandledExceptionHandler, IApplicationDirectories applicationDirectories)
		: base(TargetFrameworkFamily.NetCore, guidService, mainThreadDispatcher, targetProcessHubFactory, nativeProcessRegistry, unhandledExceptionHandler)
	{
		_title = title;
		_automateId = automateId;
		_isPythonOrHasDependencies = isPythonOrHasDependencies;
		_pythonRequirements = pythonRequirements;
		_nativeProcessJobObject = nativeProcessJobObject;
		_automatePathService = automatePathService;
		_pythonRuntimeService = pythonRuntimeService;
		_ensureDirectoryExists = ensureDirectoryExists;
		_pythonVirtualEnvironmentCreator = pythonVirtualEnvironmentCreator;
		_nativeProcessProvider = nativeProcessProvider;
		_windowsNativeProcessFactory = windowsNativeProcessFactory;
		_applicationDirectories = applicationDirectories;
		_backgroundTaskExecutor = backgroundTaskExecutorFactory.Create();
	}

	protected override void OnStart()
	{
		_backgroundTaskExecutor.ScheduleSingleTask(OnStartInternal, delegate(Exception exception)
		{
			Logger.Error(exception);
			HandleStopped();
		});
	}

	private void OnStartInternal()
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = _automatePathService.NetCoreAlgoHostFilePath,
			Arguments = $"--{"title"}=\"{_title}\" --{"target-id"}=\"{base.TargetId}\"",
			CreateNoWindow = true,
			UseShellExecute = false,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		if (_automateId.HasValue)
		{
			string algoTypeNameDataDirectoryPath = _applicationDirectories.GetAlgoTypeNameDataDirectoryPath(_title, _automateId.Value.Kind);
			_ensureDirectoryExists.EnsureExists(algoTypeNameDataDirectoryPath, shutdown: false);
			processStartInfo.WorkingDirectory = algoTypeNameDataDirectoryPath;
			if (_isPythonOrHasDependencies)
			{
				string orCreate = _pythonVirtualEnvironmentCreator.GetOrCreate(algoTypeNameDataDirectoryPath, _pythonRequirements);
				processStartInfo.Arguments += $" --{"python-venv"}=\"{orCreate}\"";
				processStartInfo.Arguments += $" --{"python-dll"}=\"{_pythonRuntimeService.PythonDllPath}\"";
			}
		}
		Logger.Info($"Target process {base.TargetId} is executing {_automateId?.Kind} {_automateId?.Identity}. Title: \"{_title}\". Working directory: {processStartInfo.WorkingDirectory}. IsPython: {_isPythonOrHasDependencies}");
		Process process = (TargetProcessBase.IsWindows7() ? _windowsNativeProcessFactory.CreateProcess(_automatePathService.NetCoreAlgoHostFilePath, "--target-id=\"" + base.TargetId.Value + "\"") : (Process.Start(processStartInfo) ?? throw new InvalidOperationException()));
		INativeProcess byProcess = _nativeProcessProvider.GetByProcess(process);
		_nativeProcessJobObject.AddProcess(process.Id);
		HandleStarted(byProcess);
	}

	public override void Dispose()
	{
		_backgroundTaskExecutor.Dispose();
		base.Dispose();
	}
}
