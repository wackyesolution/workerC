using System;
using Common.Domain.Application.Ready;
using Common.Domain.Promises;
using Core.Autofac.Extension;
using Core.Framework.Extension.UserOutput;
using cTrader.Automate.Domain.Arena.Backtesting;
using cTrader.Automate.Domain.Arena.Settings;
using cTrader.Automate.Domain.Arena.Symbol.Info;
using cTrader.Automate.Domain.Instances;
using cTrader.Automate.Domain.Instances.Settings.Export;
using cTrader.Automate.Domain.Shared;
using cTrader.Automate.Domain.Shared.Export;
using cTrader.Automate.Domain.Types;
using cTrader.Automate.TargetProcess.NetCore;
using cTrader.Console.Infrastructure.Application.AutomateParameters;
using cTrader.Console.Infrastructure.StateMachine;
using cTrader.Domain.Application;
using cTrader.Domain.Assets.DepositAsset;
using cTrader.Domain.Connection;
using cTrader.Domain.Repositories.Symbols;

namespace cTrader.Console.Infrastructure.Application;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleBacktestingInstanceManager) })]
internal class ConsoleBacktestingInstanceManager : ConsoleAlgoTypeInstanceManager, IConsoleBacktestingInstanceManager, IConsoleAlgoTypeInstanceManager, IReadyStatusProvider
{
	private readonly IAutomateParametersProvider _automateParametersProvider;

	private readonly IAutomateSettingsTranslator _automateSettingsTranslator;

	private readonly IConsoleBacktestingArenaSettingsApplier _consoleBacktestingArenaSettingsApplier;

	private readonly IConsoleBacktestingDataLoadingProgressMonitor _consoleBacktestingDataLoadingProgressMonitor;

	private readonly IConsoleBacktestingProgressMonitor _consoleBacktestingProgressMonitor;

	private readonly Lazy<IAutomateAlgoTypeInstanceRepository> _lazyAutomateTypeInstanceRepository;

	public ConsoleBacktestingInstanceManager(ILightSymbolRepository lightSymbolRepository, IServerConnectionStatusProvider serverConnectionStatusProvider, IArenaSymbolInfoRepository arenaSymbolInfoRepository, IUserOutput userOutput, IApplicationDirectories applicationDirectories, IPythonVirtualEnvironmentCreator pythonVirtualEnvironmentCreator, Lazy<IAutomateAlgoTypeInstanceRepository> lazyAutomateTypeInstanceRepository, Lazy<IAutomateTypeRepository> lazyAutomateTypeRepository, IAutomateParametersProvider automateParametersProvider, IAutomateSettingsTranslator automateSettingsTranslator, IDepositAssetProvider depositAssetProvider, IConsoleBacktestingArenaSettingsApplier consoleBacktestingArenaSettingsApplier, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleApplicationLifecycleStateContext consoleApplicationLifecycleStateContext, IConsoleBacktestingDataLoadingProgressMonitor consoleBacktestingDataLoadingProgressMonitor, IConsoleBacktestingProgressMonitor consoleBacktestingProgressMonitor)
		: base(lightSymbolRepository, serverConnectionStatusProvider, userOutput, consoleApplicationLifecycleStateContext, consoleApplicationLifecycleStateTransition, applicationDirectories, pythonVirtualEnvironmentCreator, lazyAutomateTypeRepository, arenaSymbolInfoRepository, depositAssetProvider)
	{
		_lazyAutomateTypeInstanceRepository = lazyAutomateTypeInstanceRepository;
		_automateParametersProvider = automateParametersProvider;
		_automateSettingsTranslator = automateSettingsTranslator;
		_consoleBacktestingArenaSettingsApplier = consoleBacktestingArenaSettingsApplier;
		_consoleBacktestingDataLoadingProgressMonitor = consoleBacktestingDataLoadingProgressMonitor;
		_consoleBacktestingProgressMonitor = consoleBacktestingProgressMonitor;
	}

	protected override IAutomateAlgoTypeInstance CreateAutomateTypeInstance(IAutomateType automateType)
	{
		IAutomateAlgoTypeInstance automateAlgoTypeInstance = _lazyAutomateTypeInstanceRepository.Value.CreateDetached(automateType, AutomateRuntimeType.Backtesting);
		ICBotSettings parameters = _automateParametersProvider.GetParameters();
		ApplyInstanceSettings(automateAlgoTypeInstance, parameters);
		return automateAlgoTypeInstance;
	}

	private void ApplyInstanceSettings(IAutomateAlgoTypeInstance automateAlgoTypeInstance, ICBotSettings automateParameter)
	{
		_automateSettingsTranslator.ApplyInstanceParameters(automateAlgoTypeInstance.InstanceSettings.Parameters, automateParameter);
		if (!(automateAlgoTypeInstance.Arena.Settings is IAutomateArenaSettingsWithSymbol arenaSettings))
		{
			throw new InvalidOperationException("Invalid settings");
		}
		_automateSettingsTranslator.ApplyArenaSettings(arenaSettings, automateParameter);
		if (automateAlgoTypeInstance.Arena.Settings is IBacktestingArenaSettings settings)
		{
			_consoleBacktestingArenaSettingsApplier.Apply(settings);
		}
	}

	protected override IPromise<IPromiseResult> CreateAlgoTypeInstancePostCreationPromise()
	{
		return new AutomateAlgoTypeBacktestingInstancePostCreationPromise((base.AutomateAlgoTypeInstance as IAutomateAlgoTypeBacktestingInstance) ?? throw new InvalidOperationException("Wrong instance type"));
	}

	protected override void BeforeInstanceStarted()
	{
		_consoleBacktestingDataLoadingProgressMonitor.Start();
		_consoleBacktestingProgressMonitor.Start();
	}

	protected override void BeforeInstanceDisposed()
	{
		_consoleBacktestingProgressMonitor.Stop();
		_consoleBacktestingDataLoadingProgressMonitor.Stop();
	}
}
