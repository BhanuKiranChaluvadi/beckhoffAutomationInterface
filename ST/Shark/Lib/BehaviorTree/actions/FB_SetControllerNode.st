FUNCTION_BLOCK FB_SetControllerNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Set Controller';
END_VAR
VAR
    _ipController : I_Controller;
    _rSetpoint    : LREAL;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipController : I_Controller;
    rSetpoint    : LREAL;
    sNodeName    : STRING(50) := '';
END_VAR
    IF ipController = 0 THEN RETURN; END_IF
    _ipController := ipController;
    _rSetpoint    := rSetpoint;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

PROPERTY PUBLIC Setpoint : LREAL
    GET
    Setpoint := _rSetpoint;
    END_GET
    SET
    _rSetpoint := Setpoint;
    END_SET
END_PROPERTY

METHOD Tick : ST_NodeResult
    IF _ipController = 0 THEN
        Tick := F_BuildNullInterfaceResult(sName, 'Controller interface not configured');
        RETURN;
    END_IF

    _ipController.SetSetpoint(_rSetpoint);
    Tick.eStatus := E_NodeStatus.SUCCESS;
    Tick.rProgress := 100.0;
    Tick.sDescription := CONCAT(sName, CONCAT(' - Set setpoint=', LREAL_TO_FMTSTR(_rSetpoint, 2, TRUE)));
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // Safe state: zero the setpoint.
    IF _ipController <> 0 THEN
        _ipController.SetSetpoint(0.0);
    END_IF
END_METHOD

END_FUNCTION_BLOCK
