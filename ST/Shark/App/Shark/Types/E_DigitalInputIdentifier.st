(*
Digital Input Identifier Enumeration - Shark System

Door proximity sensors from old E_DoorSensor / E_DIOIdentifier,
plus safety monitoring signals from E_DIOIdentifier.
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_DigitalInputIdentifier :
    (
        // Door Proximity Sensors
        PRXS_DOOR_LT           := 1,     // Door Sensor - Left Top
        PRXS_DOOR_LM           := 2,     // Door Sensor - Left Middle
        PRXS_DOOR_LB           := 3,     // Door Sensor - Left Bottom
        PRXS_DOOR_RT           := 4,     // Door Sensor - Right Top
        PRXS_DOOR_RM           := 5,     // Door Sensor - Right Middle
        PRXS_DOOR_RB           := 6,     // Door Sensor - Right Bottom
        PRXS_DOOR_BML          := 7,     // Door Sensor - Bottom Middle Left
        PRXS_DOOR_BMR          := 8,     // Door Sensor - Bottom Middle Right
        PRXS_DOOR_BMB          := 9,     // Door Sensor - Bottom Middle Back

        // Safety Monitoring
        HETR_VOLT_ON           := 10,    // Heater Voltage Enable
        SAFE_STO_ON            := 11,    // Safety STO - Heater Voltage Present
        SAFE_OVERRIDE          := 12     // Safety Override Status
    )INT;
END_TYPE
