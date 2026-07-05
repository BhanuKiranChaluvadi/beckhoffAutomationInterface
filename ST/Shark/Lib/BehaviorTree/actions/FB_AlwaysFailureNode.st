FUNCTION_BLOCK FB_AlwaysFailureNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Always Failure';
END_VAR

METHOD Tick : ST_NodeResult
    Tick.eStatus := E_NodeStatus.FAILURE;
    Tick.bHasError := TRUE;
    Tick.stError.eErrorId := E_ErrorId.ERR_BT_FORCED_FAILURE;
    Tick.stError.sSource := sName;
    Tick.stError.sMessage := 'Forced Failure';
    Tick.sDescription := CONCAT(sName, ' - Failure');
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
END_METHOD
END_FUNCTION_BLOCK
