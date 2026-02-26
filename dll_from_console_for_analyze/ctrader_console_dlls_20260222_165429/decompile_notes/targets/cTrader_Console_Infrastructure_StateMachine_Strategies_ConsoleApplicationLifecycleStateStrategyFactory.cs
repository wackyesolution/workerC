using System;
using System.Collections.Generic;
using System.Linq;
using Core.Autofac.Extension;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategyFactory) })]
internal class ConsoleApplicationLifecycleStateStrategyFactory : IConsoleApplicationLifecycleStateStrategyFactory
{
	private readonly Dictionary<ConsoleApplicationLifecycleState, Func<IConsoleApplicationLifecycleStateStrategy>> _strategyFactories;

	public ConsoleApplicationLifecycleStateStrategyFactory(IEnumerable<Func<IConsoleApplicationLifecycleStateStrategy>> strategyFactories)
	{
		_strategyFactories = strategyFactories.ToDictionary(StateExtractor, (Func<IConsoleApplicationLifecycleStateStrategy> factory) => factory);
	}

	public IConsoleApplicationLifecycleStateStrategy Create(ConsoleApplicationLifecycleState state)
	{
		return _strategyFactories[state]();
	}

	private static ConsoleApplicationLifecycleState StateExtractor(Func<IConsoleApplicationLifecycleStateStrategy> factory)
	{
		return factory().State;
	}
}
