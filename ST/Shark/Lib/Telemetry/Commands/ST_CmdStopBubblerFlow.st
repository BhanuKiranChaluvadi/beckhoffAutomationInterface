{attribute 'pack_mode' := '0'}
TYPE ST_CmdStopBubblerFlow EXTENDS ST_CmdHeader :
STRUCT
    side : INT;    // Side selection: Precursor(1) / Reactant(2)
END_STRUCT
END_TYPE
