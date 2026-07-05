// Manual control panel for pressure subsystem.
//
// Lib version — stateful FB; HMI binds to VAR fields directly.
// Switches between sensor view and controller view via bViewSensor.
FUNCTION_BLOCK FB_PressureManualPanel
VAR_INPUT
    ipSubsystem : I_PressureSubsystem;
    nMaxSensor  : INT;
    nMaxCtrl    : INT;
END_VAR
VAR
    // Mode Selection
    bViewSensor          : BOOL := TRUE;   // TRUE = sensor view, FALSE = controller view

    // User Inputs (HMI)
    nSelectedSensor      : INT := 1;
    nSelectedController  : INT := 1;
    rSelectedSetpoint    : LREAL := 0.0;
    bCmdApplySetpoint    : BOOL;
    bCmdEnable           : BOOL;
    bCmdDisable          : BOOL;
    bCmdReset            : BOOL;

    // Outputs
    stSelectedStatus     : ST_ControllerStatus;
    bLastCmdOk           : BOOL;
    bSelectedValid       : BOOL;
    bSubsystemAnyError   : BOOL;
    bSubsystemReady      : BOOL;

    ipSelectedSensor     : I_Sensor;
    ipSelectedController : I_Controller;
END_VAR

    IF ipSubsystem = 0 OR NOT ipSubsystem.IsInitialized() THEN
        bSubsystemReady    := FALSE;
        bSubsystemAnyError := FALSE;
        bSelectedValid     := FALSE;
        bCmdApplySetpoint  := FALSE;
        bCmdEnable         := FALSE;
        bCmdDisable        := FALSE;
        bCmdReset          := FALSE;
        RETURN;
    END_IF

    bSubsystemReady    := TRUE;
    bSubsystemAnyError := ipSubsystem.HasAnyError();

    ipSelectedSensor     := 0;
    ipSelectedController := 0;
    bSelectedValid       := FALSE;

    IF bViewSensor THEN
        IF nSelectedSensor >= 1 AND nSelectedSensor <= nMaxSensor THEN
            ipSelectedSensor := ipSubsystem.GetSensorByIndex(nSelectedSensor);
            bSelectedValid   := ipSelectedSensor <> 0;
        END_IF
    ELSE
        IF nSelectedController >= 1 AND nSelectedController <= nMaxCtrl THEN
            ipSelectedController := ipSubsystem.GetControllerByIndex(nSelectedController);
            bSelectedValid       := ipSelectedController <> 0;
        END_IF
    END_IF

    IF ipSelectedSensor <> 0 THEN
        stSelectedStatus.rMeasuredValue := ipSelectedSensor.GetMeasurement();
        stSelectedStatus.rSetpoint      := 0.0;
        stSelectedStatus.rOutput        := 0.0;
        stSelectedStatus.bEnabled       := TRUE;
        stSelectedStatus.bAtSetpoint    := FALSE;
        stSelectedStatus.stError        := ipSelectedSensor.GetError();
    ELSIF ipSelectedController <> 0 THEN
        stSelectedStatus := ipSelectedController.GetStatus();
    ELSE
        stSelectedStatus.rMeasuredValue   := 0.0;
        stSelectedStatus.rSetpoint        := 0.0;
        stSelectedStatus.rOutput          := 0.0;
        stSelectedStatus.bEnabled         := FALSE;
        stSelectedStatus.bAtSetpoint      := FALSE;
        stSelectedStatus.stError.eErrorId := E_ErrorId.NO_ERROR;
        stSelectedStatus.stError.sSource  := '';
        stSelectedStatus.stError.sMessage := '';
    END_IF

    IF bViewSensor OR ipSelectedController = 0 THEN
        bLastCmdOk        := FALSE;
        bCmdApplySetpoint := FALSE;
        bCmdEnable        := FALSE;
        bCmdDisable       := FALSE;
        bCmdReset         := FALSE;
        RETURN;
    END_IF

    bLastCmdOk := FALSE;

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
