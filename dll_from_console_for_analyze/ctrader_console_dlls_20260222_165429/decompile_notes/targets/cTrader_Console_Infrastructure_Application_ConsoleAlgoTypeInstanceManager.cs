using System;
using System.IO;
using Common.Domain.Application.Ready;
using Common.Domain.Promises;
using Core.Framework.Extension.UserOutput;
using cTrader.Automate.Domain.Instances;
using cTrader.Automate.Domain.Shared;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Domain.Types;
using cTrader.Automate.TargetProcess.NetCore;
using cTrader.Console.Infrastructure.StateMachine;
using cTrader.Domain.Application;
using cTrader.Domain.Connection;
using cTrader.Domain.Repositories.Symbols;

namespace cTrader.Console.Infrastructure.Application;

internal abstract class ConsoleAlgoTypeInstanceManager : ReadyStatusWrapper, IConsoleAlgoTypeInstanceManager, IReadyStatusProvider
{
	private readonly IConsoleApplicationLifecycleStateContext _consoleApplicationLifecycleStateContext;

	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IApplicationDirectories _applicationDirectories;

	private readonly IPythonVirtualEnvironmentCreator _pythonVirtualEnvironmentCreator;

	private readonly Lazy<IAutomateTypeRepository> _lazyAutomateTypeRepository;

	private readonly ILightSymbolRepository _lightSymbolRepository;

	private readonly IServerConnectionStatusProvider _serverConnectionStatusProvider;

	private readonly IUserOutput _userOutput;

	public IAutomateAlgoTypeInstance? AutomateAlgoTypeInstance { get; private set; }

	public bool IsInstanceRunning => AutomateAlgoTypeInstance?.IsRunning ?? false;

	public event Action? InstanceStarted;

	public event Action? InstanceStopped;

	public event Action? InstanceCrashed;

	protected ConsoleAlgoTypeInstanceManager(ILightSymbolRepository lightSymbolRepository, IServerConnectionStatusProvider serverConnectionStatusProvider, IUserOutput userOutput, IConsoleApplicationLifecycleStateContext consoleApplicationLifecycleStateContext, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IApplicationDirectories applicationDirectories, IPythonVirtualEnvironmentCreator pythonVirtualEnvironmentCreator, Lazy<IAutomateTypeRepository> lazyAutomateTypeRepository, params IReadyStatusProvider[] sourceProviders)
		: base(sourceProviders)
	{
		_lightSymbolRepository = lightSymbolRepository;
		_serverConnectionStatusProvider = serverConnectionStatusProvider;
		_userOutput = userOutput;
		_consoleApplicationLifecycleStateContext = consoleApplicationLifecycleStateContext;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_applicationDirectories = applicationDirectories;
		_pythonVirtualEnvironmentCreator = pythonVirtualEnvironmentCreator;
		_lazyAutomateTypeRepository = lazyAutomateTypeRepository;
		_serverConnectionStatusProvider.IsReadyChanged += OnServerConnectionStatusProviderIsReadyChanged;
	}

	public IPromise<IPromiseResult> CreateInstanceAsync(string algoFileName)
	{
		try
		{
			IAutomateType automateType = CreateAutomateType(algoFileName);
			IAutomateAlgoTypeInstance automateAlgoTypeInstance = CreateAutomateTypeInstance(automateType);
			_consoleApplicationLifecycleStateContext.SetValue("AutomateAlgoTypeInstance.Id", automateAlgoTypeInstance.Id);
			automateAlgoTypeInstance.InstanceSettings.WriteLogOnDisk = false;
			automateAlgoTypeInstance.Arena.Log.Received += OnLogReceived;
			automateAlgoTypeInstance.Started += OnAutomateTypeInstanceStarted;
			automateAlgoTypeInstance.Stopped += OnAutomateTypeInstanceStopped;
			automateAlgoTypeInstance.Crashed += OnAutomateTypeInstanceCrashed;
			AutomateAlgoTypeInstance = automateAlgoTypeInstance;
		}
		catch (Exception exception)
		{
			return Promise.WithResult(new CreateInstanceFailedPromiseResult(exception));
		}
		return CreateAlgoTypeInstancePostCreationPromise();
	}

	public void StartInstance()
	{
		BeforeInstanceStarted();
		AutomateAlgoTypeInstance?.Start();
	}

	public void DisposeInstance()
	{
		if (AutomateAlgoTypeInstance != null)
		{
			IAutomateAlgoTypeInstance? automateAlgoTypeInstance = AutomateAlgoTypeInstance;
			AutomateAlgoTypeInstance = null;
			automateAlgoTypeInstance.Started -= OnAutomateTypeInstanceStarted;
			automateAlgoTypeInstance.Stopped -= OnAutomateTypeInstanceStopped;
			automateAlgoTypeInstance.Crashed -= OnAutomateTypeInstanceCrashed;
			automateAlgoTypeInstance.Arena.Log.Received -= OnLogReceived;
			BeforeInstanceDisposed();
			automateAlgoTypeInstance.Dispose();
		}
	}

	protected virtual void BeforeInstanceStarted()
	{
	}

	protected virtual void BeforeInstanceDisposed()
	{
	}

	protected virtual IPromise<IPromiseResult> CreateAlgoTypeInstancePostCreationPromise()
	{
		return new Promise<IPromiseResult>(PromiseResult.Success);
	}

	protected abstract IAutomateAlgoTypeInstance CreateAutomateTypeInstance(IAutomateType automateType);

	private IAutomateType CreateAutomateType(string algoFileName)
	{
		AlgoId algoId = AlgoId.CreateForOwnType(AutomateKind.Robot, Path.GetFileNameWithoutExtension(algoFileName));
		IAutomateType automateType = _lazyAutomateTypeRepository.Value.Find(algoId);
		if (automateType == null)
		{
			throw new InvalidOperationException("Unable to find automate type in repository: " + algoId);
		}
		TrySetupPythonEnvironment(automateType);
		return automateType;
	}

	private void TrySetupPythonEnvironment(IAutomateType automateType)
	{
		if (automateType.IsPythonOrHasDependencies())
		{
			string algoTypeNameDataDirectoryPath = _applicationDirectories.GetAlgoTypeNameDataDirectoryPath(automateType.Title, automateType.Id.Kind);
			string pythonRequirementsOrNull = automateType.GetPythonRequirementsOrNull();
			_pythonVirtualEnvironmentCreator.GetOrCreate(algoTypeNameDataDirectoryPath, pythonRequirementsOrNull);
		}
	}

	private void OnAutomateTypeInstanceStarted(IAutomateAlgoTypeInstanceBase _)
	{
		this.InstanceStarted?.Invoke();
	}

	private void OnAutomateTypeInstanceStopped(IAutomateAlgoTypeInstanceBase _)
	{
		this.InstanceStopped?.Invoke();
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.RobotStopped);
	}

	private void OnAutomateTypeInstanceCrashed(IAutomateAlgoTypeInstanceBase _)
	{
		this.InstanceCrashed?.Invoke();
	}

	private void OnLogReceived(Guid instanceId, string[] newItems)
	{
		foreach (string message in newItems)
		{
			_userOutput.Info(message);
		}
	}

	private void OnServerConnectionStatusProviderIsReadyChanged()
	{
		if (_serverConnectionStatusProvider.IsReady)
		{
			_lightSymbolRepository.LoadItemsIfNeeded();
		}
	}

	public override void Dispose()
	{
		_serverConnectionStatusProvider.IsReadyChanged -= OnServerConnectionStatusProviderIsReadyChanged;
		base.Dispose();
	}
}
