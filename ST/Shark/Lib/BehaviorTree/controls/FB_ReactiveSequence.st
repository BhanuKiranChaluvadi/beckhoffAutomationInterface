(*
FB_ReactiveSequence
----------------------------------------------------------------------------
PURPOSE:
Similar to FB_SequenceNode, but re-evaluates all children from the beginning 
every tick. Once a child returns RUNNING, it stops execution and returns RUNNING.
If a child returns FAILURE, the sequence fails immediately.
If all children return SUCCESS, the sequence succeeds.

This is highly useful for priority-based behavior where higher priority 
conditions/actions must be continuously checked. If a higher priority child 
suddenly fails while a lower priority child was running, the sequence reacts
immediately.
----------------------------------------------------------------------------
*)
FUNCTION_BLOCK FB_ReactiveSequence IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Reactive Sequence';
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
    i : INT;
END_VAR

    // Loop through children starting from index 1 EVERY TICK
    FOR i := 1 TO _list.GetCount() DO

        sProgressMsg := 'Step ';
        sProgressMsg := CONCAT(sProgressMsg, TO_STRING(i));
        sProgressMsg := CONCAT(sProgressMsg, '/');
        sProgressMsg := CONCAT(sProgressMsg, TO_STRING(_list.GetCount()));

        ipChild := _list.GetChild(i);
        IF ipChild <> 0 THEN
            stChildStatus := ipChild.Tick();
            
            sProgressMsg := CONCAT(sProgressMsg, ': ');
            sProgressMsg := CONCAT(sProgressMsg, stChildStatus.sDescription);
            
        ELSE
            Tick.eStatus := E_NodeStatus.FAILURE;
            Tick.bHasError := TRUE;
            Tick.stError.eErrorId := E_ErrorId.ERR_BT_NULL_CHILD;
            Tick.stError.sSource := sName;
            Tick.stError.sMessage := CONCAT('Step ', CONCAT(TO_STRING(i), ' child is null'));
            RETURN;
        END_IF

        IF stChildStatus.eStatus = E_NodeStatus.RUNNING THEN
            // If we aborted at a lower index than last cycle, halt the orphaned child
            IF _nCurrentChildIndex > i AND _nCurrentChildIndex <= _list.GetCount() THEN
                ipChild := _list.GetChild(_nCurrentChildIndex);
                IF ipChild <> 0 THEN ipChild.Halt(); END_IF
            END_IF
            _nCurrentChildIndex := i; // Remember where we are currently RUNNING
            
            Tick.eStatus := E_NodeStatus.RUNNING;
            Tick.sDescription := sProgressMsg;
            Tick.rProgress := F_BlendChildProgress(
                nChildIndex    := i,
                nTotalChildren := _list.GetCount(),
                rChildProgress := stChildStatus.rProgress
            );
            RETURN; // Exit immediately, hold the sequence here for this cycle
            
        ELSIF stChildStatus.eStatus = E_NodeStatus.FAILURE THEN
            // If we failed at a lower index than the running index from last cycle, halt it
            IF _nCurrentChildIndex > i AND _nCurrentChildIndex <= _list.GetCount() THEN
                ipChild := _list.GetChild(_nCurrentChildIndex);
                IF ipChild <> 0 THEN ipChild.Halt(); END_IF
            END_IF
            _nCurrentChildIndex := 1; // Reset since we failed
            
            Tick := stChildStatus;
            IF NOT Tick.bHasError THEN
                Tick.bHasError := TRUE;
                Tick.stError.eErrorId := E_ErrorId.ERR_BT_SEQUENCE_FAILED;
                Tick.stError.sSource := sName;
                Tick.stError.sMessage := CONCAT('Failed at step ', TO_STRING(i));
            END_IF
            Tick.sDescription := sProgressMsg;
            RETURN;
            
        ELSIF stChildStatus.eStatus = E_NodeStatus.SUCCESS THEN
            // Just continue to the next child
            CONTINUE; 
        END_IF
    END_FOR

    // If we get here, all children succeeded
    Tick.rProgress := 100.0;
    Tick.sDescription := 'Done';
    _nCurrentChildIndex := 1; // Reset state for future execution
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