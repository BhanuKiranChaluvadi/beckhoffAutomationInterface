{attribute 'pack_mode' := '0'}
TYPE ST_PressureControllerTelemetry :
STRUCT
    Enable        : BOOL;
    Measure       : REAL;
    SetPoint      : REAL;
    ValveOutput   : REAL;
    TargetReached : BOOL;
END_STRUCT
END_TYPE
