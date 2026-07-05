(* Checks if measured value is within percentage tolerance of setpoint. *)
FUNCTION F_IsWithinTolerance : BOOL
VAR_INPUT
    rMeasured          : LREAL;    // Current measured value
    rSetpoint          : LREAL;    // Target setpoint
    rTolerancePercent  : LREAL;    // Tolerance as percentage (e.g., 2.0 = 2%)
    bEnabled           : BOOL;     // Controller must be enabled
END_VAR
VAR
    rTolerance : LREAL;
    rError     : LREAL;
END_VAR

    // Not at setpoint if disabled or zero setpoint
    IF NOT bEnabled OR rSetpoint = 0.0 THEN
        F_IsWithinTolerance := FALSE;
        RETURN;
    END_IF

    // Calculate tolerance band and check
    rTolerance := ABS(rSetpoint * rTolerancePercent / 100.0);
    rError := ABS(rMeasured - rSetpoint);
    F_IsWithinTolerance := rError <= rTolerance;

END_FUNCTION
