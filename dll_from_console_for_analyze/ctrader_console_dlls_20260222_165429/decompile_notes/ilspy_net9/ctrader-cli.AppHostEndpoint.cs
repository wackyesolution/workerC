using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Autofac;
using Common.Domain.Promises;
using Core.cBraltar;
using JetBrains.Annotations;
using NLog;
using cTrader.Console.Bootstrapper;
using cTrader.Console.Infrastructure.Application;
using cTrader.Console.Infrastructure.Application.Exceptions;
using cTrader.Console.Infrastructure.Dispatcher;
using cTrader.Console.Infrastructure.StateMachine;

namespace cTrader.Console;

public static class AppHostEndpoint
{
	public static class Launcher
	{
		[STAThread]
		[UsedImplicitly]
		public static int Main(IntPtr arg, int argLength)
		{
			EnvironmentInitializationService.LauncherInitialize();
			return RunApplication();
		}
	}

	public static class Portable
	{
		[STAThread]
		[UsedImplicitly]
		public static int Main()
		{
			EnvironmentInitializationService.PortableInitialize();
			return RunApplication();
		}
	}

	private static IConsoleApplicationLifecycleStateContext? _consoleApplicationLifecycleStateContext;

	private static IDispatcherRemainingFramesExecutor? _dispatcherRemainingFramesExecutor;

	private static ICBraltarPendingReportAwaiter? _cBraltarPendingReportAwaiter;

	private static readonly TimeSpan PendingReportsWaitingTimeout = TimeSpan.FromSeconds(10.0);

	private static int RunApplication()
	{
		try
		{
			using ConsoleBootstrapper consoleBootstrapper = new ConsoleBootstrapper();
			consoleBootstrapper.Run().OnSuccess(delegate(RunPromiseResult result)
			{
				OnContainerCreated(result.Container);
			}).OnCompleted(delegate(RunPromiseResult result)
			{
				result.Container.Dispose();
			});
			return _consoleApplicationLifecycleStateContext.ErrorCode;
		}
		catch (Exception ex)
		{
			IReadOnlyCollection<Exception> exceptions = ExpandException(ex);
			Logger currentClassLogger = LogManager.GetCurrentClassLogger();
			ConsoleInvalidUsageException exception2;
			if (TryFindException<ValidationException>(exceptions, out ValidationException exception))
			{
				System.Console.WriteLine(exception.Message);
			}
			else if (TryFindException<ConsoleInvalidUsageException>(exceptions, out exception2))
			{
				System.Console.WriteLine(exception2.ErrorMessage);
			}
			else
			{
				currentClassLogger.Fatal(ex, ex.Message);
				System.Console.WriteLine(ex);
			}
			_dispatcherRemainingFramesExecutor?.SafeExecuteRemainingFrames();
			System.Console.WriteLine(Task.Factory.Scheduler);
			return 1;
		}
		finally
		{
			_cBraltarPendingReportAwaiter?.WaitForPendingReports(PendingReportsWaitingTimeout);
		}
	}

	private static bool TryFindException<TException>(IEnumerable<Exception> exceptions, [NotNullWhen(true)] out TException? exception) where TException : Exception
	{
		exception = null;
		foreach (Exception exception2 in exceptions)
		{
			if (exception2 is TException ex)
			{
				exception = ex;
				return true;
			}
		}
		return false;
	}

	private static void OnContainerCreated(IComponentContext componentContext)
	{
		componentContext.Resolve<ISynchronizationContextInitializer>().Initialize();
		_dispatcherRemainingFramesExecutor = componentContext.Resolve<IDispatcherRemainingFramesExecutor>();
		_cBraltarPendingReportAwaiter = componentContext.Resolve<ICBraltarPendingReportAwaiter>();
		componentContext.Resolve<IConsoleApplicationLifecycleStateMachine>();
		_consoleApplicationLifecycleStateContext = componentContext.Resolve<IConsoleApplicationLifecycleStateContext>();
		componentContext.Resolve<IConsoleApplicationLoop>().Run();
	}

	private static IReadOnlyCollection<Exception> ExpandException(Exception exception)
	{
		List<Exception> list = new List<Exception>();
		while (true)
		{
			list.Add(exception);
			if (exception.InnerException == null)
			{
				break;
			}
			exception = exception.InnerException;
		}
		return list;
	}
}
