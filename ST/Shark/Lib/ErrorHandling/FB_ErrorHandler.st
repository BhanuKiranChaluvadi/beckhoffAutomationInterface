(* Centralized error handling component for raising and managing errors. *)
FUNCTION_BLOCK FB_ErrorHandler
VAR
    _stError : ST_Error;
    _sSourceName : STRING(80);
    // Heartbeat throttle: re-log a persistent fault at most once per interval
    // (T#0S disables the heartbeat: first occurrence + clear only).
    _tHeartbeatInterval : TIME := T#1M;
    _tLastLogTime : DT;
    _nOccurrenceCount : UDINT;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    sSourceName        : STRING(80);
    tHeartbeatInterval : TIME := T#1M;
END_VAR
    _sSourceName := sSourceName;
    _tHeartbeatInterval := tHeartbeatInterval;
    Clear();
    Init := TRUE;
END_METHOD

METHOD PUBLIC Raise
VAR_INPUT
    eErrorId  : E_ErrorId;
    sMessage  : STRING(255);
    eSeverity : E_TcEventSeverity := E_TcEventSeverity.Error;
END_VAR
    IF eErrorId = E_ErrorId.NO_ERROR THEN
        RETURN;
    END_IF

    // Same fault still active: count it and heartbeat periodically,
    // instead of flooding every cycle or going silent forever.
    IF _stError.eErrorId = eErrorId THEN
        _HandleRepeat();
        RETURN;
    END_IF

    _ApplyError(eErrorId := eErrorId, sMessage := sMessage, eSeverity := eSeverity);
END_METHOD

METHOD PUBLIC RaiseWithValues
VAR_INPUT
    eErrorId  : E_ErrorId;
    sLabel    : STRING(20);
    rActual   : LREAL;
    rLimit    : LREAL;
    sUnit     : STRING(10);
    eSeverity : E_TcEventSeverity := E_TcEventSeverity.Error;
END_VAR
VAR
    sMessage : STRING(255);
END_VAR
    IF eErrorId = E_ErrorId.NO_ERROR THEN
        RETURN;
    END_IF

    // Same fault still active: count it and heartbeat periodically,
    // instead of flooding every cycle or going silent forever.
    IF _stError.eErrorId = eErrorId THEN
        _HandleRepeat();
        RETURN;
    END_IF

    sMessage := F_FormatLimitError(sLabel, rActual, rLimit, sUnit);
    _ApplyError(eErrorId := eErrorId, sMessage := sMessage, eSeverity := eSeverity,
                fCurrentValue := TO_REAL(rActual), fExpectedValue := TO_REAL(rLimit), sUnits := sUnit);
END_METHOD

    (* Raises an error from a child component, chaining the source for traceability.
       Preserves the child's origin timestamp and severity. *)
METHOD PUBLIC RaiseFromChild
VAR_INPUT
    stChildError : ST_Error;
END_VAR
VAR
    sChainedMsg : STRING(255);
END_VAR
    IF stChildError.eErrorId = E_ErrorId.NO_ERROR THEN
        RETURN;
    END_IF

    // Prevent cyclic spamming of string manipulation
    IF _stError.eErrorId = stChildError.eErrorId THEN
        RETURN;
    END_IF

    sChainedMsg := LEFT(CONCAT(CONCAT('[', stChildError.sSource), CONCAT('] ', stChildError.sMessage)), 255);

    _stError.eErrorId := stChildError.eErrorId;
    _stError.eSeverity := stChildError.eSeverity;
    _stError.sSource := _sSourceName;
    _stError.sMessage := sChainedMsg;
    _stError.fCurrentValue := stChildError.fCurrentValue;
    _stError.fExpectedValue := stChildError.fExpectedValue;
    _stError.sUnits := stChildError.sUnits;
    _stError.tTimestamp := F_GetCurrentDT();
    // Preserve the root-cause occurrence time from the child when available,
    // so forensics at subsystem level can still see when the fault first hit.
    IF stChildError.tOriginTimestamp <> DT#1970-01-01-00:00:00 THEN
        _stError.tOriginTimestamp := stChildError.tOriginTimestamp;
    ELSE
        _stError.tOriginTimestamp := _stError.tTimestamp;
    END_IF
    _nOccurrenceCount := 1;
    _tLastLogTime := _stError.tTimestamp;
    // Do NOT re-log here: the error was already logged when it originated.
    // Propagation only carries state up the tree.
END_METHOD

METHOD PRIVATE _ApplyError
VAR_INPUT
    eErrorId      : E_ErrorId;
    sMessage      : STRING(255);
    eSeverity     : E_TcEventSeverity;
    fCurrentValue : REAL := 0;
    fExpectedValue: REAL := 0;
    sUnits        : STRING(15) := '';
END_VAR
VAR
    tNow : DT;
END_VAR
    tNow := F_GetCurrentDT();
    _stError.eErrorId := eErrorId;
    _stError.eSeverity := eSeverity;
    _stError.sSource := _sSourceName;
    _stError.sMessage := sMessage;
    _stError.fCurrentValue := fCurrentValue;
    _stError.fExpectedValue := fExpectedValue;
    _stError.sUnits := sUnits;
    _stError.tTimestamp := tNow;
    _stError.tOriginTimestamp := tNow;
    _nOccurrenceCount := 1;
    _tLastLogTime := tNow;
    _LogError(sMessage);
END_METHOD


METHOD PRIVATE _LogError
VAR_INPUT
    sContext : STRING(255);
END_VAR
    GVL_ErrorLogger.Logger.LogError(
        sSource := _sSourceName,
        eErrorId := _stError.eErrorId,
        sContext := sContext,
        eSeverity := _stError.eSeverity,
        fCurrentValue := _stError.fCurrentValue,
        fExpectedValue := _stError.fExpectedValue,
        sUnits := _stError.sUnits
    );
END_METHOD

    (* Handles a re-raise of the currently active fault: counts the occurrence and
       re-logs at most once per heartbeat interval so a persistent fault stays
       visible without flooding the log every scan cycle. *)
METHOD PRIVATE _HandleRepeat
VAR
    tNow         : DT;
    nElapsedSec  : DWORD;
    nIntervalSec : DWORD;
END_VAR
    _nOccurrenceCount := _nOccurrenceCount + 1;

    nIntervalSec := TIME_TO_DWORD(_tHeartbeatInterval) / 1000;
    IF nIntervalSec = 0 THEN
        RETURN; // Heartbeat disabled: first occurrence + clear only.
    END_IF

    tNow := F_GetCurrentDT();
    nElapsedSec := DT_TO_DWORD(tNow) - DT_TO_DWORD(_tLastLogTime);
    IF nElapsedSec >= nIntervalSec THEN
        _stError.tTimestamp := tNow;
        _LogHeartbeat();
        _tLastLogTime := tNow;
    END_IF
END_METHOD

    (* Re-logs the active fault with its occurrence count (heartbeat). *)
METHOD PRIVATE _LogHeartbeat
VAR
    sContext : STRING(255);
END_VAR
    sContext := CONCAT('still active (', UDINT_TO_STRING(_nOccurrenceCount));
    sContext := CONCAT(sContext, 'x): ');
    sContext := LEFT(CONCAT(sContext, _stError.sMessage), 255);
    GVL_ErrorLogger.Logger.LogError(
        sSource := _sSourceName,
        eErrorId := _stError.eErrorId,
        sContext := sContext,
        eSeverity := _stError.eSeverity,
        fCurrentValue := _stError.fCurrentValue,
        fExpectedValue := _stError.fExpectedValue,
        sUnits := _stError.sUnits
    );
END_METHOD

    (* Logs a one-line resolution summary when an active fault clears. *)
METHOD PRIVATE _LogCleared
VAR
    sContext : STRING(255);
END_VAR
    sContext := CONCAT('cleared after ', UDINT_TO_STRING(_nOccurrenceCount));
    sContext := CONCAT(sContext, ' occurrence(s)');
    GVL_ErrorLogger.Logger.LogError(
        sSource := _sSourceName,
        eErrorId := _stError.eErrorId,
        sContext := sContext,
        eSeverity := E_TcEventSeverity.Info,
        fCurrentValue := _stError.fCurrentValue,
        fExpectedValue := _stError.fExpectedValue,
        sUnits := _stError.sUnits
    );
END_METHOD

METHOD PUBLIC Clear
    // Emit a one-line resolution summary when an active fault clears, so the
    // log records when and after how many occurrences it recovered.
    IF _stError.eErrorId <> E_ErrorId.NO_ERROR THEN
        _LogCleared();
    END_IF

    _stError.eErrorId := E_ErrorId.NO_ERROR;
    _stError.eSeverity := E_TcEventSeverity.Error;
    _stError.sSource := '';
    _stError.sMessage := '';
    _stError.fCurrentValue := 0;
    _stError.fExpectedValue := 0;
    _stError.sUnits := '';
    _stError.tTimestamp := DT#1970-01-01-00:00:00;
    _stError.tOriginTimestamp := DT#1970-01-01-00:00:00;
    _nOccurrenceCount := 0;
    _tLastLogTime := DT#1970-01-01-00:00:00;
END_METHOD

METHOD PUBLIC IsActive : BOOL
    IsActive := _stError.eErrorId <> E_ErrorId.NO_ERROR;
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _stError;
END_METHOD

METHOD PUBLIC GetErrorId : E_ErrorId
    GetErrorId := _stError.eErrorId;
END_METHOD

END_FUNCTION_BLOCK




