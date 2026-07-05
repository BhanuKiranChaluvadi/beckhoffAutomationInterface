{attribute 'pack_mode' := '0'}
TYPE ST_CmdStartBubblerFlow EXTENDS ST_CmdHeader :
STRUCT
    side                          : INT  := 1;          // Side: Precursor(1) / Reactant(2)
    flowRate                      : REAL := 10.0;       // Flow rate [sccm]
    bubblerIndex                  : INT  := 1;          // Bubbler index (1-3)
    hotManifoldOperatingPressure  : REAL := 1.4;        // Hot manifold pressure [bar]
    hotManifoldPressureTimeout    : REAL := 1800000;    // HM pressure timeout [ms] (30 minutes)
    coldManifoldOperatingPressure : REAL := 1.4;        // Cold manifold pressure [bar]
    coldManifoldPressureTimeout   : REAL := 1800000;    // CM pressure timeout [ms] (30 minutes)
    MFCStabilizeTimeout           : REAL := 1800000;    // MFC stabilization timeout [ms] (30 minutes)
    MFCFlowRateTolerance          : REAL := 5.0;        // Flow rate tolerance [%]
    maxDepressurizingAttempts     : INT  := 3;          // Max depressurization attempts
END_STRUCT
END_TYPE
