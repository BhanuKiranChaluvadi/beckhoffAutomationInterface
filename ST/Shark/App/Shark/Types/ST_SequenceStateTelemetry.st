{attribute 'pack_mode' := '0'}
TYPE ST_SequenceStateTelemetry :
STRUCT
    MachineState         : INT;     // Current active E_Sequence as INT (0=None, 1=ReactantStart, 3=PrecursorStart, 5=HeatingStart, 9=SpotDeposition, 99=Error)
    SubState             : INT;     // Progress percentage (0-100) of active sequence from rProgress; SubStateCount is always 100 to normalize percentage scale
    SubStateCount        : INT;     // Denominator for progress % (always 100 for percentage-based reporting)
    ErrorID              : INT;     // E_ErrorId enum as INT; non-zero indicates error state in active sequence
    LastPrecursorIndex   : INT;     // Last purged A-side bubbler index; updated after successful PurgePrecursor sequence
    LastReactantIndex    : INT;     // Last purged B-side bubbler index; updated after successful PurgeReactant sequence
    TempAside            : REAL;    // A-side (precursor/umbilical) temperature in °C; 0.0 if not available on Shark
    TempBside            : REAL;    // B-side (reactant/umbilical) temperature in °C; 0.0 if not available on Shark
    TempChamber          : REAL;    // Chamber/reactor temperature in °C from monitor zone sensor
    HumiChamber          : REAL;    // Chamber relative humidity in %; 0.0 if not available on Shark
    isSampleHold         : BOOL;    // TRUE during ChuckVacuumHold sequence; FALSE otherwise
    CommandIndex         : DINT;    // Last command index received and parsed by FB_CommandReceiver; correlates commands with telemetry state
    sVersion             : STRING(10); // Machine firmware version string from GVL_Version (e.g. '1.2.0')
END_STRUCT
END_TYPE
