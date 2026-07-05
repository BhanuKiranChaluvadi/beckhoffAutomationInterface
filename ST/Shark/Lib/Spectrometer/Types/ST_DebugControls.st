TYPE ST_DebugControls :
STRUCT
    StartRatio   : UDINT;
    StopRatio    : UDINT;
    nTotalPixels : UINT;
    bApplyLimits : BOOL;
    Frequency    : E_ScanRate;
    bApplyFreq   : BOOL;
    TargetLight  : REAL;
    bApplyLight  : BOOL;
END_STRUCT
END_TYPE
