{attribute 'pack_mode' := '0'}
TYPE ST_MachineStateTelemetry :
STRUCT
    ChuckPumpState   : BOOL;
    ExhaustPumpState : BOOL;
    AmbTemperature   : REAL;
    AmbHumidity      : REAL;
END_STRUCT
END_TYPE
