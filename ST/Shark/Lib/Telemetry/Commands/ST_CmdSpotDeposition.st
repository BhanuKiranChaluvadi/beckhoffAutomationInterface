(*
ST_CmdSpotDeposition — Parameters for spot deposition ALD cycle sequence.
*)
{attribute 'pack_mode' := '0'}
TYPE ST_CmdSpotDeposition EXTENDS ST_CmdHeader :
STRUCT
    // Deposition cycle parameters
    nDepositionCycles             : DINT := 10;
    fPrecursorFlowRate            : REAL := 25.0;    // Precursor gas flow rate [sccm]
    fReactantFlowRate             : REAL := 25.0;    // Reactant gas flow rate [sccm]
    fInertMaxPurgeFlow            : REAL := 300.0;   // Maximum inert purge flow [sccm]
    fInertMinPurgeFlow            : REAL := 150.0;   // Minimum inert purge flow [sccm]
    nPrecursorBubblerIndex        : DINT := 1;       // Precursor bubbler index (1-3)
    nReactantBubblerIndex         : DINT := 1;       // Reactant bubbler index (1-3)

    // Pressure control parameters
    fDistributorOperatingPressure : REAL := 2.2;     // Distributor pressure [bar]
    fCombinerOperatingPressure    : REAL := 1.4;     // Combiner pressure [bar]
    fPressureTolerance            : REAL := 5.0;     // Pressure tolerance [%]

    // Timing parameters
    tReactantExposureTime         : DINT := 20000;   // Reactant exposure time [ms]
    tPrecursorExposureTime        : DINT := 20000;   // Precursor exposure time [ms]
    tInertPurgeTime               : DINT := 20000;   // Inert purge time [ms]
    tFinalPurgeTime               : DINT := 60000;   // Final purge time [ms]
END_STRUCT
END_TYPE
