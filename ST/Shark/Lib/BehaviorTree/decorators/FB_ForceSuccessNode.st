FUNCTION_BLOCK FB_ForceSuccessNode IMPLEMENTS I_TreeNode
VAR
    _ipChild : I_TreeNode;
END_VAR

METHOD PUBLIC SetChild : BOOL
VAR_INPUT
    ipChild : I_TreeNode;
END_VAR
    _ipChild := ipChild;
    SetChild := TRUE;
END_METHOD

METHOD Tick : ST_NodeResult
VAR
    stChildStatus : ST_NodeResult;
END_VAR
    IF _ipChild <> 0 THEN
        stChildStatus := _ipChild.Tick();
        IF stChildStatus.eStatus = E_NodeStatus.RUNNING THEN
            Tick := stChildStatus;
        ELSE
            Tick.eStatus := E_NodeStatus.SUCCESS;
            Tick.sDescription := CONCAT('Forced Success after: ', stChildStatus.sDescription);
            Tick.rProgress := 100.0;
        END_IF
    ELSE
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.sDescription := 'ForceSuccess missing child';
        Tick.rProgress := 100.0;
    END_IF
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
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