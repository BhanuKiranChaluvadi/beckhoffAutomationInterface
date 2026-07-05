(* Resolves the COMPLETE per-channel controller configuration for one heat channel.

   This is the single boundary between the GVL config tables and the temperature
   subsystem: FB_TemperatureSubsystem consumes the returned struct instead of
   reaching into GVL_TemperatureZoneConfig itself (DIP).

   It composes the per-zone template (F_GetBeckhoffTempZoneConfig) and layers the
   per-channel specialisation on top: identity, single-authority enable policy,
   per-channel PID gains, and per-channel duty-cycle cap.

   (Mirror of the PRO function. Shark has no safety thermocouple, so that
   PRO block is intentionally absent here.)

   Returns sZoneID = 'INVALID' for an out-of-range / unpopulated zone. *)
FUNCTION F_GetBeckhoffTempChannelConfig : ST_BeckhoffTempZoneConfig
VAR_INPUT
    eZone          : E_HeatZone;   // 1..CONTROLLABLE_ZONE_MAX
    nGlobalIdx     : INT;          // 1..CHANNEL_MAX (flat channel index)
    nChannelInZone : INT;          // 1..nChannelCount (position within the zone)
END_VAR
VAR
    stConfig : ST_BeckhoffTempZoneConfig;
    fDuty    : LREAL;
END_VAR
    // Zone-level template: setpoint clamp, output limits, timing, ramp.
    stConfig := F_GetBeckhoffTempZoneConfig(eZone := eZone);
    IF stConfig.sZoneID = 'INVALID' THEN
        F_GetBeckhoffTempChannelConfig := stConfig;
        RETURN;
    END_IF

    // The ZONE is the single control authority — channels must never self-enable;
    // enable/disable always cascades from FB_HeatZone.
    stConfig.bAutoEnable := FALSE;

    // Per-channel identity for error traceability, e.g. 'HETR_HEAD_E1'.
    stConfig.sZoneID := CONCAT(TO_STRING(eZone), CONCAT('_E', TO_STRING(nChannelInZone)));

    // Per-channel maximum duty cycle (hardware-rated cap): used as the controller
    // output limit AND as a clamp on the autotune step, so a channel can never
    // exceed its rating, even while tuning.
    IF nGlobalIdx >= 1 AND nGlobalIdx <= GVL_TemperatureZones.CHANNEL_MAX THEN
        fDuty := GVL_TemperatureZoneConfig.MAX_DUTY_CYCLE[nGlobalIdx];
        stConfig.rOutputMax := fDuty;
        stConfig.stControllerParams.fYMax := fDuty;
        IF stConfig.stControllerParams.fYTuneHeating > fDuty THEN
            stConfig.stControllerParams.fYTuneHeating := fDuty;
        END_IF
    END_IF

    // Per-channel PID gains. Channels in a zone are physically distinct bands
    // and may each have their own tuned gains (autotune results live here).
    IF nGlobalIdx >= 1 AND nGlobalIdx <= GVL_TemperatureZones.CHANNEL_MAX THEN
        stConfig.stExternalPID.KpHeat := GVL_TemperatureZoneConfig.PID_GAINS[nGlobalIdx].rKp;
        stConfig.stExternalPID.TnHeat := GVL_TemperatureZoneConfig.PID_GAINS[nGlobalIdx].tTn;
        stConfig.stExternalPID.TvHeat := GVL_TemperatureZoneConfig.PID_GAINS[nGlobalIdx].tTv;
        stConfig.stExternalPID.TdHeat := GVL_TemperatureZoneConfig.PID_GAINS[nGlobalIdx].tTd;
    END_IF

    F_GetBeckhoffTempChannelConfig := stConfig;
END_FUNCTION
