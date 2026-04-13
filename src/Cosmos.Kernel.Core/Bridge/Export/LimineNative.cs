using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;

namespace Cosmos.Kernel.Core.Bridge;

/// <summary>
/// Wrapper for C code to access managed Limine data (RSDP address, HHDM offset).
/// NOTE: This is a data accessor wrapper - C code gets managed data, then continues in C.
/// We do NOT call C code from managed code - only provide data access.
/// </summary>
public static unsafe class LimineNative
{
    /// <summary>
    /// Wrapper to expose Limine RSDP address to C bootstrap for LAI ACPI initialization
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__get_limine_rsdp_address")]
    public static void* GetRsdpAddress()
    {
        if (Limine.Rsdp.Response != null)
        {
            return Limine.Rsdp.Response->Address;
        }
        return null;
    }

    /// <summary>
    /// Expose Limine HHDM offset to C bootstrap for physical-to-virtual address translation.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__get_limine_hhdm_offset")]
    public static ulong GetHhdmOffset()
    {
        if (Limine.HHDM.Response != null)
        {
            return Limine.HHDM.Response->Offset;
        }
        return 0;
    }
}
