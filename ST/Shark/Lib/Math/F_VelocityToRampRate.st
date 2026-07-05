(* Converts velocity from °C/sec to ramp rate °C/min. *)
FUNCTION F_VelocityToRampRate : LREAL
VAR_INPUT
    rVelocity_DegPerSec : LREAL;  // Velocity in degrees per second
END_VAR
VAR CONSTANT
    SECONDS_PER_MINUTE : LREAL := 60.0;
END_VAR
    F_VelocityToRampRate := rVelocity_DegPerSec * SECONDS_PER_MINUTE;
END_FUNCTION
