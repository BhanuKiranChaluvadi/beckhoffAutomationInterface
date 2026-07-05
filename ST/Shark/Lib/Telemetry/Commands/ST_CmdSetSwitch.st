{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetSwitch EXTENDS ST_CmdHeader :
STRUCT
    switchId : INT;
    enabled  : BOOL;
END_STRUCT
END_TYPE
