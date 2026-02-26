using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Cancellation;
using Core.Framework.Extension.Extensions;
using Core.Framework.Extension.Factories;
using Core.Framework.Extension.Tasks;
using cTrader.Automate.Domain.Providers;
using cTrader.Automate.Domain.Shared.Primitives;
using cTrader.Domain.ApplicationState;
using cTrader.Domain.Tasks;

namespace cTrader.Automate.BrokerProcess;

[Export(InstanceKind.Single, new Type[] { typeof(INetCoreBrokerProcessManager) })]
internal class NetCoreBrokerProcessManager : INetCoreBrokerProcessManager, IDisposable
{
	private const int MaximumCountOfAttemptsToStartBrokerProcess = 5;

	private const int DelayBetweenAttemptsToStartBrokerProcess = 1000;

	private int _currentAttemptToStartBrokerProcess;

	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("NetCoreBrokerProcessManager");

	private readonly IApplicationStateShutdown _applicationStateShutdown;

	private readonly ITaskFactory _taskFactory;

	private readonly ITaskSchedulersProvider _taskSchedulersProvider;

	private readonly IFactory<INetCoreBrokerProcess> _brokerProcessFactory;

	private readonly IDelayService _delayService;

	private readonly IAccumulativeNetCoreBrokerProcess _accumulativeNetCoreBrokerProcess;

	private INetCoreBrokerProcess _currentBrokerProcess;

	public event Action<HostProcessParameters>? HostProcessStarted;

	public event Action<TargetId>? StartHostProcessFailed;

	public NetCoreBrokerProcessManager(IApplicationStateShutdown applicationStateShutdown, IFactory<INetCoreBrokerProcess> brokerProcessFactory, ITaskFactory taskFactory, ITaskSchedulersProvider taskSchedulersProvider, IFactory<IAccumulativeNetCoreBrokerProcess> accumulativeBrokerProcessFactory, IDelayService delayService, IAutomatePathService automatePathService)
	{
		_applicationStateShutdown = applicationStateShutdown;
		_taskFactory = taskFactory;
		_taskSchedulersProvider = taskSchedulersProvider;
		_brokerProcessFactory = brokerProcessFactory;
		_delayService = delayService;
		_accumulativeNetCoreBrokerProcess = accumulativeBrokerProcessFactory.Create();
		_currentBrokerProcess = _accumulativeNetCoreBrokerProcess;
		RecreateBrokerProcessAsync();
	}

	private async Task RecreateBrokerProcessAsync()
	{
		if (_applicationStateShutdown.IsShuttingDown)
		{
			return;
		}
		if (_currentAttemptToStartBrokerProcess >= 5)
		{
			Logger.Info("Maximum count of attempts to start broker process is reached");
			return;
		}
		if (checked(_currentAttemptToStartBrokerProcess++) > 0)
		{
			await _delayService.Delay(1000, PublicCancellationToken.None);
		}
		INetCoreBrokerProcess netCoreBrokerProcess = null;
		try
		{
			netCoreBrokerProcess = await CreateBrokerProcessAsync();
		}
		catch (Exception exception)
		{
			Logger.Info(exception, "Error during creating broker process");
			OnBrokerProcessCrashedOrExited(netCoreBrokerProcess);
			return;
		}
		if (netCoreBrokerProcess != null && !netCoreBrokerProcess.HasExited)
		{
			SetupCurrentBrokerProcess(netCoreBrokerProcess);
		}
		else
		{
			OnBrokerProcessCrashedOrExited(netCoreBrokerProcess);
		}
	}

	private async Task SetupCurrentBrokerProcess(INetCoreBrokerProcess netCoreBrokerProcess)
	{
		try
		{
			_currentBrokerProcess = netCoreBrokerProcess;
			_currentBrokerProcess.Exited += OnCurrentBrokerProcessExited;
			_currentBrokerProcess.Error += OnCurrentBrokerProcessError;
			_currentBrokerProcess.HostProcessStarted += OnHostProcessStarted;
			_currentAttemptToStartBrokerProcess = 0;
			IReadOnlyCollection<StartHostProcessRequest> readOnlyCollection = _accumulativeNetCoreBrokerProcess.TakeStartHostProcessRequests();
			foreach (StartHostProcessRequest item in readOnlyCollection)
			{
				Logger.Info("[XT-14934] Start host process: " + item.Request);
				await StartHostProcessAsync(item).HandleCancellationAsync();
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception);
		}
	}

	private void OnCurrentBrokerProcessExited(int errorCode)
	{
		Logger.Info($"Broker process exited: {errorCode}");
		OnBrokerProcessCrashedOrExited(_currentBrokerProcess);
	}

	private void OnBrokerProcessCrashedOrExited(INetCoreBrokerProcess? netCoreBrokerProcess)
	{
		_currentBrokerProcess = _accumulativeNetCoreBrokerProcess;
		TryDisposeBrokerProcess(netCoreBrokerProcess);
		RecreateBrokerProcessAsync();
	}

	private void TryDisposeBrokerProcess(INetCoreBrokerProcess? netCoreBrokerProcess)
	{
		if (netCoreBrokerProcess == null)
		{
			return;
		}
		try
		{
			netCoreBrokerProcess.Exited -= OnCurrentBrokerProcessExited;
			netCoreBrokerProcess.Error -= OnCurrentBrokerProcessError;
			netCoreBrokerProcess.HostProcessStarted -= OnHostProcessStarted;
			netCoreBrokerProcess.Dispose();
		}
		catch (Exception exception)
		{
			Logger.Info(exception, "Error during disposing broker process");
		}
	}

	private Task<INetCoreBrokerProcess?> CreateBrokerProcessAsync()
	{
		return _taskFactory.StartNew(delegate
		{
			try
			{
				return _brokerProcessFactory.Create();
			}
			catch (Exception exception)
			{
				Logger.Info(exception, "Error during creating broker process");
				return (INetCoreBrokerProcess?)null;
			}
		}, CancellationToken.None, _taskSchedulersProvider.ThreadPool);
	}

	public async Task StartHostProcessAsync(StartHostProcessRequest request)
	{
		try
		{
			INetCoreBrokerProcess currentBrokerProcess = _currentBrokerProcess;
			if (currentBrokerProcess.HasExited)
			{
				throw new InvalidOperationException("Broker process exited");
			}
			await currentBrokerProcess.StartHostProcessAsync(request);
		}
		catch (Exception exception)
		{
			Logger.Info(exception, "Error during starting host process");
			request.CancellationToken.ThrowIfCancellationRequested();
			INetCoreBrokerProcess currentBrokerProcess2 = _currentBrokerProcess;
			await _accumulativeNetCoreBrokerProcess.StartHostProcessAsync(request);
			OnBrokerProcessCrashedOrExited(currentBrokerProcess2);
		}
	}

	private void OnHostProcessStarted(HostProcessParameters parameters)
	{
		this.HostProcessStarted?.Invoke(parameters);
	}

	private void OnCurrentBrokerProcessError(TargetId? targetId)
	{
		Logger.Info("Error during creating host process");
		OnBrokerProcessCrashedOrExited(_currentBrokerProcess);
		if (targetId.HasValue)
		{
			this.StartHostProcessFailed?.Invoke(targetId.Value);
		}
	}

	public void Dispose()
	{
		if (_currentBrokerProcess != _accumulativeNetCoreBrokerProcess)
		{
			_accumulativeNetCoreBrokerProcess.Dispose();
		}
		_currentBrokerProcess.Exited -= OnCurrentBrokerProcessExited;
		_currentBrokerProcess.Error -= OnCurrentBrokerProcessError;
		_currentBrokerProcess.HostProcessStarted -= OnHostProcessStarted;
		_currentBrokerProcess.Dispose();
	}
}
