(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(* Per-zone configuration table for the Shark temperature subsystem.
   Indexed by E_HeatZone. Each row describes ONE logical heat zone
   (composed of nChannelCount independent channel controllers).

   PID gains are NOT here: they are per-channel, in PID_GAINS (below),
   indexed by global channel index. Each channel of a zone may be tuned
   independently. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    ZONE_CONFIG : ARRAY[1..GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX] OF ST_TemperatureZoneConfig := [

        // Zone 1: HETR_HEAD - shared head heater (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 150.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 2: HETR_A_STR - A-side umbilical (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 3: HETR_A_CMB - A-side hot combiner / manifold (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 4: HETR_A_B1 - A-side bubbler 1 (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 5: HETR_A_B2 - A-side bubbler 2 (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 6: HETR_A_B3 - A-side bubbler 3 (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 7: HETR_B_STR - B-side umbilical (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 8: HETR_B_CMB - B-side hot combiner / manifold (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 9: HETR_B_B1 - B-side bubbler 1 (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 10: HETR_B_B2 - B-side bubbler 2 (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 11: HETR_B_B3 - B-side bubbler 3 (single channel)
        (bPopulated := TRUE,
            nChannelCount := 1,
            eAggregation := E_TempAggregation.MASTER,
            rMaxDeltaWarn := 0.0,  rMaxDeltaTrip := 0.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 140.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 12: HETR_EXH - Exhaust (2 channels: inner + outer)
        // Looser delta-trip; end segment loses heat faster than centre.
        (bPopulated := TRUE,
            nChannelCount := 2,
            eAggregation := E_TempAggregation.AVERAGE,
            rMaxDeltaWarn := 8.0,  rMaxDeltaTrip := 25.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 160.0,
            rHeatingRampRate := 2.0,  rCoolingRampRate := 0.0),

        // Zone 13: HETR_CHK - Chuck (2 channels: inner + outer)
        // Tight delta - wafer thermal coupling requires synchronised inner/outer.
        // Replaces the old FB_BeckhoffDualZoneCoord pair coordination.
        (bPopulated := TRUE,
            nChannelCount := 2,
            eAggregation := E_TempAggregation.AVERAGE,
            rMaxDeltaWarn := 3.0,  rMaxDeltaTrip := 10.0,
            rMinSetpoint := 20.0,  rMaxSetpoint := 300.0,
            rHeatingRampRate := 0.0,  rCoolingRampRate := 0.0)
    ];

    // -----------------------------------------------------------------------
    // PER-CHANNEL MAXIMUM DUTY CYCLE (heater output cap, %)
    // -----------------------------------------------------------------------
    // Hardware-imposed maximum heater duty cycle per channel (flat globalIdx
    // 1..CHANNEL_MAX, same ordering as PRG_TEMPERATURE_SENSOR / PRG_HEAT_ZONE).
    // Applied as the controller output cap (fYMax) AND clamps the autotune step,
    // so a channel can never exceed its rating even during tuning.
    // SAFETY/HARDWARE-CRITICAL: commissioning-signed values. Default 60 %.
    //
    // Channel layout (15 channels):
    //   1 HETR_HEAD   2 HETR_A_STR  3 HETR_A_CMB  4 HETR_A_B1  5 HETR_A_B2
    //   6 HETR_A_B3   7 HETR_B_STR  8 HETR_B_CMB  9 HETR_B_B1  10 HETR_B_B2
    //   11 HETR_B_B3  12..13 HETR_EXH (inner+outer)  14..15 HETR_CHK (inner+outer)
    MAX_DUTY_CYCLE : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF LREAL := [
        90.0,  // 1 HETR_HEAD
        60.0,  // 2 HETR_A_STR
        60.0,  // 3 HETR_A_CMB
        60.0,  // 4 HETR_A_B1
        60.0,  // 5 HETR_A_B2
        60.0,  // 6 HETR_A_B3
        60.0,  // 7 HETR_B_STR
        60.0,  // 8 HETR_B_CMB
        60.0,  // 9 HETR_B_B1
        60.0,  // 10 HETR_B_B2
        60.0,  // 11 HETR_B_B3
        60.0,  // 12 HETR_EXH (inner)
        60.0,  // 13 HETR_EXH (outer)
        60.0,  // 14 HETR_CHK (inner)
        60.0   // 15 HETR_CHK (outer)
    ];

    // -----------------------------------------------------------------------
    // PER-CHANNEL PID GAINS (one tuned gain set per heater channel)
    // -----------------------------------------------------------------------
    // Flat per-channel PID gains (globalIdx 1..CHANNEL_MAX, same ordering as
    // PRG_TEMPERATURE_SENSOR / PRG_HEAT_ZONE). Channels within a zone are
    // physically distinct bands and may each need different gains; the Beckhoff
    // autotune produces a per-channel result, so this table is the single place
    // to record tuned gains.
    //
    // Team tuning policy: tune one channel with all channels of the zone active
    // at the same setpoint, then copy the resulting gains into that channel's
    // row. Channels seeded with their zone's previous shared gains.
    //
    // Channel layout (15 channels):
    //   1 HETR_HEAD   2 HETR_A_STR  3 HETR_A_CMB  4 HETR_A_B1  5 HETR_A_B2
    //   6 HETR_A_B3   7 HETR_B_STR  8 HETR_B_CMB  9 HETR_B_B1  10 HETR_B_B2
    //   11 HETR_B_B3  12..13 HETR_EXH (inner+outer)  14..15 HETR_CHK (inner+outer)
    PID_GAINS : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF ST_HeatChannelPIDGains := [
        (rKp := 23.0, tTn := T#1m20s, tTv := T#22s, tTd := T#6s),  // 1 HETR_HEAD
        (rKp := 7.0,  tTn := T#1m50s, tTv := T#31s, tTd := T#8s),  // 2 HETR_A_STR
        (rKp := 24.0, tTn := T#1m,    tTv := T#16s, tTd := T#4s),  // 3 HETR_A_CMB
        (rKp := 9.0,  tTn := T#40s,   tTv := T#11s, tTd := T#3s),  // 4 HETR_A_B1
        (rKp := 9.0,  tTn := T#40s,   tTv := T#11s, tTd := T#3s),  // 5 HETR_A_B2
        (rKp := 9.0,  tTn := T#40s,   tTv := T#11s, tTd := T#3s),  // 6 HETR_A_B3
        (rKp := 9.5,  tTn := T#1m20s, tTv := T#22s, tTd := T#5s),  // 7 HETR_B_STR
        (rKp := 18.0, tTn := T#1m10s, tTv := T#19s, tTd := T#5s),  // 8 HETR_B_CMB
        (rKp := 9.0,  tTn := T#40s,   tTv := T#11s, tTd := T#3s),  // 9 HETR_B_B1
        (rKp := 9.0,  tTn := T#40s,   tTv := T#11s, tTd := T#3s),  // 10 HETR_B_B2
        (rKp := 9.0,  tTn := T#40s,   tTv := T#11s, tTd := T#3s),  // 11 HETR_B_B3
        (rKp := 5.0,  tTn := T#2m,    tTv := T#30s, tTd := T#8s),  // 12 HETR_EXH (inner)
        (rKp := 5.0,  tTn := T#2m,    tTv := T#30s, tTd := T#8s),  // 13 HETR_EXH (outer)
        (rKp := 12.0, tTn := T#45s,   tTv := T#12s, tTd := T#3s),  // 14 HETR_CHK (inner)
        (rKp := 12.0, tTn := T#45s,   tTv := T#12s, tTd := T#3s)   // 15 HETR_CHK (outer)
    ];

    // -----------------------------------------------------------------------
    // PER-CHANNEL DUAL-RTD DEVIATION LIMIT (°C)
    // -----------------------------------------------------------------------
    // Max allowed difference between a channel's A/B RTDs before FB_DualSensor
    // raises ERR_CTRL_DEVIATION_HIGH. Per channel because each band's RTD pair
    // sees a different physical gradient, so one global limit does not fit all.
    // Same global-index ordering as PID_GAINS / PRG_TEMPERATURE_SENSOR.
    // Single-RTD channels (CHK 14..15) run FB_DualSensor in single mode and
    // ignore this value (no pair to compare).
    //
    // Channel layout (15 channels):
    //   1 HETR_HEAD   2 HETR_A_STR  3 HETR_A_CMB  4 HETR_A_B1  5 HETR_A_B2
    //   6 HETR_A_B3   7 HETR_B_STR  8 HETR_B_CMB  9 HETR_B_B1  10 HETR_B_B2
    //   11 HETR_B_B3  12..13 HETR_EXH (inner+outer)  14..15 HETR_CHK (inner+outer)
    CHANNEL_MAX_DEVIATION : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF LREAL := [
        30.0,  // 1 HETR_HEAD
        30.0,  // 2 HETR_A_STR
        30.0,  // 3 HETR_A_CMB  — raised from 15.0: physical RTD spread 15-17 °C, was nuisance-tripping at 15.0
        30.0,  // 4 HETR_A_B1
        30.0,  // 5 HETR_A_B2
        30.0,  // 6 HETR_A_B3   — raised from 15.0: physical RTD spread 15-17 °C, was nuisance-tripping at 15.0
        30.0,  // 7 HETR_B_STR
        30.0,  // 8 HETR_B_CMB
        30.0,  // 9 HETR_B_B1
        30.0,  // 10 HETR_B_B2
        30.0,  // 11 HETR_B_B3
        30.0,  // 12 HETR_EXH (inner)
        30.0,  // 13 HETR_EXH (outer)
        15.0,  // 14 HETR_CHK (inner)
        15.0   // 15 HETR_CHK (outer)
    ];
END_VAR
