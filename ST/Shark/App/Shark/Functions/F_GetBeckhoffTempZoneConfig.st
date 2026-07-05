(* Returns zone-specific configuration directly. *)
FUNCTION F_GetBeckhoffTempZoneConfig : ST_BeckhoffTempZoneConfig
VAR_INPUT
    eZone : E_HeatZone;
END_VAR
VAR
    stConfig : ST_BeckhoffTempZoneConfig;
    stParams : ST_CTRL_TempCtrlParameter;
    stZoneConfig : ST_TemperatureZoneConfig;
END_VAR

    IF eZone < 1 OR eZone > GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN     
        stConfig.sZoneID := 'INVALID';
        F_GetBeckhoffTempZoneConfig := stConfig;
        RETURN;
    END_IF

    stZoneConfig := GVL_TemperatureZoneConfig.ZONE_CONFIG[eZone];

    // Skip unpopulated zones
    IF NOT stZoneConfig.bPopulated THEN
        stConfig.sZoneID := 'INVALID';
        F_GetBeckhoffTempZoneConfig := stConfig;
        RETURN;
    END_IF

    // Zone limits and safety thresholds
    stConfig.rMinSetpoint := stZoneConfig.rMinSetpoint;
    stConfig.rMaxSetpoint := stZoneConfig.rMaxSetpoint;

    // Dynamic safety limit: Zone Max + Margin (e.g. 400 + 10 = 410)
    stConfig.rSafetyMaxTemp := stZoneConfig.rMaxSetpoint + GVL_TemperatureSystem.TEMP_SAFETY_MARGIN;
    stConfig.rAbsoluteMaxTemp := stZoneConfig.rMaxSetpoint + GVL_TemperatureSystem.TEMP_ABSOLUTE_MARGIN;

    stConfig.rSetpointTolerance := GVL_TemperatureSystem.CTRL_SETPOINT_TOLERANCE;
    stConfig.tMaxContinuousOn := GVL_TemperatureSystem.CTRL_MAX_CONTINUOUS_ON;  
    stConfig.rOutputMin := GVL_TemperatureSystem.OUTPUT_MIN;
    stConfig.rOutputMax := GVL_TemperatureSystem.OUTPUT_MAX;
    stConfig.rOutputSafeDefault := GVL_TemperatureSystem.OUTPUT_SAFE_DEFAULT;   

    // Controller mode and failure behavior
    stParams.iMode := E_CTRL_ControlMode.eCTRL_ControlMode_HEATING;
    stParams.iReactionOnFailure := E_CTRL_ReactionOnFailure.eCTRL_ReactionOnFailure_StopController;
    stParams.bSelCtrlParameterSet := TRUE;
    stParams.dwAlarmSupp := GVL_TemperatureSystem.CTRL_ALARM_SUPPRESSION;       

    // Timing configuration
    stParams.tCtrlCycleTime := GVL_TemperatureSystem.CTRL_CYCLE_TIME;
    stParams.tTaskCycleTime := GVL_TemperatureSystem.CTRL_TASK_CYCLE_TIME;      

    // Auto-tuning parameters
    stParams.iTuningMode := E_CTRL_TuneMode.eCTRL_TuneMode_HEATING;
    stParams.tTuneStabilisation := GVL_TemperatureSystem.CTRL_TUNE_STABILIZATION;
    stParams.fEndTunePercentHeating := GVL_TemperatureSystem.TUNE_END_PERCENT_HEATING;
    stParams.fYTuneHeating := GVL_TemperatureSystem.TUNE_Y_HEATING;
    stParams.fYStableHeating := GVL_TemperatureSystem.TUNE_Y_STABLE;
    stParams.fScalingFactor := GVL_TemperatureSystem.CTRL_SCALING_FACTOR;       

    // Ramping configuration
    stParams.bEnableSoftStart := TRUE;
    stParams.bEnableRamping := TRUE;
    stParams.fWVeloPos := F_RampRateToVelocity(stZoneConfig.rHeatingRampRate);  
    stParams.fWVeloNeg := F_RampRateToVelocity(stZoneConfig.rCoolingRampRate);  

    // Output limits
    stParams.fYMin := GVL_TemperatureSystem.OUTPUT_MIN;
    stParams.fYMax := GVL_TemperatureSystem.OUTPUT_MAX;
    stParams.fYManual := GVL_TemperatureSystem.OUTPUT_MANUAL_DEFAULT;
    stParams.fYOnFailure := GVL_TemperatureSystem.OUTPUT_SAFE_DEFAULT;

    // PWM configuration
    stParams.tPWMCycleTime := GVL_TemperatureSystem.CTRL_PWM_CYCLE_TIME;        
    stParams.tPWMMinOffTime := GVL_TemperatureSystem.CTRL_PWM_MIN_TIME;
    stParams.tPWMMinOnTime := GVL_TemperatureSystem.CTRL_PWM_MIN_TIME;

    stParams.iControllerType := E_CTRL_ControllerType.eCTRL_ControllerType_PID; 

    // Alarm thresholds (relative to setpoint)
    stParams.TempLow := GVL_TemperatureSystem.ALARM_TEMP_LOW;
    stParams.TempLowLow := GVL_TemperatureSystem.ALARM_TEMP_LOW_LOW;
    stParams.TempHigh := GVL_TemperatureSystem.ALARM_TEMP_HIGH;
    stParams.TempHighHigh := GVL_TemperatureSystem.ALARM_TEMP_HIGH_HIGH;        
    stParams.TempAbsoluteHigh := GVL_TemperatureSystem.SENSOR_MAX_TEMP;
    stParams.TempAbsoluteLow := GVL_TemperatureSystem.SENSOR_MIN_RANGE;

    // PID tuning multipliers
    stParams.fTuneKp := GVL_TemperatureSystem.TUNE_KP;
    stParams.fTuneTn := GVL_TemperatureSystem.TUNE_TN;
    stParams.fTuneTv := GVL_TemperatureSystem.TUNE_TV;
    stParams.fTuneTd := GVL_TemperatureSystem.TUNE_TD;

    // Setpoint limits
    stParams.fWMin := stZoneConfig.rMinSetpoint;
    stParams.fWMax := stZoneConfig.rMaxSetpoint;

    // NOTE: PID gains (stExternalPID) are NOT set here. They are per-channel and
    // applied in F_GetBeckhoffTempChannelConfig from GVL_TemperatureZoneConfig.PID_GAINS.

    stConfig.stControllerParams := stParams;
    stConfig.sZoneID := TO_STRING(eZone);

    F_GetBeckhoffTempZoneConfig := stConfig;
END_FUNCTION

