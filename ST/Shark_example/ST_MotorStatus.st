{attribute 'pack_mode' := '1'}
TYPE ST_MotorStatus :
STRUCT
	bRunning 	: BOOL;		// TRUE while the motor is running
	nErrorCode 	: UDINT;	// Non-zero when the motor has faulted
END_STRUCT
END_TYPE
