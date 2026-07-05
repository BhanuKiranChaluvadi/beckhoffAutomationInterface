(* Modbus operation type. *)
{attribute 'qualified_only'}
{attribute 'strict'}
{attribute 'to_string'}
TYPE E_ModbusOperationType :
    (
        NONE := 0,
        READ := 1,
        WRITE := 2
    );
END_TYPE
