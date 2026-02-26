using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.Factories;
using Core.Framework.Extension.Guid;
using Core.Framework.Extension.Tasks;
using Core.Framework.Extension.Threading;
using cTrader.Automate.BrokerProcess;
using cTrader.Automate.Domain.Providers;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Domain.Types;
using cTrader.Automate.TargetProcess.Native;
using cTrader.Automate.TargetProcess.Native.ProcessFactory;
using cTrader.Automate.TargetProcess.NetCore;
using cTrader.Automate.TargetProcess.NetCore.Protected;
using cTrader.Automate.TargetProcess.NetCore.Protected.Native;
using cTrader.Automate.TargetProcess.NetCore.Unprotected;
using cTrader.Automate.TargetProcess.NetFramework;
using cTrader.Domain.Application;

namespace cTrader.Automate.TargetProcess;

[Export(InstanceKind.Single, new Type[] { typeof(ITargetProcessFactory) })]
public class TargetProcessFactory : ITargetProcessFactory
{
	private readonly IAutomateTypeRepository _automateTypeRepository;

	private readonly IGuidService _guidService;

	private readonly IMainThreadDispatcher _mainThreadDispatcher;

	private readonly ITargetProcessHubFactory _targetProcessHubFactory;

	private readonly INativeProcessProvider _nativeProcessProvider;

	private readonly INativeApiProvider _nativeApiProvider;

	private readonly INativeProcessRegistry _nativeProcessRegistry;

	private readonly Lazy<IAutomateProtectedProcessAgent> _automateProtectedProcessAgentLazy;

	private readonly INativeProcessJobObject _nativeProcessJobObject;

	private readonly IAutomatePathService _automatePathService;

	private readonly IPythonRuntimeService _pythonRuntimeService;

	private readonly IEnsureDirectoryExists _ensureDirectoryExists;

	private readonly IPythonVirtualEnvironmentCreator _pythonVirtualEnvironmentCreator;

	private readonly IWindowsNativeProcessFactory _windowsNativeProcessFactory;

	private readonly IFactory<IBackgroundTaskExecutor> _backgroundTaskExecutorFactory;

	private readonly ITargetProcessUnhandledExceptionHandler _unhandledExceptionHandler;

	private readonly IApplicationDirectories _applicationDirectories;

	private readonly Func<string, AlgoId?, INetFrameworkProcess> _netFrameworkProcessFactoryMethod;

	public TargetProcessFactory(IAutomateTypeRepository automateTypeRepository, IGuidService guidService, IMainThreadDispatcher mainThreadDispatcher, ITargetProcessHubFactory targetProcessHubFactory, INativeProcessProvider nativeProcessProvider, INativeApiProvider nativeApiProvider, INativeProcessRegistry nativeProcessRegistry, Lazy<IAutomateProtectedProcessAgent> automateProtectedProcessAgentLazy, INativeProcessJobObject nativeProcessJobObject, IAutomatePathService automatePathService, IPythonRuntimeService pythonRuntimeService, IEnsureDirectoryExists ensureDirectoryExists, IPythonVirtualEnvironmentCreator pythonVirtualEnvironmentCreator, IWindowsNativeProcessFactory windowsNativeProcessFactory, IFactory<IBackgroundTaskExecutor> backgroundTaskExecutorFactory, ITargetProcessUnhandledExceptionHandler unhandledExceptionHandler, IApplicationDirectories applicationDirectories, Func<string, AlgoId?, INetFrameworkProcess> netFrameworkProcessFactoryMethod)
	{
		_automateTypeRepository = automateTypeRepository;
		_guidService = guidService;
		_mainThreadDispatcher = mainThreadDispatcher;
		_targetProcessHubFactory = targetProcessHubFactory;
		_nativeProcessProvider = nativeProcessProvider;
		_nativeApiProvider = nativeApiProvider;
		_nativeProcessRegistry = nativeProcessRegistry;
		_automateProtectedProcessAgentLazy = automateProtectedProcessAgentLazy;
		_nativeProcessJobObject = nativeProcessJobObject;
		_automatePathService = automatePathService;
		_pythonRuntimeService = pythonRuntimeService;
		_ensureDirectoryExists = ensureDirectoryExists;
		_pythonVirtualEnvironmentCreator = pythonVirtualEnvironmentCreator;
		_windowsNativeProcessFactory = windowsNativeProcessFactory;
		_backgroundTaskExecutorFactory = backgroundTaskExecutorFactory;
		_unhandledExceptionHandler = unhandledExceptionHandler;
		_applicationDirectories = applicationDirectories;
		_netFrameworkProcessFactoryMethod = netFrameworkProcessFactoryMethod;
	}

	public INetCoreProtectedProcess CreateNetCoreProtected(string title, AlgoId algoId, bool isPythonOrHasDependencies, string? pythonRequirements)
	{
		return new NetCoreProtectedProcess(title, algoId, isPythonOrHasDependencies, pythonRequirements, _automateTypeRepository, _guidService, _mainThreadDispatcher, _targetProcessHubFactory, _nativeProcessProvider, _nativeApiProvider, _nativeProcessRegistry, _automateProtectedProcessAgentLazy.Value, _unhandledExceptionHandler);
	}

	public INetCoreUnprotectedProcess CreateNetCoreUnprotected(string title, AlgoId? automateId, bool isPythonOrHasDependencies, string? pythonRequirements)
	{
		return new NetCoreUnprotectedProcess(title, automateId, isPythonOrHasDependencies, pythonRequirements, _guidService, _nativeProcessJobObject, _mainThreadDispatcher, _targetProcessHubFactory, _automatePathService, _pythonRuntimeService, _ensureDirectoryExists, _pythonVirtualEnvironmentCreator, _nativeProcessProvider, _nativeProcessRegistry, _windowsNativeProcessFactory, _backgroundTaskExecutorFactory, _unhandledExceptionHandler, _applicationDirectories);
	}

	public INetFrameworkProcess CreateNetFramework(string title, AlgoId? automateId)
	{
		return _netFrameworkProcessFactoryMethod(title, automateId);
	}
}
