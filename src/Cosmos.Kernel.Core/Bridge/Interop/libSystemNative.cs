using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Bridge.Interop;

internal static class libSystemNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessCpuInformation
    {
        internal ulong lastRecordedCurrentTime;
        internal ulong lastRecordedKernelTime;
        internal ulong lastRecordedUserTime;
    }

    [UnmanagedCallersOnly(EntryPoint = "SystemNative_GetCpuUtilization")]
    internal static unsafe double SystemNative_GetCpuUtilization(ProcessCpuInformation* previousCpuInfo)
    {
        // TODO: Implement actual CPU utilization calculation.
        return 50.0;
    }

    [UnmanagedCallersOnly(EntryPoint = "SystemNative_SchedGetCpu")]
    internal static int SystemNative_SchedGetCpu()
    {
        // TODO: Implement actual CPU retrieval logic.
        return 0;
    }
}
