{attribute 'pack_mode' := '0'}
TYPE ST_CmdHeadMoveActuators EXTENDS ST_CmdHeader :
STRUCT
    axis      : INT;
    targetPos : REAL;
END_STRUCT
END_TYPE
