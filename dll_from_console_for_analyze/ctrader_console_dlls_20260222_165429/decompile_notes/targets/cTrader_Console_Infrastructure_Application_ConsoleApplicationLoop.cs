using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.Cancellation;
using Core.Framework.Extension.Threading;
using cTrader.Console.Infrastructure.Dispatcher;
using cTrader.Console.Infrastructure.StateMachine;

namespace cTrader.Console.Infrastructure.Application;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleApplicationLoop) })]
internal class ConsoleApplicationLoop : IConsoleApplicationLoop
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleApplicationShutdownService _consoleApplicationShutdownService;

	private readonly IDispatcherFrameProvider _dispatcherFrameProvider;

	private readonly IDispatcherRemainingFramesExecutor _dispatcherRemainingFramesExecutor;

	private readonly IMainThreadDispatcher _mainThreadDispatcher;

	public ConsoleApplicationLoop(IDispatcherFrameProvider dispatcherFrameProvider, IConsoleApplicationShutdownService consoleApplicationShutdownService, IMainThreadDispatcher mainThreadDispatcher, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IDispatcherRemainingFramesExecutor dispatcherRemainingFramesExecutor)
	{
		_dispatcherFrameProvider = dispatcherFrameProvider;
		_consoleApplicationShutdownService = consoleApplicationShutdownService;
		_mainThreadDispatcher = mainThreadDispatcher;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_dispatcherRemainingFramesExecutor = dispatcherRemainingFramesExecutor;
	}

	public void Run()
	{
		_mainThreadDispatcher.ExecuteAsync(delegate
		{
			_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.ApplicationLoopStarted);
		}, DispatcherPriority.Middle, PublicCancellationToken.None);
		while (!_consoleApplicationShutdownService.IsShuttingDown)
		{
			_dispatcherFrameProvider.Get()();
		}
		_dispatcherRemainingFramesExecutor.SafeExecuteRemainingFrames();
	}
}
