FUNCTION_BLOCK FB_EnableControllerNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Enable Controller';
END_VAR
VAR
    _ipController : I_Controller;
    _bEnable      : BOOL;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipController : I_Controller;
    bEnable      : BOOL;
    sNodeName    : STRING(50) := '';
END_VAR
    IF ipController = 0 THEN RETURN; END_IF
    _ipController := ipController;
    _bEnable      := bEnable;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

METHOD Tick : ST_NodeResult
    IF _ipController = 0 THEN
        Tick := F_BuildNullInterfaceResult(sName, 'Controller interface not configured');
        RETURN;
    END_IF

    _ipController.Enable(_bEnable);
    Tick.eStatus := E_NodeStatus.SUCCESS;
    Tick.rProgress := 100.0;
    IF _bEnable THEN
        Tick.sDescription := CONCAT(sName, ' - Enabling controller');
    ELSE
        Tick.sDescription := CONCAT(sName, ' - Disabling controller');
    END_IF
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // Safe state: disable the controller.
    IF _ipController <> 0 THEN
        _ipController.Enable(FALSE);
    END_IF
END_METHOD

END_FUNCTION_BLOCK
