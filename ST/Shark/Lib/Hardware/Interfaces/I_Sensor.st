(* Base sensor interface for measurement and error status. *)
INTERFACE I_Sensor EXTENDS I_Errorable, __System.IQueryInterface

METHOD GetMeasurement : LREAL
END_METHOD

END_INTERFACE
