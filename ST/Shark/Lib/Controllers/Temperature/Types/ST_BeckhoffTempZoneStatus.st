(* Heating Zone Status Data Structure. *)
TYPE ST_BeckhoffTempZoneStatus EXTENDS ST_ControllerStatus :
STRUCT
    // Zone Identification
    sZoneID             : STRING(20);       // Unique zone identifier for multi-zone systems

    // Operational Status
    eCtrlState          : E_CTRL_STATE;     // Current controller state (IDLE/TUNING/TUNED/ACTIVE)
    rSensorDeviation    : LREAL;            // Temperature deviation between dual sensors (°C), 0 if single sensor
    bHeaterActive       : BOOL;             // TRUE when heater output is energized
    rPWMOutput          : LREAL;            // Heater PWM duty cycle (0.0-100.0%)

    // Safety Status
    bCanResetSafety     : BOOL;             // TRUE when safety trip can be reset by operator
    sBlockReason        : STRING(80);       // Human-readable reason for reset block (empty if resettable)

    // Component Health
    bSensorOK           : BOOL;             // TRUE when all temperature sensors within valid range
    bControllerOK       : BOOL;             // TRUE when controller logic executing without errors
    bHealthy            : BOOL;             // TRUE when all components operational (sensors AND controller)
END_STRUCT
END_TYPE
