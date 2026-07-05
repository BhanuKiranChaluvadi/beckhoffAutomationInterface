// Lib flow subsystem.
//
// Owns a small fixed-capacity array of I_Controller references (one per MFC
// zone). Count is supplied by the caller at Init time via the App-side GVL
// constant. Lib code never references App enums or GVL.
FUNCTION_BLOCK FB_FlowSubsystem IMPLEMENTS I_FlowSubsystem
VAR_OUTPUT
    bInitialized : BOOL := FALSE;
    bAnyError    : BOOL := FALSE;
    stFirstError : ST_Error;
END_VAR
VAR
    _ipControllers : ARRAY[1..GVL_SubsystemSizes.MAX_FLOW_ZONES] OF I_Controller;
    _nCount        : INT := 0;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    aipControllers : ARRAY[*] OF I_Controller;
    nCount         : INT;
END_VAR
VAR
    i      : INT;
    nLower : DINT;
END_VAR
    IF bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF nCount < 1 OR nCount > GVL_SubsystemSizes.MAX_FLOW_ZONES THEN
        RETURN;
    END_IF

    nLower := LOWER_BOUND(aipControllers, 1);
    IF nCount > (UPPER_BOUND(aipControllers, 1) - nLower + 1) THEN
        RETURN;
    END_IF

    _nCount := nCount;
    FOR i := 1 TO _nCount DO
        _ipControllers[i] := aipControllers[nLower + i - 1];
    END_FOR

    bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
VAR
    i        : INT;
    stStatus : ST_ControllerStatus;
END_VAR
    bAnyError := FALSE;
    stFirstError.eErrorId := E_ErrorId.NO_ERROR;
    stFirstError.sSource := '';
    stFirstError.sMessage := '';

    FOR i := 1 TO _nCount DO
        IF _ipControllers[i] <> 0 THEN
            stStatus := _ipControllers[i].GetStatus();
            _CaptureFirstError(stStatus.stError);
        END_IF
    END_FOR

    Execute := TRUE;
END_METHOD

METHOD PRIVATE _CaptureFirstError
VAR_INPUT
    stError : ST_Error;
END_VAR
    IF stError.eErrorId <> E_ErrorId.NO_ERROR THEN
        bAnyError := TRUE;
        IF stFirstError.eErrorId = E_ErrorId.NO_ERROR THEN
            stFirstError := stError;
        END_IF
    END_IF
END_METHOD

METHOD PUBLIC IsInitialized : BOOL
    IsInitialized := bInitialized;
END_METHOD

METHOD PUBLIC HasAnyError : BOOL
    HasAnyError := bAnyError;
END_METHOD

METHOD PUBLIC GetCount : INT
    GetCount := _nCount;
END_METHOD

METHOD PUBLIC GetControllerByIndex : I_Controller
VAR_INPUT
    nZone : INT;
END_VAR
    IF nZone >= 1 AND nZone <= _nCount THEN
        GetControllerByIndex := _ipControllers[nZone];
    ELSE
        GetControllerByIndex := 0;
    END_IF
END_METHOD

METHOD PUBLIC GetSensorByIndex : I_Sensor
VAR_INPUT
    nZone : INT;
END_VAR
VAR
    ipSensor : I_Sensor;
END_VAR
    IF nZone >= 1 AND nZone <= _nCount THEN
        IF _ipControllers[nZone] <> 0 AND_THEN __QUERYINTERFACE(_ipControllers[nZone], ipSensor) THEN
            GetSensorByIndex := ipSensor;
        END_IF
    END_IF
END_METHOD

END_FUNCTION_BLOCK
