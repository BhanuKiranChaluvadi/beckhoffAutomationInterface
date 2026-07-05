(* Bronkhorst Controller (Generic). Implements I_Controller for Bronkhorst mass flow and pressure controllers. *)
FUNCTION_BLOCK FB_BronkhorstController IMPLEMENTS I_Controller, I_Sensor
VAR
    _ipHardware         : I_BronkhorstController;
    _sInstanceName      : STRING(80);
    _rMinValue          : LREAL;
    _rMaxValue          : LREAL;
    _bInverseControl    : BOOL;
    _rBoundaryTolerance : LREAL;
    _rSetpointTolerance : LREAL;
    _rSetpoint          : LREAL;
    _measured           : LREAL;
    _bEnabled           : BOOL;
    _fbError            : FB_ErrorHandler;
    _eControlMode       : E_BronkhorstControlMode;
    _rValvePosition     : LREAL;
    _bInitialized       : BOOL := FALSE;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipHardware         : I_BronkhorstController;
    sInstanceName      : STRING(80);
    rMinValue          : LREAL := 0.0;
    rMaxValue          : LREAL := 100.0;
    bInverseControl    : BOOL := FALSE;
    rBoundaryTolerance : LREAL := 0.01;
    rSetpointTolerance : LREAL := 2.0;
END_VAR
    _sInstanceName := sInstanceName;
    _fbError.Init(sInstanceName);

    IF ipHardware = 0 THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware interface is null');
        RETURN;
    END_IF

    IF rMaxValue <= rMinValue THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_INVALID_PARAM, 'Max value must exceed min value');
        RETURN;
    END_IF
    _ipHardware := ipHardware;
    _rMinValue := rMinValue;
    _rMaxValue := rMaxValue;
    _bInverseControl := bInverseControl;
    _rBoundaryTolerance := rBoundaryTolerance;
    _rSetpointTolerance := rSetpointTolerance;
    _eControlMode := E_BronkhorstControlMode.VALVE_CLOSED;
    _bInitialized := TRUE;
    _fbError.Clear();
    Init := TRUE;
END_METHOD

METHOD PUBLIC Execute : BOOL
    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    IF _ipHardware = 0 THEN
        _fbError.Raise(E_ErrorId.ERR_GEN_NULL_POINTER, 'Hardware interface not set');
        RETURN;
    END_IF

    _measured := REAL_TO_LREAL(_ipHardware.GetMeasuredValue());
    _rValvePosition := REAL_TO_LREAL(_ipHardware.GetValveOutput());

    _ApplyBoundaryControl();

    _ipHardware.WriteSetpoint(LREAL_TO_REAL(_rSetpoint));
    _ipHardware.WriteEnable(_bEnabled);
    _ipHardware.WriteControlMode(TO_USINT(_eControlMode));

    IF _fbError.IsActive() THEN
        _fbError.Clear();
    END_IF
    Execute := TRUE;
END_METHOD

(*
    Boundary control logic for Bronkhorst valves.

    Normal valve (NO — Normally Open, e.g. flow MFCs):
        bInverseControl = FALSE
        Setpoint near MIN → close valve (VALVE_CLOSED)
        Setpoint near MAX → purge/fully open (VALVE_PURGE)

    Inverse valve (NC — Normally Closed, e.g. pressure controllers):
        bInverseControl = TRUE
        Setpoint near MAX → close valve (VALVE_CLOSED)
        Setpoint near MIN → purge/fully open (VALVE_PURGE)

    Between boundaries → BUS_CONTROL (PID regulates)
*)
METHOD PRIVATE _ApplyBoundaryControl
VAR
    bNearMin : BOOL;
    bNearMax : BOOL;
END_VAR
    IF NOT _bEnabled THEN
        _eControlMode := E_BronkhorstControlMode.VALVE_CLOSED;
        RETURN;
    END_IF

    bNearMin := _rSetpoint <= (_rMinValue + _rBoundaryTolerance);
    bNearMax := _rSetpoint >= (_rMaxValue - _rBoundaryTolerance);

    IF _bInverseControl THEN
        // NC valve: MAX→CLOSED, MIN→PURGE
        IF bNearMax THEN
            _eControlMode := E_BronkhorstControlMode.VALVE_CLOSED;
        ELSIF bNearMin THEN
            _eControlMode := E_BronkhorstControlMode.VALVE_PURGE;
        ELSE
            _eControlMode := E_BronkhorstControlMode.BUS_CONTROL;
        END_IF
    ELSE
        // NO valve: MIN→CLOSED, MAX→PURGE
        IF bNearMin THEN
            _eControlMode := E_BronkhorstControlMode.VALVE_CLOSED;
        ELSIF bNearMax THEN
            _eControlMode := E_BronkhorstControlMode.VALVE_PURGE;
        ELSE
            _eControlMode := E_BronkhorstControlMode.BUS_CONTROL;
        END_IF
    END_IF
END_METHOD

    (* I_Controller *)

METHOD PUBLIC Enable : BOOL
VAR_INPUT
    bEnable : BOOL;
END_VAR
    _bEnabled := bEnable;
    Enable := TRUE;
END_METHOD

METHOD PUBLIC GetStatus : ST_ControllerStatus
    GetStatus.rMeasuredValue := _measured;
    GetStatus.rSetpoint := _rSetpoint;
    GetStatus.rOutput := _rValvePosition;
    GetStatus.bEnabled := _bEnabled;
    GetStatus.bAtSetpoint := F_IsWithinTolerance(
        rMeasured := _measured,
        rSetpoint := _rSetpoint,
        rTolerancePercent := _rSetpointTolerance,
        bEnabled := _bEnabled
    );
    GetStatus.stError := _fbError.GetError();
END_METHOD

METHOD PUBLIC Reset : BOOL
    _fbError.Clear();
    Reset := TRUE;
END_METHOD

METHOD PUBLIC SetSetpoint : BOOL
VAR_INPUT
    rDesiredSetpoint : LREAL;
END_VAR
    _rSetpoint := LIMIT(_rMinValue, rDesiredSetpoint, _rMaxValue);
    SetSetpoint := TRUE;
END_METHOD

    (* I_Sensor *)

METHOD PUBLIC GetMeasurement : LREAL
    GetMeasurement := _measured;
END_METHOD

METHOD PUBLIC IsError : BOOL
    IsError := _fbError.IsActive();
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _fbError.GetError();
END_METHOD

END_FUNCTION_BLOCK

