(*
FB_SafetyManager — Safety state machine for Shark system.
*)
FUNCTION_BLOCK FB_SafetyManager IMPLEMENTS I_Errorable, I_SafetyStatus
VAR
    _eState              : E_SafetyState := E_SafetyState.ERROR;
    _nInitStep           : INT := 0;
    _bFirstCycle         : BOOL := TRUE;
    _bHeaterVoltagePresent : BOOL;

    // Edge triggers
    _rtNotSafe           : R_TRIG;
    _rtError             : R_TRIG;
    _rtVoltageOn         : R_TRIG;
    _ftVoltageOff        : F_TRIG;

    // Timers
    _tonSafetyStep       : TON;
    _tonStartupDelay     : TON;

    // Subsystem references (set via Init)
    _ipFlowSubsystem     : REFERENCE TO FB_FlowSubsystem;
    _bInitialized        : BOOL := FALSE;

    // Error surface (bridges TwinSAFE plane into the software error plane)
    _fbError             : FB_ErrorHandler;
END_VAR
VAR CONSTANT
    VOLTAGE_THRESHOLD    : REAL := 200.0;   // V — EL3443 heater voltage presence
    SAFETY_STEP_TIME     : TIME := T#100MS;
    STARTUP_DELAY        : TIME := T#5S;
END_VAR

IF NOT _bInitialized THEN
    RETURN;
END_IF

// --- Voltage monitoring ---
_bHeaterVoltagePresent := _CheckVoltage();
_rtVoltageOn(CLK := _bHeaterVoltagePresent);
_ftVoltageOff(CLK := _bHeaterVoltagePresent);

// --- Safety state machine ---
_tonSafetyStep.IN := TRUE;
_tonStartupDelay.IN := TRUE;
_tonStartupDelay(PT := STARTUP_DELAY);
_CheckSafetyStatus();

// --- Subsystem safety gating ---
IF NOT GVL_Simulation.bActive THEN
    IF _eState <> E_SafetyState.OK THEN
        // Disable heat zones when not safe (PRG_SHARK gates PRG_HEAT_ZONE behind SafetyOk)
        GVL_System.bGlobalSafetyOK := FALSE;
    ELSE
        GVL_System.bGlobalSafetyOK := TRUE;
    END_IF

    // Enable MFCs when voltage comes on
    IF _rtVoltageOn.Q THEN
        _EnableAllMFCs();
        GVL_ErrorLogger.Logger.LogInfo(
            sSource  := 'SafetyManager',
            sContext := 'heater voltage detected - MFCs enabled'
        );
    END_IF

    IF _ftVoltageOff.Q THEN
        GVL_ErrorLogger.Logger.LogInfo(
            sSource  := 'SafetyManager',
            sContext := 'heater voltage OFF'
        );
    END_IF
ELSE
    GVL_System.bGlobalSafetyOK := TRUE;
END_IF


METHOD PUBLIC Init : BOOL
VAR_INPUT
    refFlowSubsystem : REFERENCE TO FB_FlowSubsystem;
END_VAR
    IF NOT __ISVALIDREF(refFlowSubsystem) THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'SafetyManager.Init',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := 'invalid flow subsystem reference'
        );
        RETURN;
    END_IF

    _ipFlowSubsystem REF= refFlowSubsystem;
    _fbError.Init('SafetyManager');
    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD


METHOD PRIVATE _CheckSafetyStatus
    // Heartbeat — always signal running
    GVL_Safety.SafetyRunning := TRUE;

    // Detect loss of safe state
    _rtNotSafe(CLK := GVL_Safety.SafetyOk);
    IF _rtNotSafe.Q THEN
        _nInitStep := 0;
        _eState := E_SafetyState.ERROR;
    END_IF

    CASE _eState OF
        E_SafetyState.ERROR:
            // The state machine boots in ERROR while TwinSAFE is still negotiating,
            // so suppress the logged trip on the very first pass — otherwise every
            // startup emits a spurious 'safety error'. A genuine, persistent trip is
            // still surfaced by the gated _fbError.Raise('TwinSAFE not OK') below,
            // which only fires after STARTUP_DELAY.
            _rtError(CLK := _eState = E_SafetyState.ERROR);
            IF _rtError.Q AND NOT _bFirstCycle THEN
                GVL_ErrorLogger.Logger.LogError(
                    sSource  := 'SafetyManager',
                    eErrorId := E_ErrorId.ERR_CTRL_SAFETY_TRIPPED,
                    sContext := 'safety error'
                );
            END_IF
            _eState := E_SafetyState.INIT;
            _bFirstCycle := FALSE;

        E_SafetyState.INIT:
            CASE _nInitStep OF
                0:
                    // Step 1: Assert restart, clear acknowledge
                    GVL_Safety.SafetyErrorAcknowledge := FALSE;
                    GVL_Safety.SafetyRestart := TRUE;
                    IF _tonSafetyStep.Q THEN
                        _nInitStep := 1;
                        _tonSafetyStep.IN := FALSE;
                    END_IF

                1:
                    // Step 2: Assert error acknowledge
                    GVL_Safety.SafetyErrorAcknowledge := TRUE;
                    IF _tonSafetyStep.Q THEN
                        _nInitStep := 2;
                        _tonSafetyStep.IN := FALSE;
                    END_IF

                2:
                    // Step 3: Clear both, check if safe state restored
                    GVL_Safety.SafetyRestart := FALSE;
                    GVL_Safety.SafetyErrorAcknowledge := FALSE;
                    _nInitStep := 0;
                    IF GVL_Safety.SafetyOk THEN
                        _eState := E_SafetyState.OK;
                    END_IF
            END_CASE
            _tonSafetyStep(PT := SAFETY_STEP_TIME);

        E_SafetyState.WAIT_FOR_RESET_BTN:
            IF GVL_Safety.SafetyOk THEN
                _eState := E_SafetyState.OK;
            END_IF

        E_SafetyState.OK:
            IF _tonStartupDelay.Q THEN
                IF NOT GVL_Safety.SafetyOk THEN
                    _eState := E_SafetyState.ERROR;
                END_IF
            END_IF
    END_CASE

    // Simulation override
    IF GVL_Simulation.bActive THEN
        _eState := E_SafetyState.OK;
    END_IF

    // Surface state into the software error plane so subsystem aggregators
    // (FB_TemperatureSubsystem.stFirstError etc.) can see TwinSAFE trips.
    // Only raise after the startup delay elapses — otherwise every boot logs a
    // spurious trip while TwinSAFE is still negotiating.
    IF _eState = E_SafetyState.OK OR GVL_Simulation.bActive THEN
        IF _fbError.IsActive() THEN
            _fbError.Clear();
        END_IF
    ELSIF _tonStartupDelay.Q THEN
        _fbError.Raise(E_ErrorId.ERR_CTRL_SAFETY_TRIPPED, 'TwinSAFE not OK', E_TcEventSeverity.Critical);
    END_IF
END_METHOD


METHOD PRIVATE _CheckVoltage : BOOL
    IF GVL_Simulation.bActive THEN
        _CheckVoltage := TRUE;
        RETURN;
    END_IF
    _CheckVoltage := GVL_Safety.HeaterVoltage > VOLTAGE_THRESHOLD;
END_METHOD


METHOD PRIVATE _EnableAllMFCs
VAR
    i : INT;
    ipController : I_Controller;
END_VAR
    IF NOT __ISVALIDREF(_ipFlowSubsystem) THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'SafetyManager._EnableAllMFCs',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := 'flow subsystem reference invalid'
        );
        RETURN;
    END_IF

    FOR i := 1 TO GVL_FlowSystem.FLOW_ZONE_COUNT DO
        ipController := _ipFlowSubsystem.GetControllerByIndex(i);
        IF ipController <> 0 THEN
            ipController.Enable(TRUE);
        END_IF
    END_FOR
END_METHOD


PROPERTY PUBLIC HeaterVoltagePresence : BOOL
    GET
        HeaterVoltagePresence := _bHeaterVoltagePresent;
    END_GET
END_PROPERTY


PROPERTY PUBLIC State : E_SafetyState
    GET
        State := _eState;
    END_GET
END_PROPERTY


PROPERTY PUBLIC IsOk : BOOL
    GET
        IsOk := _eState = E_SafetyState.OK;
    END_GET
END_PROPERTY


// Single source of truth for "is it safe to energize heaters right now".
// Mirrors the gate previously duplicated in PRG_SHARK:
//   IF GVL_Simulation.bActive OR GVL_Safety.SafetyOk THEN PRG_HEAT_ZONE()
// Kept as a pure read (no side effects) so callers can poll safely each cycle.
PROPERTY PUBLIC IsHeatingAllowed : BOOL
    GET
        IsHeatingAllowed := GVL_Simulation.bActive OR GVL_Safety.SafetyOk;
    END_GET
END_PROPERTY

    (* I_Errorable implementation — exposes TwinSAFE state to subsystem error aggregators. *)
METHOD PUBLIC IsError : BOOL
    IsError := _fbError.IsActive();
END_METHOD

METHOD PUBLIC GetError : ST_Error
    GetError := _fbError.GetError();
END_METHOD

END_FUNCTION_BLOCK
