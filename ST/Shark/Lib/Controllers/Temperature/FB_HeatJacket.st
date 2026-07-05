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
FUNCTION_BLOCK FB_HeatZone IMPLEMENTS I_Controller
VAR_OUTPUT
    stStatus : ST_HeatZoneStatus;     // Rich aggregated status (zone-specific)
END_VAR
VAR
    _stConfig      : ST_HeatZoneConfig;
    _aipChannels   : ARRAY[1..GVL_HeatZone.MAX_CHANNELS] OF I_Controller;
    _nChannelCount : INT;

    _bInitialized  : BOOL := FALSE;
    _bEnabledCmd   : BOOL := FALSE;     // Last commanded Enable() (before delta-trip override)
    _rSetpointCmd  : LREAL := 0.0;      // Last commanded SetSetpoint()
    _bDeltaTripped : BOOL := FALSE;     // Latched; cleared by Reset()

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
    aipChannels   : ARRAY[1..GVL_HeatZone.MAX_CHANNELS] OF I_Controller;
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
    _UpdateCtrlStatus();

    Execute := NOT stStatus.bAnyError AND NOT _bDeltaTripped;
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
    _fbError.Clear();

    Reset := bAllOk;
END_METHOD

METHOD PUBLIC GetStatus : ST_ControllerStatus
    GetStatus := _stCtrlStatus;
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
END_METHOD

METHOD PRIVATE _ApplyDeltaTrip
VAR
    i : INT;
END_VAR
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

METHOD PRIVATE _UpdateCtrlStatus
    // Build the polymorphic ST_ControllerStatus surface for I_Controller callers
    _stCtrlStatus.rMeasuredValue := stStatus.rAggregated;
    _stCtrlStatus.rSetpoint      := stStatus.rSetpoint;
    _stCtrlStatus.rOutput        := 0.0;   // Per-channel; not meaningful at zone level
    _stCtrlStatus.bEnabled       := _bEnabledCmd AND NOT _bDeltaTripped;
    _stCtrlStatus.bAtSetpoint    := stStatus.bAllAtSetpoint;

    IF _bDeltaTripped THEN
        _stCtrlStatus.stError := _fbError.GetError();
    ELSIF stStatus.bAnyError THEN
        _stCtrlStatus.stError := stStatus.stFirstError;
    ELSE
        _stCtrlStatus.stError.eErrorId := E_ErrorId.NO_ERROR;
    END_IF
END_METHOD

END_FUNCTION_BLOCK
