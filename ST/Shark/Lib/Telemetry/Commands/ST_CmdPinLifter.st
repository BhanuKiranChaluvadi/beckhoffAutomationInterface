{attribute 'pack_mode' := '0'}
TYPE ST_CmdPinLifter EXTENDS ST_CmdHeader :
STRUCT
    lift : BOOL;    // TRUE = lift pins up, FALSE = lower pins
END_STRUCT
END_TYPE
