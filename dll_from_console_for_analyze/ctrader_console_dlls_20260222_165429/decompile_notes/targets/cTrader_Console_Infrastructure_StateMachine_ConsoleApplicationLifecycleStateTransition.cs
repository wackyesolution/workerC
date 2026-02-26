using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Cancellation;
using Core.Framework.Extension.Threading;
using cTrader.Console.Infrastructure.StateMachine.Strategies;

namespace cTrader.Console.Infrastructure.StateMachine;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleApplicationLifecycleStateTransition) })]
internal class ConsoleApplicationLifecycleStateTransition : IConsoleApplicationLifecycleStateTransition
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("ConsoleApplicationLifecycleStateTransition");

	private readonly Lazy<IConsoleApplicationLifecycleStateStrategyFactory> _consoleApplicationLifecycleStateStrategyLazyFactory;

	private readonly Lazy<IMainThreadDispatcher> _lazyMainThreadDispatcher;

	public event Action<IConsoleApplicationLifecycleStateStrategy>? MovedToState;

	public ConsoleApplicationLifecycleStateTransition(Lazy<IConsoleApplicationLifecycleStateStrategyFactory> consoleApplicationLifecycleStateStrategyLazyFactory, Lazy<IMainThreadDispatcher> lazyMainThreadDispatcher)
	{
		_consoleApplicationLifecycleStateStrategyLazyFactory = consoleApplicationLifecycleStateStrategyLazyFactory;
		_lazyMainThreadDispatcher = lazyMainThreadDispatcher;
	}

	public void MoveTo(ConsoleApplicationLifecycleState state)
	{
		_lazyMainThreadDispatcher.Value.ExecuteAsync(delegate
		{
			InternalMoveTo(state);
		}, DispatcherPriority.Middle, PublicCancellationToken.None);
	}

	private void InternalMoveTo(ConsoleApplicationLifecycleState state)
	{
		Logger.Info($"Moving state to: {state}");
		IConsoleApplicationLifecycleStateStrategy obj = _consoleApplicationLifecycleStateStrategyLazyFactory.Value.Create(state);
		this.MovedToState?.Invoke(obj);
	}
}
