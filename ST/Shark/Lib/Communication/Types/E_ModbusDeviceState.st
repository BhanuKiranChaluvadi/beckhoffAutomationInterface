(* Modbus device state machine. *)
{attribute 'qualified_only'}
{attribute 'strict'}
{attribute 'to_string'}
TYPE E_ModbusDeviceState :
    (
        IDLE,
        START_WRITE,
        START_READ,
        WAIT_FOR_WRITE,
        WAIT_FOR_READ
    );
END_TYPE
