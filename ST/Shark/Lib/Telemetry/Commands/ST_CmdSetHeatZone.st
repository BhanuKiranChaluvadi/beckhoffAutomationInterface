{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetHeatZone EXTENDS ST_CmdHeader :
STRUCT
    zoneId       : INT;
    temp         : REAL := -1;    // Temperature setpoint [°C], -1 = disable
    positiveRamp : REAL;          // Positive ramp rate [°C/min], 0 = no ramp
    negativeRamp : REAL;          // Negative ramp rate [°C/min], 0 = no ramp
END_STRUCT
END_TYPE
