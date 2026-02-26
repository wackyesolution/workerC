using System;
using Core.Autofac.Extension;
using cTrader.Console.Infrastructure.Application.CommandLine;
using cTrader.Console.Infrastructure.Application.Exceptions;

namespace cTrader.Console.Infrastructure.Application;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleCurrentAlgoTypeInstanceManagerProvider) })]
internal class ConsoleCurrentAlgoTypeInstanceManagerProvider : IConsoleCurrentAlgoTypeInstanceManagerProvider
{
	private readonly Lazy<IConsoleBacktestingInstanceManager> _consoleBacktestingInstanceLazyManager;

	private readonly IConsoleCommandProvider _consoleCommandProvider;

	private readonly Lazy<IConsoleRealtimeInstanceManager> _consoleRealtimeInstanceLazyManager;

	public ConsoleCurrentAlgoTypeInstanceManagerProvider(IConsoleCommandProvider consoleCommandProvider, Lazy<IConsoleRealtimeInstanceManager> consoleRealtimeInstanceLazyManager, Lazy<IConsoleBacktestingInstanceManager> consoleBacktestingInstanceLazyManager)
	{
		_consoleCommandProvider = consoleCommandProvider;
		_consoleRealtimeInstanceLazyManager = consoleRealtimeInstanceLazyManager;
		_consoleBacktestingInstanceLazyManager = consoleBacktestingInstanceLazyManager;
	}

	public IConsoleAlgoTypeInstanceManager Get()
	{
		return _consoleCommandProvider.Command switch
		{
			ConsoleCommand.Run => _consoleRealtimeInstanceLazyManager.Value, 
			ConsoleCommand.Backtest => _consoleBacktestingInstanceLazyManager.Value, 
			ConsoleCommand.Unknown => throw CreateException(), 
			ConsoleCommand.Periods => throw CreateException(), 
			ConsoleCommand.Accounts => throw CreateException(), 
			ConsoleCommand.Symbols => throw CreateException(), 
			ConsoleCommand.Cloud => _consoleRealtimeInstanceLazyManager.Value, 
			ConsoleCommand.Metadata => throw CreateException(), 
			ConsoleCommand.Version => throw CreateException(), 
			ConsoleCommand.Help => throw CreateException(), 
			ConsoleCommand.Crash => throw CreateException(), 
			_ => throw CreateException(), 
		};
	}

	private static ConsoleInvalidUsageException CreateException()
	{
		return new ConsoleInvalidUsageException("Unsupported command for current state");
	}
}
