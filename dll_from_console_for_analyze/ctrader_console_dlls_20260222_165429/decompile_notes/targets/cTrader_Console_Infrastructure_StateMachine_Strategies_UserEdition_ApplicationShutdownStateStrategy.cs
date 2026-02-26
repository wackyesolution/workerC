using System;
using Common.Domain.Connection;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application;
using cTrader.Domain.CId;
using cTrader.Domain.Connection;

namespace cTrader.Console.Infrastructure.StateMachine.Strategies.UserEdition;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IConsoleApplicationLifecycleStateStrategy) })]
internal class ApplicationShutdownStateStrategy : ConsoleStateStrategyBase
{
	private readonly IConnectionManager _connectionManager;

	private readonly IConsoleApplicationShutdownService _consoleApplicationShutdownService;

	public override ConsoleApplicationLifecycleState State => ConsoleApplicationLifecycleState.ApplicationShutdown;

	public ApplicationShutdownStateStrategy(IConsoleApplicationShutdownService consoleApplicationShutdownService, IConnectionManager connectionManager)
	{
		_consoleApplicationShutdownService = consoleApplicationShutdownService;
		_connectionManager = connectionManager;
	}

	protected override void DoEnter()
	{
		_connectionManager.CId.Status.Invalidated += OnCidConnectionStatusInvalidated;
		_connectionManager.Server.Disconnected += OnServerConnectionDisconnected;
		_connectionManager.DisconnectFromCId(DisconnectReason.Manually);
		_connectionManager.DisconnectFromServer(DisconnectReason.Manually);
		TryShutdown();
	}

	private void OnCidConnectionStatusInvalidated()
	{
		TryShutdown();
	}

	private void TryShutdown()
	{
		bool num = _connectionManager.CId.Status.Value.State == CIdMainConnectionState.Disconnected;
		if (num)
		{
			_connectionManager.CId.Status.Invalidated -= OnCidConnectionStatusInvalidated;
		}
		bool flag = _connectionManager.Server.State == ConnectionState.Disconnected;
		if (flag)
		{
			_connectionManager.Server.Disconnected -= OnServerConnectionDisconnected;
		}
		if (num && flag)
		{
			_consoleApplicationShutdownService.Shutdown();
		}
	}

	private void OnServerConnectionDisconnected(DisconnectReason _)
	{
		TryShutdown();
	}
}
