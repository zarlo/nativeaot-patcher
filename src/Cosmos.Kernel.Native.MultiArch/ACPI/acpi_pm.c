// ACPI power-management entry points (x86 only).
// Wraps LAI's lai_enter_sleep / lai_acpi_reset for use from C#.
//
// lai_enter_sleep(5) needs the AML namespace to resolve \\_S5_, so we lazily
// invoke lai_create_namespace() once on first call. lai_acpi_reset() reads
// the FADT reset register directly via laihost_scan("FACP",0) and does not
// need the namespace, so it can be called immediately.

#include <stdint.h>
#include <stddef.h>

#include <lai/core.h>
#include <lai/helpers/pm.h>

extern void __cosmos_serial_write(const char* str);

#ifdef ARCH_X64

static int g_namespace_created = 0;

// Build the AML namespace if we haven't already. Returns 0 on success, nonzero
// if the RSDP is unavailable (no ACPI). A successful call is cached.
int cosmos_acpi_init_namespace(void) {
    if (g_namespace_created) return 0;

    extern void* cosmos_acpi_get_rsdp(void);
    if (cosmos_acpi_get_rsdp() == (void*)0) {
        __cosmos_serial_write("[ACPI-PM] No RSDP, skipping namespace creation\n");
        return 1;
    }

    __cosmos_serial_write("[ACPI-PM] Creating AML namespace...\n");
    lai_create_namespace();
    g_namespace_created = 1;
    __cosmos_serial_write("[ACPI-PM] Namespace ready\n");
    return 0;
}

// ACPI S5 shutdown. Returns 0 on success (does not return on success in
// practice — the firmware powers off mid-write). Nonzero return means
// the firmware didn't accept the request and the caller should fall back.
int cosmos_acpi_shutdown(void) {
    if (cosmos_acpi_init_namespace() != 0) return 1;

    __cosmos_serial_write("[ACPI-PM] lai_enter_sleep(S5)\n");
    lai_api_error_t err = lai_enter_sleep(5);
    if (err != LAI_ERROR_NONE) {
        __cosmos_serial_write("[ACPI-PM] lai_enter_sleep failed\n");
        return (int)err;
    }
    return 0;
}

// ACPI reset via FADT reset register. Doesn't need the namespace — LAI walks
// the FADT directly. Returns 0 on success.
int cosmos_acpi_reset(void) {
    __cosmos_serial_write("[ACPI-PM] lai_acpi_reset\n");
    lai_api_error_t err = lai_acpi_reset();
    if (err != LAI_ERROR_NONE) {
        __cosmos_serial_write("[ACPI-PM] lai_acpi_reset failed\n");
        return (int)err;
    }
    return 0;
}

#else // ARCH_X64

// LAI's PM helpers are x86-specific (port I/O via FADT PM1a). On ARM64 we use
// PSCI instead, so these stubs always report unavailable.
int cosmos_acpi_init_namespace(void) { return 1; }
int cosmos_acpi_shutdown(void) { return 1; }
int cosmos_acpi_reset(void) { return 1; }

#endif
