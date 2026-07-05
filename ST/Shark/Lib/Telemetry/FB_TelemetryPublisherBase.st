// FB_TelemetryPublisherBase — Shared telemetry publication scaffolding.
//
// Holds the six subsystem references that both PRO and Shark publishers need,
// validates them in Init, and orchestrates the per-scan update by calling
// virtual hook methods. Derived publishers override the hooks that apply to
// their machine and own the machine-specific ST_MachineTelemetry struct.
//
// Polymorphic dispatch: when the derived FB body calls SUPER^(), the base
// body runs and each _Update* call dispatches to the most-derived override.
// Derived bodies should therefore consist of `SUPER^();` plus any
// machine-specific tail work that does not belong in a hook.
//
// Base has NO reference to App-side telemetry structs.
FUNCTION_BLOCK FB_TelemetryPublisherBase
VAR
    _refValveSub           : REFERENCE TO FB_ValveSubsystem;
    _refFlowSub            : REFERENCE TO FB_FlowSubsystem;
    _refPressureSub        : REFERENCE TO FB_PressureSubsystem;
    _refTempSub            : REFERENCE TO FB_TemperatureSubsystem;
    _refDISub              : REFERENCE TO FB_DigitalInputSubsystem;
    _refDOSub              : REFERENCE TO FB_DigitalOutputSubsystem;
    _bInitialized          : BOOL      := FALSE;
    _eLastSnapshotErrorId  : E_ErrorId := E_ErrorId.NO_ERROR;
END_VAR

    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    _UpdateValves();
    _UpdateMFC();
    _UpdatePressureSensors();
    _UpdatePressureControllers();
    _UpdateHeaters();
    _UpdateDigitalIO();
    _UpdateMachineSpecific();
    _UpdateSequenceStateTelemetry();

    // Check for new errors and fire snapshot capture hook when a new fault appears.
    _CheckAndTriggerSnapshot();

END_FUNCTION_BLOCK


// Initialises the six shared subsystem references.
// Derived publishers expose a public Init that calls this (renaming avoids
// signature-collision when machines need to pass additional refs).
METHOD PROTECTED InitSubsystems : BOOL
VAR_INPUT
    refValveSub    : REFERENCE TO FB_ValveSubsystem;
    refFlowSub     : REFERENCE TO FB_FlowSubsystem;
    refPressureSub : REFERENCE TO FB_PressureSubsystem;
    refTempSub     : REFERENCE TO FB_TemperatureSubsystem;
    refDISub       : REFERENCE TO FB_DigitalInputSubsystem;
    refDOSub       : REFERENCE TO FB_DigitalOutputSubsystem;
END_VAR
    IF NOT __ISVALIDREF(refValveSub)    THEN RETURN; END_IF
    IF NOT __ISVALIDREF(refFlowSub)     THEN RETURN; END_IF
    IF NOT __ISVALIDREF(refPressureSub) THEN RETURN; END_IF
    IF NOT __ISVALIDREF(refTempSub)     THEN RETURN; END_IF
    IF NOT __ISVALIDREF(refDISub)       THEN RETURN; END_IF
    IF NOT __ISVALIDREF(refDOSub)       THEN RETURN; END_IF

    _refValveSub    REF= refValveSub;
    _refFlowSub     REF= refFlowSub;
    _refPressureSub REF= refPressureSub;
    _refTempSub     REF= refTempSub;
    _refDISub       REF= refDISub;
    _refDOSub       REF= refDOSub;
    _bInitialized   := TRUE;
    InitSubsystems  := TRUE;
END_METHOD


METHOD PUBLIC IsInitialized : BOOL
    IsInitialized := _bInitialized;
END_METHOD


// ---------------------------------------------------------------------------
// Overridable hooks — base provides empty defaults.
// Derived publishers override only the hooks they need.
// ---------------------------------------------------------------------------

METHOD PROTECTED _UpdateValves
END_METHOD

METHOD PROTECTED _UpdateMFC
END_METHOD

METHOD PROTECTED _UpdatePressureSensors
END_METHOD

METHOD PROTECTED _UpdatePressureControllers
END_METHOD

METHOD PROTECTED _UpdateHeaters
END_METHOD

METHOD PROTECTED _UpdateDigitalIO
END_METHOD

METHOD PROTECTED _UpdateMachineSpecific
END_METHOD

METHOD PROTECTED _UpdateSequenceStateTelemetry
END_METHOD


// Shared per-ZONE heater update. Writes one ST_HeaterTelemetry slot per heat
// zone by aggregating directly from the zone's per-channel controllers:
//   TempAvg            — average measured value across all channels
//   SP                 — average commanded setpoint across all channels
//   Enable             — TRUE if ANY channel is enabled
//   TemperatureReached — TRUE only when enabled AND every channel is at setpoint
//   Status             — 1 if any channel has an active error, 0 otherwise
// Aggregating from channels (rather than the zone-level command state) ensures
// SP and Enable reflect the actual hardware state regardless of whether the
// command arrived via SetHeatZone or per-channel SetHeatChannel.
// Works with any array bounds (enum-bound or 0-based) via run-time bounds guards.
// Zone index i maps directly to the array slot i (1..GetZoneCount()).
METHOD PROTECTED _UpdateZoneHeaters
VAR_IN_OUT
    aHeaters : ARRAY[*] OF ST_HeaterTelemetry;
END_VAR
VAR
    i        : INT;
    c        : INT;
    nCount   : INT;
    nValid   : INT;
    ipCh     : I_Controller;
    stCh     : ST_ControllerStatus;
    rSumTemp : REAL;
    rSumSP   : REAL;
    bAnyEn   : BOOL;
    bAllAt   : BOOL;
    bAnyErr  : BOOL;
END_VAR
    IF NOT __ISVALIDREF(_refTempSub) THEN RETURN; END_IF

    FOR i := 1 TO _refTempSub.GetZoneCount() DO
        IF i < LOWER_BOUND(aHeaters, 1) OR i > UPPER_BOUND(aHeaters, 1) THEN
            CONTINUE;
        END_IF

        nCount   := _refTempSub.GetChannelCountByIndex(i);
        IF nCount < 1 THEN CONTINUE; END_IF

        rSumTemp := 0.0;
        rSumSP   := 0.0;
        bAnyEn   := FALSE;
        bAllAt   := TRUE;
        bAnyErr  := FALSE;
        nValid   := 0;

        FOR c := 1 TO nCount DO
            ipCh := _refTempSub.GetChannelControllerByZoneIndex(i, c);
            IF ipCh = 0 THEN CONTINUE; END_IF
            stCh     := ipCh.GetStatus();
            rSumTemp := rSumTemp + TO_REAL(stCh.rMeasuredValue);
            rSumSP   := rSumSP   + TO_REAL(stCh.rSetpoint);
            bAnyEn   := bAnyEn OR stCh.bEnabled;
            bAllAt   := bAllAt  AND stCh.bAtSetpoint;
            IF stCh.stError.eErrorId <> E_ErrorId.NO_ERROR THEN
                bAnyErr := TRUE;
            END_IF
            nValid := nValid + 1;
        END_FOR

        IF nValid > 0 THEN
            aHeaters[i].TempAvg            := rSumTemp / TO_REAL(nValid);
            aHeaters[i].SP                 := rSumSP   / TO_REAL(nValid);
            aHeaters[i].Enable             := bAnyEn;
            aHeaters[i].TemperatureReached := bAllAt AND bAnyEn;
            aHeaters[i].Status             := SEL(bAnyErr, INT#0, INT#1);
        END_IF
    END_FOR
END_METHOD


// ---------------------------------------------------------------------------
// Snapshot: error-time telemetry freeze
// ---------------------------------------------------------------------------

// Aggregates stFirstError from the four shared subsystems.
// Priority: pressure → temperature → flow → valves.
// Returns a zeroed ST_Error (eErrorId = NO_ERROR) if all subsystems are clean.
METHOD PRIVATE _AggregateSubsystemError : ST_Error
    IF __ISVALIDREF(_refPressureSub) AND _refPressureSub.stFirstError.eErrorId <> E_ErrorId.NO_ERROR THEN
        _AggregateSubsystemError := _refPressureSub.stFirstError;
        RETURN;
    END_IF
    IF __ISVALIDREF(_refTempSub) AND _refTempSub.stFirstError.eErrorId <> E_ErrorId.NO_ERROR THEN
        _AggregateSubsystemError := _refTempSub.stFirstError;
        RETURN;
    END_IF
    IF __ISVALIDREF(_refFlowSub) AND _refFlowSub.stFirstError.eErrorId <> E_ErrorId.NO_ERROR THEN
        _AggregateSubsystemError := _refFlowSub.stFirstError;
        RETURN;
    END_IF
    IF __ISVALIDREF(_refValveSub) AND _refValveSub.stFirstError.eErrorId <> E_ErrorId.NO_ERROR THEN
        _AggregateSubsystemError := _refValveSub.stFirstError;
        RETURN;
    END_IF
    _AggregateSubsystemError.eErrorId := E_ErrorId.NO_ERROR;
END_METHOD


// Called once per scan to detect new error edges and trigger the snapshot hook.
METHOD PRIVATE _CheckAndTriggerSnapshot
VAR
    stAggr : ST_Error;
END_VAR
    stAggr := _AggregateSubsystemError();

    IF stAggr.eErrorId = E_ErrorId.NO_ERROR THEN
        stAggr := _CollectMachineSpecificError();
    END_IF

    IF stAggr.eErrorId <> E_ErrorId.NO_ERROR
            AND stAggr.eErrorId <> _eLastSnapshotErrorId THEN
        // New or changed fault — capture the snapshot.
        _OnErrorCapture(stAggr);
        _eLastSnapshotErrorId := stAggr.eErrorId;
    ELSIF stAggr.eErrorId = E_ErrorId.NO_ERROR THEN
        // All errors cleared — re-arm so the next fault triggers a fresh capture.
        _eLastSnapshotErrorId := E_ErrorId.NO_ERROR;
    END_IF
END_METHOD


// Override in derived publishers to return machine-specific errors
// (motion, safety manager, etc.) when no shared-subsystem error is active.
// Base returns NO_ERROR by default.
METHOD PROTECTED _CollectMachineSpecificError : ST_Error
    _CollectMachineSpecificError.eErrorId := E_ErrorId.NO_ERROR;
END_METHOD


// Override in derived publishers to freeze stTelemetry into snapshot storage.
// Called at most once per unique error — not every scan.
METHOD PROTECTED _OnErrorCapture
VAR_INPUT
    stError : ST_Error;
END_VAR
    // Empty default — derived class overrides to freeze its stTelemetry.
END_METHOD


// Override in derived publishers to clear snapshot fields on reset.
METHOD PROTECTED _OnSnapshotReset
    // Empty default.
END_METHOD


// Resets the error-edge latch so the next fault triggers a fresh capture.
// Also calls _OnSnapshotReset() for derived-class cleanup (e.g. clearing bSnapshotValid).
// Call from a sequence or a command receiver after the operator has acknowledged the error.
METHOD PUBLIC ResetSnapshot
    _eLastSnapshotErrorId := E_ErrorId.NO_ERROR;
    _OnSnapshotReset();
END_METHOD
