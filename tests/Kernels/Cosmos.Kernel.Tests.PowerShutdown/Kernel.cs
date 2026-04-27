using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.PowerShutdown;

// Validates the Cosmos.Kernel.System.Power surface, the HAL wire-up, and
// then actually invokes Power.Shutdown(). The test runner launches QEMU
// with AllowGuestShutdown = true so x64's _S5 PM1a write actually exits
// the VM (dev `cosmos run` keeps -no-shutdown for inspection on panic).
// ARM64 doesn't need the flag — PSCI SYSTEM_OFF always exits QEMU.
public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[PowerShutdown Tests] BeforeRun() reached\n");

        TR.Start("PowerShutdown Tests", expectedTests: 5);

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

        TR.Run("Power_Reboot_Callable", () =>
        {
            Action reboot = Sys.Power.Reboot;
            Assert.NotNull(reboot);
        });

        Serial.WriteString("[PowerShutdown Tests] About to invoke Power.Shutdown() — QEMU should exit\n");
        TR.RunDestructive(
            "Shutdown_FiresAndExits",
            () => Sys.Power.Shutdown(),
            "Power.Shutdown() returned without powering off");

        // Only reached if Shutdown didn't fire.
        TR.Finish();
        Serial.WriteString("[PowerShutdown Tests] FATAL: reached post-Shutdown epilogue\n");
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
