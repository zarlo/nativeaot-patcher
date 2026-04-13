// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Bridge;

namespace Cosmos.Kernel.Core.CPU;

/// <summary>
/// Low-level CPU operations that can be used by Core components like the heap.
/// Native imports live in Bridge/Import/CpuNative.cs.
/// </summary>
public static class InternalCpu
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DisableInterrupts() => CpuNative.DisableInterrupts();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnableInterrupts() => CpuNative.EnableInterrupts();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Halt() => CpuNative.Halt();

    /// <summary>
    /// Creates a scope that disables interrupts and automatically re-enables them on dispose.
    /// Usage: using (InternalCpu.DisableInterruptsScope()) { ... }
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static InterruptScope DisableInterruptsScope()
    {
        return new InterruptScope();
    }

    /// <summary>
    /// A disposable scope that disables interrupts on creation and re-enables them on dispose.
    /// </summary>
    public ref struct InterruptScope
    {
        private bool _disposed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InterruptScope()
        {
            _disposed = false;
            InternalCpu.DisableInterrupts();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                InternalCpu.EnableInterrupts();
            }
        }
    }
}
