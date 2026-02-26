using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application;
using cTrader.Console.Infrastructure.Application.CommandLine;
using cTrader.Console.Infrastructure.Application.Exceptions;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies.UserEdition;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class ServerConnectedStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	private readonly IConsoleConnectionEstablishmentHandler _consoleConnectionEstablishmentHandler;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.ServerConnected;

	public ServerConnectedStateStrategy(IUserOutput userOutput, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleConnectionEstablishmentHandler consoleConnectionEstablishmentHandler, IConsoleCommandProvider consoleCommandProvider)
	{
		_userOutput = userOutput;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleConnectionEstablishmentHandler = consoleConnectionEstablishmentHandler;
		_consoleCommandProvider = consoleCommandProvider;
	}

	protected override void DoEnter()
	{
		_consoleConnectionEstablishmentHandler.StopHandle();
		switch (_consoleCommandProvider.Command)
		{
		case ConsoleCommand.Run:
		case ConsoleCommand.Backtest:
			_userOutput.Info("Logged in.");
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.SymbolNameValidating);
			break;
		case ConsoleCommand.Symbols:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.SymbolsShowing);
			break;
		default:
			throw new ConsoleInvalidUsageException("Unsupported command for current state");
		}
	}
}
