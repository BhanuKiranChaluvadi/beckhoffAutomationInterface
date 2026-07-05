{attribute 'pack_mode' := '0'}
TYPE ST_CmdSetTemperatureGradient EXTENDS ST_CmdHeader :
STRUCT
    reactantCombiner  : REAL;   // Reactant hot manifold temperature [°C]
    precursorCombiner : REAL;   // Precursor hot manifold temperature [°C]
    headTemp          : REAL;   // Head temperature [°C]
    enableMode        : INT;    // 0=Both, 1=Precursor, 2=Reactant, 3=None
END_STRUCT
END_TYPE
