using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;
using cTrader.Console.Infrastructure.StateMachine.Strategies;
using cTrader.Domain.ApplicationState;

namespace cTrader.Console.Infrastructure.StateMachine;

[Export(InstanceKind.Single, new Type[]
{
	typeof(IConsoleApplicationLifecycleStateMachine),
	typeof(IApplicationStateShutdown)
})]
internal class ConsoleApplicationLifecycleStateMachine : IConsoleApplicationLifecycleStateMachine, IApplicationStateShutdown, IDisposable
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("ConsoleApplicationLifecycleStateMachine");

	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private IConsoleApplicationLifecycleStateStrategy _currentStateStrategy;

	public ConsoleApplicationLifecycleState State => _currentStateStrategy.State;

	public bool IsShuttingDown => State == ConsoleApplicationLifecycleState.ApplicationShutdown;

	public event Action? StateChanged;

	public ConsoleApplicationLifecycleStateMachine(IConsoleApplicationLifecycleStateStrategyFactory consoleApplicationLifecycleStateStrategyFactory, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition)
	{
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleApplicationLifecycleStateTransition.MovedToState += OnMovedToLifecycleState;
		_currentStateStrategy = consoleApplicationLifecycleStateStrategyFactory.Create(ConsoleApplicationLifecycleState.ContainerCreated);
		_currentStateStrategy.Enter();
	}

	private void OnMovedToLifecycleState(IConsoleApplicationLifecycleStateStrategy newStateStrategy)
	{
		Logger.Info($"Existing state: {State}");
		_currentStateStrategy.Exit();
		_currentStateStrategy = newStateStrategy;
		Logger.Info($"Entering state: {State}");
		_currentStateStrategy.Enter();
		this.StateChanged?.Invoke();
	}

	public void Dispose()
	{
		_currentStateStrategy.Exit();
		_consoleApplicationLifecycleStateTransition.MovedToState -= OnMovedToLifecycleState;
	}
}
