{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_ExhaustState :
    (
        INIT           := 0,
        PUMP_ON        := 1,
        OPENING_VALVE  := 2,   // Commands valve open; polls IsOpen() with VALVE_TIMEOUT
        WAIT_PRESSURE  := 3,   // Waits for pressure >= threshold with PRESSURE_TIMEOUT
        READY          := 4,
        ERROR          := 99
    )INT;
END_TYPE
