(*
FB_SequenceOrchestrator — Shared sequence dispatch and execution engine.

All sequence FBs, types, and enums used here live in Lib, so this FB has
no App-level dependencies and compiles independently of any machine target.

USAGE (thin PROGRAM wrapper pattern):
  PROGRAM PRG_Sequence
  VAR
      fbOrchestrator : FB_SequenceOrchestrator;
  END_VAR
      fbOrchestrator();
  // Delegate methods call fbOrchestrator.XxxMethod()

ADDING A NEW SEQUENCE:
  1. Create FB_MySequence IMPLEMENTS I_TreeNode in Lib/Sequences/
  2. Add instance to VAR: _fbMySeq : FB_MySequence
  3. Add public dispatch method that calls _fbMySeq.Init() + _Start()
  4. Add the E_Sequence enum value to Lib/Sequences/Types/E_Sequence.st
*)
FUNCTION_BLOCK FB_SequenceOrchestrator
VAR
    eActiveSequence     : E_Sequence := E_Sequence.None;
    sStatus             : STRING(255) := 'Ready';
    bBusy               : BOOL;
    bDone               : BOOL;
    bError              : BOOL;
    rProgress           : REAL := 0.0;
    stLastError         : ST_Error;
    nLastPurgedBubblerA : INT := 0;     // Last bubbler index purged on A-side (precursor)
    nLastPurgedBubblerB : INT := 0;     // Last bubbler index purged on B-side (reactant)
END_VAR
VAR
    _ipProvider       : I_HardwareProvider;
    _bConfigured      : BOOL := FALSE;
    _ipActiveSequence : I_TreeNode;
    _stLastResult     : ST_NodeResult;
    _eSide            : E_GasSide;
    _nBubblerIndex    : DINT;

    // Shared sequences
    _fbGasStart         : FB_GasStartSequence;
    _fbGasStop          : FB_GasStopSequence;
    _fbSystemStop       : FB_SystemStopSequence;
    _fbMaterialPurge    : FB_MaterialPurgeSequence;
    _fbBubblerPurge     : FB_BubblerPurgeSequence;
    _fbMaintenancePurge : FB_MaintenancePurgeSequence;
    _fbHeating          : FB_HeatingSequence;
    _fbChuckHold        : FB_ChuckVacuumHoldSequence;
    _fbChuckRelease     : FB_ChuckVacuumReleaseSequence;

    // Extended sequences (types and FBs are in Lib; not all machines use them)
    _fbSpotDeposition   : FB_SpotDepositionSequence;
    _fbBubblerLeakTest  : FB_BubblerLeakTestSequence;
END_VAR

    IF NOT _bConfigured THEN
        sStatus := 'Not configured';
        RETURN;
    END_IF

    IF eActiveSequence <> E_Sequence.None AND eActiveSequence <> E_Sequence.Error THEN
        _ExecuteSequence();
    END_IF

END_FUNCTION_BLOCK


// =============================================================================
// PUBLIC API
// =============================================================================

METHOD PUBLIC Configure : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
    IF ipProvider = 0 THEN RETURN; END_IF
    _ipProvider  := ipProvider;
    _bConfigured := TRUE;
    sStatus      := 'Ready';
    Configure    := TRUE;
END_METHOD


METHOD PUBLIC GetStatus : ST_SequenceStatus
    GetStatus.eActiveSequence     := eActiveSequence;
    GetStatus.bBusy               := bBusy;
    GetStatus.bDone               := bDone;
    GetStatus.bError              := bError;
    GetStatus.sStatusMessage      := sStatus;
    GetStatus.rProgress           := rProgress;
    GetStatus.stLastError         := stLastError;
    GetStatus.nLastPurgedBubblerA := nLastPurgedBubblerA;
    GetStatus.nLastPurgedBubblerB := nLastPurgedBubblerB;
END_METHOD


METHOD PUBLIC ErrorAcknowledge : BOOL
    IF eActiveSequence <> E_Sequence.Error THEN RETURN; END_IF
    _ResetState();
    ErrorAcknowledge := TRUE;
END_METHOD


METHOD PUBLIC AcknowledgeDone : BOOL
    // Clears the latched bDone once a successful completion has been observed.
    IF NOT bDone THEN RETURN; END_IF
    bDone           := FALSE;
    sStatus         := 'Ready';
    AcknowledgeDone := TRUE;
END_METHOD


METHOD PUBLIC StartBubblerFlow : BOOL
VAR_INPUT
    stParams : ST_CmdStartBubblerFlow;
END_VAR
VAR
    eSide : E_GasSide;
    eSeq  : E_Sequence;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF
    IF NOT _MapSide(nSide := stParams.side, eSide => eSide) THEN RETURN; END_IF

    eSeq := SEL(eSide = E_GasSide.A_SIDE, E_Sequence.ReactantStart, E_Sequence.PrecursorStart);

    IF NOT _fbGasStart.Init(ipProvider := _ipProvider, eSide := eSide, nBubblerIndex := stParams.bubblerIndex) THEN
        _SetError('GasStart init failed');
        RETURN;
    END_IF
    _fbGasStart.SetParams(stParams);
    _ipActiveSequence := _fbGasStart;
    _eSide            := eSide;
    _nBubblerIndex    := stParams.bubblerIndex;
    _Start(eSeq, 'Starting bubbler flow...');
    StartBubblerFlow := TRUE;
END_METHOD


METHOD PUBLIC StopBubblerFlow : BOOL
VAR_INPUT
    stParams : ST_CmdStopBubblerFlow;
END_VAR
VAR
    eSide : E_GasSide;
    eSeq  : E_Sequence;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF
    IF NOT _MapSide(nSide := stParams.side, eSide => eSide) THEN RETURN; END_IF

    eSeq := SEL(eSide = E_GasSide.A_SIDE, E_Sequence.ReactantStop, E_Sequence.PrecursorStop);

    IF NOT _fbGasStop.Init(ipProvider := _ipProvider, eSide := eSide) THEN
        _SetError('GasStop init failed');
        RETURN;
    END_IF
    _ipActiveSequence := _fbGasStop;
    _eSide            := eSide;
    _Start(eSeq, 'Stopping bubbler flow...');
    StopBubblerFlow := TRUE;
END_METHOD


METHOD PUBLIC SystemStop : BOOL
    SystemStop := _InitiateSystemStop('System stop');
END_METHOD


METHOD PUBLIC EmergencyStop : BOOL
    // Immediate safe-state halt. Unlike SystemStop, this does NOT run the
    // multi-step FB_SystemStopSequence; it halts the active sequence right away
    // (Halt cascades hardware to its safe state) and returns to IDLE.
    _AbortCurrentSequence();
    _ResetState();
    sStatus       := 'Emergency stop';
    EmergencyStop := TRUE;
END_METHOD


METHOD PUBLIC StartPurge : BOOL
VAR_INPUT
    stParams : ST_CmdPurge;
END_VAR
VAR
    eSide : E_GasSide;
    eSeq  : E_Sequence;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF
    IF NOT _MapSide(nSide := stParams.side, eSide => eSide) THEN RETURN; END_IF

    eSeq           := SEL(eSide = E_GasSide.A_SIDE, E_Sequence.PurgeReactant, E_Sequence.PurgePrecursor);
    _nBubblerIndex := INT_TO_DINT(stParams.bubbler);
    _eSide         := eSide;

    CASE stParams.mode OF
        E_PurgeMode.ChangeMaterial:
            IF NOT _fbMaterialPurge.Init(ipProvider := _ipProvider, eSide := eSide) THEN
                _SetError('MaterialPurge init failed');
                RETURN;
            END_IF
            _fbMaterialPurge.SetParams(stParams);
            _ipActiveSequence := _fbMaterialPurge;
            _Start(eSeq, 'Material purge...');

        E_PurgeMode.ReplaceBubbler:
            IF NOT _fbBubblerPurge.Init(ipProvider := _ipProvider, eSide := eSide, nBubblerIndex := INT_TO_DINT(stParams.bubbler)) THEN
                _SetError('BubblerPurge init failed');
                RETURN;
            END_IF
            _fbBubblerPurge.SetParams(stParams);
            _ipActiveSequence := _fbBubblerPurge;
            _Start(eSeq, 'Bubbler purge...');

        E_PurgeMode.Maintenance:
            IF NOT _fbMaintenancePurge.Init(ipProvider := _ipProvider, eSide := eSide, nBubblerIndex := INT_TO_DINT(stParams.bubbler)) THEN
                _SetError('MaintenancePurge init failed');
                RETURN;
            END_IF
            _fbMaintenancePurge.SetParams(stParams);
            _ipActiveSequence := _fbMaintenancePurge;
            _Start(eSeq, 'Maintenance purge...');
    ELSE
        sStatus := 'Invalid purge mode';
        RETURN;
    END_CASE
    StartPurge := TRUE;
END_METHOD


METHOD PUBLIC StartHeating : BOOL
VAR_INPUT
    stParams : ST_CmdSetTemperatureGradient;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF

    IF NOT _fbHeating.Init(ipProvider := _ipProvider) THEN
        _SetError('Heating init failed');
        RETURN;
    END_IF
    _fbHeating.SetParams(stParams);
    _ipActiveSequence := _fbHeating;
    _Start(E_Sequence.HeatingStart, 'Setting temperature gradient...');
    StartHeating := TRUE;
END_METHOD


METHOD PUBLIC StartChuckVacuumHold : BOOL
VAR_INPUT
    stParams : ST_ChuckVacuumHold;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF

    IF NOT _fbChuckHold.Init(ipProvider := _ipProvider) THEN
        _SetError('ChuckVacuumHold init failed');
        RETURN;
    END_IF
    _fbChuckHold.SetParams(stParams);
    _ipActiveSequence := _fbChuckHold;
    _Start(E_Sequence.ChuckVacuumHold, 'Applying chuck vacuum...');
    StartChuckVacuumHold := TRUE;
END_METHOD


METHOD PUBLIC StartChuckVacuumRelease : BOOL
VAR_INPUT
    stParams : ST_ChuckVacuumRelease;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF

    IF NOT _fbChuckRelease.Init(ipProvider := _ipProvider) THEN
        _SetError('ChuckVacuumRelease init failed');
        RETURN;
    END_IF
    _fbChuckRelease.SetParams(stParams);
    _ipActiveSequence := _fbChuckRelease;
    _Start(E_Sequence.ChuckVacuumRelease, 'Releasing chuck vacuum...');
    StartChuckVacuumRelease := TRUE;
END_METHOD


METHOD PUBLIC StartSpotDeposition : BOOL
VAR_INPUT
    stParams : ST_CmdSpotDeposition;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF

    IF NOT _fbSpotDeposition.Init(ipProvider := _ipProvider) THEN
        _SetError('SpotDeposition init failed');
        RETURN;
    END_IF
    _fbSpotDeposition.SetParams(stParams);
    _ipActiveSequence := _fbSpotDeposition;
    _Start(E_Sequence.SpotDeposition, 'Spot deposition...');
    StartSpotDeposition := TRUE;
END_METHOD


METHOD PUBLIC StartBubblerLeakTest : BOOL
VAR_INPUT
    stParams : ST_CmdBubblerLeakTest;
END_VAR
VAR
    eSide : E_GasSide;
END_VAR
    IF NOT _CanStart() THEN RETURN; END_IF
    IF NOT _MapSide(nSide := stParams.side, eSide => eSide) THEN RETURN; END_IF

    IF NOT _fbBubblerLeakTest.Init(ipProvider := _ipProvider, eSide := eSide, nBubblerIndex := stParams.bubblerIndex) THEN
        _SetError('BubblerLeakTest init failed');
        RETURN;
    END_IF
    _fbBubblerLeakTest.SetParams(stParams);
    _ipActiveSequence := _fbBubblerLeakTest;
    _eSide            := eSide;
    _nBubblerIndex    := stParams.bubblerIndex;
    _Start(E_Sequence.BubblerLeakTest, 'Bubbler leak test...');
    StartBubblerLeakTest := TRUE;
END_METHOD


// =============================================================================
// PRIVATE — Execution Engine
// =============================================================================

METHOD PRIVATE _CanStart : BOOL
    CASE eActiveSequence OF
        E_Sequence.None:
            _CanStart := TRUE;
        E_Sequence.Error:
            sStatus := 'Error not acknowledged';
    ELSE
        sStatus := 'Sequence already running';
    END_CASE
END_METHOD


METHOD PRIVATE _Start
VAR_INPUT
    eSeq : E_Sequence;
    sMsg : STRING(255);
END_VAR
    _ipActiveSequence.Reset();
    eActiveSequence := eSeq;
    bBusy           := TRUE;
    bDone           := FALSE;
    bError          := FALSE;
    rProgress       := 0.0;
    sStatus         := sMsg;
END_METHOD


METHOD PRIVATE _ExecuteSequence
    IF _ipActiveSequence = 0 THEN
        _SetError('No active sequence', E_ErrorId.ERR_GEN_NULL_POINTER);
        RETURN;
    END_IF

    _stLastResult := _ipActiveSequence.Tick();
    sStatus       := _stLastResult.sDescription;

    CASE _stLastResult.eStatus OF
        E_NodeStatus.RUNNING:
            bBusy     := TRUE;
            rProgress := _stLastResult.rProgress;
            IF sStatus = '' THEN sStatus := 'Running...'; END_IF

        E_NodeStatus.SUCCESS:
            _UpdateLastPurgedBubbler();
            _CompleteSequence();

        E_NodeStatus.FAILURE:
            IF sStatus = '' THEN sStatus := 'Sequence failed'; END_IF
            IF _stLastResult.bHasError THEN
                stLastError := _stLastResult.stError;
                // Single audit-trail bridge: surface the runtime result error to the global logger.
                GVL_ErrorLogger.Logger.LogError(
                    sSource        := stLastError.sSource,
                    eErrorId       := stLastError.eErrorId,
                    sContext       := stLastError.sMessage,
                    eSeverity      := stLastError.eSeverity,
                    fCurrentValue  := stLastError.fCurrentValue,
                    fExpectedValue := stLastError.fExpectedValue,
                    sUnits         := stLastError.sUnits);
            END_IF
            // Drive hardware to a safe state on failure (Halt cascades to all child nodes).
            IF _ipActiveSequence <> 0 THEN
                _ipActiveSequence.Halt();
            END_IF
            eActiveSequence := E_Sequence.Error;
            bBusy           := FALSE;
            bError          := TRUE;
    END_CASE
END_METHOD


METHOD PRIVATE _MapSide : BOOL
VAR_INPUT
    nSide : INT;
END_VAR
VAR_OUTPUT
    eSide : E_GasSide;
END_VAR
    CASE nSide OF
        1: eSide := E_GasSide.A_SIDE;
        2: eSide := E_GasSide.B_SIDE;
    ELSE
        sStatus := 'Invalid side';
        RETURN;
    END_CASE
    _MapSide := TRUE;
END_METHOD


METHOD PRIVATE _AbortCurrentSequence
    IF _ipActiveSequence <> 0 THEN
        _ipActiveSequence.Halt();   // drive owned hardware to safe state, freeze logic
        _ipActiveSequence.Reset();  // rewind so the sequence can run again
    END_IF
END_METHOD


METHOD PRIVATE _SetError
VAR_INPUT
    sMsg : STRING(255);
    eErrorId : E_ErrorId := E_ErrorId.ERR_GEN_INITIALIZATION;
END_VAR
    // Drive hardware to a safe state before abandoning the active sequence.
    IF _ipActiveSequence <> 0 THEN
        _ipActiveSequence.Halt();
    END_IF
    sStatus                       := sMsg;
    eActiveSequence               := E_Sequence.Error;
    _ipActiveSequence             := 0;
    bError                        := TRUE;
    bBusy                         := FALSE;
    bDone                         := FALSE;
    stLastError.eErrorId          := eErrorId;
    stLastError.eSeverity         := E_TcEventSeverity.Error;
    stLastError.sSource           := 'FB_SequenceOrchestrator';
    stLastError.sMessage          := sMsg;
    stLastError.tTimestamp        := F_GetCurrentDT();
    stLastError.tOriginTimestamp  := stLastError.tTimestamp;
END_METHOD


METHOD PRIVATE _CompleteSequence
    sStatus         := 'Completed';
    eActiveSequence := E_Sequence.None;
    _ipActiveSequence := 0;
    bBusy           := FALSE;
    bDone           := TRUE;
    bError          := FALSE;
    rProgress       := 100.0;
END_METHOD


METHOD PRIVATE _ResetState
    IF _ipActiveSequence <> 0 THEN
        _ipActiveSequence.Reset();
    END_IF
    eActiveSequence   := E_Sequence.None;
    _ipActiveSequence := 0;
    bBusy             := FALSE;
    bDone             := FALSE;
    bError            := FALSE;
    rProgress         := 0.0;
    MEMSET(ADR(stLastError), 0, SIZEOF(stLastError));
    sStatus           := 'Ready';
END_METHOD


METHOD PRIVATE _UpdateLastPurgedBubbler
    IF eActiveSequence = E_Sequence.PurgePrecursor OR eActiveSequence = E_Sequence.PurgeReactant THEN
        IF _nBubblerIndex > 0 THEN
            IF _eSide = E_GasSide.A_SIDE THEN
                nLastPurgedBubblerA := DINT_TO_INT(_nBubblerIndex);
            ELSE
                nLastPurgedBubblerB := DINT_TO_INT(_nBubblerIndex);
            END_IF
        END_IF
    END_IF
END_METHOD


METHOD PRIVATE _InitiateSystemStop : BOOL
VAR_INPUT
    sLabel : STRING(255);
END_VAR
    _AbortCurrentSequence();
    _ResetState();

    IF NOT _fbSystemStop.Init(ipProvider := _ipProvider) THEN
        _SetError(CONCAT(sLabel, ' init failed'));
        RETURN;
    END_IF
    _ipActiveSequence    := _fbSystemStop;
    _Start(E_Sequence.Stop, CONCAT(sLabel, '...'));
    _InitiateSystemStop  := TRUE;
END_METHOD
