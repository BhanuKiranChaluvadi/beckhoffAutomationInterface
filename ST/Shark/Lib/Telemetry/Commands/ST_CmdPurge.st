(*
ST_CmdPurge — Parameters for a purge operation.
*)
{attribute 'pack_mode' := '0'}
TYPE ST_CmdPurge EXTENDS ST_CmdHeader :
STRUCT
    // Purge configuration
    mode    : INT;          // Purge mode: ChangeMaterial(1), ReplaceBubbler(2), Maintenance(3)
    side    : INT;          // Side selection: Precursor(1) / Reactant(2)
    bubbler : INT := 0;     // Bubbler index (1-3, for bubbler and maintenance purges)

    // Gas flow parameters
    inertGasFlow : REAL := 300.0;   // Inert gas flow rate [sccm]
    purgeGasFlow : REAL := 25.0;    // Purge gas flow rate [sccm]

    // Pressure parameters
    purgeHighPressureLimit       : REAL := 2.3;    // High pressure limit [bar]
    purgeLowPressureLimit        : REAL := 0.60;   // Low pressure limit [bar]
    hotManifoldOperatingPressure : REAL := 1.2;    // Hot manifold target pressure [bar]

    // Timeout parameters
    highPressureTimeout : DINT := 1800000;   // High pressure timeout [ms] (30 minutes)
    lowPressureTimeout  : DINT := 1800000;   // Low pressure timeout [ms] (30 minutes)

    // Cycle parameters
    coldManifoldPurgeCycles : INT := 3;   // Cold manifold purge cycles
    hotManifoldPurgeCycles  : INT := 3;   // Hot manifold purge cycles
    purgeLinePurgeCycles    : INT := 3;   // Purge line purge cycles
    umbilicalPurgeCycles    : INT := 3;   // Umbilical purge cycles

    // Time parameters
    umbilicalFlushTime : DINT := 300000;   // Umbilical flush time [ms] (5 minutes)
    stabilisationDelay : DINT := 35000;    // Stabilization delay [ms] (35 seconds)
END_STRUCT
END_TYPE
