using System;
using Common.Domain.Application;
using Core.Autofac.Extension;
using cTrader.Automate.Domain.Providers;
using cTrader.Automate.Domain.Shared.Algo;
using cTrader.Automate.Infrastructure.Common.Providers;
using cTrader.Domain.Application;

namespace cTrader.Automate.Infrastructure.Linux.Providers;

[Export(InstanceKind.Single, new Type[] { typeof(IAutomatePathService) })]
internal class LinuxAutomatePathService : AutomatePathBaseService, IAutomatePathService
{
	protected override string Platform => "linux-x64";

	protected override string NetCoreAlgoHostDirectoryName => "algohost.netcore";

	protected override string NetCoreAlgoHostFileName => "algohost.netcore";

	protected override string ObfuscationDirectoryName => "Obfuscation";

	public string NetCoreAlgoBrokerFilePath
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public string NetFrameworkAlgoHostDirectoryPath
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public string NetFrameworkAlgoHostFilePath
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public LinuxAutomatePathService(IApplication application, IApplicationDirectories applicationDirectories)
		: base(application, applicationDirectories)
	{
	}

	public string GetSolutionDirectoryAbsolutePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	public string GetProjectFileAbsolutePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	public string GetSolutionFileAbsolutePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	public string GetSolutionDirectoryRelativePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	public string GetProjectFileRelativePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	public string GetSolutionFileRelativePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	public string GetMainCsharpFileAbsolutePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	public string GetMainPythonFileAbsolutePath(AlgoId algoId)
	{
		throw new NotImplementedException();
	}

	string IAutomatePathService.get_NetCoreAlgoHostDirectoryPath()
	{
		return base.NetCoreAlgoHostDirectoryPath;
	}

	string IAutomatePathService.get_NetCoreAlgoHostFilePath()
	{
		return base.NetCoreAlgoHostFilePath;
	}

	string IAutomatePathService.get_RobotsDirectoryPath()
	{
		return base.RobotsDirectoryPath;
	}

	string IAutomatePathService.get_IndicatorsDirectoryPath()
	{
		return base.IndicatorsDirectoryPath;
	}

	string IAutomatePathService.get_PluginsDirectoryPath()
	{
		return base.PluginsDirectoryPath;
	}

	string IAutomatePathService.get_LocalStorageDirectoryPath()
	{
		return base.LocalStorageDirectoryPath;
	}

	string IAutomatePathService.get_CloudAlgosDirectoryPath()
	{
		return base.CloudAlgosDirectoryPath;
	}

	string IAutomatePathService.get_ObfuscationDirectoryPath()
	{
		return base.ObfuscationDirectoryPath;
	}

	string IAutomatePathService.GetAlgoFileAbsolutePath(AlgoId algoId)
	{
		return GetAlgoFileAbsolutePath(algoId);
	}

	string IAutomatePathService.GetAlgoFileRelativePath(AlgoId algoId)
	{
		return GetAlgoFileRelativePath(algoId);
	}
}
