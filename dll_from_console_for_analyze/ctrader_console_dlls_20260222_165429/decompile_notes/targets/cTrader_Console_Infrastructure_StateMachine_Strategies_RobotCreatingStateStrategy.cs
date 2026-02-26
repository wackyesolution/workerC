using System;
using Common.Domain.Promises;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Cancellation;
using Core.Framework.Extension.Threading;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application;
using cTrader.Console.Infrastructure.Application.Exceptions;
using cTrader.Console.Infrastructure.Application.Services;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class RobotCreatingStateStrategy : ConsoleStateStrategyBase
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("RobotCreatingStateStrategy");

	private readonly IConsoleAlgoFileNameProvider _consoleAlgoFileNameProvider;

	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCurrentAlgoTypeInstanceManagerProvider _consoleCurrentAlgoTypeInstanceManagerProvider;

	private readonly IMainThreadDispatcher _mainThreadDispatcher;

	private readonly IUserOutput _userOutput;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.RobotCreating;

	public RobotCreatingStateStrategy(IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IUserOutput userOutput, IConsoleAlgoFileNameProvider consoleAlgoFileNameProvider, IConsoleCurrentAlgoTypeInstanceManagerProvider consoleCurrentAlgoTypeInstanceManagerProvider, IMainThreadDispatcher mainThreadDispatcher)
	{
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_userOutput = userOutput;
		_consoleAlgoFileNameProvider = consoleAlgoFileNameProvider;
		_consoleCurrentAlgoTypeInstanceManagerProvider = consoleCurrentAlgoTypeInstanceManagerProvider;
		_mainThreadDispatcher = mainThreadDispatcher;
	}

	protected override void DoEnter()
	{
		_consoleCurrentAlgoTypeInstanceManagerProvider.Get().IsReadyChanged += OnConsoleAlgoTypeInstanceManagerIsReadyChanged;
		CreateRobotIfPossible();
	}

	protected override void DoExit()
	{
		_consoleCurrentAlgoTypeInstanceManagerProvider.Get().IsReadyChanged -= OnConsoleAlgoTypeInstanceManagerIsReadyChanged;
	}

	private void OnConsoleAlgoTypeInstanceManagerIsReadyChanged()
	{
		CreateRobotIfPossible();
	}

	private void CreateRobotIfPossible()
	{
		IConsoleAlgoTypeInstanceManager consoleAlgoTypeInstanceManager = _consoleCurrentAlgoTypeInstanceManagerProvider.Get();
		if (!consoleAlgoTypeInstanceManager.IsReady)
		{
			return;
		}
		IConsoleFileNameProviderResult consoleFileNameProviderResult = _consoleAlgoFileNameProvider.Get();
		if (consoleFileNameProviderResult.IsSuccess)
		{
			consoleAlgoTypeInstanceManager.CreateInstanceAsync(consoleFileNameProviderResult.FileName).OnSuccess(delegate
			{
				_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.RobotCreated);
			}).OnFailure(OnInstanceCreationFailed);
		}
		else
		{
			ScheduleExceptionThrowing(new ConsoleInvalidUsageException(consoleFileNameProviderResult.ErrorMessage));
		}
	}

	private void OnInstanceCreationFailed(IPromiseResult promiseResult)
	{
		_consoleApplicationLifecycleStateTransition.MoveTo(ConsoleApplicationLifecycleState.ApplicationShutdown);
		if (promiseResult is CreateInstanceFailedPromiseResult createInstanceFailedPromiseResult)
		{
			if (createInstanceFailedPromiseResult.Result is ConsoleException exception)
			{
				ScheduleExceptionThrowing(exception);
				return;
			}
			if (createInstanceFailedPromiseResult.Result != null)
			{
				ScheduleExceptionThrowing(new AggregateException("The error occurred while cbot creating", createInstanceFailedPromiseResult.Result));
				return;
			}
		}
		Logger.Error("Something went wrong while cbot creating");
	}

	private void ScheduleExceptionThrowing(Exception exception)
	{
		_mainThreadDispatcher.ExecuteAsync(delegate
		{
			throw exception;
		}, DispatcherPriority.Low, PublicCancellationToken.None);
	}
}
