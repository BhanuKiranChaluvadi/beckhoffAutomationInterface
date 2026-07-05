{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetFlowRate EXTENDS ST_CmdHeader :
STRUCT
    mfcId    : INT;
    flowRate : REAL;    // Flow rate [sccm]
END_STRUCT
END_TYPE
