using System;
using System.Linq;
using Common.Domain.Connection;
using Common.Domain.Promises;
using Common.Domain.User;
using Core.Autofac.Extension;
using Core.Framework.Extension.Extensions;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application.CommandLine;
using cTrader.Console.Infrastructure.Application.Services;
using cTrader.Domain.Account;
using cTrader.Domain.Application;
using cTrader.Domain.CId.Account;
using cTrader.Domain.Connection;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies.UserEdition;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class ServerConnectingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConnectionManager _connectionManager;

	private readonly IConsoleApplicationLifecycleStateContext _consoleApplicationLifecycleStateContext;

	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCIdTraderAccountProvider _consoleCIdTraderAccountProvider;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	private readonly ICredentialsRepository _credentialsRepository;

	private readonly IDesiredAccountStorage _desiredAccountStorage;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.ServerConnecting;

	public ServerConnectingStateStrategy(IUserOutput userOutput, IConnectionManager connectionManager, ICredentialsRepository credentialsRepository, IDesiredAccountStorage desiredAccountStorage, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleApplicationLifecycleStateContext consoleApplicationLifecycleStateContext, IConsoleCommandProvider consoleCommandProvider, IConsoleCIdTraderAccountProvider consoleCIdTraderAccountProvider)
	{
		_userOutput = userOutput;
		_connectionManager = connectionManager;
		_credentialsRepository = credentialsRepository;
		_desiredAccountStorage = desiredAccountStorage;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleApplicationLifecycleStateContext = consoleApplicationLifecycleStateContext;
		_consoleCommandProvider = consoleCommandProvider;
		_consoleCIdTraderAccountProvider = consoleCIdTraderAccountProvider;
	}

	protected override void DoEnter()
	{
		_consoleCIdTraderAccountProvider.IsReadyChanged += OnConsoleCIdTraderAccountProviderIsReadyChanged;
		TryToConnect();
	}

	private void OnConsoleCIdTraderAccountProviderIsReadyChanged()
	{
		TryToConnect();
	}

	private void TryToConnect()
	{
		if (_consoleCIdTraderAccountProvider.IsReady)
		{
			ICIdTraderAccount cIdTraderAccount = _consoleCIdTraderAccountProvider.Get();
			if (_consoleCommandProvider.Command.In(ConsoleCommand.Run, ConsoleCommand.Backtest))
			{
				_userOutput.Info($"Login to {cIdTraderAccount.TraderLogin}...");
			}
			_connectionManager.Server.IsReadyChanged += OnServerConnectionIsReadyChanged;
			ITraderCredentials traderCredentials = _credentialsRepository.Credentials.First((ITraderCredentials credentials) => credentials.TraderId == cIdTraderAccount.TraderId);
			_desiredAccountStorage.SetDesiredCredentialsId(traderCredentials.Id);
			_connectionManager.LoginToServer(traderCredentials, AuthenticationMethod.CtId).OnFailure(delegate(AuthenticationActionResult result)
			{
				HandleServerConnectionFailure(result.ConnectionFailedResult);
			});
		}
	}

	protected override void DoExit()
	{
		_connectionManager.Server.IsReadyChanged -= OnServerConnectionIsReadyChanged;
		_consoleCIdTraderAccountProvider.IsReadyChanged -= OnConsoleCIdTraderAccountProviderIsReadyChanged;
	}

	private void HandleServerConnectionFailure(ConnectionFailedResult connectionFailedResult)
	{
		_consoleApplicationLifecycleStateContext.ServerLastConnectionError = connectionFailedResult;
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.ServerConnectionFailed);
	}

	private void OnServerConnectionIsReadyChanged()
	{
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.ServerConnected);
	}
}
