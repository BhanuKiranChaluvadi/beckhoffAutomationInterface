FUNCTION_BLOCK FB_AlwaysSuccessNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Always Success';
END_VAR

METHOD Tick : ST_NodeResult
    Tick.eStatus := E_NodeStatus.SUCCESS;
    Tick.rProgress := 100.0;
    Tick.sDescription := CONCAT(sName, ' - Success');
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
END_METHOD
END_FUNCTION_BLOCK
