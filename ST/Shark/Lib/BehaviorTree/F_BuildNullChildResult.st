(* F_BuildNullChildResult - Build a standard FAILURE result for an unconfigured
   child node on a behavior-tree decorator.

   Centralizes the null-child boilerplate that would otherwise be duplicated
   in every decorator node (Retry, Inverter, Timeout, Repeat, etc.).

   Usage inside a decorator's Tick method:

       IF _ipChild = 0 THEN
           Tick := F_BuildNullChildResult(sName, 'Xxx decorator child not configured');
           RETURN;
       END_IF
*)
FUNCTION F_BuildNullChildResult : ST_NodeResult
VAR_INPUT
    sSource  : STRING(80);
    sMessage : STRING(255);
END_VAR
    F_BuildNullChildResult.eStatus             := E_NodeStatus.FAILURE;
    F_BuildNullChildResult.bHasError           := TRUE;
    F_BuildNullChildResult.stError.eErrorId    := E_ErrorId.ERR_BT_NULL_CHILD;
    F_BuildNullChildResult.stError.sSource     := sSource;
    F_BuildNullChildResult.stError.sMessage    := sMessage;
END_FUNCTION
