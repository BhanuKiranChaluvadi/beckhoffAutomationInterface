{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_EL7062_MotorState :
    (
        Idle   := 0,
        Ready  := 1,
        Moving := 2,
        Homing := 10,
        Stop   := 20
    )INT;
END_TYPE
