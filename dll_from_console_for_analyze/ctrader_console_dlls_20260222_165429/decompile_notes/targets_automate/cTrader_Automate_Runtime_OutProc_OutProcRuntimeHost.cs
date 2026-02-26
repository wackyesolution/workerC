using System;
using System.Collections.Generic;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Factories;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Domain.Shared.Messaging.Hub;
using cTrader.Automate.Domain.TargetProcess;
using cTrader.Automate.Domain.Types;
using cTrader.Automate.Infrastructure.Common.TargetProcess;
using cTrader.Automate.Sessions;

namespace cTrader.Automate.Runtime.OutProc;

[Export(InstanceKind.PerDependency, new Type[] { typeof(OutProcRuntimeHost) })]
public class OutProcRuntimeHost : IOutProcRuntimeHost, IDisposable
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("OutProcRuntimeHost");

	private readonly IAutomateSessionFactory _sessionFactory;

	private readonly HashSet<IAutomateSession<AutomateTargetOptions, AlgoResult>> _sessions;

	private readonly ITargetProcess _targetProcess;

	private readonly IOutProcDiagnosticsHandler _diagnosticsHandler;

	private readonly IOutProcSharedResources _sharedResources;

	private readonly IAutomateProcessNotRespondingHandler _processNotRespondingHandler;

	private bool _isStopped;

	private int _numberOfStoppedSessions;

	public AlgoId AlgoId { get; }

	public IAutomateTypeInfo Type { get; }

	public int SessionCount => _sessions.Count;

	public bool IsEmpty => _sessions.Count == 0;

	public bool IsAlive
	{
		get
		{
			if (!_isStopped)
			{
				return (object)_targetProcess.Hub?.AbortReasonInfo == null;
			}
			return false;
		}
	}

	public bool IsCrashed
	{
		get
		{
			IAutomateHub? hub = _targetProcess.Hub;
			if (hub == null)
			{
				return false;
			}
			return hub.AbortReasonInfo?.AbortReason == AutomateHubAbortReason.ProcessTerminated;
		}
	}

	public event Action<IOutProcRuntimeHost>? Empty;

	public event Action<IOutProcRuntimeHost>? Stopped;

	public OutProcRuntimeHost(IAutomateType automateType, ITargetProcess targetProcess, IOutProcDiagnosticsHandler diagnosticsHandler, IAutomateProcessNotRespondingHandler processNotRespondingHandler, IAutomateSessionFactory sessionFactory, IFactory<ITargetProcess, IOutProcSharedResources> sharedResourcesFactory)
	{
		_targetProcess = targetProcess;
		_diagnosticsHandler = diagnosticsHandler;
		_processNotRespondingHandler = processNotRespondingHandler;
		_sessionFactory = sessionFactory;
		AlgoId = automateType.Id;
		Type = automateType.GetInfo();
		_sharedResources = sharedResourcesFactory.Create(_targetProcess);
		_sessions = new HashSet<IAutomateSession<AutomateTargetOptions, AlgoResult>>();
		_targetProcess.StatusChanged += OnTargetProcessStatusChanged;
		_targetProcess.Start(_sharedResources.WorkingDirectory);
		if (_targetProcess.Hub == null)
		{
			Logger.Error("Target process hub is null");
		}
		_targetProcess.Hub?.Register(_diagnosticsHandler);
	}

	public IAutomateSession<AutomateTargetOptions, AlgoResult> CreateSession()
	{
		IAutomateHub hub = _targetProcess.Hub ?? throw new InvalidOperationException();
		IAutomateSession<AutomateTargetOptions, AlgoResult> automateSession = _sessionFactory.Create<AutomateTargetOptions, AlgoResult>(hub, _sharedResources);
		_sessions.Add(automateSession);
		automateSession.Stopped += OnSessionStopped;
		automateSession.Disposed += OnSessionDisposed;
		return automateSession;
	}

	public void Stop()
	{
		if (!IsEmpty)
		{
			throw new InvalidOperationException();
		}
		_targetProcess.Stop();
	}

	private void OnTargetProcessStatusChanged(ITargetProcess _)
	{
		if (_targetProcess.Status == TargetProcessStatus.Stopped)
		{
			if (_isStopped)
			{
				throw new InvalidOperationException();
			}
			_isStopped = true;
			AbortReasonInfo abortReasonInfo = _targetProcess.Hub?.AbortReasonInfo;
			this.Stopped?.Invoke(this);
			_targetProcess.Hub?.Unregister(_diagnosticsHandler);
			_sharedResources.CleanupWorkingDirectory();
			if ((object)abortReasonInfo != null && abortReasonInfo.AbortReason == AutomateHubAbortReason.ProcessNotResponding)
			{
				_processNotRespondingHandler.Handle(AlgoId, _numberOfStoppedSessions);
				_numberOfStoppedSessions = 0;
			}
		}
	}

	private void OnSessionStopped(IAutomateSession<AutomateTargetOptions, AlgoResult> session, AlgoResult? _)
	{
		session.Stopped -= OnSessionStopped;
		session.Disposed -= OnSessionDisposed;
		session.Dispose();
		_sessions.Remove(session);
		checked
		{
			if (!IsAlive)
			{
				_numberOfStoppedSessions++;
			}
			if (IsEmpty)
			{
				this.Empty?.Invoke(this);
			}
		}
	}

	private void OnSessionDisposed(IAutomateSession<AutomateTargetOptions, AlgoResult> automateSession)
	{
		_sessions.Remove(automateSession);
		checked
		{
			if (!IsAlive)
			{
				_numberOfStoppedSessions++;
			}
			if (IsEmpty)
			{
				this.Empty?.Invoke(this);
			}
		}
	}

	public void Dispose()
	{
		_targetProcess.Hub?.Unregister(_diagnosticsHandler);
		_targetProcess.StatusChanged -= OnTargetProcessStatusChanged;
		_targetProcess.Dispose();
		foreach (IAutomateSession<AutomateTargetOptions, AlgoResult> session in _sessions)
		{
			session.Stopped -= OnSessionStopped;
			session.Disposed -= OnSessionDisposed;
			session.Dispose();
		}
		_sessions.Clear();
		_sharedResources.Dispose();
	}
}
