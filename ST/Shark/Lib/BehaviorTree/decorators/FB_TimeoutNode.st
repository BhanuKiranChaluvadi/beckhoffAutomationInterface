FUNCTION_BLOCK FB_TimeoutNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Timeout Node';
    tTimeout : TIME := T#5S;
END_VAR
VAR
    _ipChild : I_TreeNode;
    _fbTimer : TON;
    _bStarted : BOOL := FALSE;
END_VAR

METHOD PUBLIC SetChild : BOOL
VAR_INPUT
    ipChild : I_TreeNode;
END_VAR
    _ipChild := ipChild;
    SetChild := TRUE;
END_METHOD

    {attribute 'estimated-stack-usage' := '500'}
METHOD Tick : ST_NodeResult
VAR
    stChildStatus : ST_NodeResult;
END_VAR
    IF _ipChild = 0 THEN
        Tick := F_BuildNullChildResult(sName, 'Timeout decorator child not configured');
        RETURN;
    END_IF

    // Call timer unconditionally every scan cycle
    _fbTimer(IN := _bStarted, PT := tTimeout);

    IF NOT _bStarted THEN
        _bStarted := TRUE;
    END_IF

    IF _fbTimer.Q THEN
        _ipChild.Halt(); // Stop child but preserve its error state
        
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_TIMEOUT;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := CONCAT('Timeout after ', TIME_TO_STRING(tTimeout));
        RETURN;
    END_IF

    stChildStatus := _ipChild.Tick();

    // Track elapsed time as progress (countdown to timeout)
    IF tTimeout > T#0S THEN
        Tick.rProgress := (TIME_TO_REAL(_fbTimer.ET) / TIME_TO_REAL(tTimeout)) * 100.0;
        IF Tick.rProgress > 100.0 THEN Tick.rProgress := 100.0; END_IF
    END_IF

    // Propagate everything exactly from the child
    IF stChildStatus.eStatus <> E_NodeStatus.RUNNING THEN
        _bStarted := FALSE;
        _fbTimer(IN := FALSE);
    END_IF

    Tick.eStatus := stChildStatus.eStatus;
    Tick.bHasError := stChildStatus.bHasError;
    Tick.stError := stChildStatus.stError;
    Tick.sDescription := stChildStatus.sDescription;

    // Use child progress if not overriding it with timer
    IF tTimeout <= T#0S THEN
        Tick.rProgress := stChildStatus.rProgress;
    END_IF

END_METHOD

    // Method: Halt - Stops execution but preserves error state
    {attribute 'estimated-stack-usage' := '200'}
{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    _bStarted := FALSE;
    _fbTimer(IN := FALSE);
    IF _ipChild <> 0 THEN
        _ipChild.Halt();
    END_IF
END_METHOD

    {attribute 'estimated-stack-usage' := '200'}
METHOD Reset : BOOL
    _bStarted := FALSE;
    _fbTimer(IN := FALSE);
    IF _ipChild <> 0 THEN
        _ipChild.Reset();
    END_IF
    Reset := TRUE;
END_METHOD

END_FUNCTION_BLOCK
