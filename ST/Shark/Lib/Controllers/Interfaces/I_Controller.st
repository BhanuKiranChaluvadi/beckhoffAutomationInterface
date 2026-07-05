(* Base Controller Interface. Defines core functionality common to all controller types. *)
INTERFACE I_Controller EXTENDS __System.IQueryInterface

METHOD Enable : BOOL
VAR_INPUT
    bEnable : BOOL;
END_VAR
END_METHOD

METHOD SetSetpoint : BOOL
VAR_INPUT
    rDesiredSetpoint : LREAL;
END_VAR
END_METHOD

METHOD GetStatus : ST_ControllerStatus
END_METHOD

METHOD Reset : BOOL
END_METHOD

END_INTERFACE
