(* FB_IfThenElseNode — Ternary branch control node

Children:
  Child 1 = Condition
  Child 2 = Then branch
  Child 3 = Else branch

BEHAVIOR:
- Ticks the condition child (1).
- If condition returns SUCCESS → ticks the Then child (2).
- If condition returns FAILURE → ticks the Else child (3).
- If condition returns RUNNING → returns RUNNING.
*)
FUNCTION_BLOCK FB_IfThenElseNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'IfThenElse';
END_VAR
VAR
    _list : FB_NodeList;
END_VAR

METHOD PUBLIC AddChild : BOOL
VAR_INPUT
    ipNode : I_TreeNode;
END_VAR
    IF _list.GetCount() >= 3 THEN
        AddChild := FALSE;
        RETURN;
    END_IF
    AddChild := _list.AddChild(ipNode);
END_METHOD

    {attribute 'estimated-stack-usage' := '500'}
METHOD Tick : ST_NodeResult
VAR
    stCondResult : ST_NodeResult;
    stBranchResult : ST_NodeResult;
    ipCondition  : I_TreeNode;
    ipThen       : I_TreeNode;
    ipElse       : I_TreeNode;
END_VAR
    IF _list.GetCount() < 3 THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_NULL_CHILD;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'IfThenElse requires exactly 3 children (condition, then, else)';
        RETURN;
    END_IF

    ipCondition := _list.GetChild(1);
    ipThen      := _list.GetChild(2);
    ipElse      := _list.GetChild(3);

    IF ipCondition = 0 OR ipThen = 0 OR ipElse = 0 THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_NULL_CHILD;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'One or more children are null';
        RETURN;
    END_IF

    // Evaluate condition
    stCondResult := ipCondition.Tick();

    CASE stCondResult.eStatus OF
        E_NodeStatus.SUCCESS:
            stBranchResult := ipThen.Tick();
            Tick := stBranchResult;
            Tick.rProgress := LIMIT(50.0, 50.0 + (stBranchResult.rProgress * 0.5), 100.0);

        E_NodeStatus.FAILURE:
            stBranchResult := ipElse.Tick();
            Tick := stBranchResult;
            Tick.rProgress := LIMIT(50.0, 50.0 + (stBranchResult.rProgress * 0.5), 100.0);

        E_NodeStatus.RUNNING:
            Tick.eStatus := E_NodeStatus.RUNNING;
            Tick.rProgress := LIMIT(0.0, stCondResult.rProgress * 0.5, 50.0);
            Tick.sDescription := 'Evaluating condition';
    ELSE
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_CHILD_FAILED;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'Condition returned unexpected status';
    END_CASE
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Halt
    _list.HaltAll();
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD Reset : BOOL
    Reset := _list.ResetAll();
END_METHOD

END_FUNCTION_BLOCK
