{attribute 'qualified_only'}
VAR_GLOBAL
	nMaxMotorSpeed 	: LREAL 	:= 100.0;	// Global speed limit shared across the project
	sPlantName 		: STRING 	:= 'Shark';	// Human-readable plant/project name
	bMotorRunSensor 	AT %I* 	: BOOL;		// Physical input: motor-running feedback sensor (to be linked to hardware)
	bMotorEnableOutput 	AT %Q* 	: BOOL;		// Physical output: motor enable command (to be linked to hardware)
END_VAR
