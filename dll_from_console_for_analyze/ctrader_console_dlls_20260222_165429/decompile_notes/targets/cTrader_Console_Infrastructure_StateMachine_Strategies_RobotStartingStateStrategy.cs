using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.UserOutput;
using cTrader.Console.Infrastructure.Application;
using cTrader.Domain.Services;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class RobotStartingStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConsoleApplicationLifecycleStateTransition _consoleApplicationLifecycleStateTransition;

	private readonly IConsoleCurrentAlgoTypeInstanceManagerProvider _consoleCurrentAlgoTypeInstanceManagerProvider;

	private readonly IUserOutput _userOutput;

	private readonly IConsoleKindProvider _consoleKindProvider;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.RobotStarting;

	public RobotStartingStateStrategy(IUserOutput userOutput, IConsoleKindProvider consoleKindProvider, IConsoleApplicationLifecycleStateTransition consoleApplicationLifecycleStateTransition, IConsoleCurrentAlgoTypeInstanceManagerProvider consoleCurrentAlgoTypeInstanceManagerProvider)
	{
		_userOutput = userOutput;
		_consoleKindProvider = consoleKindProvider;
		_consoleApplicationLifecycleStateTransition = consoleApplicationLifecycleStateTransition;
		_consoleCurrentAlgoTypeInstanceManagerProvider = consoleCurrentAlgoTypeInstanceManagerProvider;
	}

	protected override void DoEnter()
	{
		_userOutput.Info("Starting cBot...");
		IConsoleAlgoTypeInstanceManager consoleAlgoTypeInstanceManager = _consoleCurrentAlgoTypeInstanceManagerProvider.Get();
		consoleAlgoTypeInstanceManager.InstanceStarted += OnAlgoTypeInstanceStarted;
		consoleAlgoTypeInstanceManager.StartInstance();
	}

	protected override void DoExit()
	{
		_consoleCurrentAlgoTypeInstanceManagerProvider.Get().InstanceStarted -= OnAlgoTypeInstanceStarted;
	}

	private void OnAlgoTypeInstanceStarted()
	{
		if (_consoleCurrentAlgoTypeInstanceManagerProvider.Get().IsInstanceRunning)
		{
			_consoleApplicationLifecycleStateTransition.MoveTo(_consoleKindProvider.IsInternal ? ConsoleApplicationLifecycleState.CloudRobotStarted : ConsoleApplicationLifecycleState.RobotStarted);
		}
	}
}
