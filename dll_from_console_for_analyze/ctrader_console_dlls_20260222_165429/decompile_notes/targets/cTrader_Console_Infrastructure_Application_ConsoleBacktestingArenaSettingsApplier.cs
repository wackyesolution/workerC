using System;
using Core.Autofac.Extension;
using Core.Domain.Primitives;
using Core.Framework.Extension.Extensions;
using cTrader.Automate.Domain.Arena.Backtesting;
using cTrader.Automate.Domain.Shared.Backtesting.Common;
using cTrader.Automate.Domain.Shared.Backtesting.Common.DataSource.Spread;
using cTrader.Console.Infrastructure.Application.CommandLine.Validators;
using cTrader.Console.Infrastructure.Application.Exceptions;
using cTrader.Console.Infrastructure.Application.Services;
using cTrader.Domain.Account;

namespace cTrader.Console.Infrastructure.Application;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleBacktestingArenaSettingsApplier) })]
internal class ConsoleBacktestingArenaSettingsApplier : IConsoleBacktestingArenaSettingsApplier
{
	private readonly IConsoleBalanceParameterValueProvider _consoleBalanceParameterValueProvider;

	private readonly IConsoleCommissionParameterValueProvider _consoleCommissionParameterValueProvider;

	private readonly IConsoleDataFileParameterValueProvider _consoleDataFileParameterValueProvider;

	private readonly IConsoleDataModeParameterValueProvider _consoleDataModeParameterValueProvider;

	private readonly IConsoleEndParameterValueProvider _consoleEndParameterValueProvider;

	private readonly IConsoleSpreadParameterValueProvider _consoleSpreadParameterValueProvider;

	private readonly IConsoleStartParameterValueProvider _consoleStartParameterValueProvider;

	private readonly ITradingAccountInfo _tradingAccountInfo;

	public ConsoleBacktestingArenaSettingsApplier(IConsoleStartParameterValueProvider consoleStartParameterValueProvider, IConsoleEndParameterValueProvider consoleEndParameterValueProvider, IConsoleBalanceParameterValueProvider consoleBalanceParameterValueProvider, IConsoleDataModeParameterValueProvider consoleDataModeParameterValueProvider, IConsoleCommissionParameterValueProvider consoleCommissionParameterValueProvider, IConsoleSpreadParameterValueProvider consoleSpreadParameterValueProvider, IConsoleDataFileParameterValueProvider consoleDataFileParameterValueProvider, ITradingAccountInfo tradingAccountInfo)
	{
		_consoleStartParameterValueProvider = consoleStartParameterValueProvider;
		_consoleEndParameterValueProvider = consoleEndParameterValueProvider;
		_consoleBalanceParameterValueProvider = consoleBalanceParameterValueProvider;
		_consoleDataModeParameterValueProvider = consoleDataModeParameterValueProvider;
		_consoleCommissionParameterValueProvider = consoleCommissionParameterValueProvider;
		_consoleSpreadParameterValueProvider = consoleSpreadParameterValueProvider;
		_consoleDataFileParameterValueProvider = consoleDataFileParameterValueProvider;
		_tradingAccountInfo = tradingAccountInfo;
	}

	public void Apply(IBacktestingArenaSettings settings)
	{
		settings.IsVisual = false;
		settings.StartDay = _consoleStartParameterValueProvider.Get();
		settings.EndDay = _consoleEndParameterValueProvider.Get();
		ParameterValue<decimal> parameterValue = _consoleBalanceParameterValueProvider.Get();
		settings.InitialBalance = (parameterValue.IsDefault ? _tradingAccountInfo.Balance : parameterValue.Value);
		settings.DataSourceId = BacktestingDataSourceId.CreatePredefined(_consoleDataModeParameterValueProvider.Get());
		settings.CommissionValue = _consoleCommissionParameterValueProvider.Get();
		if (settings.DataSourceId.PredefinedType == BacktestingDataSourceType.CsvFile)
		{
			settings.CsvFilePath = _consoleDataFileParameterValueProvider.Get();
		}
		SpreadDetails spreadDetails = _consoleSpreadParameterValueProvider.Get();
		settings.SpreadType = spreadDetails.SpreadType;
		settings.FixedSpreadValue = spreadDetails.FixedSpreadValue;
		settings.RandomSpreadMinValue = spreadDetails.RandomSpreadMinValue;
		settings.RandomSpreadMaxValue = spreadDetails.RandomSpreadMaxValue;
		if (settings.StartDay > settings.EndDay)
		{
			throw new ConsoleInvalidSettingsException();
		}
		if (settings.InitialBalance < 0m)
		{
			throw new ConsoleInvalidSettingsException();
		}
		if (settings.CommissionValue < 0m)
		{
			throw new ConsoleInvalidSettingsException();
		}
		if (settings != null && settings.SpreadType == SpreadType.Fixed && settings.FixedSpreadValue < 0m)
		{
			throw new ConsoleInvalidSettingsException();
		}
		if (settings.SpreadType == SpreadType.Random && (settings.RandomSpreadMinValue < 0m || settings.RandomSpreadMaxValue < 0m))
		{
			throw new ConsoleInvalidSettingsException();
		}
		if (settings.DataSourceId.PredefinedType != BacktestingDataSourceType.ServerTicks && settings.FramePeriod.Type.In(FramePeriodType.Tick, FramePeriodType.Range, FramePeriodType.Renko))
		{
			throw new ConsoleInvalidSettingsException();
		}
	}
}
