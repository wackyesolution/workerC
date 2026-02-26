using System;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application.Services;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies.UserEdition;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class AccountSelectingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCIdTraderAccountProvider _consoleCIdTraderAccountProvider;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.AccountSelecting;

	public AccountSelectingStateStrategy(IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleCIdTraderAccountProvider consoleCIdTraderAccountProvider)
	{
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleCIdTraderAccountProvider = consoleCIdTraderAccountProvider;
	}

	protected override void DoEnter()
	{
		_consoleCIdTraderAccountProvider.IsReadyChanged += OnConsoleCIdTraderAccountProviderIsReadyChanged;
		EnsureAccountExisted();
	}

	private void OnConsoleCIdTraderAccountProviderIsReadyChanged()
	{
		EnsureAccountExisted();
	}

	protected override void DoExit()
	{
		_consoleCIdTraderAccountProvider.IsReadyChanged -= OnConsoleCIdTraderAccountProviderIsReadyChanged;
	}

	private void EnsureAccountExisted()
	{
		if (_consoleCIdTraderAccountProvider.IsReady)
		{
			_consoleCIdTraderAccountProvider.Get();
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.ServerConnecting);
		}
	}
}
