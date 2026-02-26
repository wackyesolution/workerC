using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application.CommandLine;
using cTrader.Console.Infrastructure.Application.Exceptions;
using cTrader.Domain.Connection.AutoConnect;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies.UserEdition;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class CIdConnectedStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateContext _consoleApplicationLifecycleStateContext;

	private readonly ICIdAndCServerAutoConnectService _cIdAndCServerAutoConnectService;

	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.CidConnected;

	public CIdConnectedStateStrategy(IUserOutput userOutput, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleCommandProvider consoleCommandProvider, IConsoleApplicationLifecycleStateContext consoleApplicationLifecycleStateContext, ICIdAndCServerAutoConnectService cIdAndCServerAutoConnectService)
	{
		_userOutput = userOutput;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleCommandProvider = consoleCommandProvider;
		_consoleApplicationLifecycleStateContext = consoleApplicationLifecycleStateContext;
		_cIdAndCServerAutoConnectService = cIdAndCServerAutoConnectService;
	}

	protected override void DoEnter()
	{
		switch (_consoleCommandProvider.Command)
		{
		case ConsoleCommand.Run:
		case ConsoleCommand.Backtest:
		{
			if (_consoleApplicationLifecycleStateContext.TryGetValue<ConsoleCidConnectionState>("cid_connection_state", out var value) && value == ConsoleCidConnectionState.Reconnecting)
			{
				_consoleApplicationLifecycleStateContext.SetValue("cid_connection_state", ConsoleCidConnectionState.Connected);
				_userOutput.TimeFormattedInfo("The connection has been restored.");
				_cIdAndCServerAutoConnectService.Resume();
			}
			else
			{
				_userOutput.TimeFormattedInfo("The connection has been established.");
			}
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.AccountSelecting);
			break;
		}
		case ConsoleCommand.Accounts:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.AccountsShowing);
			break;
		case ConsoleCommand.Symbols:
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.AccountSelecting);
			break;
		default:
			throw new ConsoleInvalidUsageException("Unsupported command for current state");
		}
	}
}
