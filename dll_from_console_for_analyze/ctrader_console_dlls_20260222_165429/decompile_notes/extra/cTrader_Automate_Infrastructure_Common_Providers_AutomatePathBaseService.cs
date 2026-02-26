using System;
using System.IO;
using Common.Domain.Application;
using cTrader.Automate.Domain.Shared;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Domain.Application;

namespace cTrader.Automate.Infrastructure.Common.Providers;

public abstract class AutomatePathBaseService
{
	protected readonly string EntryDirectory;

	private string? _netCoreAlgoHostDirectoryPath;

	private string? _netCoreAlgoHostFilePath;

	private string? _obfuscationDirectoryPath;

	protected abstract string Platform { get; }

	public string RobotsDirectoryPath { get; }

	public string IndicatorsDirectoryPath { get; }

	public string PluginsDirectoryPath { get; }

	public string LocalStorageDirectoryPath { get; }

	public string CloudAlgosDirectoryPath { get; }

	public string NetCoreAlgoHostDirectoryPath => _netCoreAlgoHostDirectoryPath ?? (_netCoreAlgoHostDirectoryPath = DiscoverNetCoreAlgoHostDirectory(EntryDirectory));

	protected abstract string NetCoreAlgoHostDirectoryName { get; }

	protected abstract string NetCoreAlgoHostFileName { get; }

	protected abstract string ObfuscationDirectoryName { get; }

	public string NetCoreAlgoHostFilePath => _netCoreAlgoHostFilePath ?? (_netCoreAlgoHostFilePath = DiscoverNetCoreAlgoHostFile(NetCoreAlgoHostDirectoryPath, Platform));

	public string ObfuscationDirectoryPath => _obfuscationDirectoryPath ?? (_obfuscationDirectoryPath = DiscoverObfuscationDirectory(EntryDirectory));

	protected AutomatePathBaseService(IApplication application, IApplicationDirectories applicationDirectories)
	{
		EntryDirectory = application.BinDirectory;
		_netCoreAlgoHostDirectoryPath = null;
		_netCoreAlgoHostFilePath = null;
		_obfuscationDirectoryPath = null;
		RobotsDirectoryPath = MakeEndWithSlash(applicationDirectories.CBotsDirectory);
		IndicatorsDirectoryPath = MakeEndWithSlash(applicationDirectories.IndicatorsDirectory);
		PluginsDirectoryPath = MakeEndWithSlash(applicationDirectories.PluginsDirectory);
		LocalStorageDirectoryPath = MakeEndWithSlash(applicationDirectories.LocalStorageDirectory);
		CloudAlgosDirectoryPath = MakeEndWithSlash(applicationDirectories.CloudAlgosDirectory);
	}

	private static string MakeEndWithSlash(string directoryPath)
	{
		return directoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
	}

	protected string GetBasePath(AlgoId algoId)
	{
		return algoId.Kind switch
		{
			AutomateKind.Indicator => IndicatorsDirectoryPath, 
			AutomateKind.Robot => RobotsDirectoryPath, 
			AutomateKind.Plugin => PluginsDirectoryPath, 
			_ => throw new ArgumentOutOfRangeException("algoId"), 
		};
	}

	public string GetAlgoFileAbsolutePath(AlgoId algoId)
	{
		if (algoId == AlgoId.Invalid || algoId.Kind == AutomateKind.BuiltInIndicators)
		{
			throw new ArgumentException("algoId");
		}
		return Path.Combine(GetBasePath(algoId), GetAlgoFileRelativePath(algoId));
	}

	public string GetAlgoFileRelativePath(AlgoId algoId)
	{
		if (algoId == AlgoId.Invalid || algoId.Kind == AutomateKind.BuiltInIndicators)
		{
			throw new ArgumentException("algoId");
		}
		return algoId.Identity + ".algo";
	}

	protected string DiscoverObfuscationDirectory(string entryDirectory)
	{
		string text = Path.Combine(entryDirectory, ObfuscationDirectoryName);
		if (Directory.Exists(text))
		{
			return text;
		}
		throw new DirectoryNotFoundException("Unable to discover obfuscation directory");
	}

	private string DiscoverNetCoreAlgoHostDirectory(string entryDirectory)
	{
		string text = Path.Combine(entryDirectory, NetCoreAlgoHostDirectoryName);
		if (Directory.Exists(text))
		{
			return text;
		}
		throw new DirectoryNotFoundException("Unable to discover host directory for NetCore runtime");
	}

	protected string DiscoverNetCoreAlgoHostFile(string hostDirectory, string platform)
	{
		string text = Path.Combine(hostDirectory, platform, NetCoreAlgoHostFileName);
		if (File.Exists(text))
		{
			return text;
		}
		throw new FileNotFoundException("Unable to discover host file for NetCore runtime");
	}
}
