// Shared base for all machine hardware providers.
// Owns the six subsystem interface pointers and exposes Init plus
// index-based bridge helpers called by derived classes via SUPER^.
//
// Usage in a derived class:
//   FUNCTION_BLOCK FB_HardwareProvider EXTENDS FB_HardwareProviderBase IMPLEMENTS I_HardwareProvider
FUNCTION_BLOCK FB_HardwareProviderBase
VAR
    _bInitialized             : BOOL := FALSE;
    _ipValveSubsystem         : I_ValveSubsystem;
    _ipPressureSubsystem      : I_PressureSubsystem;
    _ipFlowSubsystem          : I_FlowSubsystem;
    _ipDigitalOutputSubsystem : I_DigitalOutputSubsystem;
    _ipDigitalInputSubsystem  : I_DigitalInputSubsystem;
    _ipTemperatureSubsystem   : I_TemperatureSubsystem;
END_VAR

METHOD PUBLIC Init : BOOL
VAR_INPUT
    ipValveSubsystem         : I_ValveSubsystem;
    ipPressureSubsystem      : I_PressureSubsystem;
    ipFlowSubsystem          : I_FlowSubsystem;
    ipDigitalOutputSubsystem : I_DigitalOutputSubsystem;
    ipDigitalInputSubsystem  : I_DigitalInputSubsystem;
    ipTemperatureSubsystem   : I_TemperatureSubsystem;
END_VAR
    IF _bInitialized THEN
        Init := TRUE;
        RETURN;
    END_IF

    IF ipValveSubsystem         = 0 OR
       ipPressureSubsystem      = 0 OR
       ipFlowSubsystem          = 0 OR
       ipDigitalOutputSubsystem = 0 OR
       ipDigitalInputSubsystem  = 0 OR
       ipTemperatureSubsystem   = 0 THEN
        Init := FALSE;
        RETURN;
    END_IF

    _ipValveSubsystem         := ipValveSubsystem;
    _ipPressureSubsystem      := ipPressureSubsystem;
    _ipFlowSubsystem          := ipFlowSubsystem;
    _ipDigitalOutputSubsystem := ipDigitalOutputSubsystem;
    _ipDigitalInputSubsystem  := ipDigitalInputSubsystem;
    _ipTemperatureSubsystem   := ipTemperatureSubsystem;

    _bInitialized := TRUE;
    Init := TRUE;
END_METHOD

// ---------------------------------------------------------------------------
// INDEX-BASED BRIDGE HELPERS
// Derived classes call these via SUPER^.  Enum→INT implicit cast applies.
// ---------------------------------------------------------------------------

METHOD PUBLIC GetValveByIndex : I_Valve
VAR_INPUT
    nIndex : INT;
END_VAR
    GetValveByIndex := _ipValveSubsystem.GetValveByIndex(nIndex);
END_METHOD

METHOD PUBLIC GetOutputByIndex : I_DigitalOutput
VAR_INPUT
    nIndex : INT;
END_VAR
    GetOutputByIndex := _ipDigitalOutputSubsystem.GetOutputByIndex(nIndex);
END_METHOD

METHOD PUBLIC GetInputByIndex : I_DigitalInput
VAR_INPUT
    nIndex : INT;
END_VAR
    GetInputByIndex := _ipDigitalInputSubsystem.GetInputByIndex(nIndex);
END_METHOD

METHOD PUBLIC GetSensorByIndex : I_Sensor
VAR_INPUT
    nIndex : INT;
END_VAR
    GetSensorByIndex := _ipPressureSubsystem.GetSensorByIndex(nIndex);
END_METHOD

METHOD PUBLIC GetPressureControllerByIndex : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
    GetPressureControllerByIndex := _ipPressureSubsystem.GetControllerByIndex(nIndex);
END_METHOD

// Returns the pressure controller at nIndex as I_Sensor.
// Applies to EtherCAT and Modbus pressure controllers that implement both interfaces.
METHOD PUBLIC GetPressureControllerAsSensor : I_Sensor
VAR_INPUT
    nIndex : INT;
END_VAR
VAR
    ipCtrl   : I_Controller;
    ipSensor : I_Sensor;
END_VAR
    ipCtrl := _ipPressureSubsystem.GetControllerByIndex(nIndex);
    IF ipCtrl <> 0 AND_THEN __QUERYINTERFACE(ipCtrl, ipSensor) THEN
        GetPressureControllerAsSensor := ipSensor;
    END_IF
END_METHOD

METHOD PUBLIC GetFlowControllerByIndex : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
    GetFlowControllerByIndex := _ipFlowSubsystem.GetControllerByIndex(nIndex);
END_METHOD

// Returns the flow controller at nIndex as I_Sensor.
// MFC function blocks implement both I_Controller and I_Sensor.
METHOD PUBLIC GetFlowControllerAsSensor : I_Sensor
VAR_INPUT
    nIndex : INT;
END_VAR
VAR
    ipCtrl   : I_Controller;
    ipSensor : I_Sensor;
END_VAR
    ipCtrl := _ipFlowSubsystem.GetControllerByIndex(nIndex);
    IF ipCtrl <> 0 AND_THEN __QUERYINTERFACE(ipCtrl, ipSensor) THEN
        GetFlowControllerAsSensor := ipSensor;
    END_IF
END_METHOD

METHOD PUBLIC GetTemperatureControllerByIndex : I_Controller
VAR_INPUT
    nIndex : INT;
END_VAR
    GetTemperatureControllerByIndex := _ipTemperatureSubsystem.GetControllerByIndex(nIndex);
END_METHOD

METHOD PUBLIC GetTemperatureChannelByGlobalIndex : I_Controller
VAR_INPUT
    nGlobalIdx : INT;
END_VAR
    GetTemperatureChannelByGlobalIndex := _ipTemperatureSubsystem.GetChannelControllerByGlobalIndex(nGlobalIdx);
END_METHOD

END_FUNCTION_BLOCK
