using System;
using System.Threading;
using Core.Domain.Primitives;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Cancellation;
using Core.Framework.Extension.ExternalProcess;
using Core.Framework.Extension.Guid;
using Core.Framework.Extension.Threading;
using Core.Framework.Extension.Threading.Asserts;
using cTrader.Automate.Domain.Shared.Messaging.Hub;
using cTrader.Automate.Domain.Shared.Primitives;
using cTrader.Automate.Infrastructure.Common.TargetProcess;
using cTrader.Automate.TargetProcess.Native;

namespace cTrader.Automate.TargetProcess;

internal abstract class TargetProcessBase : ITargetProcess, IDisposable
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("TargetProcessBase");

	private readonly TargetFrameworkFamily _targetFrameworkFamily;

	private readonly IMainThreadDispatcher _mainThreadDispatcher;

	private readonly INativeProcessRegistry _nativeProcessRegistry;

	private readonly ITargetProcessHubFactory _targetProcessHubFactory;

	private readonly ITargetProcessUnhandledExceptionHandler _unhandledExceptionHandler;

	private INativeProcess? _nativeProcess;

	private int _status;

	protected ITargetProcessHub? InnerHub { get; private set; }

	protected string? WorkingDirectory { get; private set; }

	protected TargetId TargetId { get; }

	public IntPtr Handle => _nativeProcess?.Handle ?? IntPtr.Zero;

	public TargetProcessStatus Status
	{
		get
		{
			return (TargetProcessStatus)_status;
		}
		private set
		{
			Interlocked.Exchange(ref _status, (int)value);
			_mainThreadDispatcher.ExecuteAsync(delegate
			{
				this.StatusChanged?.Invoke(this);
			}, DispatcherPriority.Middle, PublicCancellationToken.None);
		}
	}

	public IAutomateHub? Hub => InnerHub;

	public event Action<ITargetProcess>? StatusChanged;

	protected TargetProcessBase(TargetFrameworkFamily targetFrameworkFamily, IGuidService guidService, IMainThreadDispatcher mainThreadDispatcher, ITargetProcessHubFactory targetProcessHubFactory, INativeProcessRegistry nativeProcessRegistry, ITargetProcessUnhandledExceptionHandler unhandledExceptionHandler)
	{
		_targetFrameworkFamily = targetFrameworkFamily;
		_mainThreadDispatcher = mainThreadDispatcher;
		_targetProcessHubFactory = targetProcessHubFactory;
		_nativeProcessRegistry = nativeProcessRegistry;
		_unhandledExceptionHandler = unhandledExceptionHandler;
		TargetId = new TargetId(guidService.CreateNewGuidStringWithoutHyphens());
		_status = 0;
	}

	public void Start()
	{
		Start(null);
	}

	public void Start(string? workingDirectory)
	{
		Logger.Info($"Start TargetProcess - TargetId: {TargetId}, Status: {Status}, IsInnerHubNull: {InnerHub == null}");
		MainThreadAssert.ThreadIsAlwaysMain();
		if (Status != TargetProcessStatus.Stopped)
		{
			throw new InvalidOperationException();
		}
		if (InnerHub != null)
		{
			throw new InvalidOperationException();
		}
		Status = TargetProcessStatus.Starting;
		WorkingDirectory = workingDirectory;
		InnerHub = _targetProcessHubFactory.Create(TargetId, _targetFrameworkFamily);
		OnStart();
	}

	public void Stop()
	{
		try
		{
			Logger.Info($"Stop TargetProcess - TargetId: {TargetId}, Status: {Status}, IsInnerHubNull: {InnerHub == null}");
			MainThreadAssert.ThreadIsAlwaysMain();
			TargetProcessStatus status = Status;
			if (status != TargetProcessStatus.Stopped && status != TargetProcessStatus.Stopping)
			{
				if (InnerHub == null)
				{
					throw new InvalidOperationException("InnerHub is null");
				}
				Status = TargetProcessStatus.Stopping;
				InnerHub.Abort(AutomateHubAbortReason.StopRequested);
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception);
		}
	}

	protected void HandleStarted(INativeProcess nativeProcess)
	{
		Logger.Info($"HandleStarted TargetProcess - TargetId: {TargetId}, Status: {Status}, IsInnerHubNull: {InnerHub == null}");
		ThreadAssert.AlwaysThreadIs(ThreadKind.NotImportant);
		if (InnerHub == null || _nativeProcess != null)
		{
			throw new InvalidOperationException();
		}
		if (Status == TargetProcessStatus.Stopping)
		{
			HandleStopped(nativeProcess);
			return;
		}
		Status = TargetProcessStatus.Starting;
		try
		{
			if (nativeProcess.HasExited)
			{
				HandleStopped(nativeProcess);
				return;
			}
			_nativeProcess = nativeProcess;
			_nativeProcess.Exited += OnNativeProcessExited;
			InnerHub.Disconnected += OnHubDisconnected;
			InnerHub.Connect();
			InnerHub.Run();
			Status = TargetProcessStatus.Started;
			_nativeProcessRegistry.RegisterProcess(_nativeProcess);
		}
		catch (Exception exception)
		{
			HandleStopped(nativeProcess);
			_unhandledExceptionHandler.Handle(exception);
		}
	}

	protected void HandleStopped(INativeProcess? nativeProcess = null)
	{
		Logger.Info($"HandleStopped TargetProcess - TargetId: {TargetId}, Status: {Status}, IsInnerHubNull: {InnerHub == null}");
		ThreadAssert.AlwaysThreadIs(ThreadKind.NotImportant);
		if (Status != TargetProcessStatus.Stopped)
		{
			Status = TargetProcessStatus.Stopping;
			INativeProcess nativeProcess2 = _nativeProcess ?? nativeProcess;
			if (nativeProcess2 != null)
			{
				_nativeProcessRegistry.UnregisterProcess(nativeProcess2);
				nativeProcess2.Exited -= OnNativeProcessExited;
				nativeProcess2.Kill();
				nativeProcess2.Dispose();
				_nativeProcess = null;
			}
			if (InnerHub != null)
			{
				InnerHub.Disconnected -= OnHubDisconnected;
				InnerHub.Abort(AutomateHubAbortReason.Unknown);
			}
			Status = TargetProcessStatus.Stopped;
		}
	}

	private void OnHubDisconnected()
	{
		InnerHub?.Abort(AutomateHubAbortReason.Unknown);
		_mainThreadDispatcher.ExecuteAsync(delegate
		{
			HandleStopped();
		}, DispatcherPriority.Middle, PublicCancellationToken.None);
	}

	private void OnNativeProcessExited(int _)
	{
		InnerHub?.Abort(AutomateHubAbortReason.ProcessTerminated);
		_mainThreadDispatcher.ExecuteAsync(delegate
		{
			HandleStopped();
		}, DispatcherPriority.Middle, PublicCancellationToken.None);
	}

	protected abstract void OnStart();

	protected static bool IsWindows7()
	{
		Version version = Environment.OSVersion.Version;
		if (Environment.OSVersion.Platform == PlatformID.Win32NT)
		{
			if ((object)version != null && version.Major == 6)
			{
				return version.Minor == 1;
			}
			return false;
		}
		return false;
	}

	public virtual void Dispose()
	{
		Status = TargetProcessStatus.Stopping;
		if (_nativeProcess != null)
		{
			_nativeProcess.Exited -= OnNativeProcessExited;
			_nativeProcess.Kill();
			_nativeProcess.Dispose();
			_nativeProcess = null;
		}
		if (InnerHub != null)
		{
			InnerHub.Disconnected -= OnHubDisconnected;
			InnerHub.Abort(AutomateHubAbortReason.Unknown);
			InnerHub.Dispose();
			InnerHub = null;
		}
	}
}
