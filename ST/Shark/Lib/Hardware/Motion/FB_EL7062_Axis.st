(*
Stepper Axis Controller for EL7062 Terminal

Controls a stepper motor using TwinCAT MC2 library.
Features: Homing, Absolute Positioning, Limit Switch Interlocks

Refactored to use I_DigitalInput interface instead of IDigitalIOHardware.
*)
FUNCTION_BLOCK FB_EL7062_Axis
VAR
    _Axis : AXIS_REF;
    _sInstanceName : STRING(80);

    // Startup and state
    _StartUp      : BOOL := TRUE;
    _StartHoming  : BOOL;

    // Limit switches (using I_DigitalInput interface)
    _NegativeLimitSwitch : I_DigitalInput;
    _PositiveLimitSwitch : I_DigitalInput;

    // Software limits
    _SW_PositiveLimit : LREAL := 6.0;  // Positive limit in mm
    _SW_NegativeLimit : LREAL := -6.0;  // Negative limit in mm

    // Motion parameters
    _JogVelocity  : LREAL := 0.10;
    _Acceleration : LREAL := 1.0;
    _Deceleration : LREAL := 1.0;

    // TwinCAT MC2 function blocks
    _fbPower : Tc2_MC2.MC_Power;
    _fbJog   : Tc2_MC2.MC_Jog;
    _fbHome  : Tc2_MC2.MC_Home;
    _fbStop  : Tc2_MC2.MC_Stop;
    _fbMove  : Tc2_MC2.MC_MoveAbsolute;

    // State machine
    _eState     : E_EL7062_MotorState := E_EL7062_MotorState.Idle;
    _eHomeState : E_EL7062_HomeState := E_EL7062_HomeState.Idle;
    _tmrStartUp : TON;

    // Position tracking
    _ActualPos    : LREAL;
    _TargetPos    : LREAL;
    _CommandedPos : LREAL;
    _IsCalibrated : BOOL;
    _bError       : BOOL;
END_VAR
VAR CONSTANT
    MIDDLE_POS : LREAL := 3.0;
END_VAR

    (* Initialize axis parameters *)
METHOD PUBLIC Init : BOOL
VAR_INPUT
    sInstanceName       : STRING(80);
    NegativeLimitSwitch : I_DigitalInput;
    PositiveLimitSwitch : I_DigitalInput;
    JogVelocity         : LREAL := 0.10;
    Acceleration        : LREAL := 1.0;
    Deceleration        : LREAL := 1.0;
    SW_PositiveLimit    : LREAL;
    SW_NegativeLimit    : LREAL;
END_VAR
    // Homing jogs toward the NEGATIVE limit switch, so it is mandatory.
    // The POSITIVE limit switch is optional: axes that home against a single
    // end-stop pass 0 here and rely on the software positive limit instead.
    IF NegativeLimitSwitch = 0 THEN
        _bError := TRUE;
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'FB_EL7062_Axis.Init',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := CONCAT('missing negative limit switch, axis: ', sInstanceName)
        );
        Init := FALSE;
        RETURN;
    END_IF
    _sInstanceName := sInstanceName;
    _NegativeLimitSwitch := NegativeLimitSwitch;
    _PositiveLimitSwitch := PositiveLimitSwitch;
    _JogVelocity := JogVelocity;
    _Acceleration := Acceleration;
    _Deceleration := Deceleration;
    _SW_PositiveLimit := SW_PositiveLimit;
    _SW_NegativeLimit := SW_NegativeLimit;

    _fbJog.Acceleration := _Acceleration;
    _fbJog.Deceleration := _Deceleration;
    _fbJog.Velocity := JogVelocity;

    _bError := FALSE;
    Init := TRUE;
END_METHOD

    (* Cyclic execution - must be called every scan *)
METHOD PUBLIC Execute : BOOL
VAR
    bError : BOOL;
    bDone  : BOOL;
END_VAR
    _MonitorLimit();

    CASE _eState OF
        E_EL7062_MotorState.Ready:
        _ResetFBs();

        E_EL7062_MotorState.Homing:
        _HomingState(bExecute := TRUE, HomingDone => bDone, HomingError => bError);

        IF bError THEN
            _bError := TRUE;
            GVL_ErrorLogger.Logger.LogError(
                sSource  := 'FB_EL7062_Axis',
                eErrorId := E_ErrorId.ERR_HW_MOTION_FAULT,
                sContext := CONCAT('homing procedure error, axis: ', _sInstanceName)
            );
            _eState := E_EL7062_MotorState.Idle;
        ELSIF bDone THEN
            _bError := FALSE;
            GVL_ErrorLogger.Logger.LogInfo(
                sSource  := 'FB_EL7062_Axis',
                sContext := CONCAT('homing procedure completed, axis: ', _sInstanceName)
            );
            _eState := E_EL7062_MotorState.Ready;
        END_IF

        E_EL7062_MotorState.Moving:
        _MovingState(MovingDone => bDone, MovingError => bError);
        IF bDone THEN
            _eState := E_EL7062_MotorState.Ready;
        ELSIF bError THEN
            _bError := TRUE;
            _eState := E_EL7062_MotorState.Ready;
            GVL_ErrorLogger.Logger.LogError(
                sSource  := 'FB_EL7062_Axis',
                eErrorId := E_ErrorId.ERR_HW_MOTION_FAULT,
                sContext := CONCAT('move failed, axis: ', _sInstanceName)
            );
        END_IF

        E_EL7062_MotorState.Stop:
        _StopState(bDone => bDone, bError => bError);
        IF bDone THEN
            _eState := E_EL7062_MotorState.Ready;
        ELSIF bError THEN
            _bError := TRUE;
            _eState := E_EL7062_MotorState.Ready;
            GVL_ErrorLogger.Logger.LogError(
                sSource  := 'FB_EL7062_Axis',
                eErrorId := E_ErrorId.ERR_HW_MOTION_FAULT,
                sContext := CONCAT('stop failed, axis: ', _sInstanceName)
            );
        END_IF
    END_CASE

    Execute := TRUE;
END_METHOD

    (* Get actual position *)
METHOD PUBLIC GetActPosition : LREAL
    GetActPosition := _ActualPos;
END_METHOD

    (* Get motor state *)
METHOD PUBLIC GetState : E_EL7062_MotorState
    GetState := _eState;
END_METHOD

    (* Get target position *)
METHOD PUBLIC GetTarPosition : LREAL
    GetTarPosition := _TargetPos;
END_METHOD

    (* Check if axis is calibrated *)
METHOD PUBLIC IsCalibrated : BOOL
    IsCalibrated := _IsCalibrated;
END_METHOD

METHOD PUBLIC IsError : BOOL
    IsError := _bError;
END_METHOD

    (* Move to absolute position *)
METHOD PUBLIC MoveAbs : BOOL
VAR_INPUT
    TargetPosition : LREAL;
END_VAR
    IF _eState = E_EL7062_MotorState.Ready THEN
        _TargetPos := TargetPosition;
        _eState := E_EL7062_MotorState.Moving;
        MoveAbs := TRUE;
    ELSE
        MoveAbs := FALSE;
    END_IF
END_METHOD

    (* Start homing sequence *)
METHOD PUBLIC StartHoming : BOOL
    IF _eState = E_EL7062_MotorState.Idle OR _eState = E_EL7062_MotorState.Ready THEN
        _eState := E_EL7062_MotorState.Homing;
        StartHoming := TRUE;
    ELSE
        StartHoming := FALSE;
    END_IF
END_METHOD

    (* Stop motion *)
METHOD PUBLIC Stop : BOOL
    IF _eState = E_EL7062_MotorState.Moving OR _eState = E_EL7062_MotorState.Homing THEN
        _eState := E_EL7062_MotorState.Stop;
        Stop := TRUE;
    ELSE
        Stop := FALSE;
    END_IF
END_METHOD

    (* Private: Monitor limit switches and control power *)
METHOD PRIVATE _MonitorLimit : BOOL
    _ActualPos := _Axis.NcToPlc.ActPos;
    _CommandedPos := _Axis.NcToPlc.TargetPos;

    // Startup delay
    IF _StartUp THEN
        _tmrStartUp(IN := TRUE, PT := T#5S);
        IF NOT _tmrStartUp.Q THEN
            _fbPower(Axis := _Axis);
            RETURN;
        ELSE
            _tmrStartUp(IN := FALSE, PT := T#5S);
            _StartUp := FALSE;
        END_IF
    END_IF

    _fbPower.Enable := TRUE;
    _fbPower.Override := 100;

    IF _eState = E_EL7062_MotorState.Homing THEN
        // During homing, allow movement in both directions
        _fbPower.Enable_Positive := TRUE;
        _fbPower.Enable_Negative := TRUE;
    ELSE
        // Check negative limit switch
        IF _NegativeLimitSwitch <> 0 THEN
            IF _NegativeLimitSwitch.GetState() THEN
                _fbPower.Enable_Negative := FALSE;
            ELSE
                _fbPower.Enable_Negative := TRUE;
            END_IF
        ELSE
            _fbPower.Enable_Negative := TRUE;
        END_IF

        // Check software negative limit
        IF _ActualPos <= _SW_NegativeLimit THEN
            _fbPower.Enable_Negative := FALSE;
        END_IF

        // Check positive limit switch
        IF _PositiveLimitSwitch <> 0 THEN
            IF _PositiveLimitSwitch.GetState() THEN
                _fbPower.Enable_Positive := FALSE;
            ELSE
                _fbPower.Enable_Positive := TRUE;
            END_IF
        ELSE
            _fbPower.Enable_Positive := TRUE;
        END_IF

        // Check software positive limit
        IF _ActualPos >= _SW_PositiveLimit THEN
            _fbPower.Enable_Positive := FALSE;
        END_IF
    END_IF

    _fbPower(Axis := _Axis);
    _MonitorLimit := TRUE;
END_METHOD

    (* Private: Homing state machine *)
METHOD PRIVATE _HomingState : BOOL
VAR_INPUT
    bExecute : BOOL;
END_VAR
VAR_OUTPUT
    HomingDone   : BOOL;
    HomingError  : BOOL;
    HomingActive : BOOL;
END_VAR
    CASE _eHomeState OF
        E_EL7062_HomeState.Idle:
        HomingActive := FALSE;
        HomingDone := FALSE;
        HomingError := FALSE;
        _fbJog(Axis := _Axis, JogForward := FALSE, JogBackwards := FALSE);
        _fbHome(Axis := _Axis, Execute := FALSE);
        _fbMove(Axis := _Axis, Execute := FALSE);
        _fbStop(Axis := _Axis, Execute := FALSE);

        IF bExecute THEN
            _eHomeState := E_EL7062_HomeState.StartJog;
        END_IF

        E_EL7062_HomeState.StartJog:
        HomingActive := TRUE;
        _fbJog(
            Axis := _Axis,
            JogForward := FALSE,
            JogBackwards := TRUE,
            Mode := Tc2_MC2.MC_JOGMODE_CONTINOUS,
            Velocity := _JogVelocity,
            Acceleration := _Acceleration,
            Deceleration := _Deceleration);

        IF _fbJog.Busy THEN
            _eHomeState := E_EL7062_HomeState.WaitNegLimit;
        ELSIF _fbJog.Error THEN
            _eHomeState := E_EL7062_HomeState.Fault;
        END_IF

        E_EL7062_HomeState.WaitNegLimit:
        IF _NegativeLimitSwitch <> 0 AND_THEN _NegativeLimitSwitch.GetState() THEN
            _fbStop(Axis := _Axis, Execute := TRUE, Deceleration := _Deceleration);
            IF _fbStop.Done THEN
                _eHomeState := E_EL7062_HomeState.SetHome_AtSwitch;
                _fbStop(Axis := _Axis, Execute := FALSE);
                _fbJog(Axis := _Axis, JogForward := FALSE, JogBackwards := FALSE);
            END_IF
        ELSIF _fbJog.Error THEN
            _eHomeState := E_EL7062_HomeState.Fault;
        END_IF

        E_EL7062_HomeState.SetHome_AtSwitch:
        _fbHome(Axis := _Axis, Execute := TRUE, Position := 0, HomingMode := Tc2_MC2.MC_Direct);
        IF _fbHome.Done THEN
            _eHomeState := E_EL7062_HomeState.MoveToMiddle;
            _fbJog(Axis := _Axis, JogForward := FALSE, JogBackwards := FALSE);
            _fbHome(Axis := _Axis, Execute := FALSE);
        ELSIF _fbHome.Error THEN
            _eHomeState := E_EL7062_HomeState.Fault;
        END_IF

        E_EL7062_HomeState.MoveToMiddle:
        _fbMove(
            Axis := _Axis,
            Execute := TRUE,
            Velocity := _JogVelocity,
            Acceleration := _Acceleration,
            Deceleration := _Deceleration,
            Position := MIDDLE_POS);

        IF _fbMove.Done THEN
            _eHomeState := E_EL7062_HomeState.SetHome_Final;
            _fbMove(Axis := _Axis, Execute := FALSE);
        ELSIF _fbMove.Error THEN
            _eHomeState := E_EL7062_HomeState.Fault;
        END_IF

        E_EL7062_HomeState.SetHome_Final:
        _fbHome(Axis := _Axis, Execute := TRUE, Position := 0, HomingMode := Tc2_MC2.MC_Direct);
        IF _fbHome.Done THEN
            HomingDone := TRUE;
            _eHomeState := E_EL7062_HomeState.Idle;
            _fbJog(Axis := _Axis, JogForward := FALSE, JogBackwards := FALSE);
            _IsCalibrated := TRUE;
        ELSIF _fbHome.Error THEN
            _eHomeState := E_EL7062_HomeState.Fault;
        END_IF

        E_EL7062_HomeState.Fault:
        _eHomeState := E_EL7062_HomeState.Idle;
        HomingError := TRUE;
        HomingActive := FALSE;
        HomingDone := FALSE;
        _fbJog(Axis := _Axis, JogForward := FALSE, JogBackwards := FALSE);
        _fbHome(Axis := _Axis, Execute := FALSE);
        _fbMove(Axis := _Axis, Execute := FALSE);
        _fbStop(Axis := _Axis, Execute := FALSE);
    END_CASE

    _HomingState := TRUE;
END_METHOD

    (* Private: Moving state machine *)
METHOD PRIVATE _MovingState : BOOL
VAR_OUTPUT
    MovingDone   : BOOL;
    MovingError  : BOOL;
    MovingActive : BOOL;
END_VAR
    IF NOT _fbPower.Enable OR
        (NOT _fbPower.Enable_Negative AND (_TargetPos <= _ActualPos)) OR
        (NOT _fbPower.Enable_Positive AND (_TargetPos >= _ActualPos)) THEN
        _fbMove(Axis := _Axis, Execute := FALSE);
        MovingActive := FALSE;
        MovingDone := FALSE;
        MovingError := TRUE;
        RETURN;
    END_IF

    _fbMove(
        Axis := _Axis,
        Execute := TRUE,
        Position := _TargetPos);

    IF _fbMove.Done THEN
        _fbMove(Axis := _Axis, Execute := FALSE);
        MovingActive := FALSE;
        MovingDone := TRUE;
        MovingError := FALSE;
        RETURN;
    ELSIF _fbMove.Error THEN
        _fbMove(Axis := _Axis, Execute := FALSE);
        MovingActive := FALSE;
        MovingDone := FALSE;
        MovingError := TRUE;
        RETURN;
    END_IF

    MovingActive := TRUE;
    MovingDone := FALSE;
    MovingError := FALSE;
END_METHOD

    (* Private: Stop state machine *)
METHOD PRIVATE _StopState : BOOL
VAR_OUTPUT
    bDone   : BOOL := FALSE;
    bError  : BOOL := FALSE;
    bActive : BOOL := FALSE;
END_VAR
    _fbStop(
        Axis := _Axis,
        Execute := TRUE,
        Deceleration := 4 * _Acceleration,
        Jerk := 16 * _Acceleration);

    IF _fbStop.Done THEN
        _fbStop(Axis := _Axis, Execute := FALSE);
        bDone := TRUE;
        bError := FALSE;
        bActive := FALSE;
        RETURN;
    ELSIF _fbStop.Error THEN
        _fbStop(Axis := _Axis, Execute := FALSE);
        bDone := FALSE;
        bError := TRUE;
        bActive := FALSE;
        RETURN;
    END_IF

    bDone := FALSE;
    bError := FALSE;
    bActive := TRUE;
END_METHOD

    (* Private: Reset all function blocks *)
METHOD PRIVATE _ResetFBs : BOOL
    _fbMove(Axis := _Axis, Execute := FALSE);
    _fbStop(Axis := _Axis, Execute := FALSE);
    _fbJog(Axis := _Axis, JogForward := FALSE, JogBackwards := FALSE);
    _fbHome(Axis := _Axis, Execute := FALSE);
    _eHomeState := E_EL7062_HomeState.Idle;
    _ResetFBs := TRUE;
END_METHOD

END_FUNCTION_BLOCK


