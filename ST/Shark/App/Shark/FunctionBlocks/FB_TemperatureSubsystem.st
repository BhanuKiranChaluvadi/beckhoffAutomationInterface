(*
Temperature Subsystem - orchestrates zone controllers built from channel channels.

Hardware is decoupled: PRG_TEMPERATURE_SENSOR owns sensors, PRG_HEAT_ZONE
owns heaters. Both pass flat per-channel interface arrays via Init. The
subsystem groups channels into zones (FB_HeatZone) according to
ZONE_CONFIG[i].nChannelCount.

Channel-to-zone layout (per E_HeatZone order, accumulated nChannelCount):
  HETR_HEAD  : 1                    (shared head)
  HETR_A_STR : 2     HETR_A_CMB : 3   HETR_A_B1 : 4   HETR_A_B2 : 5   HETR_A_B3 : 6
  HETR_B_STR : 7     HETR_B_CMB : 8   HETR_B_B1 : 9   HETR_B_B2 : 10  HETR_B_B3 : 11
  HETR_EXH   : 12..13  (inner + outer)
  HETR_CHK   : 14..15  (inner + outer; tight delta-trip replaces FB_BeckhoffDualZoneCoord)

Public API:
  Heat-zone level (E_HeatZone):
    GetController(zone)          : I_Controller           - the FB_HeatZone
    GetZoneStatus(zone)        : ST_HeatZoneStatus    - rich aggregated status
    GetTemperature(zone)         : I_Sensor               - first channel's sensor (legacy)
    ResetZone(zone)              : BOOL                   - reset zone (cascades to channels)
    ResetAll                     : BOOL                   - reset all zones
  Channel level (for tuning + diagnostics):
    GetChannel(zone, idx)        : I_Controller           - one channel controller
    GetChannelGlobalIndex(z, i)  : INT                    - flat 1..CHANNEL_MAX
    GetBeckhoffChannel(z, i)     : REFERENCE TO FB_BeckhoffTempController
  Monitor points (E_TemperatureMonitorZone):
    GetMonitorTemperature(zone)  : I_Sensor
*)
FUNCTION_BLOCK FB_TemperatureSubsystem IMPLEMENTS I_TemperatureSubsystem
VAR_OUTPUT
    bInitialized   : BOOL := FALSE;
    bAnyError      : BOOL := FALSE;
    bAllAtSetpoint : BOOL := FALSE;
    stFirstError   : ST_Error;
END_VAR
VAR
    // --- Channels (one per RTD+SSR channel) ---
    _afbChannels : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF FB_BeckhoffTempController;

    // --- Zones (one per E_HeatZone) ---
    _afbZones : ARRAY[1..GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX] OF FB_HeatZone;

    // Per-zone starting offset into _afbChannels (computed at init).
    // Zone i owns _afbChannels[_anZoneChannelOffset[i]+1 .. _anZoneChannelOffset[i]+nChannelCount[i]].
    _anZoneChannelOffset : ARRAY[1..GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX] OF INT;

    // Polymorphic zone array (consumed by external API).
    _ipControllers : ARRAY[1..GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX] OF I_Controller;

    // --- Injected channel interfaces ---
    _aipChannelSensors : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF I_Sensor;
    _aipChannelHeaters : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF I_DigitalOutput;
    _ipMonitorSensors  : ARRAY[1..GVL_TemperatureZones.MONITOR_ZONE_COUNT] OF I_Sensor;
END_VAR

// ============================================================================
// PUBLIC API
// ============================================================================

METHOD PUBLIC Init : BOOL
VAR_INPUT
    aipChannelSensors : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF I_Sensor;
    aipMonitorSensors : ARRAY[1..GVL_TemperatureZones.MONITOR_ZONE_COUNT] OF I_Sensor;
    aipChannelHeaters : ARRAY[1..GVL_TemperatureZones.CHANNEL_MAX] OF I_DigitalOutput;
END_VAR
VAR
    i : INT;
END_VAR
    IF bInitialized THEN Init := TRUE; RETURN; END_IF

    FOR i := 1 TO GVL_TemperatureZones.CHANNEL_MAX DO
        _aipChannelSensors[i] := aipChannelSensors[i];
        _aipChannelHeaters[i] := aipChannelHeaters[i];
    END_FOR
    FOR i := 1 TO GVL_TemperatureZones.MONITOR_ZONE_COUNT DO
        _ipMonitorSensors[i] := aipMonitorSensors[i];
    END_FOR

    _ComputeZoneChannelOffsets();
    _InitChannels();
    _InitZones();

    bInitialized := TRUE;
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
    IF NOT bInitialized THEN RETURN; END_IF

    _ExecuteChannels();
    _ExecuteZones();
    _UpdateStatus();

    Execute := TRUE;
END_METHOD

METHOD PUBLIC GetController : I_Controller
VAR_INPUT
    eZone : E_HeatZone;
END_VAR
    IF eZone >= 1 AND eZone <= GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN
        GetController := _ipControllers[eZone];
    END_IF
END_METHOD

METHOD PUBLIC GetZoneStatus : ST_HeatZoneStatus
VAR_INPUT
    eZone : E_HeatZone;
END_VAR
    IF eZone >= 1 AND eZone <= GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN
        GetZoneStatus := _afbZones[eZone].stStatus;
    END_IF
END_METHOD

(* Returns the FIRST channel's I_Sensor for the zone. Legacy callers that want
   a raw sensor handle. For the zone-aggregated reading prefer:
       GetController(eZone).GetStatus().rMeasuredValue *)
METHOD PUBLIC GetTemperature : I_Sensor
VAR_INPUT
    eZone : E_HeatZone;
END_VAR
VAR
    nGlobalIdx : INT;
END_VAR
    IF eZone >= 1 AND eZone <= GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN
        nGlobalIdx := _anZoneChannelOffset[eZone] + 1;
        GetTemperature := _aipChannelSensors[nGlobalIdx];
    END_IF
END_METHOD

METHOD PUBLIC GetMonitorTemperature : I_Sensor
VAR_INPUT
    eZone : E_TemperatureMonitorZone;
END_VAR
    IF eZone >= 1 AND eZone <= GVL_TemperatureZones.MONITOR_ZONE_COUNT THEN
        GetMonitorTemperature := _ipMonitorSensors[eZone];
    END_IF
END_METHOD

METHOD PUBLIC GetChannel : I_Controller
VAR_INPUT
    eZone        : E_HeatZone;
    nChannelIdx  : INT;     // 1..nChannelCount of the zone
END_VAR
VAR
    nGlobalIdx : INT;
END_VAR
    nGlobalIdx := GetChannelGlobalIndex(eZone, nChannelIdx);
    IF nGlobalIdx >= 1 AND nGlobalIdx <= GVL_TemperatureZones.CHANNEL_MAX THEN
        GetChannel := _afbChannels[nGlobalIdx];
    END_IF
END_METHOD

METHOD PUBLIC GetBeckhoffChannel : REFERENCE TO FB_BeckhoffTempController
VAR_INPUT
    eZone        : E_HeatZone;
    nChannelIdx  : INT;     // 1..nChannelCount of the zone
END_VAR
VAR
    nGlobalIdx : INT;
END_VAR
    nGlobalIdx := GetChannelGlobalIndex(eZone, nChannelIdx);
    IF nGlobalIdx >= 1 AND nGlobalIdx <= GVL_TemperatureZones.CHANNEL_MAX THEN
        GetBeckhoffChannel REF= _afbChannels[nGlobalIdx];
    END_IF
END_METHOD

(* Resolve a tuning target as I_Tunable. With bWholeZone = TRUE the whole zone
   is returned (autotunes every channel at once); otherwise the single channel
   at nChannelIdx is returned. Used by the tuning panel via PRG_HEAT_ZONE. *)
METHOD PUBLIC GetTunable : I_Tunable
VAR_INPUT
    eZone       : E_HeatZone;
    nChannelIdx : INT;     // 1..nChannelCount of the zone (single-channel mode)
    bWholeZone  : BOOL;    // TRUE = tune all channels in the zone simultaneously
END_VAR
VAR
    nGlobalIdx : INT;
END_VAR
    IF eZone < 1 OR eZone > GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN RETURN; END_IF

    IF bWholeZone THEN
        GetTunable := _afbZones[eZone];
    ELSE
        nGlobalIdx := GetChannelGlobalIndex(eZone, nChannelIdx);
        IF nGlobalIdx >= 1 AND nGlobalIdx <= GVL_TemperatureZones.CHANNEL_MAX THEN
            GetTunable := _afbChannels[nGlobalIdx];
        END_IF
    END_IF
END_METHOD

(* Translates (zone, elementIdx) into the flat 1..CHANNEL_MAX index.
   Returns 0 on out-of-range input. *)
METHOD PUBLIC GetChannelGlobalIndex : INT
VAR_INPUT
    eZone        : E_HeatZone;
    nChannelIdx  : INT;
END_VAR
VAR
    nCount : INT;
END_VAR
    IF eZone < 1 OR eZone > GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN RETURN; END_IF
    nCount := GVL_TemperatureZoneConfig.ZONE_CONFIG[eZone].nChannelCount;
    IF nChannelIdx < 1 OR nChannelIdx > nCount THEN RETURN; END_IF
    GetChannelGlobalIndex := _anZoneChannelOffset[eZone] + nChannelIdx;
END_METHOD

METHOD PUBLIC ResetZone : BOOL
VAR_INPUT
    eZone : E_HeatZone;
END_VAR
    IF NOT bInitialized THEN RETURN; END_IF
    IF eZone < 1 OR eZone > GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN RETURN; END_IF
    ResetZone := _afbZones[eZone].Reset();
END_METHOD

METHOD PUBLIC ResetAll : BOOL
VAR
    i      : INT;
    bAllOk : BOOL := TRUE;
END_VAR
    IF NOT bInitialized THEN RETURN; END_IF

    FOR i := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        IF _ipControllers[i] <> 0 THEN
            IF NOT _afbZones[i].Reset() THEN
                bAllOk := FALSE;
            END_IF
        END_IF
    END_FOR

    ResetAll := bAllOk;
END_METHOD

// ============================================================================
// INITIALIZATION (PRIVATE)
// ============================================================================

METHOD PRIVATE _ComputeZoneChannelOffsets
VAR
    i       : INT;
    nOffset : INT := 0;
END_VAR
    FOR i := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        _anZoneChannelOffset[i] := nOffset;
        nOffset := nOffset + GVL_TemperatureZoneConfig.ZONE_CONFIG[i].nChannelCount;
    END_FOR
END_METHOD

METHOD PRIVATE _InitChannels
VAR
    iZone      : INT;
    e          : INT;
    nGlobalIdx : INT;
    nCount     : INT;
    stConfig   : ST_BeckhoffTempZoneConfig;
END_VAR
    FOR iZone := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        IF NOT GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].bPopulated THEN
            CONTINUE;
        END_IF

        nCount := GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].nChannelCount;

        FOR e := 1 TO nCount DO
            nGlobalIdx := _anZoneChannelOffset[iZone] + e;
            IF nGlobalIdx < 1 OR nGlobalIdx > GVL_TemperatureZones.CHANNEL_MAX THEN CONTINUE; END_IF

            // Single config boundary: the factory resolves every per-channel value
            // (PID gains, single-authority enable policy, identity) so this FB never
            // reaches into the GVL config tables itself.
            stConfig := F_GetBeckhoffTempChannelConfig(
                eZone          := iZone,
                nGlobalIdx     := nGlobalIdx,
                nChannelInZone := e);
            IF stConfig.sZoneID = 'INVALID' THEN CONTINUE; END_IF

            IF _aipChannelSensors[nGlobalIdx] <> 0 AND _aipChannelHeaters[nGlobalIdx] <> 0 THEN
                _afbChannels[nGlobalIdx].Init(
                    ipTemperatureSensor := _aipChannelSensors[nGlobalIdx],
                    ipHeaterOutput      := _aipChannelHeaters[nGlobalIdx],
                    stConfiguration     := stConfig
                );
            END_IF
        END_FOR
    END_FOR
END_METHOD

METHOD PRIVATE _InitZones
VAR
    iZone       : INT;
    e           : INT;
    nGlobalIdx  : INT;
    nCount      : INT;
    stZoneCfg : ST_HeatZoneConfig;
    aipChannels : ARRAY[1..GVL_HeatZone.MAX_CHANNELS] OF I_Tunable;
END_VAR
    FOR iZone := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        IF NOT GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].bPopulated THEN
            CONTINUE;
        END_IF

        nCount := GVL_TemperatureZoneConfig.ZONE_CONFIG[iZone].nChannelCount;
        stZoneCfg := F_GetHeatZoneConfig(eZone := iZone);

        IF stZoneCfg.sZoneID = 'INVALID' THEN CONTINUE; END_IF

        // Gather pointers to this zone's channels (clear array first)
        MEMSET(ADR(aipChannels), 0, SIZEOF(aipChannels));
        FOR e := 1 TO nCount DO
            nGlobalIdx := _anZoneChannelOffset[iZone] + e;
            IF nGlobalIdx >= 1 AND nGlobalIdx <= GVL_TemperatureZones.CHANNEL_MAX THEN
                aipChannels[e] := _afbChannels[nGlobalIdx];
            END_IF
        END_FOR

        _afbZones[iZone].Init(
            stConfig      := stZoneCfg,
            aipChannels   := aipChannels,
            nChannelCount := nCount
        );

        _ipControllers[iZone] := _afbZones[iZone];
    END_FOR
END_METHOD

// ============================================================================
// EXECUTION (PRIVATE)
// ============================================================================

(* Channels first, then zones. Zones read fresh channel status during their
   Execute, so channel control loops must run before zone aggregation. *)
METHOD PRIVATE _ExecuteChannels
VAR
    i : INT;
END_VAR
    FOR i := 1 TO GVL_TemperatureZones.CHANNEL_MAX DO
        _afbChannels[i].Execute();
    END_FOR
END_METHOD

METHOD PRIVATE _ExecuteZones
VAR
    i : INT;
END_VAR
    FOR i := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        IF _ipControllers[i] <> 0 THEN
            _afbZones[i].Execute();
        END_IF
    END_FOR
END_METHOD

METHOD PRIVATE _UpdateStatus
VAR
    i        : INT;
    stStatus : ST_ControllerStatus;
END_VAR
    bAnyError := FALSE;
    bAllAtSetpoint := TRUE;
    stFirstError.eErrorId := E_ErrorId.NO_ERROR;
    stFirstError.sSource := '';
    stFirstError.sMessage := '';

    // Roll up zone status (zones already aggregate channel status internally)
    FOR i := 1 TO GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX DO
        IF _ipControllers[i] <> 0 THEN
            stStatus := _ipControllers[i].GetStatus();
            _CaptureFirstError(stStatus.stError);
            IF stStatus.bEnabled AND NOT stStatus.bAtSetpoint THEN
                bAllAtSetpoint := FALSE;
            END_IF
        END_IF
    END_FOR

    // Monitor sensor errors
    FOR i := 1 TO GVL_TemperatureZones.MONITOR_ZONE_COUNT DO
        IF _ipMonitorSensors[i] <> 0 AND_THEN _ipMonitorSensors[i].IsError() THEN
            _CaptureFirstError(_ipMonitorSensors[i].GetError());
        END_IF
    END_FOR
END_METHOD

METHOD PRIVATE _CaptureFirstError
VAR_INPUT
    stError : ST_Error;
END_VAR
    IF stError.eErrorId <> E_ErrorId.NO_ERROR THEN
        bAnyError := TRUE;
        IF stFirstError.eErrorId = E_ErrorId.NO_ERROR THEN
            stFirstError := stError;
        END_IF
    END_IF
END_METHOD

// I_TemperatureSubsystem
METHOD PUBLIC IsInitialized : BOOL
    IsInitialized := bInitialized;
END_METHOD

METHOD PUBLIC HasAnyError : BOOL
    HasAnyError := bAnyError;
END_METHOD

METHOD PUBLIC AllAtSetpoint : BOOL
    AllAtSetpoint := bAllAtSetpoint;
END_METHOD

METHOD PUBLIC GetFirstError : ST_Error
    GetFirstError := stFirstError;
END_METHOD

METHOD PUBLIC GetZoneCount : INT
    GetZoneCount := GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX;
END_METHOD

METHOD PUBLIC GetTemperatureByIndex : I_Sensor
VAR_INPUT
    nZone : INT;
END_VAR
    GetTemperatureByIndex := GetTemperature(nZone);
END_METHOD

METHOD PUBLIC GetControllerByIndex : I_Controller
VAR_INPUT
    nZone : INT;
END_VAR
    GetControllerByIndex := GetController(nZone);
END_METHOD

METHOD PUBLIC GetChannelControllerByGlobalIndex : I_Controller
VAR_INPUT
    nGlobalIdx : INT;
END_VAR
    IF nGlobalIdx >= 1 AND nGlobalIdx <= GVL_TemperatureZones.CHANNEL_MAX THEN
        GetChannelControllerByGlobalIndex := _afbChannels[nGlobalIdx];
    END_IF
END_METHOD

METHOD PUBLIC GetChannelControllerByZoneIndex : I_Controller
VAR_INPUT
    nZone       : INT;
    nChannelIdx : INT;
END_VAR
    GetChannelControllerByZoneIndex := GetChannel(nZone, nChannelIdx);
END_METHOD

METHOD PUBLIC GetChannelCountByIndex : INT
VAR_INPUT
    nZone : INT;
END_VAR
    IF nZone >= 1 AND nZone <= GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX THEN
        GetChannelCountByIndex := GVL_TemperatureZoneConfig.ZONE_CONFIG[nZone].nChannelCount;
    END_IF
END_METHOD

END_FUNCTION_BLOCK
