(*
ST_CmdBubblerLeakTest — Parameters for bubbler / manifold leak test sequence.
*)
{attribute 'pack_mode' := '0'}
TYPE ST_CmdBubblerLeakTest EXTENDS ST_CmdHeader :
STRUCT
    // Basic test parameters
    side                   : INT  := 1;           // 1=A_SIDE, 2=B_SIDE
    bubblerIndex           : DINT := 1;           // Bubbler index (1-3)
    leakThreshold          : REAL := 0.5;         // Leak threshold [bar]
    testDuration           : DINT := 300000;      // Test duration [ms] (5 minutes)
    stabilizationTime      : DINT := 60000;       // Stabilization time [ms] (1 minute)

    // Pressure and flow parameters
    fillPressure           : REAL := 2.5;         // Fill pressure [bar]
    testFlowRate           : REAL := 20.0;        // Test flow rate [sccm]
    pressureTimeout        : DINT := 600000;      // Pressure timeout [ms] (10 minutes)
    pressureTolerance      : REAL := 5.0;         // Pressure tolerance [%]

    // System pressure normalization parameters
    normalPressure         : REAL := 1.4;         // Normal operating pressure [bar]
    depressurizeTarget     : REAL := 0.5;         // Depressurization target [bar]
    normalizationFlow      : REAL := 20.0;        // Flow rate for normalization [sccm]
END_STRUCT
END_TYPE
