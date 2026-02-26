using System;
using System.Collections.Generic;
using Core.Domain.Primitives;
using Core.Framework.Extension.Application.Instrumentation.Log;
using cTrader.Automate.Arena.Optimization.Instances;
using cTrader.Automate.Domain.Arena.Optimization;
using cTrader.Automate.Domain.Arena.Optimization.Instances.Pass;

namespace cTrader.Automate.Arena.Optimization.Runners.Strategies.Genetic.Algorithm.Genome.Generation;

public class AutomateSynthesisService : IAutomateSynthesisService, IDisposable
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("AutomateSynthesisService");

	private readonly FramePeriod _defaultFramePeriod;

	private readonly Queue<IAutomateIndividual> _individualsToTest;

	private readonly Dictionary<Guid, IAutomateIndividual> _individualsUnderTest;

	private readonly IOptimizationInstancesRepository _instancesRepository;

	private readonly List<IAutomateIndividual> _testedIndividuals;

	private bool _isStopped;

	public event Action<AutomateGeneration>? SynthesisComplete;

	public event Action? Stopped;

	public AutomateSynthesisService(IOptimizationInstancesRepository instancesRepository, IOptimizationArenaSettings optimizationArenaSettings)
	{
		_instancesRepository = instancesRepository;
		_individualsToTest = new Queue<IAutomateIndividual>();
		_testedIndividuals = new List<IAutomateIndividual>();
		_individualsUnderTest = new Dictionary<Guid, IAutomateIndividual>();
		_instancesRepository.PassCompleted += OnPassCompleted;
		_instancesRepository.AllActiveInstanceStopped += OnAllInstancesStopped;
		_instancesRepository.OptimizationParallelismIncreased += OnParallelismIncreased;
		_defaultFramePeriod = optimizationArenaSettings.FramePeriod;
	}

	public void Synthesise(IEnumerable<IAutomateIndividual> individuals)
	{
		if (_isStopped)
		{
			return;
		}
		foreach (IAutomateIndividual individual in individuals)
		{
			if (individual.IsFitnessMeasured)
			{
				_testedIndividuals.Add(individual);
			}
			else
			{
				_individualsToTest.Enqueue(individual);
			}
		}
		SynthesizeQueuedGenomes();
	}

	public void Stop()
	{
		_isStopped = true;
		if (_instancesRepository.HasActiveInstances)
		{
			_instancesRepository.StopAll();
		}
		else
		{
			this.Stopped?.Invoke();
		}
	}

	private void OnParallelismIncreased()
	{
		SynthesizeQueuedGenomes();
	}

	private void OnAllInstancesStopped()
	{
		if (_isStopped)
		{
			this.Stopped?.Invoke();
		}
	}

	private void OnPassCompleted(Guid id, IRegularOptimizationPass optimizationPass)
	{
		if (_isStopped)
		{
			return;
		}
		if (!_individualsUnderTest.TryGetValue(id, out IAutomateIndividual value))
		{
			Logger.Info($"Unknown instance id: {value}");
			Logger.Error("No genome found for stopped automate instance");
		}
		else
		{
			_individualsUnderTest.Remove(id);
			double? fitness = optimizationPass.Repositories.TradeStatisticsRepository.Fitness;
			value.SetFitness(fitness);
			if (fitness.HasValue)
			{
				_testedIndividuals.Add(value);
			}
		}
		SynthesizeQueuedGenomes();
	}

	private void SynthesizeQueuedGenomes()
	{
		while (TryStartNewInstance())
		{
		}
		if (!_instancesRepository.HasActiveInstances)
		{
			IAutomateIndividual[] individuals = _testedIndividuals.ToArray();
			_testedIndividuals.Clear();
			this.SynthesisComplete?.Invoke(new AutomateGeneration(individuals));
		}
	}

	private bool TryStartNewInstance()
	{
		if (_individualsToTest.Count > 0)
		{
			IAutomateIndividual automateIndividual = _individualsToTest.Dequeue();
			if (_instancesRepository.TryStartNewInstance(automateIndividual.Genome.Parameters, _defaultFramePeriod, out var instanceId))
			{
				_individualsUnderTest.Add(instanceId.Value, automateIndividual);
				return true;
			}
			_individualsToTest.Enqueue(automateIndividual);
		}
		return false;
	}

	public void Dispose()
	{
		_instancesRepository.PassCompleted -= OnPassCompleted;
		_instancesRepository.AllActiveInstanceStopped -= OnAllInstancesStopped;
		_instancesRepository.OptimizationParallelismIncreased -= OnParallelismIncreased;
	}
}
