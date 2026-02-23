using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Optimo.CliIlPatcher;

internal static class Program
{
    private const string InfraDllName = "cTrader.Console.Infrastructure.dll";
    private const string InfraLinuxDllName = "cTrader.Console.Infrastructure.Linux.dll";

    private const string RobotTypeName =
        "cTrader.Console.Infrastructure.StateMachine.Strategies.RobotDisposingStateStrategy";

    private const string ProviderTypeName =
        "cTrader.Console.Infrastructure.Application.IConsoleCurrentAlgoTypeInstanceManagerProvider";

    private const string ManagerTypeName =
        "cTrader.Console.Infrastructure.Application.IConsoleAlgoTypeInstanceManager";

    private const string CurrentCommandParametersProviderTypeName =
        "cTrader.Console.Infrastructure.Application.Services.CurrentCommandParametersProvider";

    private const string CurrentCommandParametersBuilderProviderTypeName =
        "cTrader.Console.Infrastructure.Application.CommandLine.Builders.IConsoleCurrentCommandParametersBuilderProvider";

    private const string CommandParametersBuilderTypeName =
        "cTrader.Console.Infrastructure.Application.CommandLine.Builders.IConsoleCommandParametersBuilder";

    private const string ConsoleCommandParametersTypeName =
        "cTrader.Console.Infrastructure.Application.CommandLine.Builders.IConsoleCommandParameters";

    private const string CommandParametersBuilderBaseTypeName =
        "cTrader.Console.Infrastructure.Application.CommandLine.Builders.CommandParametersBuilderBase";

    private const string LinuxFullAccessValidatorTypeName =
        "cTrader.Console.Infrastructure.Linux.Application.CommandLine.Validators.LinuxFullAccessValidator";

    private static int Main(string[] args)
    {
        try
        {
            var cfg = ParseArgs(args);
            if (cfg is null)
            {
                PrintUsage();
                return 2;
            }

            var appDir = Path.GetFullPath(cfg.AppDir);
            var infraDll = Path.Combine(appDir, InfraDllName);
            if (!File.Exists(infraDll))
            {
                Console.Error.WriteLine($"[ERR] Missing file: {infraDll}");
                return 1;
            }

            var infraLinuxDll = Path.Combine(appDir, InfraLinuxDllName);
            if (!File.Exists(infraLinuxDll))
            {
                Console.Error.WriteLine($"[ERR] Missing file: {infraLinuxDll}");
                return 1;
            }

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(appDir);
            resolver.AddSearchDirectory(Path.Combine(appDir, "algohost.netcore"));

            var asm = AssemblyDefinition.ReadAssembly(infraDll, new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadWrite = false,
                InMemory = true,
                ReadingMode = ReadingMode.Immediate,
            });

            var actions = new List<string>();
            var changed = false;

            if (PatchRobotDisposingState(asm, out var robotAction))
            {
                actions.Add(robotAction);
                changed = true;
            }

            if (PatchCurrentCommandParametersProvider(asm, out var currentParamsAction))
            {
                actions.Add(currentParamsAction);
                changed = true;
            }

            if (PatchCommandParametersBuilderBase(asm, out var builderBaseAction))
            {
                actions.Add(builderBaseAction);
                changed = true;
            }

            if (changed)
            {
                BackupFile(infraDll);
                WriteAssemblyReplacing(infraDll, asm);
            }
            else
            {
                asm.Dispose();
            }

            var linuxAsm = AssemblyDefinition.ReadAssembly(infraLinuxDll, new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadWrite = false,
                InMemory = true,
                ReadingMode = ReadingMode.Immediate,
            });
            if (PatchLinuxFullAccessValidator(linuxAsm, out var linuxAction))
            {
                actions.Add(linuxAction);
                changed = true;
                BackupFile(infraLinuxDll);
                WriteAssemblyReplacing(infraLinuxDll, linuxAsm);
            }
            else
            {
                linuxAsm.Dispose();
            }

            if (!changed)
            {
                Console.WriteLine("[WARN] No patch applied (already patched or signature mismatch).");
                return 1;
            }

            Console.WriteLine("[OK] Patch applied:");
            foreach (var action in actions)
            {
                Console.WriteLine($" - {action}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[ERR] Patching failed:");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static bool PatchRobotDisposingState(AssemblyDefinition asm, out string action)
    {
        action = string.Empty;

        var module = asm.MainModule;
        var robotType = module.Types.FirstOrDefault(t => t.FullName == RobotTypeName);
        if (robotType is null)
        {
            Console.Error.WriteLine($"[WARN] Type not found: {RobotTypeName}");
            return false;
        }

        var doEnter = robotType.Methods.FirstOrDefault(m => m.Name == "DoEnter" && m.Parameters.Count == 0);
        if (doEnter is null || !doEnter.HasBody)
        {
            Console.Error.WriteLine("[WARN] Method not found or has no body: RobotDisposingStateStrategy.DoEnter");
            return false;
        }

        var providerIface = module.Types.FirstOrDefault(t => t.FullName == ProviderTypeName);
        var managerIface = module.Types.FirstOrDefault(t => t.FullName == ManagerTypeName);
        if (providerIface is null || managerIface is null)
        {
            Console.Error.WriteLine("[WARN] One or more required interface/enum types not found.");
            return false;
        }

        var providerField = robotType.Fields.FirstOrDefault(f => f.FieldType.FullName == ProviderTypeName);
        if (providerField is null)
        {
            Console.Error.WriteLine("[WARN] Required fields not found in RobotDisposingStateStrategy.");
            return false;
        }

        var providerGet = providerIface.Methods.FirstOrDefault(m => m.Name == "Get" && m.Parameters.Count == 0);
        var managerDispose = managerIface.Methods.FirstOrDefault(m => m.Name == "DisposeInstance" && m.Parameters.Count == 0);
        if (providerGet is null || managerDispose is null)
        {
            Console.Error.WriteLine("[WARN] Required methods/state not found.");
            return false;
        }

        doEnter.Body.ExceptionHandlers.Clear();
        doEnter.Body.Variables.Clear();
        doEnter.Body.Instructions.Clear();
        doEnter.Body.InitLocals = false;

        var il = doEnter.Body.GetILProcessor();
        var providerGetRef = module.ImportReference(providerGet);
        var managerDisposeRef = module.ImportReference(managerDispose);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, providerField));
        il.Append(il.Create(OpCodes.Callvirt, providerGetRef));
        il.Append(il.Create(OpCodes.Callvirt, managerDisposeRef));

        il.Append(il.Create(OpCodes.Ret));

        action =
            "RobotDisposingStateStrategy.DoEnter => DisposeInstance(); (stay in RobotDisposing until explicit MoveTo).";

        return true;
    }

    private static bool PatchCurrentCommandParametersProvider(AssemblyDefinition asm, out string action)
    {
        action = string.Empty;

        var module = asm.MainModule;
        var targetType = module.Types.FirstOrDefault(t => t.FullName == CurrentCommandParametersProviderTypeName);
        if (targetType is null)
        {
            Console.Error.WriteLine($"[WARN] Type not found: {CurrentCommandParametersProviderTypeName}");
            return false;
        }

        var getter = targetType.Methods.FirstOrDefault(m => m.Name == "get_Parameters" && m.Parameters.Count == 0);
        if (getter is null || !getter.HasBody)
        {
            Console.Error.WriteLine("[WARN] Method not found or has no body: CurrentCommandParametersProvider.get_Parameters");
            return false;
        }

        var builderProviderField = targetType.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, "_consoleCurrentCommandParametersBuilderProvider", StringComparison.Ordinal));
        var cachedParametersField = targetType.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, "_parameters", StringComparison.Ordinal));
        if (builderProviderField is null || cachedParametersField is null)
        {
            Console.Error.WriteLine("[WARN] Required fields not found in CurrentCommandParametersProvider.");
            return false;
        }

        var builderProviderType = module.Types.FirstOrDefault(t => t.FullName == CurrentCommandParametersBuilderProviderTypeName);
        var commandBuilderType = module.Types.FirstOrDefault(t => t.FullName == CommandParametersBuilderTypeName);
        var commandParametersType = module.Types.FirstOrDefault(t => t.FullName == ConsoleCommandParametersTypeName);
        if (builderProviderType is null || commandBuilderType is null || commandParametersType is null)
        {
            Console.Error.WriteLine("[WARN] Required interface types not found for CurrentCommandParametersProvider patch.");
            return false;
        }

        var getBuilder = builderProviderType.Methods.FirstOrDefault(m => m.Name == "get_Builder" && m.Parameters.Count == 0);
        var build = commandBuilderType.Methods.FirstOrDefault(m => m.Name == "Build" && m.Parameters.Count == 0);
        var getMergedParameters = commandParametersType.Methods.FirstOrDefault(m => m.Name == "get_MergedParameters" && m.Parameters.Count == 0);
        if (getBuilder is null || build is null || getMergedParameters is null)
        {
            Console.Error.WriteLine("[WARN] Required methods not found for CurrentCommandParametersProvider patch.");
            return false;
        }

        getter.Body.ExceptionHandlers.Clear();
        getter.Body.Variables.Clear();
        getter.Body.Instructions.Clear();
        getter.Body.InitLocals = false;

        var il = getter.Body.GetILProcessor();
        var getBuilderRef = module.ImportReference(getBuilder);
        var buildRef = module.ImportReference(build);
        var getMergedParametersRef = module.ImportReference(getMergedParameters);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, builderProviderField));
        il.Append(il.Create(OpCodes.Callvirt, getBuilderRef));
        il.Append(il.Create(OpCodes.Callvirt, buildRef));
        il.Append(il.Create(OpCodes.Callvirt, getMergedParametersRef));
        il.Append(il.Create(OpCodes.Stfld, cachedParametersField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, cachedParametersField));
        il.Append(il.Create(OpCodes.Ret));

        action =
            "CurrentCommandParametersProvider.get_Parameters => always rebuild merged parameters per call (no stale cache across session commands).";

        return true;
    }

    private static bool PatchCommandParametersBuilderBase(AssemblyDefinition asm, out string action)
    {
        action = string.Empty;

        var module = asm.MainModule;
        var targetType = module.Types.FirstOrDefault(t => t.FullName == CommandParametersBuilderBaseTypeName);
        if (targetType is null)
        {
            Console.Error.WriteLine($"[WARN] Type not found: {CommandParametersBuilderBaseTypeName}");
            return false;
        }

        var buildMethod = targetType.Methods.FirstOrDefault(m =>
            string.Equals(m.Name, "Build", StringComparison.Ordinal) && m.Parameters.Count == 0);
        var doBuildMethod = targetType.Methods.FirstOrDefault(m =>
            string.Equals(m.Name, "DoBuild", StringComparison.Ordinal) && m.Parameters.Count == 0);
        if (buildMethod is null || doBuildMethod is null || !buildMethod.HasBody)
        {
            Console.Error.WriteLine("[WARN] Required methods not found in CommandParametersBuilderBase.");
            return false;
        }

        buildMethod.Body.ExceptionHandlers.Clear();
        buildMethod.Body.Variables.Clear();
        buildMethod.Body.Instructions.Clear();
        buildMethod.Body.InitLocals = false;

        var il = buildMethod.Body.GetILProcessor();
        var doBuildRef = module.ImportReference(doBuildMethod);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, doBuildRef));
        il.Append(il.Create(OpCodes.Ret));

        action =
            "CommandParametersBuilderBase.Build => always call DoBuild() (disable first-command parameter cache).";

        return true;
    }

    private static bool PatchLinuxFullAccessValidator(AssemblyDefinition asm, out string action)
    {
        action = string.Empty;

        var module = asm.MainModule;
        var targetType = module.Types.FirstOrDefault(t => t.FullName == LinuxFullAccessValidatorTypeName);
        if (targetType is null)
        {
            Console.Error.WriteLine($"[WARN] Type not found: {LinuxFullAccessValidatorTypeName}");
            return false;
        }

        var ctor = targetType.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 2);
        if (ctor is null || !ctor.HasBody)
        {
            Console.Error.WriteLine("[WARN] Constructor not found or has no body: LinuxFullAccessValidator..ctor");
            return false;
        }

        var baseTypeDef = targetType.BaseType?.Resolve();
        var baseCtor = baseTypeDef?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        if (baseCtor is null)
        {
            Console.Error.WriteLine("[WARN] Base constructor not found for LinuxFullAccessValidator patch.");
            return false;
        }

        ctor.Body.ExceptionHandlers.Clear();
        ctor.Body.Variables.Clear();
        ctor.Body.Instructions.Clear();
        ctor.Body.InitLocals = false;

        var il = ctor.Body.GetILProcessor();
        var baseCtorRef = module.ImportReference(baseCtor);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, baseCtorRef));
        il.Append(il.Create(OpCodes.Ret));

        action =
            "LinuxFullAccessValidator..ctor => remove Linux security countdown (5..1) and startup delay.";
        return true;
    }

    private static void WriteAssemblyReplacing(string targetPath, AssemblyDefinition asm)
    {
        var tempPath = targetPath + ".tmp";
        asm.Write(tempPath);
        asm.Dispose();
        File.Copy(tempPath, targetPath, overwrite: true);
        File.Delete(tempPath);
    }

    private static void BackupFile(string file)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backup = file + ".bak_" + stamp;
        File.Copy(file, backup, overwrite: false);
        Console.WriteLine($"[INFO] Backup created: {backup}");
    }

    private sealed class Config
    {
        public string AppDir { get; init; } = string.Empty;
    }

    private static Config? ParseArgs(string[] args)
    {
        string? appDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--app-dir":
                    if (i + 1 >= args.Length)
                    {
                        return null;
                    }
                    appDir = args[++i];
                    break;
                case "-h":
                case "--help":
                    return null;
                default:
                    Console.Error.WriteLine($"[ERR] Unknown arg: {a}");
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(appDir))
        {
            return null;
        }

        return new Config
        {
            AppDir = appDir,
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet Optimo.CliIlPatcher.dll --app-dir <ctrader_cli_dir>");
    }
}
