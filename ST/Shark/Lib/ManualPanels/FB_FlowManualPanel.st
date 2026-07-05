// Manual control panel for flow subsystem.
//
// Lib version — stateful FB; HMI binds to VAR fields directly.
FUNCTION_BLOCK FB_FlowManualPanel
VAR_INPUT
    ipSubsystem : I_FlowSubsystem;
    nMaxZone    : INT;
END_VAR
VAR
    // User Inputs (HMI)
    nSelectedZone        : INT := 1;
    rSelectedSetpoint    : LREAL := 0.0;
    bCmdApplySetpoint    : BOOL;
    bCmdEnable           : BOOL;
    bCmdDisable          : BOOL;
    bCmdReset            : BOOL;

    // User Outputs (HMI)
    stSelectedStatus     : ST_ControllerStatus;
    bLastCmdOk           : BOOL;
    bSelectedZoneValid   : BOOL;
    bSubsystemAnyError   : BOOL;
    bSubsystemReady      : BOOL;

    ipSelectedController : I_Controller;
END_VAR

    IF ipSubsystem = 0 OR NOT ipSubsystem.IsInitialized() THEN
        bSubsystemReady    := FALSE;
        bSubsystemAnyError := FALSE;
        bSelectedZoneValid := FALSE;
        bCmdApplySetpoint  := FALSE;
        bCmdEnable         := FALSE;
        bCmdDisable        := FALSE;
        bCmdReset          := FALSE;
        RETURN;
    END_IF

    bSubsystemReady    := TRUE;
    bSubsystemAnyError := ipSubsystem.HasAnyError();

    ipSelectedController := 0;
    bSelectedZoneValid   := FALSE;

    IF nSelectedZone >= 1 AND nSelectedZone <= nMaxZone THEN
        ipSelectedController := ipSubsystem.GetControllerByIndex(nSelectedZone);
        bSelectedZoneValid   := ipSelectedController <> 0;
    END_IF

    IF ipSelectedController = 0 THEN
        bLastCmdOk                        := FALSE;
        bCmdApplySetpoint                 := FALSE;
        bCmdEnable                        := FALSE;
        bCmdDisable                       := FALSE;
        bCmdReset                         := FALSE;
        stSelectedStatus.rMeasuredValue   := 0.0;
        stSelectedStatus.rSetpoint        := 0.0;
        stSelectedStatus.rOutput          := 0.0;
        stSelectedStatus.bEnabled         := FALSE;
        stSelectedStatus.bAtSetpoint      := FALSE;
        stSelectedStatus.stError.eErrorId := E_ErrorId.NO_ERROR;
        stSelectedStatus.stError.sSource  := '';
        stSelectedStatus.stError.sMessage := '';
        RETURN;
    END_IF

    stSelectedStatus := ipSelectedController.GetStatus();
    bLastCmdOk       := FALSE;

    IF bCmdApplySetpoint THEN
        bLastCmdOk        := ipSelectedController.SetSetpoint(rSelectedSetpoint);
        bCmdApplySetpoint := FALSE;
    END_IF

    IF bCmdDisable THEN
        bLastCmdOk  := ipSelectedController.Enable(FALSE);
        bCmdDisable := FALSE;
        bCmdEnable  := FALSE;
    ELSIF bCmdEnable THEN
        bLastCmdOk := ipSelectedController.Enable(TRUE);
        bCmdEnable := FALSE;
    END_IF

    IF bCmdReset THEN
        bLastCmdOk := ipSelectedController.Reset();
        bCmdReset  := FALSE;
    END_IF

END_FUNCTION_BLOCK
