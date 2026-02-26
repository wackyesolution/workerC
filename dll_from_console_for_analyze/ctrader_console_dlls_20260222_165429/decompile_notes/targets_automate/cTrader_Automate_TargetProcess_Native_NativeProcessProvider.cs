using System;
using System.Diagnostics;
using Core.Autofac.Extension;
using Core.Framework.Extension.ExternalProcess;

namespace cTrader.Automate.TargetProcess.Native;

[Export(InstanceKind.Single, new Type[] { typeof(INativeProcessProvider) })]
internal class NativeProcessProvider : INativeProcessProvider
{
	public INativeProcess GetByProcessId(int processId)
	{
		return GetByProcess(Process.GetProcessById(processId));
	}

	public INativeProcess GetByProcess(Process process)
	{
		return new NativeProcessWrapper(process);
	}
}
