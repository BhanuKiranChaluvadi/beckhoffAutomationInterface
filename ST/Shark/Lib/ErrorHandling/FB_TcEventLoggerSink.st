(* Default log sink: TwinCAT 3 EventLogger transport, plus an optional ADSLOG
   console trace gated by GVL_EventLogger.bConsoleTrace.

   This is the built-in sink used by FB_ErrorLogger out of the box. Swap it via
   FB_ErrorLogger.SetSink to redirect logging (e.g. a test spy or a
   machine-specific transport) without changing any logging policy.

   NOTE: requires the Tc3_EventLogger and Tc3_JsonXml library references and an
   event class named BeckhoffLibEvents registered in XAE, holding one event per
   severity (Verbose, Info, Warning, Error, Critical). Each event text uses two
   indexed insertions, '{0} - {1}', for the error id and the dynamic context.
   TwinCAT substitutes arguments by zero-based index ({0}, {1}) — NOT printf '%s'.
   Each event also carries a JSON attribute so HMI/clients can read structured
   values directly. The attribute keeps the legacy logger's field names for
   client back-compat — {sComponentName, eErrorId, sErrorMessage, fCurrentValue,
   fExpectedValue, sUnits, tTimestamp} — and appends two new fields:
   sErrorIdText (the symbolic error-id name) and eSeverity (numeric severity). *)
FUNCTION_BLOCK FB_TcEventLoggerSink IMPLEMENTS I_LogSink
VAR
    _fbMessage    : FB_TcMessage;        // TC3 EventLogger message
    _fbSourceInfo : FB_TcSourceInfo;     // Carries the emitting source so the HMI can distinguish same-id events
    _fbJson       : FB_JsonSaxWriter;    // Builds the per-event JSON attribute consumed by HMI/clients
END_VAR

METHOD Publish
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
VAR
    sFullMsg   : STRING(255);
    nMsgType   : DWORD;
    stEntry    : TcEventEntry;
    sJson      : STRING(800);
    sTimestamp : STRING(19);
    hr         : HRESULT;
END_VAR
    // 1) Optional console trace (ADSLOG), gated by a global switch. The TC3
    //    EventLogger (below) is the primary sink; this is for live tracing.
    IF GVL_EventLogger.bConsoleTrace THEN
        CASE eSeverity OF
            E_TcEventSeverity.Verbose,
            E_TcEventSeverity.Info:
                nMsgType := ADSLOG_MSGTYPE_HINT;
            E_TcEventSeverity.Warning:
                nMsgType := ADSLOG_MSGTYPE_WARN;
            E_TcEventSeverity.Error,
            E_TcEventSeverity.Critical:
                nMsgType := ADSLOG_MSGTYPE_ERROR;
        ELSE
            nMsgType := ADSLOG_MSGTYPE_ERROR;
        END_CASE
        sFullMsg := CONCAT(CONCAT(sSource, ': '), CONCAT(sId, ' - '));
        sFullMsg := CONCAT(sFullMsg, sContext);
        ADSLOGSTR(nMsgType, '%s', sFullMsg);
    END_IF

    // 2) TC3 EventLogger (persistent; consumed by the HMI over ADS). Severity
    //    selects one of five registered events in the BeckhoffLibEvents class;
    //    the event text and severity come from that registration. sSource is
    //    attached as source info so the HMI shows which POU/hardware raised it,
    //    and sId + sContext are passed as message arguments so the registered
    //    text can carry the dynamic detail via indexed insertions ('{0} - {1}').
    CASE eSeverity OF
        E_TcEventSeverity.Verbose:
            stEntry := TC_EVENTS.BeckhoffLibEvents.Verbose;
        E_TcEventSeverity.Info:
            stEntry := TC_EVENTS.BeckhoffLibEvents.Info;
        E_TcEventSeverity.Warning:
            stEntry := TC_EVENTS.BeckhoffLibEvents.Warning;
        E_TcEventSeverity.Error:
            stEntry := TC_EVENTS.BeckhoffLibEvents.Error;
        E_TcEventSeverity.Critical:
            stEntry := TC_EVENTS.BeckhoffLibEvents.Critical;
    ELSE
        stEntry := TC_EVENTS.BeckhoffLibEvents.Error;
    END_CASE

    // Clear() resets stale fields before reuse — this single source-info instance
    // is shared across every source, so without it a prior source's data could leak.
    _fbSourceInfo.Clear();
    _fbSourceInfo.sName := sSource;
    hr := _fbMessage.CreateEx(stEntry, _fbSourceInfo);
    IF _fbMessage.ipArguments <> 0 THEN
        _fbMessage.ipArguments.Clear();
        _fbMessage.ipArguments.AddString(sId);
        _fbMessage.ipArguments.AddString(sContext);
    END_IF
    // Attach the full record as the event JSON attribute so HMI/clients get every
    // detail in one place. The first seven keys keep the legacy logger's names
    // and order for client back-compat; sErrorIdText and eSeverity are appended
    // as new, additive fields so existing parsers keep working.
    sTimestamp := F_FormatIso8601(F_GetCurrentDT());   // AddString takes its arg by reference, so it needs a writable variable
    _fbJson.ResetDocument();
    _fbJson.StartObject();
    _fbJson.AddKey('sComponentName');  _fbJson.AddString(sSource);
    _fbJson.AddKey('eErrorId');        _fbJson.AddUdint(nEventId);
    _fbJson.AddKey('sErrorMessage');   _fbJson.AddString(sContext);
    _fbJson.AddKey('fCurrentValue');   _fbJson.AddReal(fCurrentValue);
    _fbJson.AddKey('fExpectedValue');  _fbJson.AddReal(fExpectedValue);
    _fbJson.AddKey('sUnits');          _fbJson.AddString(sUnits);
    _fbJson.AddKey('tTimestamp');      _fbJson.AddString(sTimestamp);
    _fbJson.AddKey('sErrorIdText');    _fbJson.AddString(sId);
    _fbJson.AddKey('eSeverity');       _fbJson.AddDint(TO_DINT(eSeverity));
    _fbJson.EndObject();
    _fbJson.CopyDocument(sJson, SIZEOF(sJson));
    _fbMessage.SetJsonAttribute(sJson);
    hr := _fbMessage.Send(0);
END_METHOD

END_FUNCTION_BLOCK
