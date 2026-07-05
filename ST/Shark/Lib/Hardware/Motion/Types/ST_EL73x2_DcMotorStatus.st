TYPE ST_EL73x2_DcMotorStatus :
STRUCT
    ReadyToEnable  : BIT;
    Ready          : BIT;
    Warning        : BIT;
    Error          : BIT;
    MovingPositive : BIT;
    MovingNegative : BIT;
    TorqueReduced  : BIT;
    DigitalInput1  : BIT;
    DigitalInput2  : BIT;
    SyncError      : BIT;
    TxPDOToggle    : BIT;
END_STRUCT
END_TYPE
