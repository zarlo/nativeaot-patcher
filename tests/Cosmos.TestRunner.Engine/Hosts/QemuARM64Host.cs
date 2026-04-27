using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cosmos.Tools.Launcher;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// QEMU host for ARM64/AArch64 architecture. Argument construction lives in
/// <see cref="QemuLauncher"/> so this stays in sync with `cosmos run`.
/// </summary>
public class QemuARM64Host : IQemuHost
{
    public string Architecture => "arm64";

    private readonly string? _qemuBinaryOverride;
    private readonly int _memoryMb;

    public QemuARM64Host(
        string? qemuBinary = null,
        string? uefiFirmwarePath = null,
        int memoryMb = 512)
    {
        _qemuBinaryOverride = qemuBinary;
        _memoryMb = memoryMb;
        // uefiFirmwarePath ignored — QemuLauncher.ResolveArm64Firmware() handles it.
    }

    public async Task<QemuRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = 30, bool showDisplay = false, bool enableNetworkTesting = false)
    {
        if (!File.Exists(isoPath))
        {
            return new QemuRunResult
            {
                ExitCode = -1,
                ErrorMessage = $"ISO file not found: {isoPath}"
            };
        }

        // Ensure UART log directory exists
        var logDir = Path.GetDirectoryName(uartLogPath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        // Delete existing UART log
        if (File.Exists(uartLogPath))
        {
            File.Delete(uartLogPath);
        }

        QemuLaunchPlan plan;
        try
        {
            plan = await QemuLauncher.BuildAsync(new QemuLaunchOptions
            {
                Architecture = "arm64",
                IsoPath = isoPath,
                MemoryMb = _memoryMb,
                Headless = !showDisplay,
                SerialOutputFile = uartLogPath,
                EnableNetworkTesting = enableNetworkTesting,
                AllowGuestShutdown = true
            });
        }
        catch (FileNotFoundException ex)
        {
            return new QemuRunResult { ExitCode = -1, ErrorMessage = ex.Message };
        }
        var startInfo = QemuLauncher.ToProcessStartInfo(plan);
        if (_qemuBinaryOverride is not null)
        {
            startInfo.FileName = _qemuBinaryOverride;
        }

        using var process = new Process { StartInfo = startInfo };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        // Only create test servers for network tests
        UdpTestServer? udpServer = null;
        TcpTestServer? tcpServer = null;
        if (enableNetworkTesting)
        {
            udpServer = new UdpTestServer();
            tcpServer = new TcpTestServer();
        }

        bool testSuiteCompleted = false;

        try
        {
            // Start test servers for network tests
            udpServer?.Start();
            tcpServer?.Start();

            process.Start();

            // Capture stderr asynchronously for diagnostics
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Monitor UART log for TestSuiteEnd while waiting for process
            var monitorTask = MonitorUartLogForTestEndAsync(uartLogPath, cts.Token);
            var processTask = process.WaitForExitAsync(cts.Token);

            // Wait for either test completion or process exit
            var completedTask = await Task.WhenAny(monitorTask, processTask);

            if (completedTask == monitorTask && await monitorTask)
            {
                // Test suite completed - kill QEMU
                testSuiteCompleted = true;
                if (!process.HasExited)
                {
                    // Give a brief moment for final UART flush
                    await Task.Delay(200);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            else if (!process.HasExited)
            {
                // Process task completed (process exited on its own)
                await processTask;
            }

            // Give UART log a moment to flush
            await Task.Delay(100);

            // Stop test servers if running
            if (udpServer != null)
            {
                await udpServer.StopAsync();
            }

            if (tcpServer != null)
            {
                await tcpServer.StopAsync();
            }

            // Log stderr for diagnostics
            string stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine($"[QEMU stderr] {stderr.Trim()}");
            }

            // Read UART log
            string uartLog = string.Empty;
            if (File.Exists(uartLogPath))
            {
                uartLog = await File.ReadAllTextAsync(uartLogPath, Encoding.Latin1);
            }

            return new QemuRunResult
            {
                ExitCode = testSuiteCompleted ? 0 : process.ExitCode,
                UartLog = uartLog,
                TimedOut = false
            };
        }
        catch (OperationCanceledException)
        {
            // Timeout - kill QEMU
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            // Give UART log a moment to flush
            await Task.Delay(100);

            // Stop test servers if running
            if (udpServer != null)
            {
                await udpServer.StopAsync();
            }

            if (tcpServer != null)
            {
                await tcpServer.StopAsync();
            }

            // Read whatever UART output we got
            string uartLog = string.Empty;
            if (File.Exists(uartLogPath))
            {
                uartLog = await File.ReadAllTextAsync(uartLogPath, Encoding.Latin1);
            }

            return new QemuRunResult
            {
                ExitCode = -1,
                UartLog = uartLog,
                TimedOut = true,
                ErrorMessage = $"QEMU timed out after {timeoutSeconds}s"
            };
        }
        catch (Exception ex)
        {
            // Stop test servers on error if running
            if (udpServer != null)
            {
                await udpServer.StopAsync();
            }

            if (tcpServer != null)
            {
                await tcpServer.StopAsync();
            }

            return new QemuRunResult
            {
                ExitCode = -1,
                ErrorMessage = $"Failed to run QEMU: {ex.Message}"
            };
        }
    }

    // End marker: 0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE
    private static readonly byte[] TestEndMarker = { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

    /// <summary>
    /// Monitor UART log file for test suite end marker
    /// </summary>
    private static async Task<bool> MonitorUartLogForTestEndAsync(string uartLogPath, CancellationToken cancellationToken)
    {
        long lastPosition = 0;
        int markerIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(uartLogPath))
                {
                    using var fs = new FileStream(uartLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length > lastPosition)
                    {
                        fs.Seek(lastPosition, SeekOrigin.Begin);
                        var buffer = new byte[fs.Length - lastPosition];
                        int bytesRead = await fs.ReadAsync(buffer, cancellationToken);
                        lastPosition += bytesRead;

                        // Look for end marker sequence
                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (buffer[i] == TestEndMarker[markerIndex])
                            {
                                markerIndex++;
                                if (markerIndex == TestEndMarker.Length)
                                {
                                    return true;
                                }
                            }
                            else
                            {
                                markerIndex = 0;
                                // Check if current byte starts the marker
                                if (buffer[i] == TestEndMarker[0])
                                {
                                    markerIndex = 1;
                                }
                            }
                        }
                    }
                }
            }
            catch (IOException)
            {
                // File might be locked, try again
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }
}
