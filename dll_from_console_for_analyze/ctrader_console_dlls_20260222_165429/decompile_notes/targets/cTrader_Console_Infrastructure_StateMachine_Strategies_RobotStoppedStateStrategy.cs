using System;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application.CommandLine;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class RobotStoppedStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.RobotStopped;

	public RobotStoppedStateStrategy(IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleCommandProvider consoleCommandProvider)
	{
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleCommandProvider = consoleCommandProvider;
	}

	protected override void DoEnter()
	{
		switch (_consoleCommandProvider.Command)
		{
		case ConsoleCommand.Backtest:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.BacktestOutputDataShowing);
			break;
		case ConsoleCommand.Run:
		case ConsoleCommand.Cloud:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.RobotDisposing);
			break;
		default:
			throw new InvalidOperationException($"Command not supported here: {_consoleCommandProvider.Command}");
		}
	}
}
