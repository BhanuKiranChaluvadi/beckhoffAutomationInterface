(* Pure conversion: engineering units to raw Modbus register counts via linear interpolation. *)
FUNCTION F_EngineeringToRawCounts : WORD
VAR_INPUT
    rValue          : LREAL;
    rMinimumValue   : LREAL;
    rFullScale      : LREAL;
    wRawCountsMax   : WORD;
END_VAR
VAR
    rFraction  : LREAL;
    rRawCounts : LREAL;
END_VAR
    IF wRawCountsMax = 0 OR rFullScale <= rMinimumValue THEN
        F_EngineeringToRawCounts := 0;
        RETURN;
    END_IF

    rFraction := (rValue - rMinimumValue) / (rFullScale - rMinimumValue);
    rFraction := LIMIT(0.0, rFraction, 1.0);

    rRawCounts := rFraction * WORD_TO_LREAL(wRawCountsMax);
    F_EngineeringToRawCounts := LREAL_TO_WORD(LIMIT(0.0, rRawCounts, WORD_TO_LREAL(wRawCountsMax)));
END_FUNCTION
