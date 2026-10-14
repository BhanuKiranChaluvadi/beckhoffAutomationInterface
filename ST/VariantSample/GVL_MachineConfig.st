(* The only file that names machines. Every other POU reads these constants
   or tests a feature define (PCV_MODBUS / PCV_ETHERCAT), never a machine name. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
{IF defined (MachineA)}
    MACHINE_NAME     : STRING(16) := 'MachineA';
    MAX_PRESSURE_BAR : REAL       := 3.0;
    DOORS_FITTED     : BOOL       := FALSE;
{ELSIF defined (MachineB)}
    MACHINE_NAME     : STRING(16) := 'MachineB';
    MAX_PRESSURE_BAR : REAL       := 10.0;
    DOORS_FITTED     : BOOL       := TRUE;
{END_IF}
END_VAR
