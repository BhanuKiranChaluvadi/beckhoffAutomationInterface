(* Valve interface - unified control for all valve types. *)
INTERFACE I_Valve EXTENDS I_Errorable

METHOD Open : BOOL
END_METHOD

METHOD Close : BOOL
END_METHOD

METHOD IsOpen : BOOL
END_METHOD

METHOD IsClosed : BOOL
END_METHOD

METHOD Reset : BOOL
END_METHOD

END_INTERFACE
