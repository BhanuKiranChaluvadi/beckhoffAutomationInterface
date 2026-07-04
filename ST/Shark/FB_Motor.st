FUNCTION_BLOCK FB_Motor
VAR_INPUT
	bStart 	: BOOL;				// Start command
END_VAR
VAR_OUTPUT
	eState 		: E_MotorState;		// Current motor state
	stStatus 	: ST_MotorStatus;	// Current motor status
END_VAR
VAR
	fSpeed 	: LREAL := 0;		// Current speed, ramped up while running
END_VAR

// Main cyclic logic of the motor function block
IF bStart THEN
	eState 	:= E_MotorState.Running;
	fSpeed 	:= F_ClampSpeed(fSpeed := fSpeed + 1.0, fMax := 100.0);
ELSE
	eState 	:= E_MotorState.Idle;
	fSpeed 	:= 0;
END_IF

stStatus.bRunning 	:= (eState = E_MotorState.Running);
stStatus.nErrorCode := 0;

METHOD Init : BOOL
VAR_INPUT
END_VAR

// One-time initialization, called once from MAIN on the first cycle
fSpeed 	:= 0;
eState 	:= E_MotorState.Idle;
Init 	:= TRUE;

METHOD Reset : BOOL
VAR_INPUT
END_VAR

// Clears speed, state, and status back to defaults
fSpeed 				:= 0;
eState 				:= E_MotorState.Idle;
stStatus.bRunning 	:= FALSE;
stStatus.nErrorCode := 0;
Reset 				:= TRUE;
