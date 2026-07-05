// Lib pressure subsystem.
//
// Owns two fixed-capacity arrays — pressure sensors and pressure controllers.
// Counts are supplied by the caller at Init time via App-side GVL constants.
// Lib code never references App enums or GVL.
FUNCTION_BLOCK FB_PressureSubsystem IMPLEMENTS I_PressureSubsystem
VAR_OUTPUT
    bInitialized : BOOL := FALSE;
    bAnyError    : BOOL := FALSE;
    stFirstError : ST_Error;
END_VAR
VAR
    _ipSensors        : ARRAY[1..GVL_SubsystemSizes.MAX_PRESSURE_SENSORS] OF I_Sensor;
    _ipControllers    : ARRAY[1..GVL_SubsystemSizes.MAX_PRESSURE_CONTROLLERS] OF I_Controller;
    _nSensorCount     : INT := 0;
    _nControllerCount : INT := 0;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    aipSensors        : ARRAY[*] OF I_Sensor;
    nSensorCount      : INT;
    aipControllers    : ARRAY[*] OF I_Controller;
    nControllerCount  : INT;
END_VAR
VAR
    i        : INT;
    nLowerS  : DINT;
    nLowerC  : DINT;
END_VAR
    IF bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF nSensorCount < 0 OR nSensorCount > GVL_SubsystemSizes.MAX_PRESSURE_SENSORS THEN
        RETURN;
    END_IF
    IF nControllerCount < 0 OR nControllerCount > GVL_SubsystemSizes.MAX_PRESSURE_CONTROLLERS THEN
        RETURN;
    END_IF

    nLowerS := LOWER_BOUND(aipSensors, 1);
    IF nSensorCount > (UPPER_BOUND(aipSensors, 1) - nLowerS + 1) THEN
        RETURN;
    END_IF

    nLowerC := LOWER_BOUND(aipControllers, 1);
    IF nControllerCount > (UPPER_BOUND(aipControllers, 1) - nLowerC + 1) THEN
        RETURN;
    END_IF

    _nSensorCount := nSensorCount;
    FOR i := 1 TO _nSensorCount DO
        _ipSensors[i] := aipSensors[nLowerS + i - 1];
    END_FOR

    _nControllerCount := nControllerCount;
    FOR i := 1 TO _nControllerCount DO
        _ipControllers[i] := aipControllers[nLowerC + i - 1];
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

    FOR i := 1 TO _nSensorCount DO
        IF _ipSensors[i] <> 0 AND_THEN _ipSensors[i].IsError() THEN
            _CaptureFirstError(_ipSensors[i].GetError());
        END_IF
    END_FOR

    FOR i := 1 TO _nControllerCount DO
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

METHOD PUBLIC GetSensorByIndex : I_Sensor
VAR_INPUT
    nSensor : INT;
END_VAR
    IF nSensor >= 1 AND nSensor <= _nSensorCount THEN
        GetSensorByIndex := _ipSensors[nSensor];
    ELSE
        GetSensorByIndex := 0;
    END_IF
END_METHOD

METHOD PUBLIC GetSensorCount : INT
    GetSensorCount := _nSensorCount;
END_METHOD

METHOD PUBLIC GetControllerByIndex : I_Controller
VAR_INPUT
    nCtrl : INT;
END_VAR
    IF nCtrl >= 1 AND nCtrl <= _nControllerCount THEN
        GetControllerByIndex := _ipControllers[nCtrl];
    ELSE
        GetControllerByIndex := 0;
    END_IF
END_METHOD

METHOD PUBLIC GetControllerCount : INT
    GetControllerCount := _nControllerCount;
END_METHOD

METHOD PUBLIC GetControllerAsSensorByIndex : I_Sensor
VAR_INPUT
    nCtrl : INT;
END_VAR
VAR
    ipSensor : I_Sensor;
END_VAR
    IF nCtrl >= 1 AND nCtrl <= _nControllerCount THEN
        IF _ipControllers[nCtrl] <> 0 AND_THEN __QUERYINTERFACE(_ipControllers[nCtrl], ipSensor) THEN
            GetControllerAsSensorByIndex := ipSensor;
        END_IF
    END_IF
END_METHOD

END_FUNCTION_BLOCK
