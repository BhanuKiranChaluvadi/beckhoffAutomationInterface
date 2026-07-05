(*
FB_SequenceTester — Shared test harness for sequence development.

Calls FB_SequenceOrchestrator methods directly via a REFERENCE.
Held inside a thin PROGRAM wrapper in each machine's App folder.

USAGE (thin PROGRAM wrapper pattern):
  PROGRAM PRG_SequenceTester
  VAR
      fbTester : FB_SequenceTester;
  END_VAR
      fbTester();
  // Configure: PRG_SequenceTester.Configure(refOrchestrator := PRG_Sequence.fbOrchestrator)

HOW TO TEST:
1. Set eCommand to desired command (e.g., E_Command.StartBubblerFlow)
2. Configure the corresponding parameter structure if needed
3. Set bStart := TRUE (pulse — auto-clears on rising edge)
4. Monitor sStatus, bBusy, bDone, bError, rProgress
*)
FUNCTION_BLOCK FB_SequenceTester
VAR
    // User interface — command triggers (pulse TRUE to execute)
    bStart   : BOOL;
    bStop    : BOOL;
    bReset   : BOOL;
    eCommand : E_Command := E_Command.StartBubblerFlow;

    // Parameter structures (pre-configured with realistic defaults)
    stBubblerFlowParams     : ST_CmdStartBubblerFlow := (
        side := 1,
        bubblerIndex := 1,
        flowRate := 15.0,
        hotManifoldOperatingPressure := 1.4,
        hotManifoldPressureTimeout := 600000,
        coldManifoldOperatingPressure := 1.4,
        coldManifoldPressureTimeout := 600000,
        MFCStabilizeTimeout := 60000,
        MFCFlowRateTolerance := 5.0,
        maxDepressurizingAttempts := 3
    );

    stStopBubblerFlowParams : ST_CmdStopBubblerFlow := (
        side := 1
    );

    stPurgeParams           : ST_CmdPurge := (
        mode := 1,
        side := 1,
        bubbler := 1,
        inertGasFlow := 300.0,
        purgeGasFlow := 25.0,
        purgeHighPressureLimit := 1.70,
        purgeLowPressureLimit := 0.75,
        hotManifoldOperatingPressure := 1.4,
        highPressureTimeout := 600000,
        lowPressureTimeout := 60000,
        coldManifoldPurgeCycles := 5,
        hotManifoldPurgeCycles := 5,
        purgeLinePurgeCycles := 5,
        umbilicalPurgeCycles := 5,
        umbilicalFlushTime := 60000,
        stabilisationDelay := 35000
    );

    stHeatingParams         : ST_CmdSetTemperatureGradient := (
        reactantCombiner := 60.0,
        precursorCombiner := 60.0,
        headTemp := 100.0,
        enableMode := 0
    );

    stChuckParams           : ST_CmdSetChuckZone := (
        holdSample := TRUE,
        size := 200,
        fPressureThreshold := 0.75,
        tPressureTimeout := 60000
    );

    stSpotDepositionParams  : ST_CmdSpotDeposition := (
        nDepositionCycles := 5,
        fPrecursorFlowRate := 20.0,
        fReactantFlowRate := 20.0,
        fInertMaxPurgeFlow := 250.0,
        fInertMinPurgeFlow := 100.0,
        nPrecursorBubblerIndex := 1,
        nReactantBubblerIndex := 1,
        fDistributorOperatingPressure := 2.0,
        fCombinerOperatingPressure := 1.3,
        fPressureTolerance := 5.0,
        tReactantExposureTime := 3000,
        tPrecursorExposureTime := 3000,
        tInertPurgeTime := 10000,
        tFinalPurgeTime := 30000
    );

    stLeakTestParams        : ST_CmdBubblerLeakTest := (
        side := 1,
        bubblerIndex := 1,
        leakThreshold := 0.5,
        testDuration := 300000,
        stabilizationTime := 60000,
        fillPressure := 2.5,
        testFlowRate := 20.0,
        pressureTimeout := 600000,
        pressureTolerance := 5.0,
        normalPressure := 1.4,
        depressurizeTarget := 0.5,
        normalizationFlow := 20.0
    );

    // Status output
    sStatus              : STRING(255) := 'Not configured';
    bBusy                : BOOL;
    bDone                : BOOL;
    bError               : BOOL;
    rProgress            : REAL;
    stStatus             : ST_SequenceStatus;
    stLastError          : ST_Error;
    sLastCommandResult   : STRING(255);
    bLastCommandAccepted : BOOL;

    // Debug counters
    nCycleCount   : UDINT := 0;
    nCommandCount : UDINT := 0;
    bTriggerFired : BOOL;
END_VAR
VAR
    _refOrchestrator : REFERENCE TO FB_SequenceOrchestrator;
    _bConfigured     : BOOL := FALSE;
    _rtStart         : R_TRIG;
    _rtStop          : R_TRIG;
    _rtReset         : R_TRIG;
    _sPrevStatusMsg  : STRING(255);
END_VAR

    nCycleCount   := nCycleCount + 1;
    bTriggerFired := FALSE;

    _rtStart(CLK := bStart);
    _rtStop(CLK  := bStop);
    _rtReset(CLK := bReset);

    IF NOT _bConfigured OR NOT __ISVALIDREF(_refOrchestrator) THEN
        sStatus := 'Waiting for orchestrator...';
        RETURN;
    END_IF

    IF _rtStop.Q THEN
        bStop         := FALSE;
        bTriggerFired := TRUE;
        bLastCommandAccepted := _refOrchestrator.SystemStop();
        sLastCommandResult := SEL(bLastCommandAccepted, 'SystemStop REJECTED', 'SystemStop accepted');
        nCommandCount := nCommandCount + 1;
    END_IF

    IF _rtReset.Q THEN
        bReset        := FALSE;
        bTriggerFired := TRUE;
        bLastCommandAccepted := _refOrchestrator.ErrorAcknowledge();
        sLastCommandResult := SEL(bLastCommandAccepted,
            'ErrorAcknowledge REJECTED (not in error state)',
            'ErrorAcknowledge accepted');
        nCommandCount := nCommandCount + 1;
    END_IF

    IF _rtStart.Q THEN
        bStart        := FALSE;
        bTriggerFired := TRUE;
        _ExecuteCommand();
        nCommandCount := nCommandCount + 1;
    END_IF

    stStatus    := _refOrchestrator.GetStatus();
    bBusy       := stStatus.bBusy;
    bDone       := stStatus.bDone;
    bError      := stStatus.bError;
    rProgress   := stStatus.rProgress;
    stLastError := stStatus.stLastError;

    IF bBusy THEN
        IF stStatus.sStatusMessage <> _sPrevStatusMsg THEN
            sStatus         := CONCAT('BUSY: ', stStatus.sStatusMessage);
            _sPrevStatusMsg := stStatus.sStatusMessage;
        END_IF
    ELSIF bError THEN
        IF stStatus.sStatusMessage <> _sPrevStatusMsg THEN
            sStatus         := CONCAT('ERROR: ', stStatus.sStatusMessage);
            _sPrevStatusMsg := stStatus.sStatusMessage;
        END_IF
    ELSIF bDone THEN
        IF stStatus.sStatusMessage <> _sPrevStatusMsg THEN
            sStatus         := CONCAT('DONE: ', stStatus.sStatusMessage);
            _sPrevStatusMsg := stStatus.sStatusMessage;
        END_IF
    ELSE
        sStatus         := SEL(sLastCommandResult = '', 'Ready', sLastCommandResult);
        _sPrevStatusMsg := '';
    END_IF

END_FUNCTION_BLOCK


// =============================================================================
// PUBLIC API
// =============================================================================

METHOD PUBLIC Configure : BOOL
VAR_INPUT
    refOrchestrator : REFERENCE TO FB_SequenceOrchestrator;
END_VAR
    IF NOT __ISVALIDREF(refOrchestrator) THEN RETURN; END_IF
    _refOrchestrator REF= refOrchestrator;
    _bConfigured     := TRUE;
    sStatus          := 'Ready';
    sLastCommandResult := '';
    Configure        := TRUE;
END_METHOD


// =============================================================================
// PRIVATE
// =============================================================================

METHOD PRIVATE _ExecuteCommand
VAR
    bSuccess      : BOOL;
    stHoldParams  : ST_ChuckVacuumHold;
    stReleaseParams : ST_ChuckVacuumRelease;
END_VAR
    bSuccess             := FALSE;
    bLastCommandAccepted := FALSE;

    CASE eCommand OF
        E_Command.StartBubblerFlow:
            bSuccess := _refOrchestrator.StartBubblerFlow(stBubblerFlowParams);
            sLastCommandResult := SEL(bSuccess,
                'StartBubblerFlow REJECTED (sequence busy or in error)',
                'StartBubblerFlow accepted');

        E_Command.StopBubblerFlow:
            bSuccess := _refOrchestrator.StopBubblerFlow(stStopBubblerFlowParams);
            sLastCommandResult := SEL(bSuccess, 'StopBubblerFlow REJECTED', 'StopBubblerFlow accepted');

        E_Command.StartPurge:
            bSuccess := _refOrchestrator.StartPurge(stPurgeParams);
            sLastCommandResult := SEL(bSuccess, 'StartPurge REJECTED', 'StartPurge accepted');

        E_Command.SetTemperatureGradients:
            bSuccess := _refOrchestrator.StartHeating(stHeatingParams);
            sLastCommandResult := SEL(bSuccess, 'StartHeating REJECTED', 'StartHeating accepted');

        E_Command.HoldReleaseSample:
            IF stChuckParams.holdSample THEN
                stHoldParams.iSelectZone            := _WaferSizeToZoneCount(stChuckParams.size);
                stHoldParams.fLockPressureThreshold := stChuckParams.fPressureThreshold;
                stHoldParams.tPressureTimeout       := DINT_TO_TIME(stChuckParams.tPressureTimeout);
                bSuccess := _refOrchestrator.StartChuckVacuumHold(stHoldParams);
                sLastCommandResult := SEL(bSuccess, 'ChuckVacuumHold REJECTED', 'ChuckVacuumHold accepted');
            ELSE
                stReleaseParams.fReleasePressureThreshold := stChuckParams.fPressureThreshold;
                stReleaseParams.tPressureTimeout          := DINT_TO_TIME(stChuckParams.tPressureTimeout);
                bSuccess := _refOrchestrator.StartChuckVacuumRelease(stReleaseParams);
                sLastCommandResult := SEL(bSuccess, 'ChuckVacuumRelease REJECTED', 'ChuckVacuumRelease accepted');
            END_IF

        E_Command.CancelProcess:
            bSuccess := _refOrchestrator.SystemStop();
            sLastCommandResult := SEL(bSuccess, 'SystemStop REJECTED', 'SystemStop accepted');

        E_Command.ErrorAcknowledge:
            bSuccess := _refOrchestrator.ErrorAcknowledge();
            sLastCommandResult := SEL(bSuccess,
                'ErrorAcknowledge REJECTED (not in error state)',
                'ErrorAcknowledge accepted');

        E_Command.StartSpotDeposition:
            bSuccess := _refOrchestrator.StartSpotDeposition(stSpotDepositionParams);
            sLastCommandResult := SEL(bSuccess, 'SpotDeposition REJECTED', 'SpotDeposition accepted');

        E_Command.StartBubblerLeakTest:
            bSuccess := _refOrchestrator.StartBubblerLeakTest(stLeakTestParams);
            sLastCommandResult := SEL(bSuccess, 'BubblerLeakTest REJECTED', 'BubblerLeakTest accepted');

        E_Command.None:
            sLastCommandResult := 'No command selected (eCommand = None)';

    ELSE
        sLastCommandResult := 'Unknown command';
    END_CASE

    bLastCommandAccepted := bSuccess;
END_METHOD


METHOD PRIVATE _WaferSizeToZoneCount : INT
VAR_INPUT
    nWaferSize : INT;
END_VAR
    IF nWaferSize <= 150 THEN
        _WaferSizeToZoneCount := 1;
    ELSIF nWaferSize <= 200 THEN
        _WaferSizeToZoneCount := 2;
    ELSE
        _WaferSizeToZoneCount := 3;
    END_IF
END_METHOD
