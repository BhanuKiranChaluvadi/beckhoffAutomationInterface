(* Digital output interface for boolean control. *)
INTERFACE I_DigitalOutput

METHOD SetOutput
VAR_INPUT
    bValue : BOOL;
END_VAR
END_METHOD

METHOD GetOutput : BOOL
END_METHOD

(* Apply the safe state configured at Init time.
   Called by behaviour-tree nodes on Halt — the node has no knowledge of
   whether safe means TRUE or FALSE for this particular output. *)
METHOD SetSafeState
END_METHOD

END_INTERFACE
