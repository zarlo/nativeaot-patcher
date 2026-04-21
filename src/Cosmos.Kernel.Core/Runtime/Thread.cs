using System.Runtime;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Core.Bridge;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Runtime;

public class Thread
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private static object[][] threadData;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    [RuntimeExport("RhGetThreadStaticStorage")]
    internal static ref object[][] RhGetThreadStaticStorage()
    {
        if (CosmosFeatures.SchedulerEnabled)
        {
            var cpuState = SchedulerManager.GetCpuState(0);
            return ref cpuState.CurrentThread!.GetThreadStaticStorage();
        }
        else
        {
            return ref threadData;
        }
    }

    [RuntimeExport("RhGetCurrentThreadStackBounds")]
    internal static void RhGetCurrentThreadStackBounds(out IntPtr pStackLow, out IntPtr pStackHigh)
    {
        pStackLow = (nint)ContextSwitchNative.GetSp();
        pStackHigh = pStackLow + (nint)Scheduler.Thread.DefaultStackSize;
    }

    [RuntimeExport("RhSetCurrentThreadName")]
    internal static unsafe void RhSetCurrentThreadName(ushort* name)
    {
        // Do nothing, the managed thread holds the string on a field.
        var managedName = Utf8StringMarshaller.ConvertToManaged((byte*)name);

        Serial.WriteString($"[Thread] Setting current thread name to '{managedName}'\n");
    }

    [RuntimeExport("RhSetThreadExitCallback")]
    internal static void RhSetThreadExitCallback(IntPtr callback)
    {
        if (CosmosFeatures.SchedulerEnabled)
        {
            SchedulerManager.OnThreadExitCallback = callback;
        }
    }
}
