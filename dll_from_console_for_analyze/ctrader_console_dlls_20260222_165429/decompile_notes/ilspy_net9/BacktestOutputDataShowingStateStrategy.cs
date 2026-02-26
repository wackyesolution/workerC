using System;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class BacktestOutputDataShowingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleBacktestingFinalReporter _consoleBacktestingFinalReporter;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.BacktestOutputDataShowing;

	public BacktestOutputDataShowingStateStrategy(IConsoleBacktestingFinalReporter consoleBacktestingFinalReporter, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition)
	{
		_consoleBacktestingFinalReporter = consoleBacktestingFinalReporter;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
	}

	protected override void DoEnter()
	{
		_consoleBacktestingFinalReporter.IsReadyChanged += OnFinalReporterIsReadyChanged;
		PrepareReports();
	}

	private void OnFinalReporterIsReadyChanged()
	{
		PrepareReports();
	}

	private void PrepareReports()
	{
		if (_consoleBacktestingFinalReporter.IsReady)
		{
			_consoleBacktestingFinalReporter.Report();
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.BacktestReportSaving);
		}
	}

	protected override void DoExit()
	{
		_consoleBacktestingFinalReporter.IsReadyChanged -= OnFinalReporterIsReadyChanged;
	}
}
