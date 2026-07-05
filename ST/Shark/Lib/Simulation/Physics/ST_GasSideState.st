(* ST_GasSideState - Valve and controller state for one gas side

DESIGN PRINCIPLES:
- Single Responsibility: Groups all valve states for one side
- DRY: Allows single simulation method to handle both A and B sides
- KISS: Simple data structure, no logic
*)
TYPE ST_GasSideState :
STRUCT
    // Gas Input
    bGasIsoOpen : BOOL;             // Carrier gas isolation valve
    rMFCSetpoint : LREAL;           // MFC flow setpoint (sccm)

    // Cold Manifold (CM)
    bCMBubbler1Open : BOOL;         // CM bubbler valve 1
    bCMBubbler2Open : BOOL;         // CM bubbler valve 2 (A-side only)
    bCMBubbler3Open : BOOL;         // CM bubbler valve 3 (A-side only)
    bCMPurgeOpen : BOOL;            // CM purge valve
    bCMVacuumIsoOpen : BOOL;        // CM to exhaust

    // Hot Manifold (HM)
    bHMBubbler1Open : BOOL;         // HM bubbler valve 1
    bHMBubbler2Open : BOOL;         // HM bubbler valve 2 (A-side only)
    bHMBubbler3Open : BOOL;         // HM bubbler valve 3 (A-side only)
    bHMPurgeOpen : BOOL;            // HM purge valve
    bHMVacuumIsoOpen : BOOL;        // HM to exhaust

    // Output
    bNozzleIsoOpen : BOOL;          // Nozzle isolation valve
    rPCVSetpoint : LREAL;           // Pressure controller setpoint (bar)
END_STRUCT
END_TYPE
