// Lib valve subsystem.
//
// Owns a small fixed-capacity array of I_Valve references. Count is supplied
// by the caller at Init time via the App-side GVL constant. Lib code never
// references App enums or GVL.
FUNCTION_BLOCK FB_ValveSubsystem IMPLEMENTS I_ValveSubsystem
VAR_OUTPUT
    bInitialized : BOOL := FALSE;
    bAnyError    : BOOL := FALSE;
    stFirstError : ST_Error;
END_VAR
VAR
    _ipValves : ARRAY[1..GVL_SubsystemSizes.MAX_VALVES] OF I_Valve;
    _nCount   : INT := 0;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    aipValves : ARRAY[*] OF I_Valve;
    nCount    : INT;
END_VAR
VAR
    i        : INT;
    nLower   : DINT;
END_VAR
    IF bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF nCount < 1 OR nCount > GVL_SubsystemSizes.MAX_VALVES THEN
        RETURN;
    END_IF

    nLower := LOWER_BOUND(aipValves, 1);
    IF nCount > (UPPER_BOUND(aipValves, 1) - nLower + 1) THEN
        RETURN;
    END_IF

    _nCount := nCount;
    FOR i := 1 TO _nCount DO
        _ipValves[i] := aipValves[nLower + i - 1];
    END_FOR

    bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
VAR
    i : INT;
END_VAR
    bAnyError := FALSE;
    stFirstError.eErrorId := E_ErrorId.NO_ERROR;
    stFirstError.sSource  := '';
    stFirstError.sMessage := '';

    FOR i := 1 TO _nCount DO
        IF _ipValves[i] <> 0 AND_THEN _ipValves[i].IsError() THEN
            bAnyError := TRUE;
            IF stFirstError.eErrorId = E_ErrorId.NO_ERROR THEN
                stFirstError := _ipValves[i].GetError();
            END_IF
        END_IF
    END_FOR

    Execute := TRUE;
END_METHOD

METHOD PUBLIC IsInitialized : BOOL
    IsInitialized := bInitialized;
END_METHOD

METHOD PUBLIC HasAnyError : BOOL
    HasAnyError := bAnyError;
END_METHOD

METHOD PUBLIC GetValveByIndex : I_Valve
VAR_INPUT
    nValveId : INT;
END_VAR
    IF nValveId >= 1 AND nValveId <= _nCount THEN
        GetValveByIndex := _ipValves[nValveId];
    ELSE
        GetValveByIndex := 0;
    END_IF
END_METHOD

END_FUNCTION_BLOCK
