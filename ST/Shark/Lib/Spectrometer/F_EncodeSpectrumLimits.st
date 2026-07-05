(*
Encodes start/stop ratios + total pixels → packed 32-bit spectrum limit register.
Lower 16 bits = start ratio scaled to pixel count.
Upper 16 bits = stop ratio scaled to pixel count.
*)
FUNCTION F_EncodeSpectrumLimits : UDINT
VAR_INPUT
    nStartRatio  : UDINT;
    nStopRatio   : UDINT;
    nTotalPixels : UDINT;
END_VAR
VAR
    nScaledStart : UDINT;
    nScaledStop  : UDINT;
END_VAR
VAR CONSTANT
    RATIO_MAX : UDINT := 1000;
END_VAR
    IF RATIO_MAX = 0 THEN
        RETURN;
    END_IF
    nScaledStart := (nStartRatio * nTotalPixels) / RATIO_MAX;
    nScaledStop  := (nStopRatio * nTotalPixels) / RATIO_MAX;
    F_EncodeSpectrumLimits := (SHL(nScaledStop, 16) AND 16#FFFF0000) OR (nScaledStart AND 16#FFFF);
END_FUNCTION
