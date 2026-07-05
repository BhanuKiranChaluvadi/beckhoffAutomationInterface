(*
FB_ParallelNode
----------------------------------------------------------------------------
PURPOSE:
Executes all children simultaneously every tick.
Succeeds if a required number of children succeed.
Fails if too many children fail such that the required success count is impossible.
----------------------------------------------------------------------------
*)
FUNCTION_BLOCK FB_ParallelNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Parallel Node';
    nSuccessThreshold : INT := -1; // -1 means All
    nFailureThreshold : INT := 1; // Fail on first failure
END_VAR
VAR
    _list : FB_NodeList;
    _aChildStatus : ARRAY[1..GVL_BT_Constants.MAX_BT_CHILDREN] OF E_NodeStatus; 
    _nSuccessCount : INT;
    _nFailureCount : INT;
    _bStatusInitialized : BOOL := FALSE;
END_VAR

METHOD PUBLIC AddChild : BOOL
VAR_INPUT
    ipNode : I_TreeNode;
END_VAR
    AddChild := _list.AddChild(ipNode);
END_METHOD

METHOD PUBLIC ClearChildren
    _list.ClearChildren();
    _bStatusInitialized := FALSE;
END_METHOD

    {attribute 'estimated-stack-usage' := '500'}
METHOD Tick : ST_NodeResult
VAR
    i : INT;
    bDone : BOOL;
    ipChild : I_TreeNode;
    stChildStatus : ST_NodeResult;
    rTotalProgress : REAL := 0.0;
    nFirstFailedIdx : INT := 0;
END_VAR

    IF _list.GetCount() <= 0 THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_NULL_CHILD;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Parallel node has no children configured';
        RETURN;
    END_IF

    IF nFailureThreshold <= 0 THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INVALID_PARAM;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Failure threshold must be greater than zero';
        RETURN;
    END_IF

    IF ((nSuccessThreshold <> -1) AND (nSuccessThreshold <= 0)) OR ((nSuccessThreshold > _list.GetCount()) AND (nSuccessThreshold <> -1)) THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INVALID_PARAM;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Success threshold must be -1 or within child count';
        RETURN;
    END_IF

    IF nFailureThreshold > _list.GetCount() THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_GEN_INVALID_PARAM;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Failure threshold cannot exceed child count';
        RETURN;
    END_IF

    // Initialize child statuses on first call
    IF NOT _bStatusInitialized THEN
        FOR i := 1 TO GVL_BT_Constants.MAX_BT_CHILDREN DO
            _aChildStatus[i] := E_NodeStatus.IDLE;
        END_FOR
        _bStatusInitialized := TRUE;
    END_IF

    _nSuccessCount := 0;
    _nFailureCount := 0;
    bDone := TRUE;
    Tick.sDescription := 'Running';

    // Tick all children that are not finished
    FOR i := 1 TO _list.GetCount() DO
        ipChild := _list.GetChild(i);
        IF _aChildStatus[i] = E_NodeStatus.IDLE OR _aChildStatus[i] = E_NodeStatus.RUNNING THEN
            IF ipChild <> 0 THEN
                stChildStatus := ipChild.Tick();
                _aChildStatus[i] := stChildStatus.eStatus;
                
                // Track progress
                rTotalProgress := rTotalProgress + stChildStatus.rProgress;

                // Track first failed child explicitly to bubble up its exact error message
                IF stChildStatus.eStatus = E_NodeStatus.FAILURE AND nFirstFailedIdx = 0 THEN
                     nFirstFailedIdx := i;
                     Tick.stError := stChildStatus.stError; // Copy the inner error
                END_IF
            ELSE
                _aChildStatus[i] := E_NodeStatus.FAILURE;
            END_IF
        ELSE
            // Treat previously finished kids as 100% complete for averaging purposes
            rTotalProgress := rTotalProgress + 100.0;
        END_IF

        IF _aChildStatus[i] = E_NodeStatus.SUCCESS THEN
            _nSuccessCount := _nSuccessCount + 1;
        ELSIF _aChildStatus[i] = E_NodeStatus.FAILURE THEN
            _nFailureCount := _nFailureCount + 1;
        ELSIF _aChildStatus[i] = E_NodeStatus.RUNNING THEN
            bDone := FALSE;
        END_IF
    END_FOR

    // Update Progress
    Tick.rProgress := rTotalProgress / INT_TO_REAL(_list.GetCount());

    // Check thresholds: success when either
    //  - nSuccessThreshold = -1 (meaning 'All') and all children succeeded, or 
    //  - nSuccessThreshold > 0 and the success count reached the threshold     
    IF
        ( (nSuccessThreshold = -1) AND (_nSuccessCount = _list.GetCount()) )        
        OR
        ( (nSuccessThreshold > 0) AND (_nSuccessCount >= nSuccessThreshold) )   
        THEN
        
        // Halt any dynamically remaining children
        _list.HaltAll();

        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.sDescription := CONCAT('Succeeded: ', TO_STRING(_nSuccessCount));
        Tick.rProgress := 100.0;
        
        // Reset initialization flag for next execution
        _bStatusInitialized := FALSE;
        RETURN;
    END_IF

    IF _nFailureCount >= nFailureThreshold THEN

        // Halt any dynamically remaining children
        _list.HaltAll();

        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.sDescription := CONCAT('Failed: ', TO_STRING(_nFailureCount));
        Tick.bHasError := TRUE;

        // If a child didn't give us a specific error above, generate a wrapper error
        IF Tick.stError.eErrorId = E_ErrorId.NO_ERROR THEN
            Tick.stError.eErrorId := E_ErrorId.ERR_BT_PARALLEL_FAILED;
            Tick.stError.sSource := sName;
            Tick.stError.sMessage := CONCAT(TO_STRING(_nFailureCount), ' parallel children failed');
        END_IF

        _bStatusInitialized := FALSE; // Reset state for future execution       
        RETURN;
    END_IF

    IF bDone THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.sDescription := 'All children finished without meeting success threshold';
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_PARALLEL_FAILED;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := Tick.sDescription;
        _bStatusInitialized := FALSE; // Reset state for future execution       
    ELSE
        Tick.eStatus := E_NodeStatus.RUNNING;
    END_IF

END_METHOD

METHOD PRIVATE _ResetState
VAR
    i : INT;
END_VAR
    FOR i := 1 TO GVL_BT_Constants.MAX_BT_CHILDREN DO
        _aChildStatus[i] := E_NodeStatus.IDLE;
    END_FOR
    _nSuccessCount := 0;
    _nFailureCount := 0;
    _bStatusInitialized := FALSE;
END_METHOD

    // Method: Halt - Resets parallel tracking state, halts children      
    {attribute 'estimated-stack-usage' := '200'}
{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    _ResetState();
    _list.HaltAll();
END_METHOD

    {attribute 'estimated-stack-usage' := '200'}
METHOD Reset : BOOL
    _ResetState();
    Reset := _list.ResetAll();
END_METHOD

END_FUNCTION_BLOCK
