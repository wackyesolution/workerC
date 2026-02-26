using System;
using Core.Autofac.Extension;
using Core.Domain.Primitives;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Domain.Types;
using cTrader.Automate.Infrastructure.Common.TargetProcess;
using cTrader.Automate.Runtime.OutProc;

namespace cTrader.Console.Infrastructure.Automate.Runtime.OutProc;

[Export(InstanceKind.Single, new Type[] { typeof(IOutProcRuntimeHostFactory) })]
public class ConsoleOutProcRuntimeHostFactory : IOutProcRuntimeHostFactory
{
	private readonly IConsoleNetCoreProcessFactory _consoleNetCoreProcessFactory;

	private readonly Func<IAutomateType, ITargetProcess, OutProcRuntimeHost> _processRuntimeHostFactoryMethod;

	public ConsoleOutProcRuntimeHostFactory(Func<IAutomateType, ITargetProcess, OutProcRuntimeHost> processRuntimeHostFactoryMethod, IConsoleNetCoreProcessFactory consoleNetCoreProcessFactory)
	{
		_processRuntimeHostFactoryMethod = processRuntimeHostFactoryMethod;
		_consoleNetCoreProcessFactory = consoleNetCoreProcessFactory;
	}

	public IOutProcRuntimeHost Create(IAutomateType automateType)
	{
		ITargetProcess arg = CreateProcess(automateType);
		return _processRuntimeHostFactoryMethod(automateType, arg);
	}

	private ITargetProcess CreateProcess(IAutomateType automateType)
	{
		if (!automateType.TryGetInfo(out IAutomateTypeInfo info) || info.HasErrors)
		{
			throw new InvalidOperationException();
		}
		if ((info.TargetFramework?.Family ?? TargetFrameworkFamily.NetFramework) == TargetFrameworkFamily.NetCore)
		{
			return _consoleNetCoreProcessFactory.Create(automateType.Title, automateType.Id, automateType.IsPythonOrHasDependencies(), automateType.GetPythonRequirementsOrNull());
		}
		throw new InvalidOperationException();
	}
}
