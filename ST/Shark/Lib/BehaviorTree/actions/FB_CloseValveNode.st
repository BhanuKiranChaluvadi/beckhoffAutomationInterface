FUNCTION_BLOCK FB_CloseValveNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Close Valve';
END_VAR
VAR
    _ipValve : I_Valve;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipValve   : I_Valve;
    sNodeName : STRING(50) := '';
END_VAR
    IF ipValve = 0 THEN RETURN; END_IF
    _ipValve := ipValve;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

METHOD Tick : ST_NodeResult
VAR
    stValveError : ST_Error;
END_VAR
    IF _ipValve = 0 THEN
        Tick := F_BuildNullInterfaceResult(sName, 'Valve interface not configured');
        RETURN;
    END_IF

    _ipValve.Close();

    IF _ipValve.IsClosed() THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.rProgress := 100.0;
        Tick.sDescription := CONCAT(sName, ' - Valve closed');
    ELSIF _ipValve.IsError() THEN
        stValveError := _ipValve.GetError();
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.sSource := sName;
        
        IF stValveError.eErrorId <> E_ErrorId.NO_ERROR THEN
            Tick.stError.eErrorId := stValveError.eErrorId;
            Tick.stError.sMessage := stValveError.sMessage;
        ELSE
            Tick.stError.eErrorId := E_ErrorId.ERR_BT_VALVE_ERROR;
            Tick.stError.sMessage := 'Valve reported error while closing';
        END_IF
    ELSE
        Tick.eStatus := E_NodeStatus.RUNNING;
        Tick.sDescription := CONCAT(sName, ' - Closing valve, currently open');
    END_IF
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // Safe state: ensure the valve is closed (isolated).
    IF _ipValve <> 0 THEN
        _ipValve.Close();
    END_IF
END_METHOD

END_FUNCTION_BLOCK
