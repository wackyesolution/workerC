using System;
using System.IO;
using Core.Autofac.Extension;
using cTrader.Automate.Domain.Shared.Backtesting.Json.Report;
using cTrader.Automate.Domain.Shared.Backtesting.Report;
using cTrader.Automate.Instances.Backtesting.Controllers.Report;
using cTrader.Console.Infrastructure.Application.Services;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class BacktestReportSavingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IBacktestingReportMessageProvider _backtestingReportMessageProvider;

	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleReportJsonParameterValueProvider _consoleReportJsonParameterValueProvider;

	private readonly IConsoleReportParameterValueProvider _consoleReportParameterValueProvider;

	private readonly IHtmlBacktestingReportGenerator _htmlBacktestingReportGenerator;

	private readonly IBacktestingDetailsJsonSerializer _jsonSerializationService;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.BacktestReportSaving;

	public BacktestReportSavingStateStrategy(IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleReportParameterValueProvider consoleReportParameterValueProvider, IBacktestingDetailsJsonSerializer jsonSerializationService, IHtmlBacktestingReportGenerator htmlBacktestingReportGenerator, IBacktestingReportMessageProvider backtestingReportMessageProvider, IConsoleReportJsonParameterValueProvider consoleReportJsonParameterValueProvider)
	{
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleReportParameterValueProvider = consoleReportParameterValueProvider;
		_jsonSerializationService = jsonSerializationService;
		_htmlBacktestingReportGenerator = htmlBacktestingReportGenerator;
		_backtestingReportMessageProvider = backtestingReportMessageProvider;
		_consoleReportJsonParameterValueProvider = consoleReportJsonParameterValueProvider;
	}

	protected override void DoEnter()
	{
		if (_backtestingReportMessageProvider.Message == null)
		{
			throw new InvalidOperationException("Message expected");
		}
		string text = _jsonSerializationService.Serialize(_backtestingReportMessageProvider.Message.Report);
		string text2 = _consoleReportJsonParameterValueProvider.Get();
		if (!string.IsNullOrEmpty(text2))
		{
			string directoryName = Path.GetDirectoryName(text2);
			if (directoryName != null)
			{
				Directory.CreateDirectory(directoryName);
				File.WriteAllText(text2, text);
			}
		}
		string text3 = _consoleReportParameterValueProvider.Get();
		if (!string.IsNullOrEmpty(text3))
		{
			string contents = _htmlBacktestingReportGenerator.Generate(text);
			string directoryName2 = Path.GetDirectoryName(text3);
			if (directoryName2 != null)
			{
				Directory.CreateDirectory(directoryName2);
				File.WriteAllText(text3, contents);
			}
		}
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.RobotDisposing);
	}
}
