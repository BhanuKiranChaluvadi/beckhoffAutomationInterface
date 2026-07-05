(* Pure conversion: raw Modbus register counts to engineering units via linear interpolation. *)
FUNCTION F_RawCountsToEngineering : LREAL
VAR_INPUT
    wRawCounts      : WORD;
    rMinimumValue   : LREAL;
    rFullScale      : LREAL;
    wRawCountsMax   : WORD;
END_VAR
    IF wRawCountsMax = 0 OR rFullScale <= rMinimumValue THEN
        F_RawCountsToEngineering := 0.0;
        RETURN;
    END_IF

    F_RawCountsToEngineering := rMinimumValue
        + (WORD_TO_LREAL(wRawCounts) / WORD_TO_LREAL(wRawCountsMax))
        * (rFullScale - rMinimumValue);
END_FUNCTION
