(* Transport abstraction for the logging facade (FB_ErrorLogger).
   A sink receives a fully-resolved log record and is responsible ONLY for
   getting it out (TC3 EventLogger, ADSLOG console trace, a test spy, a
   machine-specific transport, ...).
   Swapping the sink lets a machine or a test change the transport without
   touching the logging policy/throttle in FB_ErrorLogger / FB_ErrorHandler. *)
INTERFACE I_LogSink

METHOD Publish
VAR_INPUT
    sSource       : STRING(255);            // Emitting source (POU/method) for HMI source info
    nEventId      : UDINT;                  // E_ErrorId / E_EventId value
    sId           : STRING(80);             // Human-readable id (for the console trace)
    sContext      : STRING(255);            // Dynamic detail (sensor reading, command, ...)
    eSeverity     : E_TcEventSeverity;
    fCurrentValue : REAL := 0;              // Optional measured value, exported in the JSON attribute
    fExpectedValue: REAL := 0;              // Optional limit/setpoint, exported in the JSON attribute
    sUnits        : STRING(15) := '';       // Optional units for the values
END_VAR
END_METHOD

END_INTERFACE
