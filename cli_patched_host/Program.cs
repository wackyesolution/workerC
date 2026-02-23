using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Optimo.CliPatchedHost;

internal static class Program
{
    private const string CliEntryAssembly = "ctrader-cli.dll";
    private const string CliCommandName = "ctrader-cli";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static int Main(string[] args)
    {
        SessionBacktestHost? sessionHost = null;
        try
        {
            var cfg = ParseArgs(args);
            if (cfg is null)
            {
                PrintUsage();
                return 2;
            }

            var cliDir = Path.GetFullPath(cfg.CliDir);
            var cliEntryPath = Path.Combine(cliDir, CliEntryAssembly);
            if (!File.Exists(cliEntryPath))
            {
                Console.Error.WriteLine($"[ERR] Missing {CliEntryAssembly} at: {cliEntryPath}");
                return 1;
            }

            var commandLineArgsField = typeof(Environment).GetField("s_commandLineArgs", BindingFlags.Static | BindingFlags.NonPublic);
            if (commandLineArgsField is null)
            {
                Console.Error.WriteLine("[ERR] Runtime does not expose Environment.s_commandLineArgs.");
                return 1;
            }

            InstallAssemblyResolver(cliDir);
            var cliAssembly = Assembly.LoadFrom(cliEntryPath);
            var entryPoint = cliAssembly.EntryPoint;
            if (entryPoint is null)
            {
                Console.Error.WriteLine("[ERR] Entry point not found in ctrader-cli.dll");
                return 1;
            }

            sessionHost = new SessionBacktestHost(cliDir, cliAssembly, commandLineArgsField);

            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var response = ExecuteRequest(line, entryPoint, commandLineArgsField, sessionHost);
                Console.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
                Console.Out.Flush();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[ERR] Fatal host error");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            sessionHost?.Dispose();
        }
    }

    private static void InstallAssemblyResolver(string cliDir)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
        {
            try
            {
                var dllName = new AssemblyName(eventArgs.Name).Name + ".dll";
                var directPath = Path.Combine(cliDir, dllName);
                if (File.Exists(directPath))
                {
                    return Assembly.LoadFrom(directPath);
                }

                var algohostPath = Path.Combine(cliDir, "algohost.netcore", dllName);
                if (File.Exists(algohostPath))
                {
                    return Assembly.LoadFrom(algohostPath);
                }
            }
            catch
            {
                // Best-effort resolver.
            }

            return null;
        };
    }

    private static CommandResponse ExecuteRequest(
        string rawJson,
        MethodInfo entryPoint,
        FieldInfo commandLineArgsField,
        SessionBacktestHost sessionHost)
    {
        CommandRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CommandRequest>(rawJson, JsonOptions);
        }
        catch (Exception ex)
        {
            return new CommandResponse
            {
                Id = string.Empty,
                Ok = false,
                ExitCode = 1,
                Error = "Invalid JSON request: " + ex.Message,
            };
        }

        if (request is null)
        {
            return new CommandResponse
            {
                Id = string.Empty,
                Ok = false,
                ExitCode = 1,
                Error = "Empty JSON request",
            };
        }

        var response = new CommandResponse
        {
            Id = request.Id ?? string.Empty,
            Ok = false,
            ExitCode = 1,
            Stdout = string.Empty,
            Stderr = string.Empty,
        };

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            response.Error = "Request field 'id' is required.";
            return response;
        }

        var args = request.Args ?? Array.Empty<string>();
        if (args.Length == 0)
        {
            response.Ok = true;
            response.ExitCode = 0;
            response.ElapsedMs = 0;
            return response;
        }

        var stdoutWriter = new StringWriter(CultureInfo.InvariantCulture);
        var stderrWriter = new StringWriter(CultureInfo.InvariantCulture);
        var oldOut = Console.Out;
        var oldErr = Console.Error;

        var sw = Stopwatch.StartNew();
        try
        {
            Console.SetOut(stdoutWriter);
            Console.SetError(stderrWriter);

            if (string.Equals(args[0], "backtest", StringComparison.OrdinalIgnoreCase))
            {
                var sessionResult = sessionHost.ExecuteBacktest(args);
                response.ExitCode = sessionResult.ExitCode;
                response.Ok = sessionResult.ExitCode == 0 && string.IsNullOrWhiteSpace(sessionResult.Error);
                response.Error = sessionResult.Error;
            }
            else
            {
                var cliArgs = new string[args.Length + 1];
                cliArgs[0] = CliCommandName;
                Array.Copy(args, 0, cliArgs, 1, args.Length);
                commandLineArgsField.SetValue(null, cliArgs);

                var result = InvokeEntryPoint(entryPoint, cliArgs);
                response.ExitCode = result;
                response.Ok = result == 0;
            }
        }
        catch (TargetInvocationException ex)
        {
            response.Error = (ex.InnerException ?? ex).ToString();
        }
        catch (Exception ex)
        {
            response.Error = ex.ToString();
        }
        finally
        {
            Console.Out.Flush();
            Console.Error.Flush();
            response.Stdout = stdoutWriter.ToString();
            response.Stderr = stderrWriter.ToString();
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
            sw.Stop();
            response.ElapsedMs = (long)sw.Elapsed.TotalMilliseconds;
        }

        return response;
    }

    private static int InvokeEntryPoint(MethodInfo entryPoint, string[] cliArgs)
    {
        object? result;
        if (entryPoint.GetParameters().Length == 0)
        {
            result = entryPoint.Invoke(null, null);
        }
        else
        {
            result = entryPoint.Invoke(null, new object[] { cliArgs });
        }

        if (result is null)
        {
            return 0;
        }

        if (result is int exitCode)
        {
            return exitCode;
        }

        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
            var t = task.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var val = t.GetProperty("Result")?.GetValue(task);
                if (val is int asyncExitCode)
                {
                    return asyncExitCode;
                }
            }
            return 0;
        }

        return 0;
    }

    private static Config? ParseArgs(string[] args)
    {
        string? cliDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--cli-dir":
                    if (i + 1 >= args.Length)
                    {
                        return null;
                    }
                    cliDir = args[++i];
                    break;
                case "-h":
                case "--help":
                    return null;
                default:
                    Console.Error.WriteLine($"[ERR] Unknown argument: {arg}");
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(cliDir))
        {
            return null;
        }

        return new Config
        {
            CliDir = cliDir,
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet Optimo.CliPatchedHost.dll --cli-dir <ctrader_cli_directory>");
        Console.WriteLine();
        Console.WriteLine("Protocol:");
        Console.WriteLine("  stdin:  one JSON line per request:  {\"id\":\"1\",\"args\":[\"backtest\",\"...\"]}");
        Console.WriteLine("  stdout: one JSON line per response with fields id/ok/exitCode/stdout/stderr/error/elapsedMs");
    }

    private sealed class Config
    {
        public string CliDir { get; init; } = string.Empty;
    }

    private sealed class CommandRequest
    {
        public string? Id { get; init; }

        public string[]? Args { get; init; }
    }

    private sealed class CommandResponse
    {
        public string Id { get; init; } = string.Empty;

        public bool Ok { get; set; }

        public int ExitCode { get; set; }

        public string Stdout { get; set; } = string.Empty;

        public string Stderr { get; set; } = string.Empty;

        public string? Error { get; set; }

        public long ElapsedMs { get; set; }
    }

    private sealed class BacktestSessionResult
    {
        public int ExitCode { get; init; }

        public string? Error { get; init; }

        public bool Fatal { get; init; }
    }

    private sealed class SessionBacktestHost : IDisposable
    {
        private const string StateRobotDisposing = "RobotDisposing";
        private const string StateApplicationShutdown = "ApplicationShutdown";

        private readonly object _sync = new();
        private readonly string _cliDir;
        private readonly Assembly _cliAssembly;
        private readonly FieldInfo _commandLineArgsField;

        private readonly Type _consoleBootstrapperType;
        private readonly MethodInfo _consoleBootstrapperRunMethod;
        private readonly MethodInfo _launcherInitializeMethod;
        private readonly Type _syncContextInitializerType;
        private readonly MethodInfo _syncContextInitializeMethod;

        private readonly Type _runPromiseResultType;
        private readonly PropertyInfo _runPromiseResultContainerProperty;

        private readonly Type _resolutionExtensionsType;
        private readonly MethodInfo _resolveByTypeMethod;

        private readonly Type _consoleLoopType;
        private readonly Type _transitionType;
        private readonly Type _stateMachineType;
        private readonly Type _stateContextType;
        private readonly Type _shutdownServiceType;
        private readonly Type _stateEnumType;

        private readonly object _appLoopStartedStateValue;

        private object? _bootstrapper;
        private object? _container;

        private object? _consoleLoop;
        private MethodInfo? _consoleLoopRunMethod;
        private object? _syncContextInitializer;

        private object? _stateTransition;
        private MethodInfo? _moveToMethod;

        private object? _stateMachine;
        private PropertyInfo? _stateProperty;

        private object? _stateContext;
        private PropertyInfo? _errorCodeProperty;
        private PropertyInfo? _unhandledExceptionProperty;

        private object? _shutdownService;
        private MethodInfo? _shutdownMethod;

        private Thread? _loopThread;
        private Exception? _loopException;
        private bool _started;

        public SessionBacktestHost(string cliDir, Assembly cliAssembly, FieldInfo commandLineArgsField)
        {
            _cliDir = cliDir;
            _cliAssembly = cliAssembly;
            _commandLineArgsField = commandLineArgsField;

            _consoleBootstrapperType = RequireType("cTrader.Console.Bootstrapper.ConsoleBootstrapper");
            _consoleBootstrapperRunMethod = _consoleBootstrapperType.GetMethod("Run", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("ConsoleBootstrapper.Run not found.");
            var envInitType = RequireType("cTrader.EnvironmentInitializationService");
            _launcherInitializeMethod = envInitType.GetMethod("LauncherInitialize", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("EnvironmentInitializationService.LauncherInitialize not found.");
            _syncContextInitializerType = RequireType("cTrader.Console.Bootstrapper.ISynchronizationContextInitializer");
            _syncContextInitializeMethod = _syncContextInitializerType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("ISynchronizationContextInitializer.Initialize not found.");

            _runPromiseResultType = RequireType("cTrader.Console.Bootstrapper.RunPromiseResult");
            _runPromiseResultContainerProperty = _runPromiseResultType.GetProperty("Container", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("RunPromiseResult.Container not found.");

            _resolutionExtensionsType = RequireType("Autofac.ResolutionExtensions");
            _resolveByTypeMethod = _resolutionExtensionsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "Resolve", StringComparison.Ordinal))
                    {
                        return false;
                    }
                    var p = m.GetParameters();
                    return p.Length == 2 && p[1].ParameterType == typeof(Type);
                })
                ?? throw new InvalidOperationException("Autofac.ResolutionExtensions.Resolve(context, type) not found.");

            _consoleLoopType = RequireType("cTrader.Console.Infrastructure.Application.IConsoleApplicationLoop");
            _transitionType = RequireType("cTrader.Console.Infrastructure.StateMachine.IConsoleApplicationLifecycleStateTransition");
            _stateMachineType = RequireType("cTrader.Console.Infrastructure.StateMachine.IConsoleApplicationLifecycleStateMachine");
            _stateContextType = RequireType("cTrader.Console.Infrastructure.StateMachine.IConsoleApplicationLifecycleStateContext");
            _shutdownServiceType = RequireType("cTrader.Console.Infrastructure.Application.IConsoleApplicationShutdownService");
            _stateEnumType = RequireType("cTrader.Console.Infrastructure.StateMachine.ConsoleApplicationLifecycleState");

            _appLoopStartedStateValue = Enum.Parse(_stateEnumType, "ApplicationLoopStarted", ignoreCase: false);
        }

        public BacktestSessionResult ExecuteBacktest(string[] args)
        {
            lock (_sync)
            {
                try
                {
                    if (!_started)
                    {
                        StartSessionWithFirstCommand(args);
                    }
                    else
                    {
                        TriggerNextBacktestCommand(args);
                    }

                    var result = WaitForBacktestCompletion(TimeSpan.FromHours(24));
                    if (result.Fatal)
                    {
                        TeardownSession();
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    TeardownSession();
                    return new BacktestSessionResult
                    {
                        ExitCode = 1,
                        Error = "session_backtest_error: " + ex,
                        Fatal = true,
                    };
                }
            }
        }

        private void StartSessionWithFirstCommand(string[] firstArgs)
        {
            var runtimeDirectory = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
            Environment.SetEnvironmentVariable("__CT_PRODUCT_PATH", _cliDir);
            Environment.SetEnvironmentVariable("__CT_DOTNET_PATH", runtimeDirectory);
            _launcherInitializeMethod.Invoke(null, null);
            Directory.SetCurrentDirectory(_cliDir);

            SetCommandLineArgs(firstArgs);

            _bootstrapper = Activator.CreateInstance(_consoleBootstrapperType)
                ?? throw new InvalidOperationException("Cannot create ConsoleBootstrapper.");

            var runPromise = _consoleBootstrapperRunMethod.Invoke(_bootstrapper, null)
                ?? throw new InvalidOperationException("ConsoleBootstrapper.Run returned null.");

            var runResult = WaitForPromiseResult(runPromise, TimeSpan.FromSeconds(10));
            _container = _runPromiseResultContainerProperty.GetValue(runResult)
                ?? throw new InvalidOperationException("RunPromiseResult.Container is null.");

            _consoleLoop = ResolveFromContainer(_consoleLoopType);
            _consoleLoopRunMethod = _consoleLoopType.GetMethod("Run", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("IConsoleApplicationLoop.Run not found.");
            _syncContextInitializer = ResolveFromContainer(_syncContextInitializerType);

            _stateTransition = ResolveFromContainer(_transitionType);
            _moveToMethod = _transitionType.GetMethod("MoveTo", BindingFlags.Instance | BindingFlags.Public, null, new[] { _stateEnumType }, null)
                ?? throw new InvalidOperationException("StateTransition.MoveTo not found.");

            _stateMachine = ResolveFromContainer(_stateMachineType);
            _stateProperty = _stateMachineType.GetProperty("State", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("StateMachine.State not found.");

            _stateContext = ResolveFromContainer(_stateContextType);
            _errorCodeProperty = _stateContextType.GetProperty("ErrorCode", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("StateContext.ErrorCode not found.");
            _unhandledExceptionProperty = _stateContextType.GetProperty("UnhandledException", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("StateContext.UnhandledException not found.");

            _shutdownService = ResolveFromContainer(_shutdownServiceType);
            _shutdownMethod = _shutdownServiceType.GetMethod("Shutdown", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("ShutdownService.Shutdown not found.");

            _loopException = null;
            _loopThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "ctrader-cli-session-loop",
            };
            _loopThread.Start();

            _started = true;
        }

        private void TriggerNextBacktestCommand(string[] args)
        {
            EnsureStarted();
            SetErrorCode(0);
            SetUnhandledException(null);
            SetCommandLineArgs(args);
            _moveToMethod!.Invoke(_stateTransition, new[] { _appLoopStartedStateValue });
        }

        private BacktestSessionResult WaitForBacktestCompletion(TimeSpan timeout)
        {
            EnsureStarted();

            var deadline = DateTime.UtcNow + timeout;
            var seenNonIdleState = false;

            while (DateTime.UtcNow < deadline)
            {
                if (_loopException is not null)
                {
                    return new BacktestSessionResult
                    {
                        ExitCode = 1,
                        Error = "session_loop_error: " + _loopException,
                        Fatal = true,
                    };
                }

                var unhandled = GetUnhandledException();
                if (unhandled is not null)
                {
                    return new BacktestSessionResult
                    {
                        ExitCode = 1,
                        Error = "cli_unhandled_exception: " + unhandled,
                        Fatal = true,
                    };
                }

                var state = GetCurrentStateName();
                if (!string.Equals(state, StateRobotDisposing, StringComparison.Ordinal))
                {
                    seenNonIdleState = true;
                }

                if (string.Equals(state, StateApplicationShutdown, StringComparison.Ordinal))
                {
                    return new BacktestSessionResult
                    {
                        ExitCode = 1,
                        Error = "cli_state_reached_shutdown",
                        Fatal = true,
                    };
                }

                if (seenNonIdleState && string.Equals(state, StateRobotDisposing, StringComparison.Ordinal))
                {
                    var errorCode = GetErrorCode();
                    return new BacktestSessionResult
                    {
                        ExitCode = errorCode,
                        Error = errorCode == 0 ? null : $"backtest_failed_error_code={errorCode}",
                        Fatal = false,
                    };
                }

                Thread.Sleep(50);
            }

            return new BacktestSessionResult
            {
                ExitCode = 1,
                Error = "backtest_timeout_waiting_for_robot_disposing",
                Fatal = true,
            };
        }

        private void RunLoop()
        {
            try
            {
                _syncContextInitializeMethod.Invoke(_syncContextInitializer, null);
                _consoleLoopRunMethod!.Invoke(_consoleLoop, null);
            }
            catch (TargetInvocationException ex)
            {
                _loopException = ex.InnerException ?? ex;
            }
            catch (Exception ex)
            {
                _loopException = ex;
            }
        }

        private object WaitForPromiseResult(object promise, TimeSpan timeout)
        {
            var resultProperty = promise.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Promise.Result (non-public) not found.");

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var value = resultProperty.GetValue(promise);
                if (value is not null)
                {
                    return value;
                }
                Thread.Sleep(10);
            }

            throw new TimeoutException("Timed out waiting for bootstrap promise completion.");
        }

        private object ResolveFromContainer(Type serviceType)
        {
            if (_container is null)
            {
                throw new InvalidOperationException("Container not initialized.");
            }
            return _resolveByTypeMethod.Invoke(null, new[] { _container, (object)serviceType })
                ?? throw new InvalidOperationException($"Resolve returned null for {serviceType.FullName}.");
        }

        private void SetCommandLineArgs(string[] args)
        {
            var cliArgs = new string[args.Length + 1];
            cliArgs[0] = CliCommandName;
            Array.Copy(args, 0, cliArgs, 1, args.Length);
            _commandLineArgsField.SetValue(null, cliArgs);
        }

        private string GetCurrentStateName()
        {
            EnsureStarted();
            return _stateProperty!.GetValue(_stateMachine)?.ToString() ?? string.Empty;
        }

        private int GetErrorCode()
        {
            EnsureStarted();
            return Convert.ToInt32(_errorCodeProperty!.GetValue(_stateContext), CultureInfo.InvariantCulture);
        }

        private void SetErrorCode(int value)
        {
            EnsureStarted();
            _errorCodeProperty!.SetValue(_stateContext, value);
        }

        private Exception? GetUnhandledException()
        {
            EnsureStarted();
            return _unhandledExceptionProperty!.GetValue(_stateContext) as Exception;
        }

        private void SetUnhandledException(Exception? value)
        {
            EnsureStarted();
            _unhandledExceptionProperty!.SetValue(_stateContext, value);
        }

        private void EnsureStarted()
        {
            if (!_started)
            {
                throw new InvalidOperationException("Session is not started.");
            }
        }

        private Type RequireType(string fullName)
        {
            var direct = _cliAssembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (direct is not null)
            {
                return direct;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (t is not null)
                {
                    return t;
                }
            }

            var fallbackPaths = new[]
            {
                Path.Combine(_cliDir, "cTrader.Console.Infrastructure.dll"),
                Path.Combine(_cliDir, "cTrader.Console.Infrastructure.Linux.dll"),
                Path.Combine(_cliDir, "Autofac.dll"),
            };

            foreach (var path in fallbackPaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                try
                {
                    var asm = Assembly.LoadFrom(path);
                    var t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                    if (t is not null)
                    {
                        return t;
                    }
                }
                catch
                {
                    // Best effort load.
                }
            }

            throw new InvalidOperationException($"Type not found: {fullName}");
        }

        private void TeardownSession()
        {
            try
            {
                if (_shutdownService is not null && _shutdownMethod is not null)
                {
                    try
                    {
                        _shutdownMethod.Invoke(_shutdownService, null);
                    }
                    catch
                    {
                        // Best effort shutdown.
                    }
                }

                if (_loopThread is not null && _loopThread.IsAlive)
                {
                    _loopThread.Join(2000);
                }
            }
            finally
            {
                try
                {
                    if (_container is IDisposable disposableContainer)
                    {
                        disposableContainer.Dispose();
                    }
                }
                catch
                {
                    // Best effort cleanup.
                }

                try
                {
                    if (_bootstrapper is IDisposable disposableBootstrapper)
                    {
                        disposableBootstrapper.Dispose();
                    }
                }
                catch
                {
                    // Best effort cleanup.
                }

                _bootstrapper = null;
                _container = null;
                _consoleLoop = null;
                _syncContextInitializer = null;
                _stateTransition = null;
                _stateMachine = null;
                _stateContext = null;
                _shutdownService = null;
                _loopThread = null;
                _loopException = null;
                _started = false;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                TeardownSession();
            }
        }
    }
}
