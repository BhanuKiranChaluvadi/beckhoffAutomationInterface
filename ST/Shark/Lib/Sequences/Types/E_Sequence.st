{attribute 'qualified_only'}
{attribute 'strict'}
{attribute 'to_string'}
TYPE E_Sequence :
(
    None              := 0,
    ReactantStart     := 1,     // B-Side bubbler start
    ReactantStop      := 2,     // B-Side bubbler stop
    PrecursorStart    := 3,     // A-Side bubbler start
    PrecursorStop     := 4,     // A-Side bubbler stop
    HeatingStart      := 5,     // Temperature gradient
    Stop              := 6,     // System stop
    PurgePrecursor    := 7,     // A-Side purge
    PurgeReactant     := 8,     // B-Side purge
    SpotDeposition    := 9,
    BubblerLeakTest   := 10,
    ChuckVacuumHold   := 11,
    ChuckVacuumRelease:= 12,
    Test              := 98,
    Error             := 99
);
END_TYPE
