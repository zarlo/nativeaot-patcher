// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using System.Runtime.InteropServices;

namespace Cosmos.Core.RAT;

public class GC
{
    [UnmanagedCallersOnly(EntryPoint = "InitializeGarbageCollector")]
    [RuntimeExport("InitializeGarbageCollector")]
    internal static void InitializeGC()
    {
        Console.WriteLine("InitializeGC");
    }

    [UnmanagedCallersOnly(EntryPoint = "GC_Collect")]
    [RuntimeExport("GC_Collect")]
    internal static void GC_Collect(int generation, int mode)
    {
        // Stubbed GC.Collect(int, GCCollectionMode)
        Console.WriteLine($"Collect called (generation={generation}, mode={mode})");
    }

    [UnmanagedCallersOnly(EntryPoint = "GC_GetTotalMemory")]
    [RuntimeExport("GC_GetTotalMemory")]
    internal static long GC_GetTotalMemory(int forceFullCollection)
    {
        Console.WriteLine("GetTotalMemory");
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "GC_WaitForPendingFinalizers")]
    [RuntimeExport("GC_WaitForPendingFinalizers")]
    internal static void GC_WaitForPendingFinalizers()
    {
        Console.WriteLine("WaitForPendingFinalizers");
    }

}
