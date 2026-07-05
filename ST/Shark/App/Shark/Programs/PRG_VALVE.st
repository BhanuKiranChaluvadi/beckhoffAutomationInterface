(*
Valve Program - Shark System
1 SMC block (4 bytes) for 29 pneumatic valves (1-29).
6 solenoid valves (30-35) via injected I_DigitalOutput from PRG_DIGITAL_OUTPUT.
*)
PROGRAM PRG_VALVE
VAR
    // Subsystem and Manual Panel
    fbValveSubsystem : FB_ValveSubsystem;
    fbManualPanel    : FB_ValveManualPanel;

    // SMC EtherCAT Output Block (1 block, 4 bytes = 32 bits, uses bits 0-28)
    IO_SMC_Outputs AT %Q* : ARRAY[0..3] OF BYTE;
    fbSMC : FB_SMCOutputBlock;

    // SMC Adapters for Pneumatic Valves (1-29)
    afbSMCAdapters : ARRAY[1..GVL_ValveSystem.PNEUMATIC_VALVE_COUNT] OF FB_SMCAdapter;

    // Universal Valve Array
    afbValves : ARRAY[1..GVL_ValveSystem.VALVE_COUNT] OF FB_Valve;
    aipValves : ARRAY[1..GVL_ValveSystem.VALVE_COUNT] OF I_Valve;

    // Injected interfaces for solenoid valves (30-35)
    _ipChuckLZ1  : I_DigitalOutput;
    _ipChuckLZ2  : I_DigitalOutput;
    _ipChuckLZ3  : I_DigitalOutput;
    _ipChuckVent : I_DigitalOutput;
    _ipChuckWIn  : I_DigitalOutput;
    _ipChuckWOut : I_DigitalOutput;

    bConfigured     : BOOL := FALSE;
    bInitialized    : BOOL := FALSE;
    _bNotConfiguredLogged : BOOL := FALSE;
    i : INT;
END_VAR

    IF NOT bConfigured THEN
        IF NOT _bNotConfiguredLogged THEN
            GVL_ErrorLogger.Logger.LogError(
                sSource  := 'PRG_VALVE',
                eErrorId := E_ErrorId.ERR_GEN_NOT_CONFIGURED,
                sContext := 'PRG_VALVE executed before Configure(); valve program will not run'
            );
            _bNotConfiguredLogged := TRUE;
        END_IF
        RETURN;
    END_IF

    IF NOT bInitialized THEN
        _Init();
    END_IF

    // Execute SMC block
    fbSMC(aOutputBytes := IO_SMC_Outputs);

    // Execute valve logic
    FOR i := 1 TO GVL_ValveSystem.VALVE_COUNT DO
        afbValves[i].Execute();
    END_FOR

    // Update subsystem status
    fbValveSubsystem.Execute();
    fbManualPanel(ipSubsystem := fbValveSubsystem, nMaxValveId := GVL_ValveSystem.VALVE_COUNT);

METHOD PUBLIC Configure
VAR_INPUT
    ipChuckLZ1  : I_DigitalOutput;
    ipChuckLZ2  : I_DigitalOutput;
    ipChuckLZ3  : I_DigitalOutput;
    ipChuckVent : I_DigitalOutput;
    ipChuckWIn  : I_DigitalOutput;
    ipChuckWOut : I_DigitalOutput;
END_VAR
    IF bConfigured THEN RETURN;
    END_IF

    _ipChuckLZ1  := ipChuckLZ1;
    _ipChuckLZ2  := ipChuckLZ2;
    _ipChuckLZ3  := ipChuckLZ3;
    _ipChuckVent := ipChuckVent;
    _ipChuckWIn  := ipChuckWIn;
    _ipChuckWOut := ipChuckWOut;

    bConfigured := TRUE;
END_METHOD

METHOD PRIVATE _Init
VAR
    iLocal   : INT;
END_VAR
    // Default: leave all valves unbound
    FOR iLocal := 1 TO GVL_ValveSystem.VALVE_COUNT DO
        aipValves[iLocal] := 0;
    END_FOR

    // Execute SMC block once to capture byte-array address, then clear
    fbSMC(aOutputBytes := IO_SMC_Outputs);
    fbSMC.ClearAll();

    // Initialize Pneumatic Valves (1-29) on SMC Block
    FOR iLocal := 1 TO GVL_ValveSystem.PNEUMATIC_VALVE_COUNT DO
        afbSMCAdapters[iLocal].Init(refBlock := fbSMC, nIndex := INT_TO_USINT(iLocal - 1));
        afbValves[iLocal].Init(sInstanceName := INT_TO_STRING(iLocal), ipOutput := afbSMCAdapters[iLocal]);
        aipValves[iLocal] := afbValves[iLocal];
    END_FOR

    // Initialize Solenoid Valves (30-35) using injected digital outputs
    _InitInjectedValve(E_ValveIdentifier.VALV_CHK_LZ1,  _ipChuckLZ1);
    _InitInjectedValve(E_ValveIdentifier.VALV_CHK_LZ2,  _ipChuckLZ2);
    _InitInjectedValve(E_ValveIdentifier.VALV_CHK_LZ3,  _ipChuckLZ3);
    _InitInjectedValve(E_ValveIdentifier.VALV_CHK_VENT, _ipChuckVent);
    _InitInjectedValve(E_ValveIdentifier.VALV_CHK_WIN,  _ipChuckWIn);
    _InitInjectedValve(E_ValveIdentifier.VALV_CHK_WOUT, _ipChuckWOut);

    fbValveSubsystem.Init(aipValves := aipValves, nCount := GVL_ValveSystem.VALVE_COUNT);
    bInitialized := TRUE;
END_METHOD

METHOD PRIVATE _InitInjectedValve
VAR_INPUT
    eValve   : E_ValveIdentifier;
    ipOutput : I_DigitalOutput;
END_VAR
    IF ipOutput = 0 THEN RETURN; END_IF

    afbValves[eValve].Init(sInstanceName := TO_STRING(eValve), ipOutput := ipOutput);
    aipValves[eValve] := afbValves[eValve];
END_METHOD

END_PROGRAM
