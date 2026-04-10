; x64 NativeAOT Runtime Stubs
; EH section accessors

global get_eh_frame_start
global get_eh_frame_end
global get_dotnet_eh_table_start
global get_dotnet_eh_table_end

extern __eh_frame_start
extern __eh_frame_end
extern __dotnet_eh_table_start
extern __dotnet_eh_table_end

; double math
global cos
global sin
global tan
global pow
global acos
global asin
global atan
global atan2
global exp
global log
global log2
global log10
; float math
global cosf
global sinf
global tanf
global powf
global expf
global logf
global log2f
global log10f

section .text

; void* get_eh_frame_start(void)
; Returns pointer to start of .eh_frame section
get_eh_frame_start:
    lea rax, [rel __eh_frame_start]
    ret

; void* get_eh_frame_end(void)
; Returns pointer to end of .eh_frame section
get_eh_frame_end:
    lea rax, [rel __eh_frame_end]
    ret

; void* get_dotnet_eh_table_start(void)
; Returns pointer to start of .dotnet_eh_table section
get_dotnet_eh_table_start:
    lea rax, [rel __dotnet_eh_table_start]
    ret

; void* get_dotnet_eh_table_end(void)
; Returns pointer to end of .dotnet_eh_table section
get_dotnet_eh_table_end:
    lea rax, [rel __dotnet_eh_table_end]
    ret

; =============================================================================
; Double-precision math (x87 FPU)
; =============================================================================

; double cos(double x)
cos:
    sub rsp, 8
    movsd [rsp], xmm0
    fld qword [rsp]
    fcos
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double sin(double x)
sin:
    sub rsp, 8
    movsd [rsp], xmm0
    fld qword [rsp]
    fsin
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double tan(double x)
tan:
    sub rsp, 8
    movsd [rsp], xmm0
    fld qword [rsp]
    fptan
    fstp st0        ; fptan pushes 1.0, we pop it to leave just the result
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double asin(double x)
; asin(x) = atan2(x, sqrt(1 - x*x))
asin:
    sub rsp, 24
    movsd [rsp], xmm0          ; save x

    ; compute 1 - x*x
    mulsd xmm0, xmm0           ; x*x
    movsd xmm1, [rel __math_one]
    subsd xmm1, xmm0           ; 1 - x*x
    sqrtsd xmm1, xmm1          ; sqrt(1 - x*x)

    ; load x and sqrt(1-x*x) to FPU, use fpatan
    movsd [rsp+8], xmm1        ; sqrt(1-x*x)
    fld qword [rsp+8]          ; ST0 = sqrt(1-x*x)
    fld qword [rsp]            ; ST0 = x, ST1 = sqrt(1-x*x)
    fpatan                     ; ST0 = atan2(x, sqrt(1-x*x)) = asin(x)
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 24
    ret

; double acos(double x)
; acos(x) = atan2(sqrt(1 - x*x), x)
acos:
    sub rsp, 24
    movsd [rsp], xmm0          ; save x

    ; compute sqrt(1 - x*x)
    mulsd xmm0, xmm0
    movsd xmm1, [rel __math_one]
    subsd xmm1, xmm0
    sqrtsd xmm1, xmm1

    ; fpatan computes atan2(ST1, ST0) = atan(ST1/ST0)
    ; We want atan2(sqrt(1-x*x), x)
    movsd [rsp+8], xmm1        ; sqrt(1-x*x)
    fld qword [rsp]            ; ST0 = x
    fld qword [rsp+8]          ; ST0 = sqrt(1-x*x), ST1 = x
    fpatan                     ; ST0 = atan2(sqrt(1-x*x), x) = acos(x)
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 24
    ret

; double atan(double x)
; atan(x) = fpatan(x, 1.0)
atan:
    sub rsp, 16
    movsd [rsp], xmm0
    fld1                       ; ST0 = 1.0
    fld qword [rsp]           ; ST0 = x, ST1 = 1.0
    fpatan                     ; ST0 = atan2(x, 1.0) = atan(x)
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 16
    ret

; double atan2(double y, double x)
; xmm0 = y, xmm1 = x
atan2:
    sub rsp, 16
    movsd [rsp], xmm1          ; x
    movsd [rsp+8], xmm0        ; y
    fld qword [rsp]            ; ST0 = x
    fld qword [rsp+8]          ; ST0 = y, ST1 = x
    fpatan                     ; ST0 = atan2(y, x)
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 16
    ret

; double exp(double x)
; exp(x) = 2^(x * log2(e))
; Uses: fldl2e, fmul, then 2^x via f2xm1 + integer part
exp:
    sub rsp, 16
    movsd [rsp], xmm0
    fld qword [rsp]           ; ST0 = x
    fldl2e                     ; ST0 = log2(e), ST1 = x
    fmulp                      ; ST0 = x * log2(e)

    ; Now compute 2^ST0
    ; Split into integer and fractional parts
    fld st0                    ; duplicate
    frndint                    ; ST0 = round(x*log2e) = int part
    fsub st1, st0              ; ST1 = fractional part
    fxch st1                   ; ST0 = frac, ST1 = int
    f2xm1                      ; ST0 = 2^frac - 1
    fld1
    faddp                      ; ST0 = 2^frac
    fscale                     ; ST0 = 2^frac * 2^int = 2^(x*log2e) = exp(x)
    fstp st1                   ; clean up ST1
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 16
    ret

; double log(double x)  -- natural log
; log(x) = ln(2) * log2(x)
log:
    sub rsp, 8
    movsd [rsp], xmm0
    fld1                       ; ST0 = 1.0
    fld qword [rsp]           ; ST0 = x, ST1 = 1.0
    fyl2x                      ; ST0 = 1.0 * log2(x)
    fldln2                     ; ST0 = ln(2), ST1 = log2(x)
    fmulp                      ; ST0 = ln(2) * log2(x) = ln(x)
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double log2(double x)
log2:
    sub rsp, 8
    movsd [rsp], xmm0
    fld1                       ; ST0 = 1.0
    fld qword [rsp]           ; ST0 = x, ST1 = 1.0
    fyl2x                      ; ST0 = 1.0 * log2(x) = log2(x)
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double log10(double x)
; log10(x) = log10(2) * log2(x)
log10:
    sub rsp, 8
    movsd [rsp], xmm0
    fld1                       ; ST0 = 1.0
    fld qword [rsp]           ; ST0 = x, ST1 = 1.0
    fyl2x                      ; ST0 = log2(x)
    fldlg2                     ; ST0 = log10(2), ST1 = log2(x)
    fmulp                      ; ST0 = log10(2) * log2(x) = log10(x)
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double pow(double x, double y)
; pow(x, y) = 2^(y * log2(x))
; xmm0 = x, xmm1 = y
pow:
    sub rsp, 16
    movsd [rsp], xmm0          ; x
    movsd [rsp+8], xmm1        ; y

    ; Check y == 0 -> return 1.0
    xorpd xmm2, xmm2
    ucomisd xmm1, xmm2
    jne .pow_nonzero_exp
    jp .pow_nonzero_exp
    movsd xmm0, [rel __math_one]
    add rsp, 16
    ret

.pow_nonzero_exp:
    ; Check x == 0
    ucomisd xmm0, xmm2
    jne .pow_normal
    jp .pow_normal
    ; x == 0: if y > 0 return 0, if y < 0 return +inf
    ucomisd xmm1, xmm2
    ja .pow_ret_zero
    movsd xmm0, [rel __math_pos_inf]
    add rsp, 16
    ret
.pow_ret_zero:
    xorpd xmm0, xmm0
    add rsp, 16
    ret

.pow_normal:
    ; Check if x is negative
    ucomisd xmm0, xmm2
    ja .pow_positive_base

    ; Negative base: check if y is integer
    movsd xmm3, xmm1
    roundsd xmm4, xmm3, 3      ; truncate y
    ucomisd xmm3, xmm4
    jne .pow_ret_nan
    jp .pow_ret_nan

    ; y is integer, negate x and compute, then fix sign
    movsd xmm5, xmm0
    movsd xmm0, [rel __math_neg_one]
    mulsd xmm0, xmm5           ; x = -x (now positive)
    movsd [rsp], xmm0

    ; Check if y is odd
    cvttsd2si rax, xmm1
    test rax, 1
    jz .pow_even_exp

    ; Odd exponent: result is negative
    fld qword [rsp+8]          ; y
    fld qword [rsp]            ; |x|
    fyl2x                      ; y * log2(|x|)
    fld st0
    frndint
    fsub st1, st0
    fxch st1
    f2xm1
    fld1
    faddp
    fscale
    fstp st1
    fchs                       ; negate result
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 16
    ret

.pow_even_exp:
    ; Even exponent: result is positive
    fld qword [rsp+8]          ; y
    fld qword [rsp]            ; |x|
    fyl2x
    fld st0
    frndint
    fsub st1, st0
    fxch st1
    f2xm1
    fld1
    faddp
    fscale
    fstp st1
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 16
    ret

.pow_ret_nan:
    movsd xmm0, [rel __math_nan]
    add rsp, 16
    ret

.pow_positive_base:
    ; pow(x,y) = 2^(y * log2(x))
    fld qword [rsp+8]          ; ST0 = y
    fld qword [rsp]            ; ST0 = x, ST1 = y
    fyl2x                      ; ST0 = y * log2(x)
    fld st0
    frndint                    ; ST0 = int part
    fsub st1, st0              ; ST1 = frac part
    fxch st1                   ; ST0 = frac, ST1 = int
    f2xm1                      ; ST0 = 2^frac - 1
    fld1
    faddp                      ; ST0 = 2^frac
    fscale                     ; ST0 = 2^frac * 2^int
    fstp st1
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 16
    ret

; =============================================================================
; Single-precision float math (wrappers calling double versions)
; =============================================================================

; float sinf(float x)
sinf:
    cvtss2sd xmm0, xmm0
    call sin
    cvtsd2ss xmm0, xmm0
    ret

; float cosf(float x)
cosf:
    cvtss2sd xmm0, xmm0
    call cos
    cvtsd2ss xmm0, xmm0
    ret

; float tanf(float x)
tanf:
    cvtss2sd xmm0, xmm0
    call tan
    cvtsd2ss xmm0, xmm0
    ret

; float expf(float x)
expf:
    cvtss2sd xmm0, xmm0
    call exp
    cvtsd2ss xmm0, xmm0
    ret

; float logf(float x)
logf:
    cvtss2sd xmm0, xmm0
    call log
    cvtsd2ss xmm0, xmm0
    ret

; float log2f(float x)
log2f:
    cvtss2sd xmm0, xmm0
    call log2
    cvtsd2ss xmm0, xmm0
    ret

; float log10f(float x)
log10f:
    cvtss2sd xmm0, xmm0
    call log10
    cvtsd2ss xmm0, xmm0
    ret

; float powf(float x, float y)
powf:
    cvtss2sd xmm0, xmm0
    cvtss2sd xmm1, xmm1
    call pow
    cvtsd2ss xmm0, xmm0
    ret

; =============================================================================
; Constants (read-only data)
; =============================================================================
section .rodata
align 16
__math_one:       dq 1.0
__math_neg_one:   dq -1.0
__math_nan:       dq 0x7FF8000000000000  ; canonical NaN
__math_pos_inf:   dq 0x7FF0000000000000  ; +infinity
