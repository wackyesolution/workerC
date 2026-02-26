using System;
using Core.Autofac.Extension;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Infrastructure.Common.TargetProcess;
using cTrader.Automate.TargetProcess;
using cTrader.Console.Infrastructure.Automate.Runtime.OutProc;

namespace cTrader.Console.Infrastructure.Linux.Automate.Runtime.OutProc;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleNetCoreProcessFactory) })]
internal class ConsoleLinuxNetCoreProcessFactory : IConsoleNetCoreProcessFactory
{
	private readonly ITargetProcessFactory _targetProcessFactory;

	public ConsoleLinuxNetCoreProcessFactory(ITargetProcessFactory targetProcessFactory)
	{
		_targetProcessFactory = targetProcessFactory;
	}

	public ITargetProcess Create(string title, AlgoId algoId, bool isPythonOrHasDependencies, string? pythonRequirements)
	{
		return _targetProcessFactory.CreateNetCoreUnprotected(title, algoId, isPythonOrHasDependencies, pythonRequirements);
	}
}
