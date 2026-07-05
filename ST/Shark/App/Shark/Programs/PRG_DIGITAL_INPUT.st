(*
Digital Input Program - Shark System
Manages all digital inputs with centralized initialization.
*)
PROGRAM PRG_DIGITAL_INPUT
VAR
    // Subsystem and Manual Panel
    fbDigitalInputSubsystem : FB_DigitalInputSubsystem;
    fbManualPanel           : FB_DigitalInputManualPanel;

    // Hardware IO - Door Proximity Sensors
    IO_DoorLT            AT %I* : BOOL;
    IO_DoorLM            AT %I* : BOOL;
    IO_DoorLB            AT %I* : BOOL;
    IO_DoorRT            AT %I* : BOOL;
    IO_DoorRM            AT %I* : BOOL;
    IO_DoorRB            AT %I* : BOOL;
    IO_DoorBML           AT %I* : BOOL;
    IO_DoorBMR           AT %I* : BOOL;
    IO_DoorBMB           AT %I* : BOOL;

    // Safety-derived digital inputs (logical, not physical IO).
    // Updated each cycle from FB_SafetyManager via Configure()/I_SafetyStatus.
    _bHeaterVoltOn   : BOOL := FALSE;  // HETR_VOLT_ON — HeaterVoltagePresence
    _bSafetyOk       : BOOL := FALSE;  // SAFE_STO_ON  — safety state machine OK
    _bSafetyOverride : BOOL := TRUE;   // SAFE_OVERRIDE — no physical switch on Shark; always active

    // Safety status interface (injected via Configure)
    _ipSafety : I_SafetyStatus;

    // Function blocks and interfaces
    afbInputs : ARRAY[1..GVL_DIOConfig.DI_COUNT] OF FB_DigitalInput;
    aipInputs : ARRAY[1..GVL_DIOConfig.DI_COUNT] OF I_DigitalInput;

    bInitialized : BOOL := FALSE;
END_VAR

    IF NOT bInitialized THEN
        _Init();
    END_IF

    // Refresh safety-derived inputs before Execute() so subsystem reads fresh values each scan
    IF _ipSafety <> 0 THEN
        _bHeaterVoltOn := _ipSafety.HeaterVoltagePresence;
        _bSafetyOk     := _ipSafety.IsOk;
        // _bSafetyOverride stays TRUE — Shark has no physical override switch
    END_IF

    fbDigitalInputSubsystem.Execute();
    fbManualPanel(ipSubsystem := fbDigitalInputSubsystem, nMaxInput := GVL_DIOConfig.DI_COUNT);

METHOD PRIVATE _Init
VAR
    i : INT;
    apIO : ARRAY[1..GVL_DIOConfig.DI_COUNT] OF POINTER TO BOOL;
END_VAR
    // Build IO pointer mapping
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_LT]    := ADR(IO_DoorLT);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_LM]    := ADR(IO_DoorLM);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_LB]    := ADR(IO_DoorLB);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_RT]    := ADR(IO_DoorRT);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_RM]    := ADR(IO_DoorRM);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_RB]    := ADR(IO_DoorRB);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_BML]   := ADR(IO_DoorBML);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_BMR]   := ADR(IO_DoorBMR);
    apIO[E_DigitalInputIdentifier.PRXS_DOOR_BMB]   := ADR(IO_DoorBMB);
    apIO[E_DigitalInputIdentifier.HETR_VOLT_ON]  := ADR(_bHeaterVoltOn);
    apIO[E_DigitalInputIdentifier.SAFE_STO_ON]   := ADR(_bSafetyOk);
    apIO[E_DigitalInputIdentifier.SAFE_OVERRIDE] := ADR(_bSafetyOverride);

    // Initialize all inputs using the mapping (all N.O. sensors, no inversion needed)
    FOR i := 1 TO GVL_DIOConfig.DI_COUNT DO
        IF apIO[i] <> 0 THEN
            afbInputs[i].Init(INT_TO_STRING(i), apIO[i]^);
            aipInputs[i] := afbInputs[i];
        END_IF
    END_FOR

    fbDigitalInputSubsystem.Init(aipInputs := aipInputs, nCount := GVL_DIOConfig.DI_COUNT);
    bInitialized := TRUE;
END_METHOD

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipSafety : I_SafetyStatus;
END_VAR
    IF ipSafety = 0 THEN
        Init := FALSE;
        RETURN;
    END_IF
    _ipSafety := ipSafety;
    Init := TRUE;
END_METHOD

END_PROGRAM
