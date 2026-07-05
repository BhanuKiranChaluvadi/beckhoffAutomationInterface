(* Configuration for one logical heat zone (composite of N channel controllers).

   The zone itself does NOT close a control loop — each channel has its own
   PID. The zone coordinates: one setpoint pushed to all channels, aggregated
   readback, delta safety, and rolled-up health. *)
TYPE ST_HeatZoneConfig :
STRUCT
    sZoneID        : STRING(20) := '';            // Human-readable zone identifier (e.g. 'BUB_P_1')

    // --- Aggregation ---
    eAggregation   : E_TempAggregation := E_TempAggregation.AVERAGE;
                                                  // How to compute rAggregated from channel readings

    // --- Delta safety (uneven heating across channels) ---
    rMaxDeltaWarn  : LREAL := 5.0;                // (°C) Spread above this raises a warning, zone stays enabled
    rMaxDeltaTrip  : LREAL := 15.0;               // (°C) Spread above this auto-disables the zone (bDeltaTripped)
                                                  // Set rMaxDeltaTrip = 0 to disable delta-trip protection

    // --- Setpoint clamp (pre-flight bounds; per-channel bounds still apply) ---
    rMinSetpoint   : LREAL := 0.0;
    rMaxSetpoint   : LREAL := 300.0;

    // --- Channel safety-thermocouple trip propagation ---
    bChannelTripStopsZone : BOOL := TRUE;   // TRUE: any channel's safety-TC over-temp trip
                                            // latches the whole zone disabled (manual Reset).
                                            // FALSE: only the offending channel cuts its heater.
END_STRUCT
END_TYPE
