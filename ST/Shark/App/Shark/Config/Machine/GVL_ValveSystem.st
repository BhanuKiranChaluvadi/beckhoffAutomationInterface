(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // Valve counts by type
    PNEUMATIC_VALVE_COUNT  : INT := 29;  // SMC Block: valves 1-29
    SOLENOID_VALVE_COUNT   : INT := 6;   // Digital outputs: valves 30-35
    VALVE_COUNT            : INT := 35;
END_VAR
