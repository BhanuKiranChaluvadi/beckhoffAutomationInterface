(* FB_WaitForSensorNode - Wait until sensor meets a condition

BEHAVIOR:
- Returns RUNNING while waiting for condition
- Returns SUCCESS when condition is met
- Returns FAILURE on errors (null sensor, sensor error) or timeout

TIMEOUT:
- tTimeout = T#0S  => no timeout, wait indefinitely (default)
- tTimeout > T#0S  => FAILURE with descriptive message when timeout expires     

COMPARE MODES:
- WITHIN_TOLERANCE: ABS(value - target) <= tolerance (default)
- GREATER_THAN:     value > target
- LESS_THAN:        value < target
*)
FUNCTION_BLOCK FB_WaitForSensorNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Wait For Sensor Node';
END_VAR
VAR
    _ipSensor     : I_Sensor;
    _rTarget      : LREAL;
    _rTolerance   : LREAL;
    _eCompareMode : E_CompareMode := E_CompareMode.WITHIN_TOLERANCE;
    _rLastValue   : LREAL;
    _tTimeout     : TIME := T#0S;
    _fbTimer      : TON;
    _bStarted     : BOOL;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipSensor   : I_Sensor;
    rTarget    : LREAL;
    rTolerance : LREAL := 0.0;
    eMode      : E_CompareMode := E_CompareMode.WITHIN_TOLERANCE;
    tTimeout   : TIME := T#0S;
    sNodeName  : STRING(50) := '';
END_VAR
    IF ipSensor = 0 THEN RETURN; END_IF
    _ipSensor     := ipSensor;
    _rTarget      := rTarget;
    _rTolerance   := rTolerance;
    _eCompareMode := eMode;
    _tTimeout     := tTimeout;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

PROPERTY PUBLIC Target : LREAL
    GET
    Target := _rTarget;
    END_GET
    SET
    _rTarget := Target;
    END_SET
END_PROPERTY

PROPERTY PUBLIC Tolerance : LREAL
    GET
    Tolerance := _rTolerance;
    END_GET
    SET
    _rTolerance := Tolerance;
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

PROPERTY PUBLIC Timeout : TIME
    GET
    Timeout := _tTimeout;
    END_GET
    SET
    _tTimeout := Timeout;
    END_SET
END_PROPERTY

METHOD PUBLIC GetStatusDescription : STRING(255)
VAR
    sResult : STRING(255);
END_VAR
    sResult := CONCAT(sName, ' - current=');
    sResult := CONCAT(sResult, LREAL_TO_FMTSTR(_rLastValue, 2, TRUE));
    sResult := CONCAT(sResult, CONCAT(', target=', LREAL_TO_FMTSTR(_rTarget, 2, TRUE)));
    IF _eCompareMode = E_CompareMode.WITHIN_TOLERANCE AND _rTolerance > 0.0 THEN
        sResult := CONCAT(sResult, CONCAT(' +/-', LREAL_TO_FMTSTR(_rTolerance, 2, TRUE)));
    ELSE
        sResult := CONCAT(sResult, CONCAT(' (', CONCAT(F_CompareModeToString(_eCompareMode), ')')));
    END_IF
    IF _tTimeout > T#0S AND _bStarted THEN
        sResult := CONCAT(sResult, CONCAT(' timeout=', CONCAT(TIME_TO_STRING(_fbTimer.ET), CONCAT('/', TIME_TO_STRING(_tTimeout)))));
    END_IF
    GetStatusDescription := sResult;
END_METHOD

METHOD Tick : ST_NodeResult
VAR
    rCurrentVal : LREAL;
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

    IF NOT _bStarted THEN
        _bStarted := TRUE;
    END_IF

    IF _tTimeout > T#0S THEN
        _fbTimer(IN := _bStarted, PT := _tTimeout);
        IF _fbTimer.Q THEN
            Tick.eStatus := E_NodeStatus.FAILURE;
            Tick.bHasError := TRUE;
            Tick.stError.eErrorId := E_ErrorId.ERR_BT_TIMEOUT;
            Tick.stError.sSource := sName;
            Tick.stError.sMessage := CONCAT(
                    CONCAT('Timeout after ', TIME_TO_STRING(_tTimeout)),        
                    CONCAT(' - current=', CONCAT(LREAL_TO_FMTSTR(_rLastValue, 2, TRUE), CONCAT(', target=', LREAL_TO_FMTSTR(_rTarget, 2, TRUE))))
                );
            RETURN;
        END_IF
        Tick.rProgress := (TIME_TO_REAL(_fbTimer.ET) / TIME_TO_REAL(_tTimeout)) * 100.0;
        IF Tick.rProgress > 100.0 THEN Tick.rProgress := 100.0; END_IF
    END_IF

    rCurrentVal := _ipSensor.GetMeasurement();
    _rLastValue := rCurrentVal;

    IF F_EvaluateCompareMode(rCurrentVal, _rTarget, _rTolerance, _eCompareMode) THEN
        _bStarted := FALSE;
        _fbTimer(IN := FALSE);

        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        Tick.sDescription := GetStatusDescription();
    ELSE
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.sDescription := GetStatusDescription();
    END_IF

END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // Stop timers, preserve state
    _bStarted := FALSE;
    _fbTimer(IN := FALSE);
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    _bStarted := FALSE;
    _fbTimer(IN := FALSE);
    Reset := TRUE;
END_METHOD


END_FUNCTION_BLOCK
