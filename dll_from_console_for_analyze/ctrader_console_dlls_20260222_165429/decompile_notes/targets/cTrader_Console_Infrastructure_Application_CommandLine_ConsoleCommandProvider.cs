using System;
using System.Linq;
using Core.Autofac.Extension;

namespace cTrader.Console.Infrastructure.Application.CommandLine;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleCommandProvider) })]
public class ConsoleCommandProvider : IConsoleCommandProvider
{
	private readonly IConsoleCommandLineArgumentsProvider _consoleCommandLineArgumentsProvider;

	public ConsoleCommand Command => GetCommand();

	public ConsoleCommandProvider(IConsoleCommandLineArgumentsProvider consoleCommandLineArgumentsProvider)
	{
		_consoleCommandLineArgumentsProvider = consoleCommandLineArgumentsProvider;
	}

	private ConsoleCommand GetCommand()
	{
		string[] array = _consoleCommandLineArgumentsProvider.Get().ToArray();
		if (array.Length < 2)
		{
			return ConsoleCommand.Help;
		}
		return array[1] switch
		{
			"run" => ConsoleCommand.Run, 
			"periods" => ConsoleCommand.Periods, 
			"accounts" => ConsoleCommand.Accounts, 
			"symbols" => ConsoleCommand.Symbols, 
			"cloud" => ConsoleCommand.Cloud, 
			"metadata" => ConsoleCommand.Metadata, 
			"crash" => ConsoleCommand.Crash, 
			"backtest" => ConsoleCommand.Backtest, 
			"-h" => ConsoleCommand.Help, 
			"--help" => ConsoleCommand.Help, 
			"--version" => ConsoleCommand.Version, 
			"-v" => ConsoleCommand.Version, 
			_ => ConsoleCommand.Unknown, 
		};
	}
}
