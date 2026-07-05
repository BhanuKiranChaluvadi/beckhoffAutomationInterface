{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetHeatChannel EXTENDS ST_CmdHeader :
STRUCT
    channelId : INT;            // Flat global channel index (E_HeatChannel)
    temp      : REAL := -1;     // Temperature setpoint [°C], -1 = disable
END_STRUCT
END_TYPE
