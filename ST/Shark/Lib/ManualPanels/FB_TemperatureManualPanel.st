// Manual control panel for temperature subsystem.
//
// Lib version — stateful FB; HMI binds to VAR fields directly.
// Default setpoint is 0.0; caller can override via rSelectedSetpoint VAR field.
FUNCTION_BLOCK FB_TemperatureManualPanel
VAR_INPUT
    ipSubsystem : I_TemperatureSubsystem;
    nMaxZone    : INT;
END_VAR
VAR
    // User Inputs - Selection
    nSelectedZone           : INT := 1;
    bSelectWholeZone        : BOOL := TRUE;   // TRUE = control whole zone, FALSE = single channel
    nSelectedChannelIdx     : INT := 1;       // 1..nSelectedZoneChannelCount (channel mode)
    rSelectedSetpoint       : LREAL := 0.0;
    bCmdApplySetpoint       : BOOL;
    bCmdEnable              : BOOL;
    bCmdDisable             : BOOL;
    bCmdReset               : BOOL;

    // User Outputs
    stSelectedStatus        : ST_ControllerStatus;
    bLastCmdOk              : BOOL;
    bSelectedValid          : BOOL;
    bIsControllable         : BOOL;
    nSelectedZoneChannelCount : INT;          // channels in the selected zone (HMI bound)
    bSubsystemAnyError      : BOOL;
    bSubsystemAllAtSetpoint : BOOL;
    bSubsystemReady         : BOOL;
    stFirstError            : ST_Error;

    ipSelectedSensor     : I_Sensor;
    ipSelectedController : I_Controller;
END_VAR

    IF ipSubsystem = 0 OR NOT ipSubsystem.IsInitialized() THEN
        bSubsystemReady         := FALSE;
        bSubsystemAnyError      := FALSE;
        bSubsystemAllAtSetpoint := FALSE;
        bSelectedValid          := FALSE;
        bIsControllable         := FALSE;
        nSelectedZoneChannelCount := 0;
        stFirstError.eErrorId   := E_ErrorId.NO_ERROR;
        bCmdApplySetpoint       := FALSE;
        bCmdEnable              := FALSE;
        bCmdDisable             := FALSE;
        bCmdReset               := FALSE;
        RETURN;
    END_IF

    bSubsystemReady         := TRUE;
    bSubsystemAnyError      := ipSubsystem.HasAnyError();
    bSubsystemAllAtSetpoint := ipSubsystem.AllAtSetpoint();
    stFirstError            := ipSubsystem.GetFirstError();

    ipSelectedSensor     := 0;
    ipSelectedController := 0;
    bSelectedValid       := FALSE;
    bIsControllable      := FALSE;
    nSelectedZoneChannelCount := 0;

    IF nSelectedZone >= 1 AND nSelectedZone <= nMaxZone THEN
        nSelectedZoneChannelCount := ipSubsystem.GetChannelCountByIndex(nSelectedZone);
        IF bSelectWholeZone THEN
            ipSelectedSensor     := ipSubsystem.GetTemperatureByIndex(nSelectedZone);
            ipSelectedController := ipSubsystem.GetControllerByIndex(nSelectedZone);
        ELSE
            ipSelectedController := ipSubsystem.GetChannelControllerByZoneIndex(nSelectedZone, nSelectedChannelIdx);
        END_IF
    END_IF

    bSelectedValid  := (ipSelectedSensor <> 0) OR (ipSelectedController <> 0);
    bIsControllable := ipSelectedController <> 0;

    IF ipSelectedController <> 0 THEN
        stSelectedStatus := ipSelectedController.GetStatus();
    ELSIF ipSelectedSensor <> 0 THEN
        stSelectedStatus.rMeasuredValue := ipSelectedSensor.GetMeasurement();
        stSelectedStatus.rSetpoint      := 0.0;
        stSelectedStatus.rOutput        := 0.0;
        stSelectedStatus.bEnabled       := FALSE;
        stSelectedStatus.bAtSetpoint    := FALSE;
        stSelectedStatus.stError        := ipSelectedSensor.GetError();
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

    IF ipSelectedController = 0 THEN
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
