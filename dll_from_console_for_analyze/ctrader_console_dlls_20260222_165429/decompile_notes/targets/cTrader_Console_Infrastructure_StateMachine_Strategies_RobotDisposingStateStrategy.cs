using System;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application;
using cTrader.Console.Infrastructure.Application.Services;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class RobotDisposingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCurrentAlgoTypeInstanceManagerProvider _consoleCurrentAlgoTypeInstanceManagerProvider;

	private readonly IConsoleExitOnStopParameterValueProvider _consoleExitOnStopParameterValueProvider;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.RobotDisposing;

	public RobotDisposingStateStrategy(IConsoleCurrentAlgoTypeInstanceManagerProvider consoleCurrentAlgoTypeInstanceManagerProvider, IConsoleExitOnStopParameterValueProvider consoleExitOnStopParameterValueProvider, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition)
	{
		_consoleCurrentAlgoTypeInstanceManagerProvider = consoleCurrentAlgoTypeInstanceManagerProvider;
		_consoleExitOnStopParameterValueProvider = consoleExitOnStopParameterValueProvider;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
	}

	protected override void DoEnter()
	{
		_consoleCurrentAlgoTypeInstanceManagerProvider.Get().DisposeInstance();
		if (_consoleExitOnStopParameterValueProvider.Get())
		{
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.ApplicationShutdown);
		}
	}
}
