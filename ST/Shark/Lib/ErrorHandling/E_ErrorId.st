(* Unified error code enumeration for the library. *)
{attribute 'qualified_only'}
{attribute 'strict'}
{attribute 'to_string'}
TYPE E_ErrorId :
    (
        NO_ERROR := 0,

        // General / System (1000+)
        ERR_GEN_INTERNAL                := 1000,
        ERR_GEN_INVALID_PARAM           := 1001,
        ERR_GEN_NULL_POINTER            := 1002,
        ERR_GEN_TIMEOUT                 := 1003,
        ERR_GEN_NOT_IMPLEMENTED         := 1004,
        ERR_GEN_INITIALIZATION          := 1005,
        ERR_GEN_NOT_CONFIGURED          := 1006, // POU executed before Configure() was called

        ERR_HW_SENSOR_OPEN              := 2000, // Wire break / Open circuit
        ERR_HW_SENSOR_SHORT             := 2001, // Short circuit
        ERR_HW_SENSOR_OVERRANGE         := 2002, // Value > Max physical limit
        ERR_HW_SENSOR_UNDERRANGE        := 2003, // Value < Min physical limit
        ERR_HW_OUTPUT_FAULT             := 2010, // Digital/Analog output failure
        ERR_HW_POWER_SUPPLY             := 2020,
        ERR_HW_MOTION_FAULT             := 2030, // Axis motion fault (homing/move/stop) — phase in context

        ERR_CTRL_LIMIT_EXCEEDED         := 3000, // Process Value > Safety Limit
        ERR_CTRL_DEVIATION_HIGH         := 3001, // PV deviates from SP > Tolerance
        ERR_CTRL_WATCHDOG_HEATING       := 3002, // Heating active but Temp dropping
        ERR_CTRL_WATCHDOG_COOLING       := 3003, // Cooling active but Temp rising
        ERR_CTRL_TUNING_ERROR           := 3004,
        ERR_CTRL_FAULT                  := 3005, // Controller reported a generic fault
        ERR_CTRL_SAFETY_TRIPPED         := 3006, // TwinSAFE / system-level safety not OK

        ERR_COMM_PORT_CLOSED            := 4000, // Serial port not open
        ERR_COMM_TIMEOUT                := 4001, // No response from slave
        ERR_COMM_CRC_ERROR              := 4002, // Checksum mismatch
        ERR_COMM_BUFFER_OVERFLOW        := 4003, // Receive buffer full
        ERR_COMM_FRAME_ERROR            := 4004, // Invalid frame format
        ERR_COMM_PROTOCOL               := 4005, // Protocol-level error
        ERR_COMM_INVALID_RESPONSE       := 4006, // Invalid or unexpected response
        ERR_COMM_DATA_CONVERSION        := 4007, // Data conversion failed
        ERR_COMM_MODBUS_EXCEPTION       := 4010, // Slave returned Modbus Exception
        ERR_COMM_ETHERCAT_STATE         := 4020,

        ERR_DEV_BRONKHORST_FAULT        := 5000, // Bronkhorst specific error bit
        ERR_DEV_HARDWARE_ERROR          := 5030, // Generic device hardware error
        ERR_DEVICE_NACK                 := 5040, // Device returned NACK

        ERR_VALVE_NOT_CONFIGURED        := 6000, // Valve not configured
        ERR_VALVE_INVALID_INDEX         := 6001, // Invalid output index
        ERR_VALVE_FEEDBACK_TIMEOUT      := 6002, // Feedback not received in time
        ERR_VALVE_FEEDBACK_MISMATCH     := 6003, // Feedback doesn't match command

        // Behavior Tree Errors (7000+)
        ERR_BT_NULL_INTERFACE           := 7000, // Interface not configured
        ERR_BT_NULL_CHILD               := 7001, // Child node not set
        ERR_BT_CHILD_FAILED             := 7002, // Child node returned failure
        ERR_BT_SEQUENCE_FAILED          := 7003, // Sequence node failed
        ERR_BT_SENSOR_ERROR             := 7004, // Sensor reported error
        ERR_BT_VALVE_ERROR              := 7005, // Valve reported error
        ERR_BT_CONDITION_FAILED         := 7006, // Condition check failed
        ERR_BT_TIMEOUT                  := 7007, // Operation timed out
        ERR_BT_MAX_RETRIES              := 7008, // Maximum retry attempts exceeded
        ERR_BT_MAX_CHILDREN             := 7009, // Maximum children exceeded
        ERR_BT_PARALLEL_FAILED          := 7010, // Parallel node failure threshold reached
        ERR_BT_FORCED_FAILURE           := 7011, // FB_AlwaysFailureNode / ForceFailure decorator
        ERR_BT_CONDITION_NOT_CONFIGURED := 7012  // FB_ConditionNode reference not set
    ) UDINT;
END_TYPE
