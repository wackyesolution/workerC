using System;
using Core.Domain.Primitives;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Exceptions;
using Core.Framework.Extension.ExternalProcess;
using Core.Framework.Extension.Guid;
using Core.Framework.Extension.Threading;
using cTrader.Automate.BrokerProcess;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Domain.Types;
using cTrader.Automate.Infrastructure.Common.TargetProcess;
using cTrader.Automate.TargetProcess.Native;
using cTrader.Automate.TargetProcess.NetCore.Protected.Native;

namespace cTrader.Automate.TargetProcess.NetCore.Protected;

internal sealed class NetCoreProtectedProcess : TargetProcessBase, INetCoreProtectedProcess, ITargetProcess, IDisposable
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("NetCoreProtectedProcess");

	private readonly AlgoId _algoId;

	private readonly bool _isPythonOrHasDependencies;

	private readonly string? _pythonRequirements;

	private readonly IAutomateTypeRepository _automateTypeRepository;

	private readonly IAutomateProtectedProcessAgent _automateProtectedProcessAgent;

	private readonly INativeApiProvider _nativeApiProvider;

	private readonly INativeProcessProvider _nativeProcessProvider;

	private readonly string _title;

	public NetCoreProtectedProcess(string title, AlgoId algoId, bool isPythonOrHasDependencies, string? pythonRequirements, IAutomateTypeRepository automateTypeRepository, IGuidService guidService, IMainThreadDispatcher mainThreadDispatcher, ITargetProcessHubFactory targetProcessHubFactory, INativeProcessProvider nativeProcessProvider, INativeApiProvider nativeApiProvider, INativeProcessRegistry nativeProcessRegistry, IAutomateProtectedProcessAgent automateProtectedProcessAgent, ITargetProcessUnhandledExceptionHandler unhandledExceptionHandler)
		: base(TargetFrameworkFamily.NetCore, guidService, mainThreadDispatcher, targetProcessHubFactory, nativeProcessRegistry, unhandledExceptionHandler)
	{
		_title = title;
		_algoId = algoId;
		_isPythonOrHasDependencies = isPythonOrHasDependencies;
		_pythonRequirements = pythonRequirements;
		_automateTypeRepository = automateTypeRepository;
		_nativeProcessProvider = nativeProcessProvider;
		_nativeApiProvider = nativeApiProvider;
		_automateProtectedProcessAgent = automateProtectedProcessAgent;
	}

	protected override void OnStart()
	{
		_nativeApiProvider.Execute(StartProcess);
	}

	private async void StartProcess()
	{
		if (base.InnerHub == null)
		{
			throw new InvalidOperationException();
		}
		try
		{
			string text = base.WorkingDirectory.NotNull();
			Logger.Info($"Target process {base.TargetId} is executing {_algoId.Kind} {_algoId.Identity}. Title: \"{_title}\". Working directory: \"{text}\"");
			int processId = await _automateProtectedProcessAgent.StartProcessAsync(_title, base.TargetId, _algoId, _isPythonOrHasDependencies, _pythonRequirements, text);
			INativeProcess byProcessId = _nativeProcessProvider.GetByProcessId(processId);
			HandleStarted(byProcessId);
		}
		catch (Exception exception)
		{
			LogException(exception);
			HandleStopped();
		}
	}

	private void LogException(Exception exception)
	{
		bool flag = false;
		try
		{
			IAutomateType automateType = _automateTypeRepository.Find(_algoId);
			if (automateType != null && automateType.HasAlgo)
			{
				TargetFramework? targetFramework = automateType.GetInfo().TargetFramework;
				if (targetFramework.HasValue && targetFramework.GetValueOrDefault().Family == TargetFrameworkFamily.NetFramework)
				{
					flag = true;
				}
			}
		}
		catch
		{
		}
		if (flag)
		{
			Logger.Error(exception);
		}
		else
		{
			Logger.Info(exception);
		}
	}
}
