// Manual control panel for valve subsystem.
//
// Lib version — stateful FB; HMI binds to VAR fields directly.
// Subsystem is injected via the interface; max-id is supplied by the caller.
FUNCTION_BLOCK FB_ValveManualPanel
VAR_INPUT
    ipSubsystem : I_ValveSubsystem;
    nMaxValveId : INT;
END_VAR
VAR
    // User Inputs (HMI)
    nSelectedValveId    : INT := 1;
    bCmdOpen            : BOOL;
    bCmdClose           : BOOL;
    bCmdReset           : BOOL;

    // User Outputs (HMI)
    bLastCmdOk          : BOOL;
    bSelectedValveValid : BOOL;
    bIsOpen             : BOOL;
    bSubsystemAnyError  : BOOL;
    bSubsystemReady     : BOOL;

    ipSelectedValve     : I_Valve;
END_VAR

    IF ipSubsystem = 0 OR NOT ipSubsystem.IsInitialized() THEN
        bSubsystemReady     := FALSE;
        bSubsystemAnyError  := FALSE;
        bSelectedValveValid := FALSE;
        bIsOpen             := FALSE;
        bLastCmdOk          := FALSE;
        bCmdOpen            := FALSE;
        bCmdClose           := FALSE;
        bCmdReset           := FALSE;
        RETURN;
    END_IF

    bSubsystemReady    := TRUE;
    bSubsystemAnyError := ipSubsystem.HasAnyError();

    ipSelectedValve     := 0;
    bSelectedValveValid := FALSE;

    IF nSelectedValveId >= 1 AND nSelectedValveId <= nMaxValveId THEN
        ipSelectedValve     := ipSubsystem.GetValveByIndex(nSelectedValveId);
        bSelectedValveValid := ipSelectedValve <> 0;
    END_IF

    IF ipSelectedValve = 0 THEN
        bIsOpen    := FALSE;
        bLastCmdOk := FALSE;
        bCmdOpen   := FALSE;
        bCmdClose  := FALSE;
        bCmdReset  := FALSE;
        RETURN;
    END_IF

    bIsOpen    := ipSelectedValve.IsOpen();
    bLastCmdOk := FALSE;

    IF bCmdOpen THEN
        ipSelectedValve.Open();
        bLastCmdOk := TRUE;
        bCmdOpen   := FALSE;
    END_IF

    IF bCmdClose THEN
        ipSelectedValve.Close();
        bLastCmdOk := TRUE;
        bCmdClose  := FALSE;
    END_IF

    IF bCmdReset THEN
        ipSelectedValve.Reset();
        bLastCmdOk := TRUE;
        bCmdReset  := FALSE;
    END_IF

END_FUNCTION_BLOCK
