using System;
using Common.Domain.Connection;
using Common.Domain.Promises;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Extensions;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application;
using cTrader.Console.Infrastructure.Application.CommandLine;
using cTrader.Console.Infrastructure.Application.Services;
using cTrader.Domain.CId.Authentication;
using cTrader.Domain.Connection;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies.UserEdition;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class CIdConnectingStateStrategy : ConsoleStateStrategyBase
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("CIdConnectingStateStrategy");

	private readonly IConnectionManager _connectionManager;

	private readonly IConsoleApplicationLifecycleStateContext _consoleApplicationLifecycleStateContext;

	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	private readonly IConsoleConnectionEstablishmentHandler _consoleConnectionEstablishmentHandler;

	private readonly IConsoleLoginAuthTokenProvider _consoleLoginAuthTokenProvider;

	private readonly IConsolePasswordReader _consolePasswordReader;

	private readonly ICurrentCommandParametersProvider _currentCommandParametersProvider;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.CidConnecting;

	public CIdConnectingStateStrategy(IConnectionManager connectionManager, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IUserOutput userOutput, IConsoleApplicationLifecycleStateContext consoleApplicationLifecycleStateContext, IConsolePasswordReader consolePasswordReader, IConsoleConnectionEstablishmentHandler consoleConnectionEstablishmentHandler, IConsoleCommandProvider consoleCommandProvider, ICurrentCommandParametersProvider currentCommandParametersProvider, IConsoleLoginAuthTokenProvider consoleLoginAuthTokenProvider)
	{
		_connectionManager = connectionManager;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_userOutput = userOutput;
		_consoleApplicationLifecycleStateContext = consoleApplicationLifecycleStateContext;
		_consolePasswordReader = consolePasswordReader;
		_consoleConnectionEstablishmentHandler = consoleConnectionEstablishmentHandler;
		_consoleCommandProvider = consoleCommandProvider;
		_currentCommandParametersProvider = currentCommandParametersProvider;
		_consoleLoginAuthTokenProvider = consoleLoginAuthTokenProvider;
	}

	protected override void DoEnter()
	{
		string parameterValue = _currentCommandParametersProvider.GetParameterValue(KnownOption.Ctid);
		_connectionManager.CId.IsReadyChanged += OnCIdConnectionIsReadyChanged;
		if (_consoleCommandProvider.Command.In(ConsoleCommand.Accounts, ConsoleCommand.Symbols))
		{
			_consoleConnectionEstablishmentHandler.StartHandle();
		}
		else
		{
			_userOutput.TimeFormattedInfo("Establishing connection using " + parameterValue + "...");
		}
		string text = _consoleLoginAuthTokenProvider.Get();
		if (text == null)
		{
			string parameterValue2 = _currentCommandParametersProvider.GetParameterValue(KnownOption.PasswordFile);
			string password = _consolePasswordReader.Read(parameterValue2);
			Logger.Info("Logging with credentials");
			_connectionManager.LoginToCIdWithCredentials(parameterValue, password, keepLoggedIn: false).OnFailure(delegate(CIdCredentialsAuthenticationResult result)
			{
				HandleCidConnectionFailure(result.Error);
			});
		}
		else
		{
			Logger.Info("Logging with auth token");
			_connectionManager.LoginToCId(text).OnFailure(delegate(CIdTokenAuthenticationResult result)
			{
				HandleCidConnectionFailure(result.Error);
			});
		}
	}

	protected override void DoExit()
	{
		_connectionManager.CId.IsReadyChanged -= OnCIdConnectionIsReadyChanged;
	}

	private void HandleCidConnectionFailure(Error? error)
	{
		Logger.Info("HandleCidConnectionFailure: " + error?.ErrorCode + ", " + error?.Description);
		_consoleApplicationLifecycleStateContext.CidLastConnectionError = error;
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.CidConnectionFailed);
	}

	private void OnCIdConnectionIsReadyChanged()
	{
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.CidConnected);
	}
}
