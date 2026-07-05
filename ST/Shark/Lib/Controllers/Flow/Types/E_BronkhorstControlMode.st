(* Bronkhorst Control Mode Enumeration. Defines control modes supported by Bronkhorst controllers. *)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_BronkhorstControlMode :
    (
        BUS_CONTROL := 0,   // Automatic PID control (setpoint control)
        VALVE_CLOSED := 3,  // Direct valve fully closed
        VALVE_PURGE := 8    // Direct valve fully open (purge position)
    );
END_TYPE
