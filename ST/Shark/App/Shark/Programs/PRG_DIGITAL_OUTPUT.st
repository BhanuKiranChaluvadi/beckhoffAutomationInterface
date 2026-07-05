(*
Digital Output Program - Shark System
Manages all digital outputs with centralized initialization.
*)
PROGRAM PRG_DIGITAL_OUTPUT
VAR
    // Subsystem and Manual Panel
    fbDigitalOutputSubsystem : FB_DigitalOutputSubsystem;
    fbManualPanel            : FB_DigitalOutputManualPanel;

    // Hardware IO (Digital Outputs) - Signal Lights
    IO_SignalRed         AT %Q* : BOOL;
    IO_SignalYellow      AT %Q* : BOOL;
    IO_SignalGreen       AT %Q* : BOOL;

    // Hardware IO - Camera Lights
    IO_CameraLightTop    AT %Q* : BOOL;
    IO_CameraLightSide   AT %Q* : BOOL;

    // Hardware IO - Vacuum Pumps
    IO_ExhaustPump       AT %Q* : BOOL;
    IO_ChuckVacuumPump   AT %Q* : BOOL;

    // Hardware IO - Chiller
    IO_ChillerPower      AT %Q* : BOOL;

    // Hardware IO - Door Locks
    IO_DoorLockFL        AT %Q* : BOOL;
    IO_DoorLockFR        AT %Q* : BOOL;

    // Hardware IO - Heater Voltage Enable
    IO_HeaterVoltageOn   AT %Q* : BOOL;

    // Hardware IO - Solenoid Valve Outputs (consumed by PRG_VALVE)
    IO_ChuckLZ1          AT %Q* : BOOL;
    IO_ChuckLZ2          AT %Q* : BOOL;
    IO_ChuckLZ3          AT %Q* : BOOL;
    IO_ChuckVent         AT %Q* : BOOL;
    IO_ChuckWaterIn      AT %Q* : BOOL;
    IO_ChuckWaterOut     AT %Q* : BOOL;

    // Function blocks and interfaces
    afbOutputs : ARRAY[1..GVL_DIOConfig.DO_COUNT] OF FB_DigitalOutput;
    aipOutputs : ARRAY[1..GVL_DIOConfig.DO_COUNT] OF I_DigitalOutput;

    bInitialized : BOOL := FALSE;
END_VAR

    IF NOT bInitialized THEN
        _Init();
    END_IF

    fbDigitalOutputSubsystem.Execute();
    fbManualPanel(ipSubsystem := fbDigitalOutputSubsystem, nMaxOutput := GVL_DIOConfig.DO_COUNT);

METHOD PRIVATE _Init
VAR
    i : INT;
    apIO       : ARRAY[1..GVL_DIOConfig.DO_COUNT] OF POINTER TO BOOL;
    aSafeState : ARRAY[1..GVL_DIOConfig.DO_COUNT] OF BOOL;
END_VAR
    // ------------------------------------------------------------------
    // Safe states — applied by SetSafeState() on sequence abort / Halt().
    // Every output that is not listed here defaults to FALSE (de-energise).
    // SAFETY: door locks TRUE = stay locked; chiller TRUE = keep running.
    // ------------------------------------------------------------------
    aSafeState[E_DigitalOutputIdentifier.PUMP_AB_EXH]   := TRUE; 
    aSafeState[E_DigitalOutputIdentifier.LGHT_SGN_GRN]  := FALSE; // Lights off
    aSafeState[E_DigitalOutputIdentifier.LGHT_SGN_RED]  := FALSE;
    aSafeState[E_DigitalOutputIdentifier.LGHT_SGN_YEL]  := FALSE;
    aSafeState[E_DigitalOutputIdentifier.LGHT_CMR_TOP]  := TRUE;
    aSafeState[E_DigitalOutputIdentifier.LGHT_CMR_SIDE] := TRUE;
    aSafeState[E_DigitalOutputIdentifier.CHIL_PWR]      := TRUE;  // SAFETY: chiller must keep running to protect hardware
    aSafeState[E_DigitalOutputIdentifier.LOCK_DOOR_FL]  := TRUE;  // SAFETY: doors stay locked on abort
    aSafeState[E_DigitalOutputIdentifier.LOCK_DOOR_FR]  := TRUE;  // SAFETY: doors stay locked on abort
    aSafeState[E_DigitalOutputIdentifier.VALV_CHK_LZ1]  := FALSE; // Chuck zone valves close
    aSafeState[E_DigitalOutputIdentifier.VALV_CHK_LZ2]  := FALSE;
    aSafeState[E_DigitalOutputIdentifier.VALV_CHK_LZ3]  := FALSE;
    aSafeState[E_DigitalOutputIdentifier.VALV_CHK_VENT] := FALSE; // Vent closes
    aSafeState[E_DigitalOutputIdentifier.VALV_CHK_WIN]  := FALSE; // Water valves close
    aSafeState[E_DigitalOutputIdentifier.VALV_CHK_WOUT] := FALSE;

    // Build IO pointer mapping
    apIO[E_DigitalOutputIdentifier.LGHT_SGN_RED]   := ADR(IO_SignalRed);
    apIO[E_DigitalOutputIdentifier.LGHT_SGN_YEL]   := ADR(IO_SignalYellow);
    apIO[E_DigitalOutputIdentifier.LGHT_SGN_GRN]   := ADR(IO_SignalGreen);
    apIO[E_DigitalOutputIdentifier.LGHT_CMR_TOP]   := ADR(IO_CameraLightTop);
    apIO[E_DigitalOutputIdentifier.LGHT_CMR_SIDE]  := ADR(IO_CameraLightSide);
    apIO[E_DigitalOutputIdentifier.PUMP_AB_EXH]    := ADR(IO_ExhaustPump);
    apIO[E_DigitalOutputIdentifier.PUMP_CHK_VAC]   := ADR(IO_ChuckVacuumPump);
    apIO[E_DigitalOutputIdentifier.CHIL_PWR]       := ADR(IO_ChillerPower);
    apIO[E_DigitalOutputIdentifier.LOCK_DOOR_FL]   := ADR(IO_DoorLockFL);
    apIO[E_DigitalOutputIdentifier.LOCK_DOOR_FR]   := ADR(IO_DoorLockFR);
    apIO[E_DigitalOutputIdentifier.VALV_CHK_LZ1]   := ADR(IO_ChuckLZ1);
    apIO[E_DigitalOutputIdentifier.VALV_CHK_LZ2]   := ADR(IO_ChuckLZ2);
    apIO[E_DigitalOutputIdentifier.VALV_CHK_LZ3]   := ADR(IO_ChuckLZ3);
    apIO[E_DigitalOutputIdentifier.VALV_CHK_VENT]  := ADR(IO_ChuckVent);
    apIO[E_DigitalOutputIdentifier.VALV_CHK_WIN]   := ADR(IO_ChuckWaterIn);
    apIO[E_DigitalOutputIdentifier.VALV_CHK_WOUT]  := ADR(IO_ChuckWaterOut);

    // Initialize all outputs using the mapping
    FOR i := 1 TO GVL_DIOConfig.DO_COUNT DO
        IF apIO[i] <> 0 THEN
            afbOutputs[i].Init(INT_TO_STRING(i), apIO[i]^, aSafeState[i]);
            aipOutputs[i] := afbOutputs[i];
        END_IF
    END_FOR

    fbDigitalOutputSubsystem.Init(aipOutputs := aipOutputs, nCount := GVL_DIOConfig.DO_COUNT);
    bInitialized := TRUE;
END_METHOD

END_PROGRAM
