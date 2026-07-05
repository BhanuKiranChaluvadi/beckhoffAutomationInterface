(*
DC Motor Controller for EL73x2 Terminal (No Encoder)

Controls a DC motor using limit switches for position feedback.
Refactored to use I_DigitalInput interface instead of IDigitalIOHardware.
*)
FUNCTION_BLOCK FB_EL73x2_DcMotorNoEncoder
VAR
    // Hardware IO
    bEnable   AT %Q* : BOOL;
    nVelocity AT %Q* : INT;
    stDcMotorStatus AT %I* : ST_EL73x2_DcMotorStatus;

    // Limit switch inputs (using I_DigitalInput interface)
    _NegativeLimitSwitch : I_DigitalInput;
    _PositiveLimitSwitch : I_DigitalInput;

    // Internal state
    _bStop         : BOOL;
    _bPosDirection : BOOL := FALSE;
    _bNegDirection : BOOL := FALSE;

    // Position tracking
    _ActualPosition : E_EL73x2_DcMotorPosition;
    _TargetPosition : E_EL73x2_DcMotorPosition;
END_VAR
VAR CONSTANT
    MAX_VELOCITY : INT := 15000;
END_VAR

    (* Initialize limit switches *)
METHOD PUBLIC Init : BOOL
VAR_INPUT
    NegativeLimitSwitch : I_DigitalInput;
    PositiveLimitSwitch : I_DigitalInput;
END_VAR
    IF NegativeLimitSwitch = 0 OR PositiveLimitSwitch = 0 THEN
        Init := FALSE;
        RETURN;
    END_IF
    _NegativeLimitSwitch := NegativeLimitSwitch;
    _PositiveLimitSwitch := PositiveLimitSwitch;
    Init := TRUE;
END_METHOD

    (* Cyclic execution - must be called every scan *)
METHOD PUBLIC Execute : BOOL
    // Emergency/Stop has highest priority
    IF _bStop THEN
        _bStop := FALSE;
        bEnable := FALSE;
        _bPosDirection := FALSE;
        _bNegDirection := FALSE;
        nVelocity := 0;
    ELSE
        // Enforce mutual exclusivity of direction commands
        IF _bPosDirection AND _bNegDirection THEN
            _bPosDirection := FALSE;
            _bNegDirection := FALSE;
        END_IF

        IF bEnable THEN
            // Drive logic with limit interlocks
            IF _bPosDirection THEN
                IF _PositiveLimitSwitch <> 0 AND_THEN _PositiveLimitSwitch.GetState() THEN
                    // Forward limit hit -> stop & clear command
                    bEnable := FALSE;
                    _bPosDirection := FALSE;
                    nVelocity := 0;
                ELSE
                    nVelocity := MAX_VELOCITY;
                END_IF

            ELSIF _bNegDirection THEN
                IF _NegativeLimitSwitch <> 0 AND_THEN _NegativeLimitSwitch.GetState() THEN
                    // Reverse limit hit -> stop & clear command
                    bEnable := FALSE;
                    _bNegDirection := FALSE;
                    nVelocity := 0;
                ELSE
                    nVelocity := -MAX_VELOCITY;
                END_IF
            ELSE
                // Enabled but no direction selected
                nVelocity := 0;
            END_IF
        ELSE
            // Not enabled -> motor at rest
            nVelocity := 0;
        END_IF
    END_IF

    // Update actual position based on limit switches
    IF _NegativeLimitSwitch <> 0 AND_THEN _NegativeLimitSwitch.GetState() THEN
        _ActualPosition := E_EL73x2_DcMotorPosition.NEGATIVE;
    ELSIF _PositiveLimitSwitch <> 0 AND_THEN _PositiveLimitSwitch.GetState() THEN
        _ActualPosition := E_EL73x2_DcMotorPosition.POSITIVE;
    ELSE
        _ActualPosition := E_EL73x2_DcMotorPosition.UNKNOWN;
    END_IF

    Execute := TRUE;
END_METHOD

    (* Get current position *)
METHOD PUBLIC GetPosition : E_EL73x2_DcMotorPosition
    GetPosition := _ActualPosition;
END_METHOD

    (* Get target position *)
METHOD PUBLIC GetTargetPosition : E_EL73x2_DcMotorPosition
    GetTargetPosition := _TargetPosition;
END_METHOD

    (* Move to negative limit *)
METHOD PUBLIC GoToNegLimit : BOOL
    bEnable := TRUE;
    _bNegDirection := TRUE;
    _bPosDirection := FALSE;
    _TargetPosition := E_EL73x2_DcMotorPosition.NEGATIVE;
    GoToNegLimit := TRUE;
END_METHOD

    (* Move to positive limit *)
METHOD PUBLIC GoToPosLimit : BOOL
    bEnable := TRUE;
    _bPosDirection := TRUE;
    _bNegDirection := FALSE;
    _TargetPosition := E_EL73x2_DcMotorPosition.POSITIVE;
    GoToPosLimit := TRUE;
END_METHOD

    (* Check for errors *)
METHOD PUBLIC HasError : BOOL
    HasError := stDcMotorStatus.Error;
END_METHOD

    (* Check if at negative limit *)
METHOD PUBLIC InNegLimit : BOOL
    IF _NegativeLimitSwitch <> 0 THEN
        InNegLimit := _NegativeLimitSwitch.GetState();
    ELSE
        InNegLimit := FALSE;
    END_IF
END_METHOD

    (* Check if at positive limit *)
METHOD PUBLIC InPosLimit : BOOL
    IF _PositiveLimitSwitch <> 0 THEN
        InPosLimit := _PositiveLimitSwitch.GetState();
    ELSE
        InPosLimit := FALSE;
    END_IF
END_METHOD

    (* Check if motor is moving *)
METHOD PUBLIC IsMoving : BOOL
    IsMoving := (stDcMotorStatus.MovingNegative = 1) OR (stDcMotorStatus.MovingPositive = 1);
END_METHOD

    (* Stop motor immediately *)
METHOD PUBLIC Stop : BOOL
    _bStop := TRUE;
    Stop := TRUE;
END_METHOD

END_FUNCTION_BLOCK

