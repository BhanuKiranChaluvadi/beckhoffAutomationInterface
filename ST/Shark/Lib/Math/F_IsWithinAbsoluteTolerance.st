(* Checks if the measured value is within an absolute tolerance of the setpoint. *)
FUNCTION F_IsWithinAbsoluteTolerance : BOOL
VAR_INPUT
    rMeasured           : LREAL;            // Current measured value
    rSetpoint           : LREAL;            // Target setpoint
    rToleranceAbsolute  : LREAL;            // Tolerance in engineering units (e.g., 0.5)
    bEnabled            : BOOL := FALSE;    // Active flag
END_VAR
VAR
    rError : LREAL;
END_VAR

    // Not at setpoint if disabled
    IF NOT bEnabled THEN
        F_IsWithinAbsoluteTolerance := FALSE;
        RETURN;
    END_IF

    // Calculate absolute error and check against tolerance
    rError := ABS(rMeasured - rSetpoint);
    F_IsWithinAbsoluteTolerance := rError <= rToleranceAbsolute;

END_FUNCTION
