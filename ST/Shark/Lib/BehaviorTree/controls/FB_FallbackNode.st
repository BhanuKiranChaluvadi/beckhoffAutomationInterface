FUNCTION_BLOCK FB_FallbackNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Fallback';
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
    ipChild       : I_TreeNode;
    sProgressMsg  : STRING(255);
END_VAR

    // If we finished last time, reset to start
    IF _nCurrentChildIndex > _list.GetCount() THEN
        _nCurrentChildIndex := 1;
    END_IF

    WHILE _nCurrentChildIndex <= _list.GetCount() DO

        // Update Progress String for Monitoring
        sProgressMsg := 'Option ';
        sProgressMsg := CONCAT(sProgressMsg, TO_STRING(_nCurrentChildIndex));
        sProgressMsg := CONCAT(sProgressMsg, '/');
        sProgressMsg := CONCAT(sProgressMsg, TO_STRING(_list.GetCount()));

        ipChild := _list.GetChild(_nCurrentChildIndex);

        IF ipChild <> 0 THEN
            stChildStatus := ipChild.Tick();

            sProgressMsg := CONCAT(sProgressMsg, ': ');
            sProgressMsg := CONCAT(sProgressMsg, stChildStatus.sDescription);

            // Blend the running child's own progress with its position so UI
            // moves smoothly, matching FB_SequenceNode / FB_ReactiveSequence.
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
            Tick.stError.sMessage := CONCAT('Option ', CONCAT(TO_STRING(_nCurrentChildIndex), ' child is null'));
            RETURN;
        END_IF

        IF stChildStatus.eStatus = E_NodeStatus.RUNNING THEN
            Tick.eStatus := E_NodeStatus.RUNNING;
            Tick.sDescription := sProgressMsg;
            RETURN;

        ELSIF stChildStatus.eStatus = E_NodeStatus.SUCCESS THEN
            // Fallback: If one succeeds, the whole node succeeds - clear any errors from previous fallbacks inherently
            Tick.sDescription := 'Done';
            Tick.rProgress := 100.0;
            _nCurrentChildIndex := 1;
            Tick.eStatus := E_NodeStatus.SUCCESS;
            RETURN;

        ELSIF stChildStatus.eStatus = E_NodeStatus.FAILURE THEN
            // Fallback: If one fails, try the next one
            _nCurrentChildIndex := _nCurrentChildIndex + 1;
        END_IF
    END_WHILE

    // If we get here, ALL children failed.
    // stChildStatus already holds the last child's FAILURE result from the loop above —
    // use it directly instead of re-ticking the child, which would issue hardware
    // commands a second time in the same scan cycle.
    IF _list.GetCount() > 0 THEN
        Tick := stChildStatus;  // preserve last child's specific error details
    END_IF
    IF NOT Tick.bHasError THEN
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_CHILD_FAILED;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'All fallback options failed';
    END_IF

    Tick.eStatus := E_NodeStatus.FAILURE;
    Tick.rProgress := 0.0;
    _nCurrentChildIndex := 1;
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
