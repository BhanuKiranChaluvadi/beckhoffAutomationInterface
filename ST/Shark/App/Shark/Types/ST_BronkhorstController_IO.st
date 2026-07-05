TYPE ST_BronkhorstController_IO :
STRUCT
    // Inputs from device (EtherCAT PDO, 0-32000 raw counts)
    MeasuredValue     AT %I* : UINT;   // Input: Feedback (CoE fMeasure 2021:01)
    ActualSetpoint    AT %I* : UINT;   // Input: Acknowledged Setpoint (CoE fSetpoint 2021:04)
    ValveOutput       AT %I* : UDINT;  // Input: Valve Position (CoE ValveOutput 2072:02, 0-16777215)

    // Outputs to device (EtherCAT PDO, 0-32000 raw counts)
    CommandedSetpoint AT %Q* : UINT;   // Output: Target Setpoint (CoE fSetpoint 2021:04)
    ControlByte       AT %Q* : USINT;  // Output: Enable/Disable (CoE 2001:01)
    ControlMode       AT %Q* : USINT;  // Output: Control Mode (CoE 2001:05)
END_STRUCT
END_TYPE
