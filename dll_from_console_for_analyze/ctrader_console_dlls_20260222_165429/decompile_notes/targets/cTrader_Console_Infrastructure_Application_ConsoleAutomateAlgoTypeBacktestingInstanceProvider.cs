using System;
using System.Diagnostics.CodeAnalysis;
using Core.Autofac.Extension;
using cTrader.Automate.Domain.Instances;
using cTrader.Console.Infrastructure.StateMachine;

namespace cTrader.Console.Infrastructure.Application;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleAutomateAlgoTypeBacktestingInstanceProvider) })]
internal class ConsoleAutomateAlgoTypeBacktestingInstanceProvider : IConsoleAutomateAlgoTypeBacktestingInstanceProvider
{
	private readonly IAutomateAlgoTypeInstanceRepository _automateAlgoTypeInstanceRepository;

	private readonly IConsoleApplicationLifecycleStateContext _consoleApplicationLifecycleStateContext;

	public ConsoleAutomateAlgoTypeBacktestingInstanceProvider(IConsoleApplicationLifecycleStateContext consoleApplicationLifecycleStateContext, IAutomateAlgoTypeInstanceRepository automateAlgoTypeInstanceRepository)
	{
		_consoleApplicationLifecycleStateContext = consoleApplicationLifecycleStateContext;
		_automateAlgoTypeInstanceRepository = automateAlgoTypeInstanceRepository;
	}

	public bool TryGet([NotNullWhen(true)] out IAutomateAlgoTypeBacktestingInstance? automateAlgoTypeBacktestingInstance)
	{
		automateAlgoTypeBacktestingInstance = null;
		if (!_consoleApplicationLifecycleStateContext.TryGetValue<Guid>("AutomateAlgoTypeInstance.Id", out var value))
		{
			return false;
		}
		if (!(_automateAlgoTypeInstanceRepository.FindById(value) is IAutomateAlgoTypeBacktestingInstance automateAlgoTypeBacktestingInstance2))
		{
			return false;
		}
		automateAlgoTypeBacktestingInstance = automateAlgoTypeBacktestingInstance2;
		return true;
	}
}
