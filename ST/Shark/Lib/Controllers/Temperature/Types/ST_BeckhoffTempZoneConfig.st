(* Heating zone configuration with temperature limits and controller parameters. *)
TYPE ST_BeckhoffTempZoneConfig :
STRUCT
    sZoneID             : STRING(20) := '';
    rMinSetpoint        : LREAL := 0.0;
    rMaxSetpoint        : LREAL := 300.0;
    rSafetyMaxTemp      : LREAL := 350.0;
    rAbsoluteMaxTemp    : LREAL := 400.0;
    // Independent safety-thermocouple over-temperature interlock (optional).
    // rSafetyTcTripLimit <= 0 means "no safety TC configured" (InitSafety is a no-op).
    rSafetyTcTripLimit       : LREAL := 0.0;   // (°C) TC over-temp trip threshold
    rSafetyTcResetHysteresis : LREAL := 5.0;   // (°C) must cool this far below limit to reset
    rSetpointTolerance  : LREAL := 0.5;     // Generic default for absolute temp tolerance
    tMaxContinuousOn    : TIME := T#10M;
    rOutputMin          : LREAL := 0.0;
    rOutputMax          : LREAL := 100.0;
    rOutputSafeDefault  : LREAL := 0.0;
    bAutoEnable         : BOOL := TRUE;
    stControllerParams  : ST_CTRL_TempCtrlParameter;
    stExternalPID       : ST_CTRL_ParaController;
END_STRUCT
END_TYPE
