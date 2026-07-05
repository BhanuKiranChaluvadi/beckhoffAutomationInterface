// Manual control panel for digital-input subsystem.
//
// Lib version — stateful FB; HMI binds to VAR fields directly.
FUNCTION_BLOCK FB_DigitalInputManualPanel
VAR_INPUT
    ipSubsystem : I_DigitalInputSubsystem;
    nMaxInput   : INT;
END_VAR
VAR
    // User Inputs (HMI)
    nSelectedInput  : INT := 1;

    // User Outputs (HMI)
    bSelectedState  : BOOL;
    bSelectedValid  : BOOL;
    bSubsystemValid : BOOL;
    bSubsystemReady : BOOL;

    ipSelectedInput : I_DigitalInput;
END_VAR

    IF ipSubsystem = 0 OR NOT ipSubsystem.IsInitialized() THEN
        bSubsystemReady := FALSE;
        bSubsystemValid := FALSE;
        bSelectedValid  := FALSE;
        bSelectedState  := FALSE;
        RETURN;
    END_IF

    bSubsystemReady := TRUE;
    bSubsystemValid := TRUE;
    ipSelectedInput := 0;
    bSelectedValid  := FALSE;

    IF nSelectedInput >= 1 AND nSelectedInput <= nMaxInput THEN
        ipSelectedInput := ipSubsystem.GetInputByIndex(nSelectedInput);
        bSelectedValid  := ipSelectedInput <> 0;
    END_IF

    IF ipSelectedInput <> 0 THEN
        bSelectedState := ipSelectedInput.GetState();
    ELSE
        bSelectedState := FALSE;
    END_IF

END_FUNCTION_BLOCK
