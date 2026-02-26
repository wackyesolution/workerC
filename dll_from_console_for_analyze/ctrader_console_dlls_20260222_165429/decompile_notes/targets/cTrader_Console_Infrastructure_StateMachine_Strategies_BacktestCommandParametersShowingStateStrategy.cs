using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application.Services;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class BacktestCommandParametersShowingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleBacktestingInstanceParameterInfoProvider _consoleBacktestingInstanceParameterInfoProvider;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.BacktestCommandParametersShowing;

	public BacktestCommandParametersShowingStateStrategy(IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IUserOutput userOutput, IConsoleBacktestingInstanceParameterInfoProvider consoleBacktestingInstanceParameterInfoProvider)
	{
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_userOutput = userOutput;
		_consoleBacktestingInstanceParameterInfoProvider = consoleBacktestingInstanceParameterInfoProvider;
	}

	protected override void DoEnter()
	{
		foreach (string item in _consoleBacktestingInstanceParameterInfoProvider.Get())
		{
			_userOutput.Info(item);
		}
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.CidConnecting);
	}
}
