{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_SafetyState :
    (
        INIT                := 0,
        OK                  := 1,
        WAIT_FOR_RESET_BTN  := 2,
        ERROR               := 99
    )INT;
END_TYPE
