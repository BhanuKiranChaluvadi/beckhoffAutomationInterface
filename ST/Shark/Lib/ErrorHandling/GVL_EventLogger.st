(* TC3 EventLogger integration: console-trace switch. *)
{attribute 'qualified_only'}
VAR_GLOBAL
    // Master switch for the parallel ADSLOG console trace in FB_TcEventLoggerSink.
    // The TC3 EventLogger is always the primary sink. Keep TRUE while verifying
    // the EventLogger path in XAE; set FALSE to make the EventLogger the sole sink.
    bConsoleTrace : BOOL := TRUE;
END_VAR
