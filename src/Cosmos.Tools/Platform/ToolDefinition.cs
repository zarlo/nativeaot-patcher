namespace Cosmos.Tools.Platform;

public abstract class ToolDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public bool Required { get; init; } = true;
}

public class CommandToolDefinition : ToolDefinition
{
    public string[]? WindowsCommands { get; init; }
    public string[]? LinuxCommands { get; init; }
    public string[]? MacOSCommands { get; init; }
    public string? VersionArg { get; init; } = "--version";

    /// <summary>
    /// Asset name prefix in the tools-latest GitHub release (e.g., "llvm-tools", "xorriso", "yasm", "qemu").
    /// If null, the tool is not distributed via the release (e.g., .NET SDK — user installs it manually).
    /// </summary>
    public string? ReleaseAsset { get; init; }

    /// <summary>
    /// Manual installation instructions for tools not in the release (e.g., .NET SDK).
    /// </summary>
    public string? ManualInstructions { get; init; }

    public string[] GetCommands(OSPlatform platform) => platform switch
    {
        OSPlatform.Windows => WindowsCommands ?? [],
        OSPlatform.Linux => LinuxCommands ?? [],
        OSPlatform.MacOS => MacOSCommands ?? [],
        _ => []
    };
}

public class FileToolDefinition : ToolDefinition
{
    public string[]? WindowsPaths { get; init; }
    public string[]? LinuxPaths { get; init; }
    public string[]? MacOSPaths { get; init; }

    public string[]? GetPaths(OSPlatform platform) => platform switch
    {
        OSPlatform.Windows => WindowsPaths,
        OSPlatform.Linux => LinuxPaths,
        OSPlatform.MacOS => MacOSPaths,
        _ => null
    };
}

public static class ToolDefinitions
{
    public static readonly CommandToolDefinition DotNetSdk = new()
    {
        Name = "dotnet",
        DisplayName = ".NET SDK",
        Description = ".NET 10.0 SDK for building Cosmos kernels",
        WindowsCommands = ["dotnet"],
        LinuxCommands = ["dotnet"],
        MacOSCommands = ["dotnet"],
        VersionArg = "--version",
        Required = true,
        ManualInstructions = "Download from https://dot.net/download"
    };

    public static readonly CommandToolDefinition Clang = new()
    {
        Name = "clang",
        DisplayName = "Clang Compiler",
        Description = "LLVM C compiler for x64 and ARM64 bare-metal targets",
        WindowsCommands = ["clang"],
        LinuxCommands = ["clang"],
        MacOSCommands = ["clang"],
        VersionArg = "--version",
        Required = true,
        ReleaseAsset = "llvm-tools"
    };

    public static readonly CommandToolDefinition LLD = new()
    {
        Name = "ld.lld",
        DisplayName = "LLD Linker",
        Description = "LLVM linker for linking kernel binaries",
        WindowsCommands = ["ld.lld", "lld"],
        LinuxCommands = ["ld.lld", "lld"],
        MacOSCommands = ["ld.lld", "lld"],
        VersionArg = "--version",
        Required = true,
        ReleaseAsset = "llvm-tools"
    };

    public static readonly CommandToolDefinition Xorriso = new()
    {
        Name = "xorriso",
        DisplayName = "xorriso",
        Description = "ISO creation tool for bootable kernel images",
        WindowsCommands = ["xorriso"],
        LinuxCommands = ["xorriso"],
        MacOSCommands = ["xorriso"],
        VersionArg = "--version",
        Required = true,
        ReleaseAsset = "xorriso"
    };

    public static readonly CommandToolDefinition Yasm = new()
    {
        Name = "yasm",
        DisplayName = "Yasm Assembler",
        Description = "x64 assembler for native code",
        WindowsCommands = ["yasm"],
        LinuxCommands = ["yasm"],
        MacOSCommands = ["yasm"],
        VersionArg = "--version",
        Required = true,
        ReleaseAsset = "yasm"
    };

    public static readonly CommandToolDefinition QemuX64 = new()
    {
        Name = "qemu-system-x86_64",
        DisplayName = "QEMU x64",
        Description = "x64 system emulator for testing kernels",
        WindowsCommands = ["qemu-system-x86_64"],
        LinuxCommands = ["qemu-system-x86_64"],
        MacOSCommands = ["qemu-system-x86_64"],
        VersionArg = "--version",
        Required = false,
        ReleaseAsset = "qemu"
    };

    public static readonly CommandToolDefinition QemuArm64 = new()
    {
        Name = "qemu-system-aarch64",
        DisplayName = "QEMU ARM64",
        Description = "ARM64 system emulator for testing kernels",
        WindowsCommands = ["qemu-system-aarch64"],
        LinuxCommands = ["qemu-system-aarch64"],
        MacOSCommands = ["qemu-system-aarch64"],
        VersionArg = "--version",
        Required = false,
        ReleaseAsset = "qemu"
    };

    public static readonly FileToolDefinition QemuEfiArm64 = new()
    {
        Name = "QEMU EFI (ARM64)",
        DisplayName = "QEMU UEFI Firmware",
        Description = "UEFI firmware for ARM64 QEMU — bundled with QEMU",
        Required = false,
        // Single canonical bundle path on every OS (matches QemuLauncher.ResolveArm64Firmware).
        WindowsPaths = [@"%LOCALAPPDATA%\Cosmos\Tools\qemu\share\qemu\edk2-aarch64-code.fd"],
        LinuxPaths = ["~/.cosmos/tools/qemu/share/qemu/edk2-aarch64-code.fd"],
        MacOSPaths = ["~/.cosmos/tools/qemu/share/qemu/edk2-aarch64-code.fd"]
    };

    public static IEnumerable<ToolDefinition> GetAllTools() =>
    [
        DotNetSdk,
        Clang,
        LLD,
        Xorriso,
        Yasm,
        QemuX64,
        QemuArm64,
        QemuEfiArm64
    ];
}
