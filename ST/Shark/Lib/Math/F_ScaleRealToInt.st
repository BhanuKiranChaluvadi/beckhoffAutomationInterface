(*
Scales a REAL physics value into an INT raw count format for memory overriding.
Used heavily in the Simulation PRG to spoof Beckhoff A/D terminal behavior.

Formula: Output = (Input - InMin) / (InMax - InMin) * (OutMax - OutMin) + OutMin

Rules observed: Functional Preference (Pure generic math).
*)
FUNCTION F_ScaleRealToInt : INT
VAR_INPUT
    rValue   : REAL;  // The calculated physics engine value (e.g., 150.5 °C)
    rInMin   : REAL;  // The physical minimum (e.g., 0.0 °C)
    rInMax   : REAL;  // The physical maximum (e.g., 300.0 °C)
    nOutMin  : INT;   // The analog minimum (e.g., 0)
    nOutMax  : INT;   // The analog maximum (e.g., 32767)
END_VAR
VAR
    rPercent : REAL;
    rOutTemp : REAL;
END_VAR

// Fail-fast / Division by zero protection
IF (rInMax - rInMin) = 0.0 THEN
    F_ScaleRealToInt := nOutMin;
    RETURN;
END_IF

// Calculate purely
rPercent := (rValue - rInMin) / (rInMax - rInMin);
rOutTemp := (rPercent * INT_TO_REAL(nOutMax - nOutMin)) + INT_TO_REAL(nOutMin);

// Clipping to prevent overflow wrap-around in memory
IF rOutTemp > 32767.0 THEN
    F_ScaleRealToInt := 32767;
ELSIF rOutTemp < -32768.0 THEN
    F_ScaleRealToInt := -32768;
ELSE
    F_ScaleRealToInt := REAL_TO_INT(rOutTemp);
END_IF

END_FUNCTION
