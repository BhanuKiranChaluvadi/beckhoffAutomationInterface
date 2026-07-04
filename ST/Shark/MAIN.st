PROGRAM MAIN
VAR
	sHelloWorld 	: STRING 	:= 'Hello, World!'; 	// Sample greeting string
	nCounter		: DINT 		:= 0; 					// Increments once every PLC cycle
	fbMotor			: FB_Motor;							// Motor function block instance
	bInitDone		: BOOL 		:= FALSE;				// Latches once Init() has run
	bResetDone		: BOOL 		:= FALSE;				// Latches the result of the last Reset() call
END_VAR

// Increment the counter on every cycle
nCounter := nCounter + 1;

// Call Init() once on the first cycle
IF NOT bInitDone THEN
	bInitDone := fbMotor.Init();
END_IF

// Periodically reset the motor (demo trigger)
IF nCounter MOD 1000 = 0 THEN
	bResetDone := fbMotor.Reset();
END_IF

// Drive the motor and run its main cyclic logic
fbMotor(bStart := TRUE);
