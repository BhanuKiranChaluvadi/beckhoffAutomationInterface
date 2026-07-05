// Manual control panel for digital-output subsystem.
//
// Lib version — stateful FB; HMI binds to VAR fields directly.
FUNCTION_BLOCK FB_DigitalOutputManualPanel
VAR_INPUT
    ipSubsystem : I_DigitalOutputSubsystem;
    nMaxOutput  : INT;
END_VAR
VAR
    // User Inputs (HMI)
    nSelectedOutput  : INT := 1;
    bCmdSetOn        : BOOL;
    bCmdSetOff       : BOOL;
    bCmdToggle       : BOOL;

    // User Outputs (HMI)
    bSelectedState   : BOOL;
    bSelectedValid   : BOOL;
    bLastCmdOk       : BOOL;
    bSubsystemReady  : BOOL;

    ipSelectedOutput : I_DigitalOutput;
END_VAR

    IF ipSubsystem = 0 OR NOT ipSubsystem.IsInitialized() THEN
        bSubsystemReady := FALSE;
        bSelectedValid  := FALSE;
        bSelectedState  := FALSE;
        bCmdSetOn       := FALSE;
        bCmdSetOff      := FALSE;
        bCmdToggle      := FALSE;
        RETURN;
    END_IF

    bSubsystemReady  := TRUE;
    ipSelectedOutput := 0;
    bSelectedValid   := FALSE;

    IF nSelectedOutput >= 1 AND nSelectedOutput <= nMaxOutput THEN
        ipSelectedOutput := ipSubsystem.GetOutputByIndex(nSelectedOutput);
        bSelectedValid   := ipSelectedOutput <> 0;
    END_IF

    IF NOT bSelectedValid OR ipSelectedOutput = 0 THEN
        bLastCmdOk     := FALSE;
        bSelectedState := FALSE;
        bCmdSetOn      := FALSE;
        bCmdSetOff     := FALSE;
        bCmdToggle     := FALSE;
        RETURN;
    END_IF

    bSelectedState := ipSelectedOutput.GetOutput();
    bLastCmdOk     := FALSE;

    IF bCmdSetOn THEN
        ipSelectedOutput.SetOutput(TRUE);
        bLastCmdOk := TRUE;
        bCmdSetOn  := FALSE;
    END_IF

    IF bCmdSetOff THEN
        ipSelectedOutput.SetOutput(FALSE);
        bLastCmdOk := TRUE;
        bCmdSetOff := FALSE;
    END_IF

    IF bCmdToggle THEN
        ipSelectedOutput.SetOutput(NOT bSelectedState);
        bLastCmdOk := TRUE;
        bCmdToggle := FALSE;
    END_IF

END_FUNCTION_BLOCK
