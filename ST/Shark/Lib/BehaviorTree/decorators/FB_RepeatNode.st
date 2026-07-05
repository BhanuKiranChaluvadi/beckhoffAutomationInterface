FUNCTION_BLOCK FB_RepeatNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Repeat Node';
    nNumCycles : INT := 3; // -1 for infinite
END_VAR
VAR
    _ipChild : I_TreeNode;
    _nCycles : INT := 0;
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
        Tick := F_BuildNullChildResult(sName, 'Repeat decorator child not configured');
        RETURN;
    END_IF

    stChildStatus := _ipChild.Tick();

    IF stChildStatus.eStatus = E_NodeStatus.FAILURE THEN
        // Propagate child error
        Tick := stChildStatus;
        IF NOT Tick.bHasError THEN
            Tick.bHasError := TRUE;
            Tick.stError.eErrorId := E_ErrorId.ERR_BT_CHILD_FAILED;
            Tick.stError.sSource := sName;
            Tick.stError.sMessage := 'Repeat child failed';
        END_IF
        _nCycles := 0;
        RETURN;
    ELSIF stChildStatus.eStatus = E_NodeStatus.SUCCESS THEN
        _nCycles := _nCycles + 1;
        IF nNumCycles <> -1 AND _nCycles >= nNumCycles THEN
            _nCycles := 0;
            Tick.eStatus := E_NodeStatus.SUCCESS;
            Tick.rProgress := 100.0;
            Tick.sDescription := CONCAT('Completed ', TO_STRING(nNumCycles));
            Tick.sDescription := CONCAT(Tick.sDescription, ' cycles');
        ELSE
            _ipChild.Reset();
            Tick.eStatus := E_NodeStatus.RUNNING; // Keep repeating
            Tick.sDescription := CONCAT('Repeating sequence: ', TO_STRING(_nCycles));
            
            // Progress: completed cycles + child sub-progress
            IF nNumCycles > 0 THEN
                Tick.rProgress := (INT_TO_REAL(_nCycles) / INT_TO_REAL(nNumCycles)) * 100.0;
            END_IF
        END_IF
    ELSE
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.sDescription := stChildStatus.sDescription;

        // Child still running — blend cycle progress with child sub-progress 
        IF nNumCycles > 0 THEN
            Tick.rProgress := (INT_TO_REAL(_nCycles) + stChildStatus.rProgress / 100.0) / INT_TO_REAL(nNumCycles) * 100.0;
        END_IF
    END_IF

END_METHOD

// Method: Halt - Stops execution but preserves error state
{attribute 'estimated-stack-usage' := '500'}
{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    _nCycles := 0;
    IF _ipChild <> 0 THEN
        _ipChild.Halt();
    END_IF
END_METHOD

    {attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    IF _ipChild <> 0 THEN
        _ipChild.Reset();
    END_IF
    Reset := TRUE;
END_METHOD

END_FUNCTION_BLOCK
