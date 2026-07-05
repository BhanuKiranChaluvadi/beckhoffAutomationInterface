FUNCTION_BLOCK FB_Motor EXTENDS FB_MotorBase IMPLEMENTS I_Motor
VAR_INPUT
	bStart 	: BOOL;				// Start command
END_VAR
VAR_OUTPUT
	eState 		: E_MotorState;		// Current motor state
	stStatus 	: ST_MotorStatus;	// Current motor status
END_VAR
VAR
	fSpeed 	: T_MotorSpeed := 0;	// Current speed, ramped up while running
END_VAR

// Main cyclic logic of the motor function block
IF bStart THEN
	eState 	:= E_MotorState.Running;
	fSpeed 	:= CalculateRampedSpeed();
ELSE
	eState 	:= E_MotorState.Idle;
	fSpeed 	:= 0;
END_IF

// Drive the physical enable output from the start command, and only report
// "Running" once the physical run-sensor input confirms it
GVL_Shark.bMotorEnableOutput 	:= bStart;
stStatus.bRunning 				:= (eState = E_MotorState.Running) AND GVL_Shark.bMotorRunSensor;
stStatus.nErrorCode := 0;

METHOD PUBLIC Init : BOOL
VAR_INPUT
END_VAR

// One-time initialization, called once from MAIN on the first cycle
fSpeed 	:= 0;
eState 	:= E_MotorState.Idle;
Init 	:= TRUE;

METHOD PUBLIC Reset : BOOL
VAR_INPUT
END_VAR

// Clears speed, state, and status back to defaults
fSpeed 				:= 0;
eState 				:= E_MotorState.Idle;
stStatus.bRunning 	:= FALSE;
stStatus.nErrorCode := 0;
Reset 				:= TRUE;

METHOD PUBLIC Stop : BOOL
VAR_INPUT
END_VAR

// Overrides the abstract FB_MotorBase.Stop \u2014 forces the motor back to idle
eState 				:= E_MotorState.Idle;
fSpeed 				:= 0;
stStatus.bRunning 	:= FALSE;
Stop 				:= TRUE;

METHOD PRIVATE CalculateRampedSpeed : T_MotorSpeed
VAR_INPUT
END_VAR

// Internal helper (not part of I_Motor): ramps fSpeed up by 1.0 each cycle, clamped to 100.0
CalculateRampedSpeed := F_ClampSpeed(fSpeed := fSpeed + 1.0, fMax := 100.0);
