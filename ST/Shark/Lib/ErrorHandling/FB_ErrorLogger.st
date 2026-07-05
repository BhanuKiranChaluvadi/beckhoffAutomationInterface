(* Global logger. Single funnel for all library logging (errors + events).
   Owns logging POLICY only (the LogError/LogEvent/LogInfo API + id/severity
   resolution); the actual transport is delegated to an I_LogSink. The sink
   defaults to the built-in FB_TcEventLoggerSink and can be replaced via
   SetSink (e.g. a test spy or a machine-specific transport).
   Deduplication/throttling is handled by callers (e.g. FB_ErrorHandler). *)
FUNCTION_BLOCK FB_ErrorLogger
VAR
    _fbDefaultSink : FB_TcEventLoggerSink; // Built-in transport (TC3 EventLogger + console trace)
    _ipSink        : I_LogSink;            // Active transport; defaults to _fbDefaultSink
END_VAR

METHOD PUBLIC LogError : BOOL
VAR_INPUT
    sSource       : STRING(255);
    eErrorId      : E_ErrorId;
    sContext      : STRING(255);
    eSeverity     : E_TcEventSeverity := E_TcEventSeverity.Error;
    fCurrentValue : REAL := 0;          // Optional: measured value, exported in the event JSON attribute
    fExpectedValue: REAL := 0;          // Optional: limit/setpoint, exported in the event JSON attribute
    sUnits        : STRING(15) := '';   // Optional: units for the values (e.g. 'sccm', 'bar')
END_VAR

    IF eErrorId = E_ErrorId.NO_ERROR THEN
        RETURN;
    END_IF

    _Emit(sSource := sSource, nEventId := TO_UDINT(eErrorId), sId := TO_STRING(eErrorId), sContext := sContext, eSeverity := eSeverity,
          fCurrentValue := fCurrentValue, fExpectedValue := fExpectedValue, sUnits := sUnits);
    LogError := TRUE;
END_METHOD

    (* Logs a non-error operational event (severity defaults to Info). *)
METHOD PUBLIC LogEvent : BOOL
VAR_INPUT
    sSource    : STRING(255);
    eEventId   : E_EventId;
    sContext   : STRING(255);
    eSeverity  : E_TcEventSeverity := E_TcEventSeverity.Info;
END_VAR

    _Emit(sSource := sSource, nEventId := TO_UDINT(eEventId), sId := TO_STRING(eEventId), sContext := sContext, eSeverity := eSeverity);
    LogEvent := TRUE;
END_METHOD

    (* Logs a generic informational message (free text, no specific event ID). *)
METHOD PUBLIC LogInfo : BOOL
VAR_INPUT
    sSource  : STRING(255);
    sContext : STRING(255);
END_VAR

    _Emit(sSource := sSource, nEventId := TO_UDINT(E_EventId.EVT_INFO), sId := TO_STRING(E_EventId.EVT_INFO), sContext := sContext, eSeverity := E_TcEventSeverity.Info);
    LogInfo := TRUE;
END_METHOD

    (* Shared sink: resolves the active transport (defaulting to the built-in
       TC3 EventLogger sink) and publishes the record. This is the single point
       that controls all logging. *)
METHOD PRIVATE _Emit
VAR_INPUT
    sSource       : STRING(255);
    nEventId      : UDINT;
    sId           : STRING(80);
    sContext      : STRING(255);
    eSeverity     : E_TcEventSeverity;
    fCurrentValue : REAL := 0;
    fExpectedValue: REAL := 0;
    sUnits        : STRING(15) := '';
END_VAR
    // Lazily bind the default sink so the logger is never null and works out of
    // the box; SetSink can override it before or after first use.
    IF _ipSink = 0 THEN
        _ipSink := _fbDefaultSink;
    END_IF

    _ipSink.Publish(sSource := sSource, nEventId := nEventId, sId := sId,
                    sContext := sContext, eSeverity := eSeverity,
                    fCurrentValue := fCurrentValue, fExpectedValue := fExpectedValue, sUnits := sUnits);
END_METHOD

    (* Overrides the transport sink (e.g. a test spy or a machine-specific
       transport). A null sink is ignored so logging is never broken. *)
METHOD PUBLIC SetSink
VAR_INPUT
    ipSink : I_LogSink;
END_VAR
    IF ipSink <> 0 THEN
        _ipSink := ipSink;
    END_IF
END_METHOD

END_FUNCTION_BLOCK
