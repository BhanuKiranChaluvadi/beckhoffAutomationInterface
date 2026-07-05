{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_EL7062_HomeState :
    (
        Idle            := 0,
        StartJog        := 10,
        WaitNegLimit    := 20,
        SetHome_AtSwitch := 30,
        MoveToMiddle    := 40,
        SetHome_Final   := 50,
        Fault           := 900
    )INT;
END_TYPE
