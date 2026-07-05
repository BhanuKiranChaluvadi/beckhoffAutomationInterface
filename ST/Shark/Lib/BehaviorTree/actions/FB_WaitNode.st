FUNCTION_BLOCK FB_WaitNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Wait';
END_VAR
VAR
    _tWaitTime : TIME := T#2S;
    _fbTimer : TON;
    _bStarted : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    tWaitTime : TIME;
    sNodeName : STRING(50) := '';
END_VAR
    _tWaitTime := tWaitTime;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

// Property to get/set wait time at runtime
PROPERTY PUBLIC WaitTime : TIME
    GET
    WaitTime := _tWaitTime;
    END_GET
    SET
    _tWaitTime := WaitTime;
    END_SET
END_PROPERTY

METHOD Tick : ST_NodeResult
    // Call timer unconditionally every scan cycle
    _fbTimer(IN := _bStarted, PT := _tWaitTime);

    IF NOT _bStarted THEN
        _bStarted := TRUE;
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.rProgress := 0.0;
        Tick.bHasError := FALSE;
        Tick.sDescription := CONCAT(sName, CONCAT(' - Wait ', TIME_TO_STRING(_tWaitTime)));
        RETURN;
    END_IF

    // Calculate progress based on elapsed time
    IF _tWaitTime > T#0S THEN
        Tick.rProgress := (TIME_TO_REAL(_fbTimer.ET) / TIME_TO_REAL(_tWaitTime)) * 100.0;
        IF Tick.rProgress > 100.0 THEN Tick.rProgress := 100.0; END_IF
    ELSE
        Tick.rProgress := 100.0;
    END_IF

    IF _fbTimer.Q THEN
        _bStarted := FALSE; // Reset for next full execution
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        Tick.sDescription := CONCAT(sName, ' - Done');
    ELSE
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.sDescription := CONCAT(sName, CONCAT(' - Waiting ', CONCAT(TIME_TO_STRING(_fbTimer.ET), CONCAT('/', TIME_TO_STRING(_tWaitTime)))));
    END_IF
END_METHOD

    // Method: Halt - Stops execution but preserves error state
{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
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
