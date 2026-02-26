using System;
using System.Collections.Generic;
using Core.Autofac.Extension;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application;
using cTrader.Console.Infrastructure.Serialization;
using cTrader.Console.Infrastructure.UserOutput;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies.UserEdition;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class AccountsShowingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleAvailableAccountsProvider _consoleAvailableAccountsProvider;

	private readonly IPrettyJsonSerializer _prettyJsonSerializer;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.AccountsShowing;

	public AccountsShowingStateStrategy(IUserOutput userOutput, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleAvailableAccountsProvider consoleAvailableAccountsProvider, IPrettyJsonSerializer prettyJsonSerializer)
	{
		_userOutput = userOutput;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleAvailableAccountsProvider = consoleAvailableAccountsProvider;
		_prettyJsonSerializer = prettyJsonSerializer;
	}

	protected override void DoEnter()
	{
		_consoleAvailableAccountsProvider.IsReadyChanged += OnConsoleAvailableAccountsProviderIsReadyChanged;
		TryShowAccounts();
	}

	private void OnConsoleAvailableAccountsProviderIsReadyChanged()
	{
		TryShowAccounts();
	}

	private void TryShowAccounts()
	{
		if (_consoleAvailableAccountsProvider.IsReady)
		{
			_consoleAvailableAccountsProvider.IsReadyChanged -= OnConsoleAvailableAccountsProviderIsReadyChanged;
			IEnumerable<AccountOutputInfo> all = _consoleAvailableAccountsProvider.GetAll();
			string message = _prettyJsonSerializer.Serialize(all);
			_userOutput.Info(message);
			_userOutput.Flush();
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.ApplicationShutdown);
		}
	}

	protected override void DoExit()
	{
		_consoleAvailableAccountsProvider.IsReadyChanged -= OnConsoleAvailableAccountsProviderIsReadyChanged;
	}
}
