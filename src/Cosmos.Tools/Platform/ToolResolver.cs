using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cosmos.Tools.Platform;

public enum ToolSource
{
    NotFound,
    Override,
    System,
    Bundle
}

public sealed record ResolvedTool(string Path, ToolSource Source, string? Version);

/// <summary>
/// Resolves a tool to a concrete on-disk path using the policy:
///   1. explicit override path (e.g. MSBuild $(CCPath))
///   2. system tool on PATH, accepted only if its version matches the bundle's
///      pinned version (read from the bundle's VERSION stamp)
///   3. cosmos-bundled tool under ~/.cosmos/tools/&lt;asset&gt;/ (or %LOCALAPPDATA%\Cosmos\Tools)
/// </summary>
public static class ToolResolver
{
    private const int CommandTimeoutMs = 5000;
    private static readonly ConcurrentDictionary<string, ResolvedTool> s_cache = new();

    public static Task<ResolvedTool> ResolveAsync(CommandToolDefinition tool, string? overridePath = null)
    {
        string cacheKey = $"{tool.Name}|{overridePath ?? string.Empty}";
        if (s_cache.TryGetValue(cacheKey, out var cached))
        {
            return Task.FromResult(cached);
        }
        return ResolveUncachedAsync(tool, overridePath, cacheKey);
    }

    private static async Task<ResolvedTool> ResolveUncachedAsync(
        CommandToolDefinition tool, string? overridePath, string cacheKey)
    {
        ResolvedTool result = await ResolveCoreAsync(tool, overridePath);
        // Only cache successful resolutions — NotFound must be retried on the next
        // call so `cosmos install`'s post-download verification sees newly extracted
        // bundle binaries instead of the pre-download miss.
        if (result.Source != ToolSource.NotFound)
        {
            s_cache[cacheKey] = result;
        }
        return result;
    }

    private static async Task<ResolvedTool> ResolveCoreAsync(CommandToolDefinition tool, string? overridePath)
    {
        // 1. Explicit override wins unconditionally if it points to a real file.
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
        {
            string? overrideVersion = await TryGetVersionAsync(overridePath, tool.VersionArg);
            return new ResolvedTool(overridePath, ClassifySource(overridePath, ToolSource.Override), overrideVersion);
        }

        // 2. System PATH lookup, gated by version match against the bundle's VERSION
        //    stamp. If no bundle is installed there's nothing to compare against,
        //    so we accept the system tool as-is (user is opting out of the bundle).
        string? pinnedVersion = ReadBundleVersion(tool);
        foreach (string command in tool.GetCommands(PlatformInfo.CurrentOS))
        {
            string? systemPath = await FindOnPathAsync(command);
            if (systemPath is null)
            {
                continue;
            }
            string? systemVersion = await TryGetVersionAsync(systemPath, tool.VersionArg);
            if (pinnedVersion is null || VersionsMatch(pinnedVersion, systemVersion))
            {
                return new ResolvedTool(systemPath, ClassifySource(systemPath, ToolSource.System), systemVersion);
            }
            // Version mismatch — fall through to bundle.
        }

        // 3. Bundle. Prefer the stamped pinned version, but for bundles released
        //    before VERSION stamping was added, fall back to querying the binary
        //    directly so callers (e.g. InstallCommand's post-install verification)
        //    still see a non-null version.
        string? bundlePath = FindInBundle(tool);
        if (bundlePath is not null)
        {
            string? bundleVersion = pinnedVersion ?? await TryGetVersionAsync(bundlePath, tool.VersionArg);
            return new ResolvedTool(bundlePath, ToolSource.Bundle, bundleVersion);
        }

        return new ResolvedTool(string.Empty, ToolSource.NotFound, null);
    }

    /// <summary>
    /// "System" means the user installed it themselves outside our bundle. If the
    /// path actually lives inside %LOCALAPPDATA%\Cosmos\Tools (or ~/.cosmos/tools),
    /// it's a bundle file regardless of which lookup found it — Windows installs
    /// add the bundle dirs to user PATH so `where clang` finds the bundled binary.
    /// </summary>
    private static ToolSource ClassifySource(string path, ToolSource lookupSource)
    {
        string toolsRoot = ToolChecker.GetCosmosToolsPath();
        try
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(toolsRoot);
            if (fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return ToolSource.Bundle;
            }
        }
        catch
        {
            // Fall through to original classification if path normalization fails.
        }
        return lookupSource;
    }

    /// <summary>
    /// Read the VERSION stamp written by the build-tools.yml packaging step.
    /// Returns null if no bundle is installed for this tool.
    /// </summary>
    public static string? ReadBundleVersion(CommandToolDefinition tool)
    {
        if (string.IsNullOrEmpty(tool.ReleaseAsset))
        {
            return null;
        }
        string versionFile = Path.Combine(ToolChecker.GetCosmosToolsPath(), tool.ReleaseAsset, "VERSION");
        if (!File.Exists(versionFile))
        {
            return null;
        }
        try
        {
            return File.ReadAllText(versionFile).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindInBundle(CommandToolDefinition tool)
    {
        if (string.IsNullOrEmpty(tool.ReleaseAsset))
        {
            return null;
        }
        string toolsPath = ToolChecker.GetCosmosToolsPath();
        string ext = PlatformInfo.CurrentOS == OSPlatform.Windows ? ".exe" : "";
        string bundleRoot = Path.Combine(toolsPath, tool.ReleaseAsset);

        foreach (string command in tool.GetCommands(PlatformInfo.CurrentOS))
        {
            // Try {bundle}/{cmd}{ext} (qemu, yasm, xorriso) and {bundle}/bin/{cmd}{ext} (llvm-tools).
            foreach (string subDir in new[] { "", "bin" })
            {
                string dir = string.IsNullOrEmpty(subDir) ? bundleRoot : Path.Combine(bundleRoot, subDir);
                if (!string.IsNullOrEmpty(ext))
                {
                    string withExt = Path.Combine(dir, command + ext);
                    if (File.Exists(withExt))
                    {
                        return withExt;
                    }
                }
                string plain = Path.Combine(dir, command);
                if (File.Exists(plain))
                {
                    return plain;
                }
            }
        }
        return null;
    }

    private static async Task<string?> FindOnPathAsync(string command)
    {
        string finder = PlatformInfo.CurrentOS == OSPlatform.Windows ? "where" : "which";
        var (success, output) = await RunCommandAsync(finder, command);
        if (!success)
        {
            return null;
        }
        string candidate = output.Split('\n', '\r')[0].Trim();
        return File.Exists(candidate) ? candidate : null;
    }

    private static async Task<string?> TryGetVersionAsync(string command, string? versionArg)
    {
        if (string.IsNullOrEmpty(versionArg))
        {
            return null;
        }
        var (success, output) = await RunCommandAsync(command, versionArg);
        if (!success || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }
        foreach (string line in output.Split('\n', '\r'))
        {
            var match = Regex.Match(line, @"(\d+\.\d+(?:\.\d+)?)");
            if (match.Success)
            {
                return match.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Loose match: major.minor must agree. Patch differences accepted so a system
    /// 10.2.4 satisfies a bundle pinned at 10.2.2 (point releases stay compatible).
    /// </summary>
    private static bool VersionsMatch(string pinned, string? system)
    {
        if (string.IsNullOrEmpty(system))
        {
            return false;
        }
        var pinnedParts = pinned.Split('.');
        var systemParts = system.Split('.');
        if (pinnedParts.Length < 2 || systemParts.Length < 2)
        {
            return false;
        }
        return pinnedParts[0] == systemParts[0] && pinnedParts[1] == systemParts[1];
    }

    private static async Task<(bool success, string output)> RunCommandAsync(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return (false, "");
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(CommandTimeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                return (false, "");
            }
            string output = await stdoutTask;
            string error = await stderrTask;
            string combined = (output + "\n" + error).Trim();
            return (process.ExitCode == 0 || !string.IsNullOrEmpty(combined), combined);
        }
        catch
        {
            return (false, "");
        }
    }
}
