(* Per-zone configuration row.

   PID gains are NOT held here: they are per-channel, in
   GVL_TemperatureZoneConfig.PID_GAINS (indexed by global channel index). *)
TYPE ST_TemperatureZoneConfig :
STRUCT
    bPopulated      : BOOL := FALSE;     // TRUE if zone has hardware

    // --- Composite zone structure ---
    nChannelCount   : INT := 1;                          // 1..GVL_HeatZone.MAX_CHANNELS
    eAggregation    : E_TempAggregation := E_TempAggregation.AVERAGE;
    rMaxDeltaWarn   : LREAL := 5.0;                      // advisory delta (deg)
    rMaxDeltaTrip   : LREAL := 15.0;                     // auto-disable threshold (deg)

    // --- Setpoint range ---
    rMinSetpoint    : LREAL := 20.0;
    rMaxSetpoint    : LREAL := 300.0;

    // --- Ramp (applied identically to every channel) ---
    rHeatingRampRate : LREAL := 2.0;
    rCoolingRampRate : LREAL := 0.0;
END_STRUCT
END_TYPE
