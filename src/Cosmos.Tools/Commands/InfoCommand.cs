using System.ComponentModel;
using System.Runtime.InteropServices;
using Cosmos.Tools.Platform;
using Spectre.Console;
using Spectre.Console.Cli;
using SysOSPlatform = System.Runtime.InteropServices.OSPlatform;

namespace Cosmos.Tools.Commands;

public class InfoSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output as JSON (for IDE integration)")]
    public bool Json { get; set; }
}

public class InfoCommand : AsyncCommand<InfoSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InfoSettings settings)
    {
        string platform = GetPlatformName();
        string arch = PlatformInfo.CurrentArch.ToString().ToLower();
        string packageManager = PlatformInfo.GetPackageManager();
        string displayBackend = GetDisplayBackend();
        string gdbCommand = GetGdbCommand();
        string gdbCommandX64 = await ResolveToolPathAsync(ToolDefinitions.X64ElfGdb) ?? gdbCommand;
        string gdbCommandArm64 = await ResolveToolPathAsync(ToolDefinitions.Aarch64ElfGdb) ?? gdbCommand;
        if (settings.Json)
        {
            Console.WriteLine("{");
            Console.WriteLine($"  \"platform\": \"{platform}\",");
            Console.WriteLine($"  \"platformName\": \"{PlatformInfo.GetDistroName()}\",");
            Console.WriteLine($"  \"arch\": \"{arch}\",");
            Console.WriteLine($"  \"packageManager\": \"{packageManager}\",");
            Console.WriteLine($"  \"qemuDisplay\": \"{displayBackend}\",");
            Console.WriteLine($"  \"gdbCommand\": \"{EscapeJson(gdbCommand)}\",");
            Console.WriteLine($"  \"gdbCommandX64\": \"{EscapeJson(gdbCommandX64)}\",");
            Console.WriteLine($"  \"gdbCommandArm64\": \"{EscapeJson(gdbCommandArm64)}\"");
            Console.WriteLine("}");
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Cosmos Tools Info[/]");
            AnsiConsole.WriteLine("  " + new string('-', 50));
            AnsiConsole.MarkupLine($"  Platform: [blue]{PlatformInfo.GetDistroName()}[/] ({platform})");
            AnsiConsole.MarkupLine($"  Architecture: [blue]{arch}[/]");
            AnsiConsole.MarkupLine($"  Package Manager: [blue]{packageManager}[/]");
            AnsiConsole.MarkupLine($"  QEMU Display: [blue]{displayBackend}[/]");
            AnsiConsole.MarkupLine($"  GDB (x64): [blue]{Markup.Escape(gdbCommandX64)}[/]");
            AnsiConsole.MarkupLine($"  GDB (ARM64): [blue]{Markup.Escape(gdbCommandArm64)}[/]");
            AnsiConsole.WriteLine();
        }

        return 0;
    }

    private static async Task<string?> ResolveToolPathAsync(CommandToolDefinition tool)
    {
        var status = await ToolChecker.CheckToolAsync(tool);
        return status.Found ? status.Path : null;
    }

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.OSX))
        {
            return "macos";
        }

        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Linux))
        {
            return "linux";
        }

        return "unknown";
    }

    private static string GetDisplayBackend()
    {
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.OSX))
        {
            return "cocoa";
        }

        return "gtk";
    }

    private static string GetGdbCommand()
    {
        // On Linux, prefer gdb-multiarch for cross-architecture debugging
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Linux))
        {
            // Check if gdb-multiarch exists
            string gdbMultiarchPath = "/usr/bin/gdb-multiarch";
            if (File.Exists(gdbMultiarchPath))
            {
                return "gdb-multiarch";
            }
        }

        // macOS and Windows typically just use 'gdb'
        // Windows might need the full path if installed via MinGW
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            string mingwPath = @"C:\msys64\mingw64\bin\gdb.exe";
            if (File.Exists(mingwPath))
            {
                return mingwPath;
            }
        }

        return "gdb";
    }

    private static string? GetCosmosToolsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string toolsDir = Path.Combine(home, ".dotnet", "tools");

        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            string exePath = Path.Combine(toolsDir, "cosmos.exe");
            if (File.Exists(exePath))
            {
                return exePath;
            }
        }
        else
        {
            string path = Path.Combine(toolsDir, "cosmos");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
