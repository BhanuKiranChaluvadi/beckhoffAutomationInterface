(*
Decodes a 32-bit light intensity register value.
Upper 16 bits ≠ 0 → auto-adjust mode (upper16 / ADC_12BIT_MAX * 100)
Upper 16 bits = 0 → fixed mode (lower16 / DEVICE_SCALE_FACTOR)
*)
FUNCTION F_DecodeLightIntensity : REAL
VAR_INPUT
    nRegisterValue : UDINT;
END_VAR
VAR_OUTPUT
    bAutoAdjust : BOOL;
END_VAR
VAR
    nUpper16 : UDINT;
    nLower16 : UDINT;
END_VAR
VAR CONSTANT
    ADC_12BIT_MAX       : REAL := 4095.0;
    DEVICE_SCALE_FACTOR : REAL := 10.0;
END_VAR
    nUpper16 := SHR(nRegisterValue, 16) AND 16#FFFF;
    nLower16 := nRegisterValue AND 16#FFFF;

    IF nUpper16 <> 0 THEN
        bAutoAdjust := TRUE;
        F_DecodeLightIntensity := TO_REAL(nUpper16) / ADC_12BIT_MAX * 100.0;
    ELSE
        bAutoAdjust := FALSE;
        F_DecodeLightIntensity := TO_REAL(nLower16) / DEVICE_SCALE_FACTOR;
    END_IF
END_FUNCTION
