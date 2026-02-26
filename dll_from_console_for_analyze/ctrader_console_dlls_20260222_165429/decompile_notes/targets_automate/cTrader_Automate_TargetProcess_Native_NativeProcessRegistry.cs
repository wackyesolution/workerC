using System;
using Core.Autofac.Extension;
using Core.Framework.Extension.ExternalProcess;

namespace cTrader.Automate.TargetProcess.Native;

[Export(InstanceKind.Single, new Type[]
{
	typeof(INativeProcessRegistry),
	typeof(INativeProcessRegisterNotification)
})]
internal class NativeProcessRegistry : INativeProcessRegistry, INativeProcessRegisterNotification
{
	public event Action<int>? ProcessAdded;

	public event Action<int>? ProcessRemoved;

	public void RegisterProcess(INativeProcess process)
	{
		OnProcessRegistered(process);
	}

	public void UnregisterProcess(INativeProcess process)
	{
		OnProcessUnregistered(process);
	}

	private void OnProcessRegistered(INativeProcess process)
	{
		this.ProcessAdded?.Invoke(process.Id);
	}

	private void OnProcessUnregistered(INativeProcess process)
	{
		this.ProcessRemoved?.Invoke(process.Id);
	}
}
