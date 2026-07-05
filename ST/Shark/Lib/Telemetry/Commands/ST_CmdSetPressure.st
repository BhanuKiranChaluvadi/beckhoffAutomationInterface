{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetPressure EXTENDS ST_CmdHeader :
STRUCT
    psctrlId : INT;
    pressure : REAL;    // Pressure setpoint [bar]
END_STRUCT
END_TYPE
