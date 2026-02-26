using System;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application.CommandLine;
using cTrader.Console.Infrastructure.Application.Exceptions;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class ApplicationLoopStartedStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.ApplicationLoopStarted;

	public ApplicationLoopStartedStateStrategy(IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleCommandProvider consoleCommandProvider)
	{
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleCommandProvider = consoleCommandProvider;
	}

	protected override void DoEnter()
	{
		switch (_consoleCommandProvider.Command)
		{
		case ConsoleCommand.Run:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.RunCommandFlagsValidating);
			break;
		case ConsoleCommand.Periods:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.PeriodsCommandFlagsValidating);
			break;
		case ConsoleCommand.Accounts:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.AccountsCommandFlagsValidating);
			break;
		case ConsoleCommand.Symbols:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.SymbolsCommandFlagsValidating);
			break;
		case ConsoleCommand.Metadata:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.MetadataCommandFlagsValidating);
			break;
		case ConsoleCommand.Version:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.VersionShowing);
			break;
		case ConsoleCommand.Crash:
			throw new InvalidOperationException("Unhandled exception emulation");
		case ConsoleCommand.Backtest:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.BacktestCommandFlagsValidating);
			break;
		case ConsoleCommand.Help:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.HelpShowing);
			break;
		default:
			throw new ConsoleInvalidUsageException("Command not supported");
		}
	}
}
