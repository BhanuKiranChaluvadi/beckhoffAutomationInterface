(* Base interface for components that can report errors. *)
INTERFACE I_Errorable EXTENDS __System.IQueryInterface

METHOD IsError : BOOL
END_METHOD

METHOD GetError : ST_Error
END_METHOD

END_INTERFACE
