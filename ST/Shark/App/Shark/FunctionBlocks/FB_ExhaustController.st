(*
FB_ExhaustController — Exhaust pump controller.

Keeps the exhaust pump energised at all times and optionally verifies
operation via an isolation valve and pressure sensor.

Configuration variants (set at Init time):
  A. Valve before sensor (bIsoValveBeforeSensor=TRUE, Shark):
       PUMP_ON → OPENING_VALVE → WAIT_PRESSURE → READY
  B. Sensor before valve, or no valve (bIsoValveBeforeSensor=FALSE):
       PUMP_ON → WAIT_PRESSURE → READY   (valve not managed here)
  C. Sensor only, no valve:
       PUMP_ON → WAIT_PRESSURE → READY
  D. Neither:
       PUMP_ON → READY

Pump output is re-driven every scan cycle — it cannot be silenced by
Halt() calls or any other external writer.
*)
FUNCTION_BLOCK FB_ExhaustController
VAR CONSTANT
    PRESSURE_THRESHOLD : LREAL := 0.4;   // bar — minimum to confirm pump health
    ISO_VALVE_OPEN_TIMEOUT  : TIME  := T#10S; // maximum wait for IsOpen() to return TRUE
    PRESSURE_BUILD_TIMEOUT  : TIME  := T#60S; // maximum wait for pressure to reach threshold
END_VAR
VAR
    _ipExhaustPump          : I_DigitalOutput;
    _ipIsoValve             : I_Valve;
    _ipPressureSensor       : I_Sensor;
    _bIsoValveBeforeSensor : BOOL;
    _eState                : E_ExhaustState := E_ExhaustState.INIT;
    _tonIsoValveOpen       : TON;
    _tonPressureBuild      : TON;
    _bInitialized           : BOOL := FALSE;
END_VAR

    IF NOT _bInitialized THEN
        RETURN;
    END_IF

    // Re-drive every scan — prevents any external SetOutput(FALSE) from lasting more than one cycle
    _ipExhaustPump.SetOutput(TRUE);

    CASE _eState OF

        E_ExhaustState.INIT:
            _eState := E_ExhaustState.PUMP_ON;

        E_ExhaustState.PUMP_ON:
            IF _ipIsoValve <> 0 AND_THEN _bIsoValveBeforeSensor THEN
                _eState := E_ExhaustState.OPENING_VALVE;
            ELSIF _ipPressureSensor <> 0 THEN
                _eState := E_ExhaustState.WAIT_PRESSURE;
            ELSE
                _eState := E_ExhaustState.READY;
            END_IF

        E_ExhaustState.OPENING_VALVE:
            _ipIsoValve.Open();
            _tonIsoValveOpen(IN := TRUE, PT := ISO_VALVE_OPEN_TIMEOUT);
            IF _ipIsoValve.IsOpen() THEN
                _tonIsoValveOpen(IN := FALSE);
                IF _ipPressureSensor <> 0 THEN
                    _eState := E_ExhaustState.WAIT_PRESSURE;
                ELSE
                    _eState := E_ExhaustState.READY;
                END_IF
            ELSIF _tonIsoValveOpen.Q THEN
                GVL_ErrorLogger.Logger.LogError(
                    sSource  := 'ExhaustController',
                    eErrorId := E_ErrorId.ERR_VALVE_FEEDBACK_TIMEOUT,
                    sContext := 'iso valve did not open within timeout',
                    fCurrentValue  := TO_REAL(TIME_TO_DWORD(_tonIsoValveOpen.ET)) / 1000.0,
                    fExpectedValue := TO_REAL(TIME_TO_DWORD(ISO_VALVE_OPEN_TIMEOUT)) / 1000.0,
                    sUnits := 's'
                );
                _eState := E_ExhaustState.ERROR;
            END_IF

        E_ExhaustState.WAIT_PRESSURE:
            _tonPressureBuild(IN := TRUE, PT := PRESSURE_BUILD_TIMEOUT);
            IF _ipPressureSensor.GetMeasurement() >= PRESSURE_THRESHOLD THEN
                _tonPressureBuild(IN := FALSE);
                _eState := E_ExhaustState.READY;
            ELSIF _tonPressureBuild.Q THEN
                GVL_ErrorLogger.Logger.LogError(
                    sSource  := 'ExhaustController',
                    eErrorId := E_ErrorId.ERR_GEN_TIMEOUT,
                    sContext := 'pressure threshold not reached',
                    fCurrentValue  := TO_REAL(_ipPressureSensor.GetMeasurement()),
                    fExpectedValue := TO_REAL(PRESSURE_THRESHOLD),
                    sUnits := 'bar'
                );
                _eState := E_ExhaustState.ERROR;
            END_IF

        E_ExhaustState.READY:
            ; // Pump always on — nothing to monitor

        E_ExhaustState.ERROR:
            ; // Stays here until Reset()

    END_CASE


METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipExhaustPump          : I_DigitalOutput;
    ipIsoValve             : I_Valve  := 0;    // Optional — 0 if machine has no iso valve
    ipPressureSensor       : I_Sensor := 0;    // Optional — 0 if machine has no pressure sensor
    bIsoValveBeforeSensor  : BOOL     := FALSE; // TRUE = iso valve sits between pump and sensor (Shark);
                                                 // pressure is not meaningful until valve is open
END_VAR
    IF ipExhaustPump = 0 THEN
        GVL_ErrorLogger.Logger.LogError(
            sSource  := 'ExhaustController.Init',
            eErrorId := E_ErrorId.ERR_GEN_NULL_POINTER,
            sContext := 'null pump interface'
        );
        RETURN;
    END_IF
    _ipExhaustPump          := ipExhaustPump;
    _ipIsoValve             := ipIsoValve;
    _ipPressureSensor       := ipPressureSensor;
    _bIsoValveBeforeSensor  := bIsoValveBeforeSensor;
    _bInitialized           := TRUE;
    _eState                 := E_ExhaustState.INIT;
    Init := TRUE;
END_METHOD


METHOD PUBLIC Reset
    IF _bInitialized THEN
        _tonIsoValveOpen(IN := FALSE);
        _tonPressureBuild(IN := FALSE);
        IF _ipIsoValve <> 0 THEN
            _ipIsoValve.Close();
        END_IF
        _eState := E_ExhaustState.INIT;
    END_IF
END_METHOD


PROPERTY PUBLIC IsReady : BOOL
    GET
        IsReady := _eState = E_ExhaustState.READY;
    END_GET
END_PROPERTY


PROPERTY PUBLIC State : E_ExhaustState
    GET
        State := _eState;
    END_GET
END_PROPERTY

END_FUNCTION_BLOCK
