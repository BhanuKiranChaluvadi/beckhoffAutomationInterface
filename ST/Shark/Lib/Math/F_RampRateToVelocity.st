(* Converts ramp rate from °C/min to velocity °C/sec. *)
FUNCTION F_RampRateToVelocity : LREAL
VAR_INPUT
    rRate_DegPerMin : LREAL;  // Ramp rate in degrees per minute
END_VAR
VAR CONSTANT
    SECONDS_PER_MINUTE : LREAL := 60.0;
END_VAR
    F_RampRateToVelocity := rRate_DegPerMin / SECONDS_PER_MINUTE;
END_FUNCTION
