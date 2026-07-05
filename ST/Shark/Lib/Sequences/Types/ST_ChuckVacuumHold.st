TYPE ST_ChuckVacuumHold :
STRUCT
    iSelectZone            : INT  := 1;      // Number of zones to activate (1-3)
    fLockPressureThreshold : REAL := 0.65;   // Target vacuum pressure threshold [bar]
    tPressureTimeout       : TIME := T#1M;   // Timeout for reaching vacuum [1 minute]
END_STRUCT
END_TYPE
