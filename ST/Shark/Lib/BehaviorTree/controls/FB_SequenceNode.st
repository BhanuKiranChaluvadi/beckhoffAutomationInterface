FUNCTION_BLOCK FB_SequenceNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Sequence';
END_VAR
VAR
    _list : FB_NodeList;
    _nCurrentChildIndex : INT := 1;
END_VAR

METHOD PUBLIC AddChild : BOOL
VAR_INPUT
    ipNode : I_TreeNode;
END_VAR
    AddChild := _list.AddChild(ipNode);
END_METHOD

METHOD PUBLIC ClearChildren
    _list.ClearChildren();
    _nCurrentChildIndex := 1;
END_METHOD

    {attribute 'estimated-stack-usage' := '500'}
METHOD Tick : ST_NodeResult
VAR
    stChildStatus : ST_NodeResult;
    ipChild : I_TreeNode;
    sProgressMsg : STRING(255);
END_VAR

    // If we finished last time, reset to start
    IF _nCurrentChildIndex > _list.GetCount() THEN
        _nCurrentChildIndex := 1;
    END_IF

    // Loop through children
    WHILE _nCurrentChildIndex <= _list.GetCount() DO

        // Update Progress String for Monitoring
        sProgressMsg := 'Step ';
        sProgressMsg := CONCAT(sProgressMsg, TO_STRING(_nCurrentChildIndex));
        sProgressMsg := CONCAT(sProgressMsg, '/');
        sProgressMsg := CONCAT(sProgressMsg, TO_STRING(_list.GetCount()));

        ipChild := _list.GetChild(_nCurrentChildIndex);

        IF ipChild <> 0 THEN
            stChildStatus := ipChild.Tick();
            
            sProgressMsg := CONCAT(sProgressMsg, ': ');
            sProgressMsg := CONCAT(sProgressMsg, stChildStatus.sDescription);

            // Smooth progress
            Tick.rProgress := F_BlendChildProgress(
                nChildIndex    := _nCurrentChildIndex,
                nTotalChildren := _list.GetCount(),
                rChildProgress := stChildStatus.rProgress
            );
        ELSE
            Tick.eStatus := E_NodeStatus.FAILURE;
            Tick.bHasError := TRUE;
            Tick.stError.eErrorId := E_ErrorId.ERR_BT_NULL_CHILD;
            Tick.stError.sSource := sName;
            Tick.stError.sMessage := CONCAT('Step ', CONCAT(TO_STRING(_nCurrentChildIndex), ' child is null'));
            RETURN;
        END_IF

        IF stChildStatus.eStatus = E_NodeStatus.RUNNING THEN
            Tick.eStatus := E_NodeStatus.RUNNING;
            Tick.sDescription := sProgressMsg;
            RETURN;
            
        ELSIF stChildStatus.eStatus = E_NodeStatus.FAILURE THEN
            // Capture child error and propagate directly
            Tick := stChildStatus;
            
            IF NOT stChildStatus.bHasError THEN
                // Child failed but didn't set error - create generic error
                Tick.bHasError := TRUE;
                Tick.stError.eErrorId := E_ErrorId.ERR_BT_SEQUENCE_FAILED;
                Tick.stError.sSource := sName;
                Tick.stError.sMessage := CONCAT('Failed at step ', TO_STRING(_nCurrentChildIndex));
            END_IF

            _nCurrentChildIndex := 1;
            Tick.sDescription := sProgressMsg;
            RETURN;
            
        ELSIF stChildStatus.eStatus = E_NodeStatus.SUCCESS THEN
            _nCurrentChildIndex := _nCurrentChildIndex + 1; // Move to next child
        END_IF
    END_WHILE

    // If we get here, all children succeeded
    Tick.rProgress := 100.0;
    Tick.sDescription := 'Done';
    _nCurrentChildIndex := 1;
    Tick.eStatus := E_NodeStatus.SUCCESS;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    _list.HaltAll();
    _nCurrentChildIndex := 1;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    _nCurrentChildIndex := 1;
    Reset := _list.ResetAll();
END_METHOD

END_FUNCTION_BLOCK
