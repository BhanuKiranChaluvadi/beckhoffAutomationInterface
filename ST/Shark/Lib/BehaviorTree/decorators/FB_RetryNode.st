FUNCTION_BLOCK FB_RetryNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Retry Node';
    nMaxAttempts : INT := 3;
END_VAR
VAR
    _ipChild : I_TreeNode;
    _nAttempts : INT := 0;
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
    sErrorMsg : STRING(255);
END_VAR
    IF _ipChild = 0 THEN
        Tick := F_BuildNullChildResult(sName, 'Retry decorator child not configured');
        RETURN;
    END_IF

    IF nMaxAttempts <= 0 THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INVALID_PARAM;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Retry max attempts must be greater than zero';
        RETURN;
    END_IF

    stChildStatus := _ipChild.Tick();

    IF stChildStatus.eStatus = E_NodeStatus.SUCCESS THEN
        _nAttempts := 0;
        Tick := stChildStatus;
        RETURN;
    ELSIF stChildStatus.eStatus = E_NodeStatus.FAILURE THEN
        _nAttempts := _nAttempts + 1;
        IF _nAttempts < nMaxAttempts THEN
            _ipChild.Reset();
            Tick.eStatus := E_NodeStatus.RUNNING; // Keep trying
            Tick.sDescription := CONCAT('Retrying: attempt ', TO_STRING(_nAttempts));
            
            // Progress: attempts used so far
            IF nMaxAttempts > 0 THEN
                Tick.rProgress := (INT_TO_REAL(_nAttempts) / INT_TO_REAL(nMaxAttempts)) * 100.0;
            END_IF
        ELSE
            // Max retries exceeded - propagate child error or set generic      
            Tick := stChildStatus;
            
            sErrorMsg := CONCAT('Failed after ', TO_STRING(nMaxAttempts));      
            sErrorMsg := CONCAT(sErrorMsg, ' attempts');
            
            IF NOT Tick.bHasError THEN
                Tick.bHasError := TRUE;
                Tick.stError.eErrorId := E_ErrorId.ERR_BT_MAX_RETRIES;
                Tick.stError.sSource := sName;
            END_IF
            Tick.stError.sMessage := CONCAT(sErrorMsg, CONCAT(': ', Tick.stError.sMessage));

            _nAttempts := 0;
        END_IF
    ELSE
        // Child still running — blend attempt progress with child sub-progress
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.sDescription := stChildStatus.sDescription;

        IF nMaxAttempts > 0 THEN
            Tick.rProgress := (INT_TO_REAL(_nAttempts) + stChildStatus.rProgress / 100.0) / INT_TO_REAL(nMaxAttempts) * 100.0;
        END_IF
    END_IF

END_METHOD

    // Method: Halt - Stops execution but preserves error state
{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    _nAttempts := 0;
    IF _ipChild <> 0 THEN
        _ipChild.Halt();
    END_IF
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    _nAttempts := 0;
    IF _ipChild <> 0 THEN
        _ipChild.Reset();
    END_IF
    Reset := TRUE;
END_METHOD

END_FUNCTION_BLOCK
