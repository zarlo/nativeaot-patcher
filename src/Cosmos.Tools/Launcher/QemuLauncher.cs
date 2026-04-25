using System.Diagnostics;
using System.Text;
using Cosmos.Tools.Platform;

namespace Cosmos.Tools.Launcher;

public sealed class QemuLaunchOptions
{
    public required string Architecture { get; init; }
    public required string IsoPath { get; init; }
    public int MemoryMb { get; init; } = 512;
    public bool Headless { get; init; }
    public bool Debug { get; init; }
    /// <summary>If null, serial goes to stdio (interactive CLI). Otherwise, to this file path (test runner).</summary>
    public string? SerialOutputFile { get; init; }
    /// <summary>Adds the test-runner port forwards (UDP 5556, TCP 5558) needed by network tests.</summary>
    public bool EnableNetworkTesting { get; init; }
    public IReadOnlyList<string> ExtraArgs { get; init; } = Array.Empty<string>();
}

public sealed record QemuLaunchPlan(string BinaryPath, string Arguments, ToolSource Source);

/// <summary>
/// Single source of truth for QEMU command-line construction. Used by both
/// `cosmos run` and `Cosmos.TestRunner.Engine` so a tweak (e.g. dropping a
/// broken display backend) propagates to all callers.
/// </summary>
public static class QemuLauncher
{
    public static async Task<QemuLaunchPlan> BuildAsync(QemuLaunchOptions options)
    {
        CommandToolDefinition tool = options.Architecture switch
        {
            "x64" => ToolDefinitions.QemuX64,
            "arm64" => ToolDefinitions.QemuArm64,
            _ => throw new ArgumentException($"Unsupported architecture: {options.Architecture}", nameof(options))
        };

        ResolvedTool resolved = await ToolResolver.ResolveAsync(tool);
        if (resolved.Source == ToolSource.NotFound)
        {
            throw new InvalidOperationException(
                $"QEMU for {options.Architecture} not found. Run `cosmos install` to fetch the bundled toolchain, " +
                $"or install qemu-system-{(options.Architecture == "x64" ? "x86_64" : "aarch64")} system-wide.");
        }

        var args = new StringBuilder();

        // Single rule, all OSes: when QEMU is bundled, point it at the bundle's
        // share/qemu/ for BIOS/firmware lookup. The MSYS2 Windows build never
        // auto-discovers its data dir, and even the Linux/macOS build's runtime
        // search depends on the build's compile-time prefix matching the install
        // prefix — we ship a portable bundle, so neither holds. Explicit -L is
        // the only universally reliable mechanism.
        if (resolved.Source == ToolSource.Bundle)
        {
            string shareQemu = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(resolved.Path)!, "..", "share", "qemu"));
            args.Append($"-L \"{shareQemu}\" ");
        }

        if (options.Architecture == "x64")
        {
            AppendX64Args(args, options);
        }
        else
        {
            AppendArm64Args(args, options);
        }

        // Display: omit -display when a window is wanted so QEMU picks its compiled-in
        // default (SDL on Windows, GTK on Linux/macOS). Passing a backend QEMU wasn't
        // built with causes it to abort with "Display 'X' is not available".
        if (options.Headless)
        {
            args.Append(" -display none");
        }

        // Serial: file for tests (parseable log), stdio for CLI (interactive).
        if (options.SerialOutputFile is not null)
        {
            args.Append($" -serial file:\"{options.SerialOutputFile}\"");
        }
        else
        {
            args.Append(" -serial stdio");
        }

        if (options.EnableNetworkTesting)
        {
            string nic = options.Architecture == "x64" ? "e1000e" : "virtio-net-device";
            args.Append($" -netdev user,id=net0,hostfwd=udp::5556-:5556,hostfwd=tcp::5558-:5558 -device {nic},netdev=net0");
        }

        if (options.Debug)
        {
            args.Append(" -s -S");
        }

        foreach (string extra in options.ExtraArgs)
        {
            args.Append(' ');
            args.Append(extra);
        }

        return new QemuLaunchPlan(resolved.Path, args.ToString().TrimStart(), resolved.Source);
    }

    private static void AppendX64Args(StringBuilder args, QemuLaunchOptions options)
    {
        args.Append($"-M q35 -cpu max -m {options.MemoryMb}M");
        args.Append($" -cdrom \"{options.IsoPath}\"");
        args.Append(" -boot d -no-reboot -no-shutdown");
        if (!options.Headless)
        {
            args.Append(" -vga std");
        }
    }

    private static void AppendArm64Args(StringBuilder args, QemuLaunchOptions options)
    {
        string firmware = ResolveArm64Firmware();
        args.Append($"-M virt,highmem=off -cpu cortex-a72 -m {options.MemoryMb}M");
        args.Append($" -bios \"{firmware}\"");
        args.Append($" -cdrom \"{options.IsoPath}\"");
        args.Append(" -boot d -no-reboot");
        // ramfb is required for Limine framebuffer support even when headless.
        args.Append(" -device ramfb");
    }

    public static string ResolveArm64Firmware()
    {
        // Single canonical bundle path (cross-OS). System-wide install fallbacks
        // are kept only for users who haven't run `cosmos install`.
        string toolsRoot = ToolChecker.GetCosmosToolsPath();
        foreach (string candidate in new[]
        {
            Path.Combine(toolsRoot, "qemu", "share", "qemu", "edk2-aarch64-code.fd"),
            "/usr/share/AAVMF/AAVMF_CODE.fd",
            "/usr/share/qemu-efi-aarch64/QEMU_EFI.fd",
            "/opt/homebrew/share/qemu/edk2-aarch64-code.fd",
            "/usr/local/share/qemu/edk2-aarch64-code.fd"
        })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException(
            "ARM64 UEFI firmware (edk2-aarch64-code.fd) not found. Run `cosmos install` to fetch it with the QEMU bundle.");
    }

    public static ProcessStartInfo ToProcessStartInfo(QemuLaunchPlan plan)
    {
        return new ProcessStartInfo
        {
            FileName = plan.BinaryPath,
            Arguments = plan.Arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };
    }
}
