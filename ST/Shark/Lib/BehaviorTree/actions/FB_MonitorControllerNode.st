(* FB_MonitorControllerNode - Monitor controller for errors while running

BEHAVIOR:
- Returns RUNNING while controller has no error
- Returns FAILURE immediately if controller reports error
- Never returns SUCCESS on its own (used in parallel with other nodes)

USE CASE:
Run in parallel with other nodes. If the controller enters an error state       
this node fails immediately, causing the parallel node to fail without
waiting for the other branches to complete.
*)
FUNCTION_BLOCK FB_MonitorControllerNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Monitor Controller Node';
END_VAR
VAR
    _ipController : I_Controller;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipController : I_Controller;
    sNodeName    : STRING(50) := '';
END_VAR
    IF ipController = 0 THEN RETURN; END_IF
    _ipController := ipController;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD


METHOD Tick : ST_NodeResult
VAR
    stStatus : ST_ControllerStatus;
END_VAR
    IF _ipController = 0 THEN
        Tick := F_BuildNullInterfaceResult(sName, 'Controller interface not configured');
        RETURN;
    END_IF

    stStatus := _ipController.GetStatus();
    IF stStatus.stError.eErrorId <> E_ErrorId.NO_ERROR THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError := stStatus.stError;
        RETURN;
    END_IF

    Tick.eStatus := E_NodeStatus.RUNNING;
    Tick.sDescription := CONCAT(sName, ' - Monitoring (OK)');
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // No explicit action required
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

END_FUNCTION_BLOCK


