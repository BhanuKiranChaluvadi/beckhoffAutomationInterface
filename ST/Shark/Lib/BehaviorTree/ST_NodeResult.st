TYPE ST_NodeResult :
STRUCT
    eStatus       : E_NodeStatus;
    rProgress     : REAL;
    bHasError     : BOOL;
    stError       : ST_Error;        // Populated if bHasError = TRUE
    sDescription  : STRING(255);     // Human-readable status (e.g. "Step 2/4: Heating")
END_STRUCT
END_TYPE
