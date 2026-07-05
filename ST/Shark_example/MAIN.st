PROGRAM MAIN
VAR
	sHelloWorld 	: STRING 	:= 'Hello, World!'; 	// Sample greeting string
	nCounter		: DINT 		:= 0; 					// Increments once every PLC cycle
	fbMotor			: FB_Motor;							// Motor function block instance
	ipMotor			: I_Motor;							// Interface reference to fbMotor, exercised via I_Motor
	bInitDone		: BOOL 		:= FALSE;				// Latches once Init() has run
	bResetDone		: BOOL 		:= FALSE;				// Latches the result of the last Reset() call
	bStopDone		: BOOL 		:= FALSE;				// Latches the result of the last Stop() call
END_VAR

// Increment the counter on every cycle
nCounter := nCounter + 1;

// Bind the interface reference to the FB instance (FB_Motor IMPLEMENTS I_Motor)
ipMotor := fbMotor;

// Call Init() once on the first cycle, via the interface
IF NOT bInitDone THEN
	bInitDone := ipMotor.Init();
END_IF

// Periodically reset the motor (demo trigger), via the interface
IF nCounter MOD 1000 = 0 THEN
	bResetDone := ipMotor.Reset();
END_IF

// Periodically stop the motor (demo trigger), calling the overridden
// FB_MotorBase.Stop directly on the instance (Stop is not part of I_Motor)
IF nCounter MOD 5000 = 0 THEN
	bStopDone := fbMotor.Stop();
END_IF

// Drive the motor and run its main cyclic logic
fbMotor(bStart := TRUE);
