(* FB_WhileDoElseNode — While-loop control node

Children:
  Child 1 = Condition
  Child 2 = Do branch (executed while condition succeeds)
  Child 3 = Else branch (executed when condition fails)

BEHAVIOR:
- Ticks the condition child (1).
- If condition returns SUCCESS → ticks the Do child (2).
  - If Do returns RUNNING → returns RUNNING.
  - If Do returns SUCCESS → re-evaluates condition next tick.
  - If Do returns FAILURE → returns FAILURE.
- If condition returns FAILURE → ticks the Else child (3) and returns its result.
- If condition returns RUNNING → returns RUNNING.
*)
FUNCTION_BLOCK FB_WhileDoElseNode IMPLEMENTS I_TreeNode
VAR_INPUT
    sName : STRING(50) := 'WhileDoElse';
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
    ipCondition  : I_TreeNode;
    ipDo         : I_TreeNode;
    ipElse       : I_TreeNode;
END_VAR
    IF _list.GetCount() < 3 THEN
        Tick.eStatus := E_NodeStatus.FAILURE;
        Tick.bHasError := TRUE;
        Tick.stError.eErrorId := E_ErrorId.ERR_BT_NULL_CHILD;
        Tick.stError.sSource := sName;
        Tick.stError.sMessage := 'WhileDoElse requires exactly 3 children (condition, do, else)';
        RETURN;
    END_IF

    ipCondition := _list.GetChild(1);
    ipDo        := _list.GetChild(2);
    ipElse      := _list.GetChild(3);

    IF ipCondition = 0 OR ipDo = 0 OR ipElse = 0 THEN
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
            // Condition true → execute Do branch
            Tick := ipDo.Tick();

        E_NodeStatus.FAILURE:
            // Condition false → execute Else branch
            Tick := ipElse.Tick();

        E_NodeStatus.RUNNING:
            Tick.eStatus := E_NodeStatus.RUNNING;
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
