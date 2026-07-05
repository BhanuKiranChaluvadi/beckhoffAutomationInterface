// Decodes a packed 32-bit spectrum limit register → start (lower 16) and stop (upper 16)
FUNCTION F_DecodeSpectrumLimits : BOOL
VAR_INPUT
    nRegisterValue : UDINT;
END_VAR
VAR_OUTPUT
    nStartRatio : UDINT;
    nStopRatio  : UDINT;
END_VAR
    nStartRatio := nRegisterValue AND 16#FFFF;
    nStopRatio  := SHR(nRegisterValue, 16) AND 16#FFFF;
    F_DecodeSpectrumLimits := TRUE;
END_FUNCTION
