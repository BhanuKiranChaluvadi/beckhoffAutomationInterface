FUNCTION_BLOCK FB_CheckSensorNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Check Sensor Node';
END_VAR
VAR
    _ipSensor     : I_Sensor;
    _rThreshold   : LREAL;
    _eCompareMode : E_CompareMode := E_CompareMode.GREATER_THAN;
    _rTolerance   : LREAL;
    _rLastValue   : LREAL;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipSensor   : I_Sensor;
    rThreshold : LREAL;
    eMode      : E_CompareMode := E_CompareMode.GREATER_THAN;
    rTolerance : LREAL := 0.0;
    sNodeName  : STRING(50) := '';
END_VAR
    IF ipSensor = 0 THEN RETURN; END_IF
    _ipSensor     := ipSensor;
    _rThreshold   := rThreshold;
    _eCompareMode := eMode;
    _rTolerance   := rTolerance;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

PROPERTY PUBLIC Threshold : LREAL
    GET
    Threshold := _rThreshold;
    END_GET
    SET
    _rThreshold := Threshold;
    END_SET
END_PROPERTY

PROPERTY PUBLIC CompareMode : E_CompareMode
    GET
    CompareMode := _eCompareMode;
    END_GET
    SET
    _eCompareMode := CompareMode;
    END_SET
END_PROPERTY

METHOD PRIVATE GetStatusDescription : STRING(255)
VAR
    sResult : STRING(255);
END_VAR
    sResult := CONCAT(sName, ' - current=');
    sResult := CONCAT(sResult, LREAL_TO_FMTSTR(_rLastValue, 2, TRUE));
    sResult := CONCAT(sResult, CONCAT(', threshold=', LREAL_TO_FMTSTR(_rThreshold, 2, TRUE)));
    IF _eCompareMode = E_CompareMode.WITHIN_TOLERANCE AND _rTolerance > 0.0 THEN
        sResult := CONCAT(sResult, CONCAT(' +/-', LREAL_TO_FMTSTR(_rTolerance, 2, TRUE)));
    ELSE
        sResult := CONCAT(sResult, CONCAT(' (', CONCAT(F_CompareModeToString(_eCompareMode), ')')));
    END_IF
    GetStatusDescription := sResult;
END_METHOD

METHOD Tick : ST_NodeResult
VAR
    rCurrent  : LREAL;
    sErrorMsg : STRING(255);
END_VAR
    IF _ipSensor = 0 THEN
        Tick := F_BuildNullInterfaceResult(sName, 'Sensor interface not configured');
        RETURN;
    END_IF

    IF _ipSensor.IsError() THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_SENSOR_ERROR;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Sensor reported error';
        RETURN;
    END_IF

    rCurrent    := _ipSensor.GetMeasurement();
    _rLastValue := rCurrent;

    IF F_EvaluateCompareMode(rCurrent, _rThreshold, _rTolerance, _eCompareMode) THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        Tick.sDescription := GetStatusDescription();
    ELSE
        sErrorMsg := CONCAT('Value ', LREAL_TO_FMTSTR(rCurrent, 2, TRUE));
        sErrorMsg := CONCAT(sErrorMsg, CONCAT(' not ', F_CompareModeToString(_eCompareMode)));
        sErrorMsg := CONCAT(sErrorMsg, CONCAT(' ', LREAL_TO_FMTSTR(_rThreshold, 2, TRUE)));
        
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_CONDITION_FAILED;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := sErrorMsg;
        Tick.sDescription := GetStatusDescription();
    END_IF
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // No explicit action required
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

END_FUNCTION_BLOCK
