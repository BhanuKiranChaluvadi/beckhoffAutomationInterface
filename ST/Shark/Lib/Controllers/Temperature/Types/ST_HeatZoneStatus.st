(* Aggregated status published by FB_HeatZone.

   A zone wraps N channel controllers (each = one PID + one RTD + one SSR).
   This struct collapses N readings + N statuses into one set of numbers
   the HMI / sequence layer can consume. *)
TYPE ST_HeatZoneStatus :
STRUCT
    // ───── Setpoint / commanded ─────
    rSetpoint      : LREAL;   // The single setpoint the zone pushes to every channel.
                              // Example: operator sets BUB_P_1 to 80°C
                              //          -> all 3 channels (A,B,C) receive 80°C.

    // ───── Aggregated reading (the "headline" temperature for HMI) ─────
    rAggregated    : LREAL;   // ONE number that represents the zone as a whole.
                              // HOW it is computed depends on stConfig.eAggregation:
                              //   AVERAGE : (T1 + T2 + ... + Tn) / n         ← typical default
                              //   MAXIMUM : hottest channel                  ← conservative for safety display
                              //   MINIMUM : coldest channel                  ← "are we ready yet?"
                              //   MASTER  : channel[1] only                  ← legacy single-sensor behavior
                              // Example (AVERAGE, BUB_P_1 = 78, 80, 82°C):
                              //   rAggregated = 80.0

    // ───── Spread across the zone (diagnostic) ─────
    rMin           : LREAL;   // Coldest channel's measurement (°C). Example above: 78.0
    rMax           : LREAL;   // Hottest channel's measurement (°C). Example above: 82.0
    rDelta         : LREAL;   // rMax - rMin (°C). Indicates uneven heating.
                              // Example above: 4.0
                              // If rDelta > rMaxDeltaWarn → bDeltaWarning set (HMI should warn).
                              // If rDelta > rMaxDeltaTrip → zone auto-disables (bDeltaTripped).
    nColdestIdx    : INT;     // 1..nChannelCount — which channel is coldest (helps tech locate bad RTD/SSR)
    nHottestIdx    : INT;     // 1..nChannelCount — which channel is hottest

    // ───── Convergence (sequences should wait on this, not on rAggregated) ─────
    bAllAtSetpoint : BOOL;    // TRUE only when EVERY channel reports at-setpoint.
                              // Example: 2 of 3 channels at 80°C, third still at 65°C → FALSE.

    // ───── Health rollup ─────
    bAnyError      : BOOL;    // TRUE if ANY channel has a sensor fault, watchdog trip, or PID error.
                              // Use stFirstError + nFirstErrorIdx to find which one.
    nFirstErrorIdx : INT;     // 1..nChannelCount of the first channel raising an error this scan.
                              // 0 if no error.
    stFirstError   : ST_Error;// Copy of the first failing channel's error record.

    bDeltaWarning  : BOOL;    // rDelta > rMaxDeltaWarn (advisory, zone stays enabled).
    bDeltaTripped  : BOOL;    // rDelta > rMaxDeltaTrip — zone disabled itself.
                              // Cleared by Reset(). Indicates real hardware imbalance — needs attention.

    // ───── Safety-thermocouple trip rollup ─────
    bSafetyTripped       : BOOL;  // TRUE when a channel safety-TC over-temp latched the whole zone.
                                  // Cleared by Reset() once the TC has cooled below its threshold.
    nSafetyTrippedIdx    : INT;   // 1..nChannelCount of the first channel whose safety TC tripped (0 = none).

    // ───── External main-power interlock (e.g. MCR) ─────
    bExternalPowerCut    : BOOL;  // TRUE when external main heater power is cut (zone forced disabled).
                                  // Live-follows the external signal; not latched here.

    // ───── Channel count (so HMI knows how to render) ─────
    nChannelCount  : INT;     // How many channels this zone actually has (1..MAX_CHANNELS).
                              // Example: BUB_P_1 = 3 (A,B,C); HEAD = 1 (single-channel zone).

    // ───── Identification ─────
    sZoneID        : STRING(20); // Mirrors stConfig.sZoneID for convenience
END_STRUCT
END_TYPE
