FUNCTION F_ClampSpeed : LREAL
VAR_INPUT
	fSpeed 	: LREAL;	// Requested speed
	fMax 	: LREAL;	// Maximum allowed speed
END_VAR

IF fSpeed > fMax THEN
	F_ClampSpeed := fMax;
ELSE
	F_ClampSpeed := fSpeed;
END_IF
