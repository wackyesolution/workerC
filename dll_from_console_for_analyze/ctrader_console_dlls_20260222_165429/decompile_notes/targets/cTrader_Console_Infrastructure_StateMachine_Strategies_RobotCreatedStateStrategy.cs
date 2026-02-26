using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.UserOutput;
using cTrader.Automate.Domain.Instances;
using cTrader.Console.Infrastructure.Application;
using cTrader.Console.Infrastructure.Application.CommandLine;
using cTrader.Console.Infrastructure.Application.Exceptions;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class RobotCreatedStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	private readonly IConsoleCurrentAlgoTypeInstanceManagerProvider _consoleCurrentAlgoTypeInstanceManagerProvider;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.RobotCreated;

	public RobotCreatedStateStrategy(IUserOutput userOutput, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleCurrentAlgoTypeInstanceManagerProvider consoleCurrentAlgoTypeInstanceManagerProvider, IConsoleCommandProvider consoleCommandProvider)
	{
		_userOutput = userOutput;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleCurrentAlgoTypeInstanceManagerProvider = consoleCurrentAlgoTypeInstanceManagerProvider;
		_consoleCommandProvider = consoleCommandProvider;
	}

	protected override void DoEnter()
	{
		if (_consoleCommandProvider.Command == ConsoleCommand.Backtest)
		{
			IConsoleAlgoTypeInstanceManager consoleAlgoTypeInstanceManager = _consoleCurrentAlgoTypeInstanceManagerProvider.Get();
			if (consoleAlgoTypeInstanceManager.AutomateAlgoTypeInstance == null)
			{
				throw new InvalidOperationException("Can't be null here");
			}
			if (!(consoleAlgoTypeInstanceManager.AutomateAlgoTypeInstance is IAutomateAlgoTypeBacktestingInstance automateAlgoTypeBacktestingInstance))
			{
				throw new InvalidOperationException("Expected backtesting instance here");
			}
			if (automateAlgoTypeBacktestingInstance.Arena.Settings.StartDay < automateAlgoTypeBacktestingInstance.Arena.Settings.DataLoaderProvider.MinDay || automateAlgoTypeBacktestingInstance.Arena.Settings.EndDay > automateAlgoTypeBacktestingInstance.Arena.Settings.DataLoaderProvider.MaxDay)
			{
				throw new ConsoleNoHistoricalDataException();
			}
		}
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.RobotStarting);
	}
}
