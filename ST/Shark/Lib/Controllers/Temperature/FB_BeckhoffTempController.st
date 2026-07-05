(* Beckhoff temperature controller for single heating zone. Integrates dual sensor support, PID control, and safety monitoring. *)
FUNCTION_BLOCK FB_BeckhoffTempController IMPLEMENTS I_Tunable
VAR_OUTPUT
    stStatus : ST_BeckhoffTempZoneStatus;  // Consolidated status (all-in-one)
END_VAR
VAR
    // Configuration
    _stConfig : ST_BeckhoffTempZoneConfig;
    _bInitialized : BOOL := FALSE;

    // Hardware
    _ipTemperatureSensor : I_Sensor;     // Temperature sensor (single or dual)
    _ipHeaterOutput     : I_DigitalOutput;

    // Core components - Direct use of Beckhoff controller
    _fbTempController   : FB_CTRL_TempController;

    // Safety system (heater watchdog only - execution monitoring via TwinCAT task watchdog)
    _fbHeaterWatchdog   : FB_HeaterSafetyWatchdog;

    // Independent over-temperature interlock via an optional safety thermocouple.
    // Defence in depth: measured independently of the control RTD. Wired in via
    // InitSafety(); channels without a safety TC leave _bSafetyMonitorActive FALSE
    // and behave exactly as before.
    _fbOverTempMonitor    : FB_OverTemperatureMonitor;
    _bSafetyMonitorActive : BOOL := FALSE;

    // Control state
    _bEnabled           : BOOL := FALSE;
    _rTargetSetpoint    : LREAL := 0.0;
    _eControlMode       : E_CTRL_MODE := E_CTRL_MODE.eCTRL_MODE_IDLE;
    _bHeaterOutput      : BOOL := FALSE;

    // Quick access
    _rCurrentTemperature : LREAL;                        // Current measured temperature (°C)
    _bSensorError       : BOOL;                         // Cached sensor error state

    // Centralized error handling
    _fbError            : FB_ErrorHandler;

    // Execution health
    _bControllerOK      : BOOL;
    _bSafetyTripped     : BOOL;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipTemperatureSensor : I_Sensor;  // Temperature sensor
    ipHeaterOutput      : I_DigitalOutput;      // Heater output
    stConfiguration     : ST_BeckhoffTempZoneConfig; // Zone configuration
END_VAR

    _ipTemperatureSensor := ipTemperatureSensor;
    _ipHeaterOutput := ipHeaterOutput;
    _stConfig := stConfiguration;

    // Use zone ID as error source (identifies which zone has the error)
    _fbError.Init(_stConfig.sZoneID);

    // Initialize watchdog with zone-specific name for error traceability
    _fbHeaterWatchdog.Init(
        sInstanceName := CONCAT('WD_', _stConfig.sZoneID),
        tMaxContinuousOn := stConfiguration.tMaxContinuousOn);

    IF _ipTemperatureSensor = 0 OR _ipHeaterOutput = 0 THEN
        RETURN;
    END_IF

    IF NOT _ValidateConfiguration() THEN
        RETURN;
    END_IF

    // Set control mode based on configuration
    IF stConfiguration.stControllerParams.tCtrlCycleTime = T#0S THEN
        _eControlMode := E_CTRL_MODE.eCTRL_MODE_TUNE;
    ELSE
        _eControlMode := E_CTRL_MODE.eCTRL_MODE_ACTIVE;
    END_IF

    IF _stConfig.bAutoEnable AND _eControlMode = E_CTRL_MODE.eCTRL_MODE_ACTIVE THEN
        _bEnabled := TRUE;
    END_IF

    _bInitialized := TRUE;
    Init := TRUE;

END_METHOD

(* Optional: attach an independent safety thermocouple over-temperature interlock.
   Call after Init(). The trip limit and reset hysteresis come from the controller
   configuration (set at Init); only the sensor interface is injected here, keeping
   hardware wiring (param) separate from configuration (struct). Channels with no
   safety TC simply never call this, or call it with rSafetyTcTripLimit <= 0, and
   keep their existing behaviour. The TC sensor lifecycle (Execute) is owned by the
   wiring program; this controller only reads it through the monitor. Fail-safe on
   TC fault is a fixed policy (always trip). *)
METHOD PUBLIC InitSafety : BOOL
VAR_INPUT
    ipSafetyTc : I_Sensor;     // Independent safety thermocouple (hardware injection)
END_VAR
    IF NOT _bInitialized THEN RETURN; END_IF
    IF ipSafetyTc = 0 THEN RETURN; END_IF
    IF _stConfig.rSafetyTcTripLimit <= 0.0 THEN RETURN; END_IF   // not configured for this channel

    IF NOT _fbOverTempMonitor.Init(
        sInstanceName           := CONCAT('OTM_', _stConfig.sZoneID),
        ipSafetyTc              := ipSafetyTc,
        rTripLimit              := _stConfig.rSafetyTcTripLimit,
        rResetHysteresis        := _stConfig.rSafetyTcResetHysteresis,
        bTreatSensorFaultAsTrip := TRUE)   // fixed fail-safe policy: TC fault is always a trip
    THEN
        RETURN;
    END_IF

    _bSafetyMonitorActive := TRUE;
    InitSafety := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
VAR
    eActiveMode : E_CTRL_MODE;
END_VAR

    IF NOT _bInitialized THEN
        IF _ipHeaterOutput <> 0 THEN _ipHeaterOutput.SetOutput(FALSE);
        END_IF
        RETURN;
    END_IF

    // 1. Read Inputs
    _rCurrentTemperature := _ipTemperatureSensor.GetMeasurement();
    _bSensorError := _ipTemperatureSensor.IsError();

    _fbHeaterWatchdog.bHeaterState := _ipHeaterOutput.GetOutput();
    _fbHeaterWatchdog.bManualTrip := FALSE;
    _fbHeaterWatchdog.Execute();

    // Independent over-temperature interlock (optional safety thermocouple)
    IF _bSafetyMonitorActive THEN
        _fbOverTempMonitor.Execute();
    END_IF

    // 2. Determine Safety State (includes over-temperature protection)
    _bSafetyTripped := _fbHeaterWatchdog.IsTripActive()
    OR _bSensorError
    OR (_rCurrentTemperature > _stConfig.rSafetyMaxTemp)
    OR (_bSafetyMonitorActive AND _fbOverTempMonitor.IsTripActive());

    // 3. Determine Control Mode
    IF _eControlMode = E_CTRL_MODE.eCTRL_MODE_RESET THEN
        // Allow RESET command to pass through regardless of enable state
        eActiveMode := E_CTRL_MODE.eCTRL_MODE_RESET;
    ELSIF _bSafetyTripped OR NOT _bEnabled THEN
        eActiveMode := E_CTRL_MODE.eCTRL_MODE_IDLE;
    ELSE
        eActiveMode := _eControlMode;
    END_IF

    // 4. Execute Controller
    _fbTempController(
        eCtrlMode := eActiveMode,
        bSelSetpoint := FALSE,
        fW1 := _rTargetSetpoint,
        fX := _rCurrentTemperature,
        fYManual := _stConfig.rOutputSafeDefault,
        bOpenThermocouple := FALSE,
        bReverseThermocouple := FALSE,
        bBackVoltage := FALSE,
        bLeakage := FALSE,
        bShortCircuit := FALSE,
        bOpenCircuit := FALSE,
        sParaControllerExternal := _stConfig.stExternalPID,
        sControllerParameter := _stConfig.stControllerParams
    );

    _bControllerOK := NOT _fbTempController.bError;

    // 5. Update Outputs (Safety Override)
    _bHeaterOutput := _fbTempController.bYPWMPos AND NOT _bSafetyTripped AND _bControllerOK;

    IF _ipHeaterOutput <> 0 THEN
        _ipHeaterOutput.SetOutput(_bHeaterOutput);
    END_IF

    // 6. Update Status
    _UpdateStatus();

    Execute := NOT _bSafetyTripped;

END_METHOD

METHOD PUBLIC SetSetpoint : BOOL
VAR_INPUT
    rDesiredSetpoint : LREAL;                           // Desired temperature (°C)
END_VAR

    IF rDesiredSetpoint >= _stConfig.rMinSetpoint AND rDesiredSetpoint <= _stConfig.rMaxSetpoint THEN
        _rTargetSetpoint := rDesiredSetpoint;
        SetSetpoint := TRUE;
    END_IF

END_METHOD

METHOD PUBLIC Enable : BOOL
VAR_INPUT
    bEnable : BOOL;
END_VAR

    IF NOT _bSafetyTripped THEN
        _bEnabled := bEnable;

        // Auto-recovery: If enabling, ensure we are in ACTIVE mode if currently in IDLE or RESET
        // IMPORTANT: Do NOT auto-switch if in TUNE mode (would interfere with tuning or coordinator)
        IF bEnable AND (_eControlMode = E_CTRL_MODE.eCTRL_MODE_IDLE OR _eControlMode = E_CTRL_MODE.eCTRL_MODE_RESET) THEN
            _eControlMode := E_CTRL_MODE.eCTRL_MODE_ACTIVE;
        END_IF

        Enable := TRUE;
    ELSE
        _bEnabled := FALSE;
    END_IF

END_METHOD

METHOD PUBLIC Reset : BOOL
VAR
    bWatchdogReset : BOOL;
    bMonitorReset  : BOOL := TRUE;
END_VAR

    bWatchdogReset := _fbHeaterWatchdog.Reset();

    // Cascade the reset to the over-temperature interlock when present
    IF _bSafetyMonitorActive THEN
        bMonitorReset := _fbOverTempMonitor.Reset();
    END_IF

    Reset := bWatchdogReset AND bMonitorReset;

    // Reset controller by setting mode
    SetControlMode(E_CTRL_MODE.eCTRL_MODE_RESET);

    ClearError();

    IF Reset THEN
        _bSafetyTripped := FALSE;
    END_IF

END_METHOD

    // Error Handling Methods

METHOD PRIVATE RaiseError
VAR_INPUT
    eErrorId : E_ErrorId;
    sMessage : STRING(255);
END_VAR
    _fbError.Raise(eErrorId, sMessage);
    stStatus.stError := _fbError.GetError();
END_METHOD

METHOD PRIVATE ClearError
    IF _fbError.IsActive() THEN
        _fbError.Clear();
        stStatus.stError := _fbError.GetError();
    END_IF
END_METHOD

METHOD PRIVATE _UpdateStatus
VAR
    sCtrlErrorMsg : STRING(255);
END_VAR
    // Basic State
    stStatus.bEnabled := _bEnabled;
    stStatus.rSetpoint := _rTargetSetpoint;
    stStatus.rMeasuredValue := _rCurrentTemperature;
    stStatus.rOutput := _fbTempController.fYAnalog;
    stStatus.eCtrlState := _fbTempController.eCtrlState;
    stStatus.bHeaterActive := _fbTempController.bYPWMPos;
    stStatus.rPWMOutput := stStatus.rOutput;
    stStatus.sZoneID := _stConfig.sZoneID;
    stStatus.bSafetyTripped := _bSafetyTripped;
    stStatus.bSensorOK := NOT _bSensorError;
    stStatus.bControllerOK := _bControllerOK;
    // stStatus.bHealthy is updated below

    // Setpoint Reached Check - use common utility function
    stStatus.bAtSetpoint := F_IsWithinAbsoluteTolerance(
        rMeasured := _rCurrentTemperature,
        rSetpoint := _rTargetSetpoint,
        rToleranceAbsolute := _stConfig.rSetpointTolerance,
        bEnabled := _bEnabled AND stStatus.bSensorOK AND NOT _bSafetyTripped
    );

    // Health Check
    stStatus.bHealthy := _bInitialized
    AND stStatus.bSensorOK
    AND _bControllerOK
    AND NOT _bSafetyTripped;

    // Error Detail - Update error based on current conditions
    IF _rCurrentTemperature > _stConfig.rSafetyMaxTemp THEN
        _fbError.RaiseWithValues(E_ErrorId.ERR_CTRL_LIMIT_EXCEEDED, 'Temp', _rCurrentTemperature, _stConfig.rSafetyMaxTemp, 'C');
        stStatus.stError := _fbError.GetError();
        stStatus.bCanResetSafety := (_rCurrentTemperature < _stConfig.rSafetyMaxTemp);
        stStatus.sBlockReason := 'Over-temperature';
    ELSIF _bSafetyMonitorActive AND _fbOverTempMonitor.IsTripActive() THEN
        // Independent safety-thermocouple over-temperature interlock. Inert on
        // machines that never call InitSafety (_bSafetyMonitorActive = FALSE), so
        // this branch only runs where a TC is actually fitted. Surface the
        // monitor's own error so the zone rollup reports the true TC cause.
        _fbError.RaiseFromChild(_fbOverTempMonitor.GetError());
        stStatus.stError := _fbError.GetError();
        stStatus.bCanResetSafety := _fbOverTempMonitor.CanReset();
        stStatus.sBlockReason := _fbOverTempMonitor.GetBlockReason();
    ELSIF _fbHeaterWatchdog.IsTripActive() THEN
        _fbError.RaiseFromChild(_fbHeaterWatchdog.GetError());
        stStatus.stError := _fbError.GetError();
        stStatus.bCanResetSafety := _fbHeaterWatchdog.CanReset();
        stStatus.sBlockReason := _fbHeaterWatchdog.GetBlockReason();
    ELSIF NOT stStatus.bSensorOK THEN
        _fbError.RaiseFromChild(_ipTemperatureSensor.GetError());
        stStatus.stError := _fbError.GetError();
        stStatus.bCanResetSafety := FALSE;
        stStatus.sBlockReason := 'Sensor fault';
    ELSIF _fbTempController.bError THEN
        sCtrlErrorMsg := CONCAT('Controller internal error: ', TO_STRING(_fbTempController.eErrorId));
        RaiseError(E_ErrorId.ERR_GEN_INTERNAL, sCtrlErrorMsg);
        stStatus.bCanResetSafety := FALSE;
        stStatus.sBlockReason := 'Controller error';
    ELSE
        // Clear error when all conditions are OK
        ClearError();
        stStatus.bCanResetSafety := FALSE;
        stStatus.sBlockReason := '';
    END_IF

END_METHOD

METHOD PUBLIC SetControlMode : BOOL
VAR_INPUT
    eMode : E_CTRL_MODE;
END_VAR

    IF NOT _bInitialized THEN RETURN;
    END_IF

    _eControlMode := eMode;
    SetControlMode := TRUE;
END_METHOD

METHOD PUBLIC GetControlMode : E_CTRL_MODE
    GetControlMode := _eControlMode;
END_METHOD

METHOD PUBLIC GetStatus : ST_ControllerStatus
    GetStatus := stStatus;
END_METHOD

METHOD PUBLIC GetZoneStatus : ST_BeckhoffTempZoneStatus
    GetZoneStatus := stStatus;
END_METHOD

    // Ramp Rate Methods

METHOD PUBLIC SetHeatingRampRate : BOOL
VAR_INPUT
    rRate_DegPerMin : LREAL;  // Ramp rate in °C per minute (0 = instant)
END_VAR
    IF NOT _bInitialized THEN RETURN;
    END_IF

    _stConfig.stControllerParams.fWVeloPos := F_RampRateToVelocity(rRate_DegPerMin);
    SetHeatingRampRate := TRUE;
END_METHOD

METHOD PUBLIC SetCoolingRampRate : BOOL
VAR_INPUT
    rRate_DegPerMin : LREAL;  // Ramp rate in °C per minute (0 = instant)
END_VAR
    IF NOT _bInitialized THEN RETURN;
    END_IF

    _stConfig.stControllerParams.fWVeloNeg := F_RampRateToVelocity(rRate_DegPerMin);
    SetCoolingRampRate := TRUE;
END_METHOD

METHOD PUBLIC GetHeatingRampRate : LREAL
    IF _bInitialized THEN
        GetHeatingRampRate := F_VelocityToRampRate(_stConfig.stControllerParams.fWVeloPos);
    ELSE
        GetHeatingRampRate := 0.0;
    END_IF
END_METHOD

METHOD PUBLIC GetCoolingRampRate : LREAL
    IF _bInitialized THEN
        GetCoolingRampRate := F_VelocityToRampRate(_stConfig.stControllerParams.fWVeloNeg);
    ELSE
        GetCoolingRampRate := 0.0;
    END_IF
END_METHOD

METHOD PRIVATE _ValidateConfiguration : BOOL
    IF _stConfig.rMinSetpoint >= _stConfig.rMaxSetpoint THEN
        RaiseError(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Min setpoint must be less than max');
        RETURN;
    END_IF

    IF _stConfig.rSafetyMaxTemp <= _stConfig.rMaxSetpoint THEN
        RaiseError(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Safety max must exceed max setpoint');
        RETURN;
    END_IF

    IF _stConfig.rAbsoluteMaxTemp <= _stConfig.rSafetyMaxTemp THEN
        RaiseError(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Absolute max must exceed safety max');
        RETURN;
    END_IF

    IF _stConfig.rSetpointTolerance <= 0.0 THEN
        RaiseError(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Setpoint tolerance must be positive');
        RETURN;
    END_IF

    _ValidateConfiguration := TRUE;
END_METHOD

END_FUNCTION_BLOCK

