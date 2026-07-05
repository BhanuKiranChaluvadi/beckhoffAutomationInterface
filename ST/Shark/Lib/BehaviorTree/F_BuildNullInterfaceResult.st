(* F_BuildNullInterfaceResult - Build a standard FAILURE result for an unconfigured
   hardware interface on a behavior-tree leaf node.

   Centralizes the null-interface boilerplate that would otherwise be duplicated
   in every action node (OpenValve, CloseValve, SetController, etc.).

   Usage inside a node's Tick method:

       IF _ipValve = 0 THEN
           Tick := F_BuildNullInterfaceResult(sName, 'Valve interface not configured');
           RETURN;
       END_IF
*)
FUNCTION F_BuildNullInterfaceResult : ST_NodeResult
VAR_INPUT
    sSource  : STRING(80);
    sMessage : STRING(255);
END_VAR
    F_BuildNullInterfaceResult.eStatus             := E_NodeStatus.FAILURE;
    F_BuildNullInterfaceResult.bHasError           := TRUE;
    F_BuildNullInterfaceResult.stError.eErrorId    := E_ErrorId.ERR_BT_NULL_INTERFACE;
    F_BuildNullInterfaceResult.stError.sSource     := sSource;
    F_BuildNullInterfaceResult.stError.sMessage    := sMessage;
END_FUNCTION
