using System;
using System.Collections.Generic;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application.CommandLine.Parameters;
using cTrader.Console.Infrastructure.Application.Parameters;

namespace cTrader.Console.Infrastructure.Application.CommandLine.Builders.BacktestCommand;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleCommandParametersBuilder) })]
internal class BacktestCommandParametersBuilder : CommandParametersBuilderBase
{
	private readonly IConsoleAlgoFileParametersProvider _consoleAlgoFileParametersProvider;

	private readonly IConsoleCBotSetParametersProvider _consoleCBotSetParametersProvider;

	private readonly IConsoleCommandLineParametersProvider _consoleCommandLineParametersProvider;

	private readonly IEnvironmentCommandParametersProvider _environmentCommandParametersProvider;

	public override ConsoleCommand Command => ConsoleCommand.Backtest;

	public BacktestCommandParametersBuilder(IConsoleCommandLineParametersProvider consoleCommandLineParametersProvider, IConsoleAlgoFileParametersProvider consoleAlgoFileParametersProvider, IConsoleCBotSetParametersProvider consoleCBotSetParametersProvider, IEnvironmentCommandParametersProvider environmentCommandParametersProvider)
	{
		_consoleCommandLineParametersProvider = consoleCommandLineParametersProvider;
		_consoleAlgoFileParametersProvider = consoleAlgoFileParametersProvider;
		_consoleCBotSetParametersProvider = consoleCBotSetParametersProvider;
		_environmentCommandParametersProvider = environmentCommandParametersProvider;
	}

	protected override IConsoleCommandParameters DoBuild()
	{
		IReadOnlyCollection<ICommandParameter> commandLineParameters = _consoleCommandLineParametersProvider.Get();
		IReadOnlyCollection<ICommandParameter> environmentVariablesParameters = _environmentCommandParametersProvider.Get();
		IReadOnlyCollection<ICommandParameter> algoParameters = _consoleAlgoFileParametersProvider.Get();
		IReadOnlyCollection<ICommandParameter> cBotSetParameters = _consoleCBotSetParametersProvider.Get();
		return new BacktestCommandParameters(commandLineParameters, algoParameters, environmentVariablesParameters, cBotSetParameters);
	}
}
