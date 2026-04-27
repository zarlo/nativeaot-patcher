using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Power;

// Validates the Cosmos.Kernel.System.Power surface and the underlying HAL
// wire-up. The final test actually invokes Power.Reboot(), which on QEMU
// causes a clean exit via -no-reboot. Shutdown isn't exercised end-to-end
// because QEMU x64 is launched with -no-shutdown (the VM stays alive on
// guest shutdown), so we settle for an API-surface check there.
public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[Power Tests] BeforeRun() reached\n");

        TR.Start("Power Tests", expectedTests: 5);

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

        // Final test: actually invoke Reboot. With QEMU's -no-reboot the VM
        // exits cleanly, this method never returns, and the pre-emptive pass
        // emitted by RunDestructive is the last record left in the log. If
        // Reboot regresses and returns, RunDestructive overrides the pass
        // with a fail and the suite finalises normally.
        Serial.WriteString("[Power Tests] About to invoke Power.Reboot() — QEMU should exit\n");
        TR.RunDestructive(
            "Reboot_FiresAndExits",
            () => Sys.Power.Reboot(),
            "Power.Reboot() returned without rebooting");

        // Only reached if Reboot didn't fire.
        TR.Finish();
        Serial.WriteString("[Power Tests] FATAL: reached post-Reboot epilogue\n");
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
