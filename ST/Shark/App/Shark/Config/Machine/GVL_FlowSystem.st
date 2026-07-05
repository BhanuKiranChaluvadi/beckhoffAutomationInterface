(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // Flow Ranges - Precursor MFC (0-25 sccm)
    MIN_FLOW_PRECURSOR_SCCM : REAL := 0.0;
    MAX_FLOW_PRECURSOR_SCCM : REAL := 25.0;

    // Flow Ranges - Reactant MFC (0-25 sccm)
    MIN_FLOW_REACTANT_SCCM  : REAL := 0.0;
    MAX_FLOW_REACTANT_SCCM  : REAL := 25.0;

    // Flow Ranges - Inert MFC (0-500 sccm)
    MIN_FLOW_INERT_SCCM     : REAL := 0.0;
    MAX_FLOW_INERT_SCCM     : REAL := 500.0;

    // System Configuration
    FLOW_ZONE_COUNT          : INT := 3;

    // Tolerance for setpoint boundary detection
    SETPOINT_TOLERANCE       : REAL := 0.01;
END_VAR
