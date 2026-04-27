using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.PowerReboot;

// Validates the Cosmos.Kernel.System.Power surface, the HAL wire-up, and
// then actually invokes Power.Reboot(). With QEMU's -no-reboot the VM
// exits cleanly so the suite only passes when the destructive op truly
// fires. A separate Cosmos.Kernel.Tests.PowerShutdown suite covers the
// _S5 / PSCI SYSTEM_OFF path.
public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[PowerReboot Tests] BeforeRun() reached\n");

        TR.Start("PowerReboot Tests", expectedTests: 5);

        TR.Run("PlatformHAL_PowerOps_NotNull", () =>
        {
            Assert.NotNull(PlatformHAL.PowerOps, "PlatformHAL.PowerOps should be wired up by the platform initializer");
        });

        TR.Run("PlatformHAL_CpuOps_NotNull", () =>
        {
            Assert.NotNull(PlatformHAL.CpuOps, "PlatformHAL.CpuOps should be wired up by the platform initializer");
        });

        TR.Run("Power_Halt_Callable", () =>
        {
            Action halt = Sys.Power.Halt;
            Assert.NotNull(halt);
        });

        TR.Run("Power_Shutdown_Callable", () =>
        {
            Action shutdown = Sys.Power.Shutdown;
            Assert.NotNull(shutdown);
        });

        Serial.WriteString("[PowerReboot Tests] About to invoke Power.Reboot() — QEMU should exit\n");
        TR.RunDestructive(
            "Reboot_FiresAndExits",
            () => Sys.Power.Reboot(),
            "Power.Reboot() returned without rebooting");

        // Only reached if Reboot didn't fire.
        TR.Finish();
        Serial.WriteString("[PowerReboot Tests] FATAL: reached post-Reboot epilogue\n");
    }

    protected override void Run()
    {
        Stop();
    }

    protected override void AfterRun()
    {
        TR.Complete();
        Sys.Power.Halt();
    }
}
