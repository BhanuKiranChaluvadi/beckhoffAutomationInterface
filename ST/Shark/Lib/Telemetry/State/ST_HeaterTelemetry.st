{attribute 'pack_mode' := '0'}
TYPE ST_HeaterTelemetry :
STRUCT
    Enable             : BOOL;
    TempAvg            : REAL;
    SP                 : REAL;
    TemperatureReached : BOOL;
    Status             : INT;
END_STRUCT
END_TYPE
