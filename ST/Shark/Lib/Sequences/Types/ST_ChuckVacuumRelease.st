TYPE ST_ChuckVacuumRelease :
STRUCT
    fReleasePressureThreshold : REAL := 0.9;    // Target release pressure threshold [bar]
    tPressureTimeout          : TIME := T#1M;   // Timeout for releasing vacuum [1 minute]
END_STRUCT
END_TYPE
