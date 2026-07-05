(*
FB_HeatZone — composite heat zone made of N independent channel controllers.

Each channel owns its own RTD + SSR + PID (e.g. an FB_BeckhoffTempController).
The zone itself does NOT close a control loop. It coordinates:
  * one setpoint pushed to all channels
  * aggregated readback (avg/max/min/master) for the HMI
  * delta safety (auto-disable if channels drift apart)
  * rolled-up health (any channel fault → zone fault)

Implements I_Controller, so a zone and a single channel controller are
interchangeable from the subsystem's point of view. A 1-channel zone
is a valid, minimal-overhead wrapper around one channel.
*)
FUNCTION_BLOCK FB_HeatZone IMPLEMENTS I_Tunable
VAR_OUTPUT
    stStatus : ST_HeatZoneStatus;     // Rich aggregated status (zone-specific)
END_VAR
VAR
    _stConfig      : ST_HeatZoneConfig;
    _aipChannels   : ARRAY[1..GVL_HeatZone.MAX_CHANNELS] OF I_Tunable;
    _nChannelCount : INT;

    _bInitialized  : BOOL := FALSE;
    _bEnabledCmd   : BOOL := FALSE;     // Last commanded Enable() (before delta-trip override)
    _rSetpointCmd  : LREAL := 0.0;      // Last commanded SetSetpoint()
    _bDeltaTripped : BOOL := FALSE;     // Latched; cleared by Reset()
    _bChannelSafetyTripped : BOOL := FALSE;  // Latched zone trip from a channel safety latch (any cause); cleared by Reset()
    _bAnyChSafetyTrip      : BOOL := FALSE;  // Instantaneous: any channel reported a safety trip this scan
    _nFirstChSafetyTripIdx : INT := 0;       // Instantaneous: first channel with a safety trip this scan
    _stFirstChSafetyError  : ST_Error;       // Actual fault of the first safety-tripped channel (propagated to the zone)
    _bPowerAvailable : BOOL := TRUE;    // External main-power interlock (e.g. MCR). FALSE = power cut:
                                        // force all channels disabled and refuse Enable(TRUE).
    _eZoneMode     : E_CTRL_MODE := E_CTRL_MODE.eCTRL_MODE_ACTIVE;  // Last mode cascaded to channels

    _fbError       : FB_ErrorHandler;

    // Cached I_Controller status (rebuilt every Execute)
    _stCtrlStatus  : ST_ControllerStatus;
END_VAR

// ============================================================================
// PUBLIC API
// ============================================================================

METHOD PUBLIC Init : BOOL
VAR_INPUT
    stConfig      : ST_HeatZoneConfig;
    aipChannels   : ARRAY[1..GVL_HeatZone.MAX_CHANNELS] OF I_Tunable;
    nChannelCount : INT;
END_VAR
VAR
    i : INT;
END_VAR

    _fbError.Init(stConfig.sZoneID);

    // Validate channel count
    IF nChannelCount < 1 OR nChannelCount > GVL_HeatZone.MAX_CHANNELS THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Zone channel count out of range');
        RETURN;
    END_IF

    // Validate every channel pointer is non-null
    FOR i := 1 TO nChannelCount DO
        IF aipChannels[i] = 0 THEN
            _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Zone channel pointer is null');
            RETURN;
        END_IF
    END_FOR

    // Validate setpoint clamp
    IF stConfig.rMinSetpoint >= stConfig.rMaxSetpoint THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Zone min setpoint must be less than max');
        RETURN;
    END_IF

    // Copy in
    _stConfig := stConfig;
    _nChannelCount := nChannelCount;
    FOR i := 1 TO nChannelCount DO
        _aipChannels[i] := aipChannels[i];
    END_FOR

    stStatus.sZoneID := stConfig.sZoneID;
    stStatus.nChannelCount := nChannelCount;

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
    IF NOT _bInitialized THEN RETURN; END_IF

    _ReadChannels();
    _ApplyDeltaTrip();
    _ApplyChannelSafetyTrip();
    _ApplyExternalPowerCut();
    _UpdateCtrlStatus();

    Execute := NOT stStatus.bAnyError AND NOT _bDeltaTripped AND NOT _bChannelSafetyTripped
               AND _bPowerAvailable;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD PUBLIC Enable : BOOL
VAR_INPUT
    bEnable : BOOL;
END_VAR
VAR
    i : INT;
    ipChannel : I_Controller;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    // Refuse to enable while delta-tripped; disable always allowed
    IF bEnable AND _bDeltaTripped THEN
        Enable := FALSE;
        RETURN;
    END_IF

    // Refuse to enable while external main power is cut (e.g. MCR trip);
    // disable is always allowed.
    IF bEnable AND NOT _bPowerAvailable THEN
        Enable := FALSE;
        RETURN;
    END_IF

    _bEnabledCmd := bEnable;

    FOR i := 1 TO _nChannelCount DO
        ipChannel := _aipChannels[i];
        ipChannel.Enable(bEnable);
    END_FOR

    Enable := TRUE;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD PUBLIC SetSetpoint : BOOL
VAR_INPUT
    rDesiredSetpoint : LREAL;
END_VAR
VAR
    i : INT;
    ipChannel : I_Controller;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    // Zone-level clamp; channel-level clamp still applies inside each channel
    IF rDesiredSetpoint < _stConfig.rMinSetpoint OR rDesiredSetpoint > _stConfig.rMaxSetpoint THEN
        SetSetpoint := FALSE;
        RETURN;
    END_IF

    _rSetpointCmd := rDesiredSetpoint;

    FOR i := 1 TO _nChannelCount DO
        ipChannel := _aipChannels[i];
        ipChannel.SetSetpoint(rDesiredSetpoint);
    END_FOR

    SetSetpoint := TRUE;
END_METHOD

{attribute 'estimated-stack-usage' := '500'}
METHOD PUBLIC Reset : BOOL
VAR
    i : INT;
    bAllOk : BOOL := TRUE;
    ipChannel : I_Controller;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    FOR i := 1 TO _nChannelCount DO
        ipChannel := _aipChannels[i];
        IF NOT ipChannel.Reset() THEN
            bAllOk := FALSE;
        END_IF
    END_FOR

    _bDeltaTripped := FALSE;
    stStatus.bDeltaTripped := FALSE;

    // Clear the channel-safety latch ONLY when every channel actually reset
    // (i.e. each safety TC has cooled below its reset threshold). Clearing it
    // unconditionally would drop bSafetyTripped to FALSE for one cycle before
    // _ApplyChannelSafetyTrip re-latches it — unsafe for a flag external logic
    // may sample. While still tripped we also keep the active error so the HMI
    // continues to show the cause.
    IF bAllOk THEN
        _bChannelSafetyTripped := FALSE;
        stStatus.bSafetyTripped := FALSE;
        stStatus.nSafetyTrippedIdx := 0;
        _fbError.Clear();
    END_IF

    Reset := bAllOk;
END_METHOD

(* External main-power interlock (e.g. an MCR that physically cuts heater mains).
   When bAvailable = FALSE the zone forces every channel disabled each Execute and
   refuses Enable(TRUE), so de-powered channels stop accumulating windup / "heating
   but temperature dropping" errors. This is a live gate, NOT a latch: when power
   returns the zone stays disabled until an operator explicitly re-enables. *)
METHOD PUBLIC SetPowerAvailable : BOOL
VAR_INPUT
    bAvailable : BOOL;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    _bPowerAvailable := bAvailable;
    SetPowerAvailable := TRUE;
END_METHOD

METHOD PUBLIC GetStatus : ST_ControllerStatus
    GetStatus := _stCtrlStatus;
END_METHOD

// ----------------------------------------------------------------------------
// I_Tunable — cascade the control mode to every channel so an entire zone can
// be put into autotune (or back to ACTIVE) with a single call.
// ----------------------------------------------------------------------------

// FB_HeatZone implements I_Tunable and forwards SetControlMode to its channels,
// which are themselves I_Tunable. The compiler cannot prove a channel is never a
// nested zone, so it treats zone -> I_Tunable.SetControlMode -> zone as potential
// recursion and cannot complete stack analysis (C0298). The recursion is bounded
// by zone-nesting depth (a zone owns leaf channels, so depth is normally 1);
// declare a safe per-call stack estimate so the stack calculation completes.
{attribute 'estimated-stack-usage' := '1000'}
METHOD PUBLIC SetControlMode : BOOL
VAR_INPUT
    eMode : E_CTRL_MODE;
END_VAR
VAR
    i : INT;
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF

    _eZoneMode := eMode;
    FOR i := 1 TO _nChannelCount DO
        _aipChannels[i].SetControlMode(eMode);
    END_FOR

    SetControlMode := TRUE;
END_METHOD

METHOD PUBLIC GetControlMode : E_CTRL_MODE
    GetControlMode := _eZoneMode;
END_METHOD

// ----------------------------------------------------------------------------
// Diagnostics accessors (zone-specific; outside I_Controller surface)
// ----------------------------------------------------------------------------

METHOD PUBLIC GetChannel : I_Controller
VAR_INPUT
    nIndex : INT;   // 1..nChannelCount
END_VAR
    IF nIndex >= 1 AND nIndex <= _nChannelCount THEN
        GetChannel := _aipChannels[nIndex];
    ELSE
        GetChannel := 0;
    END_IF
END_METHOD

METHOD PUBLIC GetChannelCount : INT
    GetChannelCount := _nChannelCount;
END_METHOD

// ============================================================================
// PRIVATE
// ============================================================================

METHOD PRIVATE _ReadChannels
VAR
    i              : INT;
    stCh           : ST_ControllerStatus;
    rSum           : LREAL := 0.0;
    rTemp          : LREAL;
    bAllAt         : BOOL := TRUE;
    bAnyErr        : BOOL := FALSE;
    nFirstErr      : INT := 0;
    stFirstErr     : ST_Error;
    bAnySafetyTrip : BOOL := FALSE;
    nFirstSafetyTrip : INT := 0;
    stFirstSafetyErr : ST_Error;
END_VAR
    // Seed min/max with the first channel so SEL works for all N (incl. N=1)
    stCh := _aipChannels[1].GetStatus();
    stStatus.rMin := stCh.rMeasuredValue;
    stStatus.rMax := stCh.rMeasuredValue;
    stStatus.nColdestIdx := 1;
    stStatus.nHottestIdx := 1;

    FOR i := 1 TO _nChannelCount DO
        stCh := _aipChannels[i].GetStatus();
        rTemp := stCh.rMeasuredValue;
        rSum := rSum + rTemp;

        IF rTemp < stStatus.rMin THEN
            stStatus.rMin := rTemp;
            stStatus.nColdestIdx := i;
        END_IF
        IF rTemp > stStatus.rMax THEN
            stStatus.rMax := rTemp;
            stStatus.nHottestIdx := i;
        END_IF

        IF NOT stCh.bAtSetpoint THEN
            bAllAt := FALSE;
        END_IF

        IF stCh.stError.eErrorId <> E_ErrorId.NO_ERROR THEN
            IF NOT bAnyErr THEN
                nFirstErr := i;
                stFirstErr := stCh.stError;
            END_IF
            bAnyErr := TRUE;
        END_IF

        IF stCh.bSafetyTripped THEN
            IF NOT bAnySafetyTrip THEN
                nFirstSafetyTrip := i;
                stFirstSafetyErr := stCh.stError;
            END_IF
            bAnySafetyTrip := TRUE;
        END_IF
    END_FOR

    // Aggregation
    CASE _stConfig.eAggregation OF
        E_TempAggregation.AVERAGE:
            stStatus.rAggregated := rSum / TO_LREAL(_nChannelCount);
        E_TempAggregation.MAXIMUM:
            stStatus.rAggregated := stStatus.rMax;
        E_TempAggregation.MINIMUM:
            stStatus.rAggregated := stStatus.rMin;
        E_TempAggregation.MASTER:
            stCh := _aipChannels[1].GetStatus();
            stStatus.rAggregated := stCh.rMeasuredValue;
    ELSE
        stStatus.rAggregated := rSum / TO_LREAL(_nChannelCount);
    END_CASE

    stStatus.rDelta := stStatus.rMax - stStatus.rMin;
    stStatus.rSetpoint := _rSetpointCmd;
    stStatus.bAllAtSetpoint := bAllAt AND _bEnabledCmd;
    stStatus.bAnyError := bAnyErr;
    stStatus.nFirstErrorIdx := nFirstErr;
    stStatus.stFirstError := stFirstErr;
    stStatus.bDeltaWarning := (_stConfig.rMaxDeltaWarn > 0.0)
                             AND (stStatus.rDelta > _stConfig.rMaxDeltaWarn);

    // Hand the instantaneous channel safety detection to _ApplyChannelSafetyTrip
    _bAnyChSafetyTrip      := bAnySafetyTrip;
    _nFirstChSafetyTripIdx := nFirstSafetyTrip;
    _stFirstChSafetyError  := stFirstSafetyErr;
END_METHOD

METHOD PRIVATE _ApplyDeltaTrip
VAR
    i : INT;
END_VAR
    // Skip delta-trip during autotune: each channel runs its own independent PID
    // self-tuning and they intentionally diverge. Per-channel over-temp / watchdog
    // safety stays active inside each FB_BeckhoffTempController.
    IF _eZoneMode = E_CTRL_MODE.eCTRL_MODE_TUNE THEN RETURN; END_IF

    // Already tripped — stays latched until Reset()
    IF _bDeltaTripped THEN RETURN; END_IF

    // Trip disabled (rMaxDeltaTrip = 0) or zone not enabled — skip
    IF _stConfig.rMaxDeltaTrip <= 0.0 OR NOT _bEnabledCmd THEN RETURN; END_IF

    IF stStatus.rDelta > _stConfig.rMaxDeltaTrip THEN
        _bDeltaTripped := TRUE;
        stStatus.bDeltaTripped := TRUE;
        _fbError.RaiseWithValues(
            E_ErrorId.ERR_CTRL_LIMIT_EXCEEDED,
            'ZoneDelta',
            stStatus.rDelta,
            _stConfig.rMaxDeltaTrip,
            'C');

        // Disable every channel (override commanded enable)
        FOR i := 1 TO _nChannelCount DO
            _aipChannels[i].Enable(FALSE);
        END_FOR
    END_IF
END_METHOD

(* Propagate a per-channel safety latch to the whole zone. A channel latches its
   safety state for ANY of: heater watchdog, sensor fault/deviation, control-RTD
   over-temperature, or — where fitted — an independent safety-thermocouple
   over-temperature. Unlike _ApplyDeltaTrip this is NOT suspended during autotune:
   a genuine safety latch is always unsafe, whereas channel divergence is expected
   only for the delta check. The offending channel has already cut its own heater
   (authoritative); this latches the zone so every sibling channel is disabled too
   and an operator Reset() is required. The zone reports the offending channel's
   ACTUAL fault, never a fabricated cause. *)
METHOD PRIVATE _ApplyChannelSafetyTrip
VAR
    i : INT;
END_VAR
    // Feature disabled for this zone — leave channel-local cutoff to act alone
    IF NOT _stConfig.bChannelTripStopsZone THEN RETURN; END_IF

    // Already latched — stays tripped until Reset()
    IF _bChannelSafetyTripped THEN RETURN; END_IF

    // No channel safety trip detected this scan
    IF NOT _bAnyChSafetyTrip THEN RETURN; END_IF

    _bChannelSafetyTripped := TRUE;
    stStatus.bSafetyTripped := TRUE;
    stStatus.nSafetyTrippedIdx := _nFirstChSafetyTripIdx;

    // Propagate the offending channel's ACTUAL fault (sensor deviation, RTD
    // over-temp, heater watchdog, or — where fitted — a safety-TC over-temp)
    // instead of fabricating a fixed "TC over-temperature" reason. The child
    // already logged the root cause; RaiseFromChild carries it up without
    // re-logging, so the zone reports the true reason it disabled.
    _fbError.RaiseFromChild(_stFirstChSafetyError);

    // Disable every channel (override commanded enable)
    FOR i := 1 TO _nChannelCount DO
        _aipChannels[i].Enable(FALSE);
    END_FOR
END_METHOD

(* Force every channel disabled while external main power is cut (e.g. MCR trip).
   Runs every scan (live gate, no latch): the external TwinSAFE project owns the
   trip/reset; we only mirror it so de-powered channels stop trying to heat and
   stop accumulating windup / watchdog errors. Commanded enable is preserved
   internally only insofar as Enable(TRUE) is refused while power is unavailable. *)
METHOD PRIVATE _ApplyExternalPowerCut
VAR
    i : INT;
END_VAR
    IF _bPowerAvailable THEN RETURN; END_IF

    _bEnabledCmd := FALSE;
    FOR i := 1 TO _nChannelCount DO
        _aipChannels[i].Enable(FALSE);
    END_FOR
END_METHOD

METHOD PRIVATE _UpdateCtrlStatus
    // Build the polymorphic ST_ControllerStatus surface for I_Controller callers
    _stCtrlStatus.rMeasuredValue := stStatus.rAggregated;
    _stCtrlStatus.rSetpoint      := stStatus.rSetpoint;
    _stCtrlStatus.rOutput        := 0.0;   // Per-channel; not meaningful at zone level
    _stCtrlStatus.bEnabled       := _bEnabledCmd AND NOT _bDeltaTripped AND NOT _bChannelSafetyTripped
                                    AND _bPowerAvailable;
    _stCtrlStatus.bAtSetpoint    := stStatus.bAllAtSetpoint;
    _stCtrlStatus.bSafetyTripped := _bChannelSafetyTripped;
    stStatus.bExternalPowerCut   := NOT _bPowerAvailable;

    IF _bChannelSafetyTripped OR _bDeltaTripped THEN
        _stCtrlStatus.stError := _fbError.GetError();
    ELSIF stStatus.bAnyError THEN
        _stCtrlStatus.stError := stStatus.stFirstError;
    ELSE
        _stCtrlStatus.stError.eErrorId := E_ErrorId.NO_ERROR;
    END_IF
END_METHOD

END_FUNCTION_BLOCK
