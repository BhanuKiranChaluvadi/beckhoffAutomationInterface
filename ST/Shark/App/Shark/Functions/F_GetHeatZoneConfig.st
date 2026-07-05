(* Builds an ST_HeatZoneConfig from the per-zone row in GVL_TemperatureZoneConfig.
   Used by FB_TemperatureSubsystem when initialising each FB_HeatZone. *)
FUNCTION F_GetHeatZoneConfig : ST_HeatZoneConfig
VAR_INPUT
    eZone : E_HeatZone;
END_VAR
VAR
    stConfig     : ST_HeatZoneConfig;
    stZoneConfig : ST_TemperatureZoneConfig;
END_VAR

    IF eZone < 1 OR eZone > GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN
        stConfig.sZoneID := 'INVALID';
        F_GetHeatZoneConfig := stConfig;
        RETURN;
    END_IF

    stZoneConfig := GVL_TemperatureZoneConfig.ZONE_CONFIG[eZone];

    IF NOT stZoneConfig.bPopulated THEN
        stConfig.sZoneID := 'INVALID';
        F_GetHeatZoneConfig := stConfig;
        RETURN;
    END_IF

    stConfig.sZoneID    := TO_STRING(eZone);
    stConfig.eAggregation := stZoneConfig.eAggregation;
    stConfig.rMaxDeltaWarn := stZoneConfig.rMaxDeltaWarn;
    stConfig.rMaxDeltaTrip := stZoneConfig.rMaxDeltaTrip;
    stConfig.rMinSetpoint := stZoneConfig.rMinSetpoint;
    stConfig.rMaxSetpoint := stZoneConfig.rMaxSetpoint;

    F_GetHeatZoneConfig := stConfig;
END_FUNCTION
