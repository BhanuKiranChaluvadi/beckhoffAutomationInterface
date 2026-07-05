{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetValve EXTENDS ST_CmdHeader :
STRUCT
    valveId : INT;
    enabled : BOOL;    // TRUE = open, FALSE = close
END_STRUCT
END_TYPE
