using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Autofac.Extension;
using Core.Framework.Extension.ExternalProcess;
using cTrader.Automate.Domain.Shared.Primitives;

namespace cTrader.Automate.BrokerProcess;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IAccumulativeNetCoreBrokerProcess) })]
internal class AccumulativeNetCoreBrokerProcess : IAccumulativeNetCoreBrokerProcess, INetCoreBrokerProcess, INativeProcess, IDisposable
{
	private readonly List<StartHostProcessRequest> _startHostProcessRequests = new List<StartHostProcessRequest>();

	public int Id => -1;

	public IntPtr Handle => IntPtr.Zero;

	public bool HasExited => false;

	public int ExitCode => 0;

	public event Action<int>? Exited;

	public event Action<HostProcessParameters>? HostProcessStarted;

	public event Action<TargetId?>? Error;

	public Task StartHostProcessAsync(StartHostProcessRequest request)
	{
		_startHostProcessRequests.Add(request);
		return Task.CompletedTask;
	}

	public IReadOnlyCollection<StartHostProcessRequest> TakeStartHostProcessRequests()
	{
		StartHostProcessRequest[] result = _startHostProcessRequests.ToArray();
		_startHostProcessRequests.Clear();
		return (IReadOnlyCollection<StartHostProcessRequest>)(object)result;
	}

	public void Kill()
	{
	}

	public void Dispose()
	{
	}
}
