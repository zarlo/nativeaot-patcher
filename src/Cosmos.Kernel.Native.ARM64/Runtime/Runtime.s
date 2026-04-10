// ARM64 NativeAOT Runtime Stubs
// Write barriers and EH section accessors

.global RhpAssignRefArm64
.global RhpCheckedAssignRefArm64
.global RhpByRefAssignRefArm64
.global RhpAssignRefAVLocation
.global RhpCheckedAssignRefAVLocation
.global RhpByRefAssignRefAVLocation1
.global get_eh_frame_start
.global get_eh_frame_end
.global get_dotnet_eh_table_start
.global get_dotnet_eh_table_end

// double math
.global cos
.global sin
.global tan
.global pow
.global acos
.global asin
.global atan
.global atan2
.global exp
.global log
.global log2
.global log10
// float math
.global cosf
.global sinf
.global tanf
.global powf
.global expf
.global logf
.global log2f
.global log10f

.text
.align 4

// void RhpByRefAssignRefArm64()
//
// Write barrier for by-reference assignments (copies object reference from one location to another)
//
// On entry:
//   x13 : source address (points to object reference to copy)
//   x14 : destination address (where to write the reference)
//
// On exit:
//   x13 : incremented by 8
//   x14 : incremented by 8
//   x15 : trashed
RhpByRefAssignRefArm64:
RhpByRefAssignRefAVLocation1:
        ldr     x15, [x13], #8      // Load source object reference, post-increment x13
        b       RhpCheckedAssignRefArm64

// void RhpCheckedAssignRefArm64()
//
// Write barrier for assignments to locations that may not be on the managed heap
// (e.g., static fields, stack locations)
//
// On entry:
//   x14 : destination address
//   x15 : object reference to store
//
// On exit:
//   x14 : incremented by 8
RhpCheckedAssignRefArm64:
RhpCheckedAssignRefAVLocation:
        // Store the object reference
        str     x15, [x14], #8      // Store and post-increment x14
        ret

// void RhpAssignRefArm64()
//
// Write barrier for assignments to the managed heap
// Ensures proper memory ordering for concurrent GC
//
// On entry:
//   x14 : destination address (on managed heap)
//   x15 : object reference to store
//
// On exit:
//   x14 : incremented by 8
RhpAssignRefArm64:
RhpAssignRefAVLocation:
        // Store the object reference
        str     x15, [x14], #8      // Store and post-increment x14

        // Memory barrier to ensure stores are visible to other cores and GC
        dmb     ish                 // Inner shareable domain barrier (sufficient for ARM64)

        ret

// void* get_eh_frame_start(void)
// Returns pointer to start of .eh_frame section
get_eh_frame_start:
    adrp    x0, __eh_frame_start
    add     x0, x0, :lo12:__eh_frame_start
    ret

// void* get_eh_frame_end(void)
// Returns pointer to end of .eh_frame section
get_eh_frame_end:
    adrp    x0, __eh_frame_end
    add     x0, x0, :lo12:__eh_frame_end
    ret

// void* get_dotnet_eh_table_start(void)
// Returns pointer to start of .dotnet_eh_table section
get_dotnet_eh_table_start:
    adrp    x0, __dotnet_eh_table_start
    add     x0, x0, :lo12:__dotnet_eh_table_start
    ret

// void* get_dotnet_eh_table_end(void)
// Returns pointer to end of .dotnet_eh_table section
get_dotnet_eh_table_end:
    adrp    x0, __dotnet_eh_table_end
    add     x0, x0, :lo12:__dotnet_eh_table_end
    ret

// ============================================================================
// Math Functions (ARM64 Implementation)
// Uses Taylor Series approximations with range reduction.
// ============================================================================

.data
.align 4
MATH_CONSTANTS:
    .double 3.14159265358979323846      // 0:  PI
    .double 6.28318530717958647692      // 8:  2*PI
    .double 1.57079632679489661923      // 16: PI/2
    .double 0.0                         // 24: 0.0
    .double 1.0                         // 32: 1.0
    .double 0.5                         // 40: 0.5
    .double -1.0                        // 48: -1.0
    .double 2.71828182845904523536      // 56: E
    .double 1.44269504088896340736      // 64: log2(e)
    .double 0.69314718055994530942      // 72: ln(2)
    .double 0.43429448190325182765      // 80: log10(e)

.text
.align 4

// Helper: Range reduction to [-PI, PI]
// Input: d0 (x)
// Output: d0 (x reduced)
// Trashes: d1, d2
range_reduce:
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d2, [x2, #8]    // 2*PI

    fdiv    d1, d0, d2      // x / 2PI
    frintz  d1, d1          // trunc(x / 2PI)
    fmul    d1, d1, d2      // trunc(...) * 2PI
    fsub    d0, d0, d1      // x - trunc(...) * 2PI
    ret

// ============================================================================
// double sin(double x)
// Taylor: x - x^3/3! + x^5/5! - x^7/7! + x^9/9! - x^11/11! + x^13/13!
// ============================================================================
sin:
    stp     lr, xzr, [sp, #-16]!

    // NaN/Inf check
    fmov    x0, d0
    lsr     x1, x0, #52
    and     x1, x1, #0x7FF
    cmp     x1, #0x7FF
    b.eq    .Lsin_nan

    bl      range_reduce

    fmov    d1, d0          // term = x
    fmov    d2, d0          // sum = x
    fmul    d3, d0, d0      // x^2

    // Term 3: -x^3/6
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d5, #6.0
    fdiv    d1, d1, d5
    fadd    d2, d2, d1

    // Term 5: +x^5/120
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d5, #20.0
    fdiv    d1, d1, d5
    fadd    d2, d2, d1

    // Term 7: -x^7/5040
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #42
    scvtf   d5, x9
    fdiv    d1, d1, d5
    fadd    d2, d2, d1

    // Term 9: +x^9/362880
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #72
    scvtf   d5, x9
    fdiv    d1, d1, d5
    fadd    d2, d2, d1

    // Term 11: -x^11/39916800
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #110
    scvtf   d5, x9
    fdiv    d1, d1, d5
    fadd    d2, d2, d1

    // Term 13: +x^13/6227020800
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #156
    scvtf   d5, x9
    fdiv    d1, d1, d5
    fadd    d2, d2, d1

    fmov    d0, d2
    ldp     lr, xzr, [sp], #16
    ret

.Lsin_nan:
    // Return NaN for NaN/Inf input
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double cos(double x)
// Taylor: 1 - x^2/2! + x^4/4! - x^6/6! + x^8/8! - x^10/10! + x^12/12!
// ============================================================================
cos:
    stp     lr, xzr, [sp, #-16]!

    // NaN/Inf check
    fmov    x0, d0
    lsr     x1, x0, #52
    and     x1, x1, #0x7FF
    cmp     x1, #0x7FF
    b.eq    .Lcos_nan

    bl      range_reduce

    fmov    d1, #1.0        // term = 1.0
    fmov    d2, #1.0        // sum = 1.0
    fmul    d3, d0, d0      // x^2

    // Term 2: -x^2/2
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d4, #2.0
    fdiv    d1, d1, d4
    fadd    d2, d2, d1

    // Term 4: +x^4/24
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d4, #12.0
    fdiv    d1, d1, d4
    fadd    d2, d2, d1

    // Term 6: -x^6/720
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d4, #30.0
    fdiv    d1, d1, d4
    fadd    d2, d2, d1

    // Term 8: +x^8/40320
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #56
    scvtf   d4, x9
    fdiv    d1, d1, d4
    fadd    d2, d2, d1

    // Term 10: -x^10/3628800
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #90
    scvtf   d4, x9
    fdiv    d1, d1, d4
    fadd    d2, d2, d1

    // Term 12: +x^12/479001600
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #132
    scvtf   d4, x9
    fdiv    d1, d1, d4
    fadd    d2, d2, d1

    fmov    d0, d2
    ldp     lr, xzr, [sp], #16
    ret

.Lcos_nan:
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double tan(double x) = sin(x) / cos(x)
// ============================================================================
tan:
    stp     d8, d9, [sp, #-16]!
    stp     lr, xzr, [sp, #-16]!
    str     d0, [sp, #-16]!

    bl      sin
    fmov    d8, d0              // d8 = sin(x)

    ldr     d0, [sp]
    bl      cos
    fmov    d9, d0              // d9 = cos(x)

    fdiv    d0, d8, d9

    ldr     d1, [sp], #16       // pop x
    ldp     lr, xzr, [sp], #16
    ldp     d8, d9, [sp], #16
    ret

// ============================================================================
// double asin(double x)
// Taylor: x + x^3/6 + 3x^5/40 + 15x^7/336 + 105x^9/3456 + 945x^11/42240
// Valid for |x| <= 1
// ============================================================================
asin:
    stp     lr, xzr, [sp, #-16]!

    // Check |x| > 1 -> NaN
    fabs    d1, d0
    fmov    d2, #1.0
    fcmp    d1, d2
    b.gt    .Lasin_nan

    // Check |x| == 1 -> +/- PI/2
    b.eq    .Lasin_one

    fmov    d2, d0          // save x
    fmul    d3, d0, d0      // x^2

    // sum = x (term 1)
    fmov    d4, d0          // sum = x
    fmov    d1, d0          // term = x

    // term 2: x^3 * (1/6)
    fmul    d1, d1, d3      // x^3
    mov     x9, #6
    scvtf   d5, x9
    fdiv    d6, d1, d5
    fadd    d4, d4, d6

    // term 3: x^5 * (3/40)
    fmul    d1, d1, d3      // x^5
    fmov    d5, #3.0
    fmul    d6, d1, d5
    mov     x9, #40
    scvtf   d5, x9
    fdiv    d6, d6, d5
    fadd    d4, d4, d6

    // term 4: x^7 * (15/336)
    fmul    d1, d1, d3      // x^7
    fmov    d5, #15.0
    fmul    d6, d1, d5
    mov     x9, #336
    scvtf   d5, x9
    fdiv    d6, d6, d5
    fadd    d4, d4, d6

    // term 5: x^9 * (105/3456)
    fmul    d1, d1, d3      // x^9
    mov     x9, #105
    scvtf   d5, x9
    fmul    d6, d1, d5
    mov     x9, #3456
    scvtf   d5, x9
    fdiv    d6, d6, d5
    fadd    d4, d4, d6

    // term 6: x^11 * (945/42240)
    fmul    d1, d1, d3      // x^11
    mov     x9, #945
    scvtf   d5, x9
    fmul    d6, d1, d5
    mov     x9, #42240
    scvtf   d5, x9
    fdiv    d6, d6, d5
    fadd    d4, d4, d6

    // term 7: x^13 * (10395/599040)
    fmul    d1, d1, d3      // x^13
    mov     x9, #10395
    scvtf   d5, x9
    fmul    d6, d1, d5
    mov     x9, #599040
    scvtf   d5, x9
    fdiv    d6, d6, d5
    fadd    d4, d4, d6

    fmov    d0, d4
    ldp     lr, xzr, [sp], #16
    ret

.Lasin_one:
    // Return sign(x) * PI/2
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d1, [x2, #16]   // PI/2
    // Copy sign from d0 to d1
    fmov    x0, d0
    and     x0, x0, #0x8000000000000000  // sign bit
    fmov    x1, d1
    orr     x0, x0, x1
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

.Lasin_nan:
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double acos(double x) = PI/2 - asin(x)
// ============================================================================
acos:
    stp     lr, xzr, [sp, #-16]!

    // Check |x| > 1 -> NaN
    fabs    d1, d0
    fmov    d2, #1.0
    fcmp    d1, d2
    b.gt    .Lacos_nan

    bl      asin
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d1, [x2, #16]   // PI/2
    fsub    d0, d1, d0      // PI/2 - asin(x)

    ldp     lr, xzr, [sp], #16
    ret

.Lacos_nan:
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double atan(double x)
// Taylor for |x| <= 1: x - x^3/3 + x^5/5 - x^7/7 + ...
// For |x| > 1: atan(x) = sign(x)*PI/2 - atan(1/x)
// ============================================================================
atan:
    stp     lr, xzr, [sp, #-16]!
    stp     d8, d9, [sp, #-16]!

    // Check NaN
    fcmp    d0, d0
    b.ne    .Latan_nan

    // Check +/-Inf
    fmov    x0, d0
    lsr     x1, x0, #52
    and     x1, x1, #0x7FF
    cmp     x1, #0x7FF
    b.eq    .Latan_inf

    // Save sign and work with |x|
    fmov    d8, d0          // save original
    fabs    d0, d0

    // Check if |x| > 1
    fmov    d1, #1.0
    fcmp    d0, d1
    b.gt    .Latan_large

    // |x| <= 1: use Taylor directly
    bl      .Latan_taylor
    b       .Latan_fix_sign

.Latan_large:
    // atan(x) = PI/2 - atan(1/x)  for x > 0
    fmov    d1, #1.0
    fdiv    d0, d1, d0      // 1/|x|
    bl      .Latan_taylor
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d1, [x2, #16]   // PI/2
    fsub    d0, d1, d0      // PI/2 - atan(1/|x|)

.Latan_fix_sign:
    // Restore original sign
    fmov    x0, d8
    tbnz    x0, #63, .Latan_negate
    ldp     d8, d9, [sp], #16
    ldp     lr, xzr, [sp], #16
    ret

.Latan_negate:
    fneg    d0, d0
    ldp     d8, d9, [sp], #16
    ldp     lr, xzr, [sp], #16
    ret

.Latan_inf:
    // atan(+Inf) = PI/2, atan(-Inf) = -PI/2
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d0, [x2, #16]   // PI/2
    fmov    x1, d8
    tbnz    x1, #63, .Latan_inf_neg
    ldp     d8, d9, [sp], #16
    ldp     lr, xzr, [sp], #16
    ret
.Latan_inf_neg:
    fneg    d0, d0
    ldp     d8, d9, [sp], #16
    ldp     lr, xzr, [sp], #16
    ret

.Latan_nan:
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     d8, d9, [sp], #16
    ldp     lr, xzr, [sp], #16
    ret

// Taylor series for atan(x), |x| <= 1
// x - x^3/3 + x^5/5 - x^7/7 + x^9/9 - x^11/11 + x^13/13
.Latan_taylor:
    fmov    d2, d0          // sum = x
    fmov    d1, d0          // term = x
    fmul    d3, d0, d0      // x^2

    // -x^3/3
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d5, #3.0
    fdiv    d6, d1, d5
    fadd    d2, d2, d6

    // +x^5/5
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d5, #5.0
    fdiv    d6, d1, d5
    fadd    d2, d2, d6

    // -x^7/7
    fneg    d1, d1
    fmul    d1, d1, d3
    fmov    d5, #7.0
    fdiv    d6, d1, d5
    fadd    d2, d2, d6

    // +x^9/9
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #9
    scvtf   d5, x9
    fdiv    d6, d1, d5
    fadd    d2, d2, d6

    // -x^11/11
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #11
    scvtf   d5, x9
    fdiv    d6, d1, d5
    fadd    d2, d2, d6

    // +x^13/13
    fneg    d1, d1
    fmul    d1, d1, d3
    mov     x9, #13
    scvtf   d5, x9
    fdiv    d6, d1, d5
    fadd    d2, d2, d6

    fmov    d0, d2
    ret

// ============================================================================
// double atan2(double y, double x)
// d0 = y, d1 = x
// ============================================================================
atan2:
    stp     lr, xzr, [sp, #-16]!
    stp     d8, d9, [sp, #-16]!

    fmov    d8, d0          // save y
    fmov    d9, d1          // save x

    // Check x == 0
    fcmp    d1, #0.0
    b.ne    .Latan2_x_nonzero

    // x == 0: return sign(y) * PI/2 (or 0 if y == 0)
    fcmp    d0, #0.0
    b.eq    .Latan2_zero
    b.gt    .Latan2_pos_pi2
    // y < 0
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d0, [x2, #16]   // PI/2
    fneg    d0, d0
    b       .Latan2_done

.Latan2_pos_pi2:
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d0, [x2, #16]   // PI/2
    b       .Latan2_done

.Latan2_zero:
    fmov    d0, xzr
    b       .Latan2_done

.Latan2_x_nonzero:
    // atan(y/x)
    fdiv    d0, d8, d9      // y/x
    bl      atan

    // If x < 0, adjust by +/- PI
    fcmp    d9, #0.0
    b.ge    .Latan2_done

    // x < 0
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d1, [x2]        // PI

    fcmp    d8, #0.0
    b.lt    .Latan2_sub_pi
    fadd    d0, d0, d1      // y >= 0: add PI
    b       .Latan2_done
.Latan2_sub_pi:
    fsub    d0, d0, d1      // y < 0: subtract PI

.Latan2_done:
    ldp     d8, d9, [sp], #16
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double exp(double x)
// exp(x) = 2^(x / ln(2)) = 2^(x * log2(e))
// Split into integer + fractional, use polynomial for 2^frac
// ============================================================================
exp:
    stp     lr, xzr, [sp, #-16]!

    // Check NaN
    fcmp    d0, d0
    b.ne    .Lexp_nan

    // Check +Inf
    fmov    x0, d0
    mov     x1, #0x7FF0
    lsl     x1, x1, #48
    cmp     x0, x1
    b.eq    .Lexp_pos_inf

    // Check -Inf
    mov     x1, #0xFFF0
    lsl     x1, x1, #48
    cmp     x0, x1
    b.eq    .Lexp_ret_zero

    // z = x * log2(e)
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d1, [x2, #64]   // log2(e)
    fmul    d0, d0, d1      // z = x * log2(e)

    // Split: n = round(z), f = z - n
    frintn  d1, d0          // n = nearest integer
    fsub    d2, d0, d1      // f = z - n (fractional, |f| <= 0.5)
    fcvtzs  x0, d1          // n as integer

    // 2^f approximation using minimax polynomial (degree 5)
    // p(f) = 1 + f*ln2 + f^2*ln2^2/2 + f^3*ln2^3/6 + f^4*ln2^4/24 + f^5*ln2^5/120
    ldr     d3, [x2, #72]   // ln(2)

    fmov    d4, #1.0        // c0 = 1
    fmov    d5, d3          // c1 = ln2

    fmul    d6, d3, d3      // ln2^2
    fmov    d7, #2.0
    fdiv    d6, d6, d7      // c2 = ln2^2/2

    fmul    d7, d6, d3      // ln2^3/2
    fmov    d16, #3.0
    fdiv    d7, d7, d16     // c3 = ln2^3/6

    // Evaluate: result = c0 + f*(c1 + f*(c2 + f*c3))
    fmadd   d16, d2, d7, d6    // c2 + f*c3
    fmadd   d16, d2, d16, d5   // c1 + f*(c2 + f*c3)
    fmadd   d16, d2, d16, d4   // 1 + f*(c1 + f*(c2 + f*c3))

    // Scale by 2^n: multiply by 2^n using integer exponent manipulation
    // IEEE 754: 2^n = (n + 1023) << 52
    add     x0, x0, #1023
    // Clamp to valid range
    cmp     x0, #0
    b.le    .Lexp_underflow
    cmp     x0, #2046
    b.ge    .Lexp_overflow
    lsl     x0, x0, #52
    fmov    d0, x0
    fmul    d0, d16, d0

    ldp     lr, xzr, [sp], #16
    ret

.Lexp_underflow:
    fmov    d0, xzr
    ldp     lr, xzr, [sp], #16
    ret

.Lexp_overflow:
.Lexp_pos_inf:
    mov     x0, #0x7FF0
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

.Lexp_ret_zero:
    fmov    d0, xzr
    ldp     lr, xzr, [sp], #16
    ret

.Lexp_nan:
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double log(double x) - natural logarithm
// log(x) = (e - 1) * (1 - (e-1)/2 + (e-1)^2/3 - ...) where x = m * 2^e
// Actually: decompose x = m * 2^e, log(x) = e*ln(2) + log(m), 1 <= m < 2
// log(m) via Taylor around 1: log(1+u) = u - u^2/2 + u^3/3 - ...
// ============================================================================
log:
    stp     lr, xzr, [sp, #-16]!

    // x <= 0 -> NaN (for negative), -Inf (for zero)
    fcmp    d0, #0.0
    b.eq    .Llog_neg_inf
    b.lt    .Llog_nan

    // Check NaN
    fcmp    d0, d0
    b.ne    .Llog_nan

    // Check +Inf
    fmov    x0, d0
    mov     x1, #0x7FF0
    lsl     x1, x1, #48
    cmp     x0, x1
    b.eq    .Llog_pos_inf

    // Decompose: extract exponent and mantissa
    // x = m * 2^e where 1.0 <= m < 2.0
    fmov    x0, d0
    lsr     x1, x0, #52          // biased exponent
    and     x1, x1, #0x7FF
    sub     x1, x1, #1023        // unbiased exponent e
    scvtf   d1, x1               // d1 = e (as double)

    // Extract mantissa: set exponent to 1023 (gives 1.0 <= m < 2.0)
    mov     x2, #0x000FFFFFFFFFFFFF
    and     x0, x0, x2           // mantissa bits
    mov     x2, #1023
    lsl     x2, x2, #52
    orr     x0, x0, x2           // m with exponent = 0 (biased 1023)
    fmov    d0, x0               // d0 = m, 1.0 <= m < 2.0

    // u = m - 1
    fmov    d2, #1.0
    fsub    d0, d0, d2           // u = m - 1, 0 <= u < 1

    // log(1+u) = u - u^2/2 + u^3/3 - u^4/4 + u^5/5 - u^6/6 + u^7/7
    fmov    d3, d0               // sum = u
    fmov    d4, d0               // power = u
    fmul    d5, d0, d0           // u^2

    // -u^2/2
    fmul    d4, d4, d0
    fmov    d6, #2.0
    fdiv    d7, d4, d6
    fsub    d3, d3, d7

    // +u^3/3
    fmul    d4, d4, d0
    fmov    d6, #3.0
    fdiv    d7, d4, d6
    fadd    d3, d3, d7

    // -u^4/4
    fmul    d4, d4, d0
    fmov    d6, #4.0
    fdiv    d7, d4, d6
    fsub    d3, d3, d7

    // +u^5/5
    fmul    d4, d4, d0
    fmov    d6, #5.0
    fdiv    d7, d4, d6
    fadd    d3, d3, d7

    // -u^6/6
    fmul    d4, d4, d0
    fmov    d6, #6.0
    fdiv    d7, d4, d6
    fsub    d3, d3, d7

    // +u^7/7
    fmul    d4, d4, d0
    fmov    d6, #7.0
    fdiv    d7, d4, d6
    fadd    d3, d3, d7

    // -u^8/8
    fmul    d4, d4, d0
    fmov    d6, #8.0
    fdiv    d7, d4, d6
    fsub    d3, d3, d7

    // result = e * ln(2) + log(m)
    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d6, [x2, #72]   // ln(2)
    fmul    d1, d1, d6      // e * ln(2)
    fadd    d0, d1, d3      // e * ln(2) + log(m)

    ldp     lr, xzr, [sp], #16
    ret

.Llog_neg_inf:
    mov     x0, #0xFFF0
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

.Llog_pos_inf:
    mov     x0, #0x7FF0
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

.Llog_nan:
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double log2(double x) = log(x) / ln(2)
// ============================================================================
log2:
    stp     lr, xzr, [sp, #-16]!

    bl      log

    // Check if result is NaN or Inf (pass through)
    fcmp    d0, d0
    b.ne    .Llog2_done
    fmov    x0, d0
    lsr     x1, x0, #52
    and     x1, x1, #0x7FF
    cmp     x1, #0x7FF
    b.eq    .Llog2_done

    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d1, [x2, #72]   // ln(2)
    fdiv    d0, d0, d1

.Llog2_done:
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double log10(double x) = log(x) * log10(e)
// ============================================================================
log10:
    stp     lr, xzr, [sp, #-16]!

    bl      log

    // Check if result is NaN or Inf (pass through)
    fcmp    d0, d0
    b.ne    .Llog10_done
    fmov    x0, d0
    lsr     x1, x0, #52
    and     x1, x1, #0x7FF
    cmp     x1, #0x7FF
    b.eq    .Llog10_done

    adrp    x2, MATH_CONSTANTS
    add     x2, x2, :lo12:MATH_CONSTANTS
    ldr     d1, [x2, #80]   // log10(e)
    fmul    d0, d0, d1

.Llog10_done:
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// double pow(double x, double y)
// d0 = x (base), d1 = y (exponent)
// pow(x, y) = exp(y * log(x))
// ============================================================================
pow:
    stp     lr, xzr, [sp, #-16]!
    stp     d8, d9, [sp, #-16]!

    fmov    d8, d0          // save x
    fmov    d9, d1          // save y

    // y == 0 -> return 1.0
    fcmp    d1, #0.0
    b.eq    .Lpow_ret_one

    // x == 0
    fcmp    d0, #0.0
    b.eq    .Lpow_x_zero

    // x == 1 -> return 1.0
    fmov    d2, #1.0
    fcmp    d0, d2
    b.eq    .Lpow_ret_one

    // x < 0: check if y is integer
    fcmp    d0, #0.0
    b.lt    .Lpow_neg_base

    // x > 0: pow(x,y) = exp(y * log(x))
    bl      log             // d0 = log(x)
    fmul    d0, d0, d9      // y * log(x)
    bl      exp
    b       .Lpow_done

.Lpow_neg_base:
    // Check if y is integer
    frintz  d2, d9          // trunc(y)
    fcmp    d9, d2
    b.ne    .Lpow_ret_nan   // non-integer exponent of negative base -> NaN

    // |x|^y, then fix sign if y is odd
    fneg    d0, d8          // |x|
    bl      log
    fmul    d0, d0, d9
    bl      exp

    // Check if y is odd
    fcvtzs  x0, d9
    tbnz    x0, #0, .Lpow_neg_result
    b       .Lpow_done

.Lpow_neg_result:
    fneg    d0, d0
    b       .Lpow_done

.Lpow_x_zero:
    // x == 0, y > 0 -> 0, y < 0 -> +Inf
    fcmp    d9, #0.0
    b.gt    .Lpow_ret_zero
    mov     x0, #0x7FF0
    lsl     x0, x0, #48
    fmov    d0, x0
    b       .Lpow_done

.Lpow_ret_one:
    fmov    d0, #1.0
    b       .Lpow_done

.Lpow_ret_zero:
    fmov    d0, xzr
    b       .Lpow_done

.Lpow_ret_nan:
    mov     x0, #0x7FF8
    lsl     x0, x0, #48
    fmov    d0, x0

.Lpow_done:
    ldp     d8, d9, [sp], #16
    ldp     lr, xzr, [sp], #16
    ret

// ============================================================================
// Single-precision float wrappers (promote to double, call double, demote)
// ============================================================================

// float sinf(float x)
sinf:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    bl      sin
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret

// float cosf(float x)
cosf:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    bl      cos
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret

// float tanf(float x)
tanf:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    bl      tan
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret

// float expf(float x)
expf:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    bl      exp
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret

// float logf(float x)
logf:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    bl      log
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret

// float log2f(float x)
log2f:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    bl      log2
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret

// float log10f(float x)
log10f:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    bl      log10
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret

// float powf(float x, float y)
powf:
    stp     lr, xzr, [sp, #-16]!
    fcvt    d0, s0
    fcvt    d1, s1
    bl      pow
    fcvt    s0, d0
    ldp     lr, xzr, [sp], #16
    ret
