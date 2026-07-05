(* Constants used by Lib/Sequences/* function blocks.
   Lifted from App-side GVL_Constants so Lib code does not depend on
   App-scoped configuration.

   These describe properties of the shared hardware (Bronkhorst PCV needle
   valve) and are identical across all machines that use this valve. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // Pressure Control Valve (PCV / Needle Valve) Setpoints
    // Bronkhorst Modbus needle valve: 0.0 bar = fully open, 3.0 bar = fully closed
    PCV_CLOSED_SETPOINT : LREAL := 3.0;     // Setpoint that fully closes needle valve (no flow)
    PCV_OPEN_SETPOINT   : LREAL := 0.0;     // Setpoint that fully opens needle valve (max flow)
END_VAR
