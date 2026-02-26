using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Common.Domain.Repositories.Collections;
using Core.Domain.Primitives;
using Core.Framework.Extension.FileManagement;
using cTrader.Automate.Arena.Optimization.Chart;
using cTrader.Automate.Arena.Optimization.Instances.Pass;
using cTrader.Automate.Arena.Optimization.Repositories;
using cTrader.Automate.Domain.Arena.Optimization;
using cTrader.Automate.Domain.Arena.Optimization.Instances;
using cTrader.Automate.Domain.Arena.Optimization.Instances.Pass;
using cTrader.Automate.Domain.Instances;
using cTrader.Automate.Domain.Instances.Log;
using cTrader.Automate.Domain.Instances.Settings;
using cTrader.Automate.Domain.Shared;
using cTrader.Automate.Domain.Types;
using cTrader.Automate.Instances;
using cTrader.Automate.Instances.Optimization.Services;
using cTrader.Chart.Domain;
using cTrader.Domain.Application;

namespace cTrader.Automate.Arena.Optimization.Instances;

public class OptimizationInstancesRepository : IOptimizationInstancesRepository, IOptimizationPassRepository, IDisposable
{
	private readonly HashSet<IAutomateInstance> _activeInstances = new HashSet<IAutomateInstance>();

	private readonly IAutomateInstanceFactory _automateInstanceFactory;

	private readonly IAutomateInstanceSettingsFactory _automateInstanceSettingsFactory;

	private readonly IOptimizationInstanceRepositoriesStorage _instanceRepositoriesStorage;

	private readonly IApplicationDirectories _applicationDirectories;

	private readonly IFileService _fileService;

	private readonly IAutomateInstanceAutoRestartService _instanceAutoRestartService;

	private readonly IIsAllInstancesStoppingService _allInstancesStoppingService;

	private readonly IPathService _pathService;

	private readonly IDirectoryService _directoryService;

	private readonly IAutomateInstanceTitleFormatter _instanceTitleFormatter;

	private readonly IRepositoryItemsCollection<IAutomateOptimizationPass> _items;

	private readonly IOptimizationArenaSettings _optimizationArenaSettings;

	private readonly IOptimizationArenaVirtualChartRepository _virtualChartRepository;

	public OptimizationMainInstanceIdDataFolder MainInstanceIdDataFolderPath { get; set; }

	public IRepositoryItems<IAutomateOptimizationPass> Items => _items;

	public bool CanStartNewInstance => _activeInstances.Count < _optimizationArenaSettings.OptimizationParallelism;

	public bool HasActiveInstances => _activeInstances.Count > 0;

	public event Action<Guid, IRegularOptimizationPass>? PassCompleted;

	public event Action? AllActiveInstanceStopped;

	public event Action? OptimizationParallelismIncreased;

	public OptimizationInstancesRepository(IOptimizationArenaSettings optimizationArenaSettings, IAutomateInstanceFactory automateInstanceFactory, IAutomateInstanceSettingsFactory automateInstanceSettingsFactory, IAutomateInstanceTitleFormatter instanceTitleFormatter, IOptimizationArenaVirtualChartRepository virtualChartRepository, IRepositoryItemsCollectionFactory repositoryItemsCollectionFactory, IOptimizationInstanceRepositoriesStorage instanceRepositoriesStorage, IApplicationDirectories applicationDirectories, IFileService fileService, IAutomateInstanceAutoRestartService instanceAutoRestartService, IIsAllInstancesStoppingService allInstancesStoppingService, IPathService pathService, IDirectoryService directoryService)
	{
		_optimizationArenaSettings = optimizationArenaSettings;
		_automateInstanceFactory = automateInstanceFactory;
		_automateInstanceSettingsFactory = automateInstanceSettingsFactory;
		_instanceTitleFormatter = instanceTitleFormatter;
		_virtualChartRepository = virtualChartRepository;
		_instanceRepositoriesStorage = instanceRepositoriesStorage;
		_applicationDirectories = applicationDirectories;
		_fileService = fileService;
		_instanceAutoRestartService = instanceAutoRestartService;
		_allInstancesStoppingService = allInstancesStoppingService;
		_pathService = pathService;
		_directoryService = directoryService;
		_items = repositoryItemsCollectionFactory.Create<IAutomateOptimizationPass>();
		ResetInstanceDataFolderPath();
		_optimizationArenaSettings.Updated += OnSettingsUpdated;
	}

	public bool TryGetItem(Guid instanceId, [NotNullWhen(true)] out IRegularOptimizationPass? optimizationPass)
	{
		optimizationPass = null;
		IRegularOptimizationPass regularOptimizationPass = _items.OfType<IRegularOptimizationPass>().FirstOrDefault((IRegularOptimizationPass p) => p.Id == instanceId);
		if (regularOptimizationPass == null)
		{
			return false;
		}
		optimizationPass = regularOptimizationPass;
		return true;
	}

	public bool TryStartNewInstance(IAutomateOptimizationPassParameter[] instanceParameters, FramePeriod defaultFramePeriod, [NotNullWhen(true)] out Guid? instanceId)
	{
		if (!CanStartNewInstance)
		{
			instanceId = null;
			return false;
		}
		IAutomateInstance automateInstance = CreateInstance(instanceParameters);
		FramePeriod framePeriod = GetFramePeriod(instanceParameters, defaultFramePeriod);
		IChartContext chartContext = _virtualChartRepository.Create(_optimizationArenaSettings.SymbolPersistenceId, framePeriod);
		_activeInstances.Add(automateInstance);
		int pass = checked(_items.Count + 1);
		RegularOptimizationPass regularOptimizationPass = new RegularOptimizationPass(pass, automateInstance, _instanceRepositoriesStorage.GetOrCreateContainer(automateInstance.Id), _optimizationArenaSettings, _pathService.Combine(MainInstanceIdDataFolderPath, pass.ToString()));
		_items.Add(regularOptimizationPass);
		automateInstance.Stopped += OnInstanceStopped;
		AutomateInstanceStartOptions automateInstanceStartOptions = AutomateInstanceStartOptions.From(_instanceTitleFormatter.Format(_optimizationArenaSettings.AutomateType.Title, _optimizationArenaSettings.SymbolPersistenceId.Name, framePeriod), instanceFolderPath: _pathService.Combine(MainInstanceIdDataFolderPath, regularOptimizationPass.Number.ToString()), typeTitle: _optimizationArenaSettings.AutomateType.Title, chartContext: chartContext, kind: _optimizationArenaSettings.AutomateType.Id.Kind, isPythonOrHasDependencies: _optimizationArenaSettings.AutomateType.IsPythonOrHasDependencies());
		_instanceAutoRestartService.AddOrUpdate(automateInstance, automateInstanceStartOptions);
		automateInstance.Start(automateInstanceStartOptions);
		instanceId = automateInstance.Id;
		return true;
	}

	public void StopAll()
	{
		_allInstancesStoppingService.SetStopping(value: true);
		IAutomateInstance[] array = _activeInstances.ToArray();
		foreach (IAutomateInstance automateInstance in array)
		{
			automateInstance.Stop();
			OnInstanceStoppedInternal(automateInstance);
		}
	}

	public void Clear()
	{
		DisposeInstances();
		_virtualChartRepository.Clear();
		_items.Clear();
		_activeInstances.Clear();
		if (!MainInstanceIdDataFolderPath.IsExported)
		{
			_fileService.TryDelete(MainInstanceIdDataFolderPath);
		}
	}

	public void Import(IEnumerable<IImportedOptimizationPass> passes)
	{
		_items.AddRange(passes);
	}

	public bool TryGetInstance(Guid instanceId, [NotNullWhen(true)] out IAutomateInstance? automateInstance)
	{
		automateInstance = _activeInstances.FirstOrDefault((IAutomateInstance i) => i.Id == instanceId);
		return automateInstance != null;
	}

	private void OnSettingsUpdated()
	{
		if (HasActiveInstances && CanStartNewInstance)
		{
			this.OptimizationParallelismIncreased?.Invoke();
		}
	}

	private static FramePeriod GetFramePeriod(IEnumerable<IAutomateOptimizationPassParameter> instanceParameters, FramePeriod defaultFramePeriod)
	{
		object obj = instanceParameters.SingleOrDefault((IAutomateOptimizationPassParameter value) => value.PropertyName == "c02b25309272471b89a4de3831c473c5")?.GetValue();
		if (obj is FramePeriod)
		{
			return (FramePeriod)obj;
		}
		return defaultFramePeriod;
	}

	private void OnInstanceStopped(IAutomateInstance automateInstance)
	{
		if (automateInstance == null || !automateInstance.IsCrashed || automateInstance.IsStopRequested)
		{
			OnInstanceStoppedInternal(automateInstance);
		}
	}

	private void OnInstanceStoppedInternal(IAutomateInstance automateInstance)
	{
		if (!_activeInstances.All((IAutomateInstance i) => i.Id != automateInstance.Id) && TryGetItem(automateInstance.Id, out IRegularOptimizationPass optimizationPass))
		{
			automateInstance.Stopped -= OnInstanceStopped;
			_activeInstances.Remove(automateInstance);
			_instanceAutoRestartService.Remove(automateInstance);
			this.PassCompleted?.Invoke(automateInstance.Id, optimizationPass);
			if (!HasActiveInstances)
			{
				this.AllActiveInstanceStopped?.Invoke();
			}
		}
	}

	private IAutomateInstance CreateInstance(IEnumerable<IAutomateReadOnlyParameter> parameters)
	{
		IAutomateType automateType = _optimizationArenaSettings.AutomateType;
		IAutomateInstanceSettings automateInstanceSettings = _automateInstanceSettingsFactory.Create(automateType.GetTypeMetadata());
		automateInstanceSettings.UpdateFrom(parameters);
		return _automateInstanceFactory.Create(automateType, AutomateInstanceLevel.Regular, automateInstanceSettings, AutomateRuntimeType.Optimization);
	}

	[MemberNotNull("MainInstanceIdDataFolderPath")]
	public void ResetInstanceDataFolderPath()
	{
		MainInstanceIdDataFolderPath = OptimizationMainInstanceIdDataFolder.CreateDefault(_applicationDirectories.GetAlgoInstanceDataWithRuntimeDirectoryPath(_optimizationArenaSettings.AutomateType.Title, _optimizationArenaSettings.AutomateType.Id.Kind, _optimizationArenaSettings.GroupInstanceId?.ToString() ?? _optimizationArenaSettings.InstanceId.ToString(), AutomateRuntimeType.Optimization));
	}

	private void DisposeInstances()
	{
		foreach (IRegularOptimizationPass item in _items.OfType<IRegularOptimizationPass>())
		{
			item.AutomateInstance.Stopped -= OnInstanceStopped;
			item.Dispose();
		}
	}

	public void Dispose()
	{
		_optimizationArenaSettings.Updated -= OnSettingsUpdated;
		Clear();
	}
}
