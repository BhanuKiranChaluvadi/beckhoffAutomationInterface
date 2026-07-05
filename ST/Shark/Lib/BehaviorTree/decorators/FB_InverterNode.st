FUNCTION_BLOCK FB_InverterNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Inverter';
END_VAR
VAR
    _ipChild : I_TreeNode;
END_VAR

METHOD PUBLIC SetChild : BOOL
VAR_INPUT
    ipNode : I_TreeNode;
END_VAR
    IF ipNode = 0 THEN
        SetChild := FALSE;
        RETURN;
    END_IF
    _ipChild := ipNode;
    SetChild := TRUE;
END_METHOD

METHOD Tick : ST_NodeResult
VAR
    stChildStatus : ST_NodeResult;
END_VAR
    IF _ipChild = 0 THEN
        Tick := F_BuildNullChildResult(sName, 'Inverter child not configured');
        RETURN;
    END_IF

    stChildStatus := _ipChild.Tick();
    Tick := stChildStatus; // copies error and progress naturally

    // Inverter swaps SUCCESS<->FAILURE
    IF stChildStatus.eStatus = E_NodeStatus.SUCCESS THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.sDescription := CONCAT('Inverted SUCCESS: ', stChildStatus.sDescription);
    ELSIF stChildStatus.eStatus = E_NodeStatus.FAILURE THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.bHasError := FALSE; // clear error!
        Tick.stError.eErrorId := E_ErrorId.NO_ERROR;
        Tick.sDescription := CONCAT('Inverted FAILURE: ', stChildStatus.sDescription);
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
        Reset := _ipChild.Reset();
    ELSE
        Reset := TRUE;
    END_IF
END_METHOD

END_FUNCTION_BLOCK
