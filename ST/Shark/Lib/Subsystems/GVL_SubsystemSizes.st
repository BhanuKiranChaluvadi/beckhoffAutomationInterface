{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // FB_DoorLock: maximum proximity sensors per lock actuator.
    MAX_SENSORS_PER_LOCK      : INT := 4;

    // FB_DigitalInputSubsystem: maximum registered digital inputs.
    MAX_DIGITAL_INPUTS        : INT := 128;

    // FB_DigitalOutputSubsystem: maximum registered digital outputs.
    MAX_DIGITAL_OUTPUTS       : INT := 128;

    // FB_FlowSubsystem: maximum MFC zones.
    MAX_FLOW_ZONES            : INT := 64;

    // FB_PressureSubsystem: maximum pressure sensors.
    MAX_PRESSURE_SENSORS      : INT := 32;

    // FB_PressureSubsystem: maximum pressure controllers.
    MAX_PRESSURE_CONTROLLERS  : INT := 32;

    // FB_ValveSubsystem: maximum valve instances.
    MAX_VALVES                : INT := 256;
END_VAR

