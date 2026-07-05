(* Analog output interface for hardware abstraction. *)
INTERFACE I_AnalogOutput EXTENDS I_Errorable

METHOD SetOutput
VAR_INPUT
    nValue : INT;  // Raw analog value (typically 0-32767 for 0-100%)
END_VAR
END_METHOD

METHOD SetPercentage
VAR_INPUT
    rPercentage : REAL;  // Percentage value (0.0 - 100.0)
END_VAR
END_METHOD

METHOD GetOutput : INT
END_METHOD

METHOD GetPercentage : REAL
END_METHOD

(* Apply the safe raw value configured at Init time.
   Called by behaviour-tree nodes on Halt — the node has no knowledge of
   what the safe value is for this particular output. *)
METHOD SetSafeState
END_METHOD

END_INTERFACE
