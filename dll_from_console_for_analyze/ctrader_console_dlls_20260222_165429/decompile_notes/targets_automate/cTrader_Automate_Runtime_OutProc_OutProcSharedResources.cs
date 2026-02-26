using System;
using System.Collections.Generic;
using Core.Autofac.Extension;
using cTrader.Automate.Domain.Shared.Backtesting.Visual.State;
using cTrader.Automate.Infrastructure.Common.Sessions;
using cTrader.Automate.Infrastructure.Common.TargetProcess;
using cTrader.Automate.Runtime.OutProc.FileStorage;

namespace cTrader.Automate.Runtime.OutProc;

[Export(InstanceKind.PerDependency, new Type[] { typeof(IOutProcSharedResources) })]
public class OutProcSharedResources : IOutProcSharedResources, IAutomateSharedResources, IDisposable
{
	private readonly IOutProcFileStorage _fileStorage;

	private readonly IOutProcFileStorageLifetimeController _fileStorageLifetimeController;

	private readonly Dictionary<IntPtr, IntPtr> _sharedHandles;

	private readonly ISystemCalls _systemCalls;

	private readonly ITargetProcess _targetProcess;

	public string WorkingDirectory => _fileStorage.RootPath;

	public OutProcSharedResources(ITargetProcess targetProcess, IOutProcFileStorageLifetimeController fileStorageLifetimeController, ISystemCalls systemCalls)
	{
		_targetProcess = targetProcess;
		_systemCalls = systemCalls;
		_fileStorageLifetimeController = fileStorageLifetimeController;
		_fileStorage = _fileStorageLifetimeController.CreateNew();
		_sharedHandles = new Dictionary<IntPtr, IntPtr>();
	}

	public IntPtr ShareHandle(IntPtr localHandle)
	{
		if (_targetProcess.Handle == IntPtr.Zero)
		{
			throw new InvalidOperationException();
		}
		if (!_sharedHandles.TryGetValue(localHandle, out var value))
		{
			_systemCalls.SetInheritable(localHandle);
			value = _systemCalls.DuplicateHandle(localHandle, _targetProcess.Handle);
			_sharedHandles.Add(localHandle, value);
		}
		return value;
	}

	public void InvalidateSharedHandle(IntPtr localHandle)
	{
		_sharedHandles.Remove(localHandle);
	}

	public void CleanupWorkingDirectory()
	{
		_fileStorageLifetimeController.CleanupStorage(_fileStorage.StorageId);
	}

	public void Dispose()
	{
		_fileStorageLifetimeController.CleanupStorage(_fileStorage.StorageId);
	}
}
