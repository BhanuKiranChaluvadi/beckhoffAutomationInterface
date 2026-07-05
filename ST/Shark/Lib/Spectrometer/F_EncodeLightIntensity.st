(*
Encodes a light intensity percentage + mode into a 32-bit register value.
Fixed mode: lower 16 bits = percentage * DEVICE_SCALE_FACTOR
Auto mode:  upper 16 bits = percentage / 100 * ADC_12BIT_MAX
*)
FUNCTION F_EncodeLightIntensity : UDINT
VAR_INPUT
    fPercentage : REAL;
    bAutoAdjust : BOOL;
END_VAR
VAR CONSTANT
    ADC_12BIT_MAX       : REAL := 4095.0;
    DEVICE_SCALE_FACTOR : REAL := 10.0;
END_VAR
    IF bAutoAdjust THEN
        F_EncodeLightIntensity := SHL(TO_UDINT(fPercentage / 100.0 * ADC_12BIT_MAX), 16);
    ELSE
        F_EncodeLightIntensity := TO_UDINT(fPercentage * DEVICE_SCALE_FACTOR);
    END_IF
END_FUNCTION
