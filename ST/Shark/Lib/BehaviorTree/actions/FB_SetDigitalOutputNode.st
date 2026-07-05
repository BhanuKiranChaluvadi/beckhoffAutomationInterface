FUNCTION_BLOCK FB_SetDigitalOutputNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Set Digital Output Node';
END_VAR
VAR
    _ipDO : I_DigitalOutput;
    _bState : BOOL;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipDO      : I_DigitalOutput;
    bState    : BOOL;
    sNodeName : STRING(50) := '';
END_VAR
    IF ipDO = 0 THEN RETURN; END_IF
    _ipDO   := ipDO;
    _bState := bState;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

METHOD Tick : ST_NodeResult
    IF _ipDO = 0 THEN
        Tick := F_BuildNullInterfaceResult(sName, 'Digital output interface not configured');
        RETURN;
    END_IF

    _ipDO.SetOutput(_bState);
    Tick.eStatus := E_NodeStatus.SUCCESS;
    Tick.sDescription := CONCAT(sName, ' - Output set');
    Tick.rProgress := 100.0;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // Apply the safe state configured at Init time.
    // The node has no knowledge of whether safe means TRUE or FALSE.
    IF _ipDO <> 0 THEN
        _ipDO.SetSafeState();
    END_IF
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

END_FUNCTION_BLOCK