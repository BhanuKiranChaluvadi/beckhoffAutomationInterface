(* Bronkhorst device interface — shared by MFC (flow) and PCV (pressure/needle valve).
   Implemented by EtherCAT adapter, Modbus adapter, and simulation mock. *)
INTERFACE I_BronkhorstController

    // Common controller hardware methods
METHOD GetMeasuredValue : REAL
END_METHOD

METHOD WriteSetpoint
VAR_INPUT
    rSetpoint : REAL;
END_VAR
END_METHOD

METHOD WriteEnable
VAR_INPUT
    bEnable : BOOL;
END_VAR
END_METHOD

    // Bronkhorst-specific methods
METHOD GetValveOutput : REAL
END_METHOD

METHOD WriteControlMode
VAR_INPUT
    nMode : USINT;
END_VAR
END_METHOD

END_INTERFACE
