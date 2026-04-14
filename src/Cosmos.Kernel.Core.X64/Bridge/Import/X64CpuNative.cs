using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.X64.Bridge;

public static partial class X64CpuNative
{
    [LibraryImport("*", EntryPoint = "_native_cpu_rdtsc")]
    [SuppressGCTransition]
    public static partial ulong ReadTsc();
}
