(* Consolidated status structure for controllers. *)
TYPE ST_ControllerStatus :
STRUCT
    // Process Values
    rMeasuredValue      : LREAL;             // Current measurement
    rSetpoint           : LREAL;             // Active setpoint
    rOutput             : LREAL;             // Control output (0-100% or similar)
    // State Flags
    bEnabled            : BOOL;              // Operation enabled
    bAtSetpoint         : BOOL;              // Within tolerance
    bSafetyTripped      : BOOL;              // TRUE when a safety interlock has latched (heater forced OFF)
    // Diagnostics
    stError             : ST_Error;          // Active error details
END_STRUCT
END_TYPE
