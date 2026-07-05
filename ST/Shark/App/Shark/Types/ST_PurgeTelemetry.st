{attribute 'pack_mode' := '0'}
TYPE ST_PurgeTelemetry :
STRUCT
    bEnable            : BOOL;
    nIterationTarget   : INT;
    nIterationActual   : INT;
    nState             : INT;
END_STRUCT
END_TYPE
