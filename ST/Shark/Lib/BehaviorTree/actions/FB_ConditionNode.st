// FB_ConditionNode - Pure leaf node that evaluates a boolean condition
// Returns SUCCESS if condition is TRUE, FAILURE if FALSE
// This is a leaf node, it has no child.
//
// Usage:
//   fbCondition.Init(refCondition := bMyFlag);
//   fbSequence.AddChild(fbCondition);
//
FUNCTION_BLOCK FB_ConditionNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'Condition Node';
END_VAR
VAR
    _refCondition : REFERENCE TO BOOL;
    _bInvertResult : BOOL := FALSE;
END_VAR

    // Configure with reference to boolean condition
METHOD PUBLIC Init : BOOL
VAR_INPUT
    refCondition  : REFERENCE TO BOOL;
    bInvertResult : BOOL := FALSE; // Optional: invert the result
    sNodeName     : STRING(50) := '';
END_VAR
    IF NOT __ISVALIDREF(refCondition) THEN
        Init := FALSE;
        RETURN;
    END_IF

    _refCondition REF= refCondition;
    _bInvertResult := bInvertResult;
    IF sNodeName <> '' THEN sName := sNodeName; END_IF
    Init := TRUE;
END_METHOD

METHOD Tick : ST_NodeResult
VAR
    bConditionValue : BOOL;
END_VAR

    IF NOT __ISVALIDREF(_refCondition) THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_CONDITION_NOT_CONFIGURED;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Condition reference not configured';
        RETURN;
    END_IF

    bConditionValue := _refCondition;

    // Apply inversion if configured
    IF _bInvertResult THEN
        bConditionValue := NOT bConditionValue;
    END_IF

    IF bConditionValue THEN
        Tick.eStatus := E_NodeStatus.SUCCESS;
        Tick.sDescription := CONCAT(sName, ' - Condition Met');
        Tick.rProgress := 100.0;
    ELSE
        // Condition false is not an error - it's expected behavior
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.sDescription := CONCAT(sName, ' - Condition Not Met');
    END_IF
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    // Nothing to halt
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := TRUE;
END_METHOD

END_FUNCTION_BLOCK
