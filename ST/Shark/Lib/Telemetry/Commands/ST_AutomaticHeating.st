TYPE ST_AutomaticHeating :
STRUCT
    // Enable mode: both, precursor only, reactant only, or none
    eEnableMode               : E_TemperatureGradientEnableMode;

    // Precursor side temperatures (°C)
    fPrecursorBubblerLineTemp : REAL;
    fPrecursorHotManifoldTemp : REAL;
    fPrecursorUmbilicalTemp   : REAL;

    // Reactant side temperatures (°C)
    fReactantBubblerLineTemp  : REAL;
    fReactantHotManifoldTemp  : REAL;
    fReactantUmbilicalTemp    : REAL;

    // Head temperature (°C)
    fHeadTemperature          : REAL;
END_STRUCT
END_TYPE
