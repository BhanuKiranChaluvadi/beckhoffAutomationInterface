{attribute 'qualified_only'}
{attribute 'strict'}
{attribute 'to_string'}
TYPE E_ChuckVacuumState :
(
    Idle            := 0,   // Ready to start
    VerifyingPump   := 1,   // Checking/starting vacuum pump
    OpeningValves   := 2,   // Opening zone valves (hold) or closing (release)
    WaitingVacuum   := 3,   // Waiting for vacuum/release to be achieved
    Done            := 10,  // Operation completed successfully
    Error           := 99   // Operation failed
) INT;
END_TYPE
