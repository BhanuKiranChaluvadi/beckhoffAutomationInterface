(* 
Generic Analog Actuator Simulator
Simulates a device that ramps toward a setpoint (like an MFC or Proportional Valve)
over time, giving physical feedback to the system.

Engineering Rules Used:
- Functional Preference (Pure calculation, isolated I/O)
- Simplicity (Linear ramping)
*)
FUNCTION_BLOCK FB_SimulateAnalogActuator
VAR_INPUT
    rSetpoint   : REAL;  // Commanded target value from %Q
    rRampRate   : REAL;  // Units per cycle to change
END_VAR
VAR_OUTPUT
    rActualFeedback : REAL; // Current position/flow to feedback to %I
END_VAR
VAR
    _rCurrentPhysicalValue : REAL := 0.0;
END_VAR

// Integration logic
IF rSetpoint > _rCurrentPhysicalValue THEN
    IF (rSetpoint - _rCurrentPhysicalValue) > rRampRate THEN
        _rCurrentPhysicalValue := _rCurrentPhysicalValue + rRampRate;
    ELSE
        _rCurrentPhysicalValue := rSetpoint; // Snap to target
    END_IF
ELSIF rSetpoint < _rCurrentPhysicalValue THEN
    IF (_rCurrentPhysicalValue - rSetpoint) > rRampRate THEN
        _rCurrentPhysicalValue := _rCurrentPhysicalValue - rRampRate;
    ELSE
        _rCurrentPhysicalValue := rSetpoint; // Snap to target
    END_IF
END_IF

// Output to caller
rActualFeedback := _rCurrentPhysicalValue;
END_FUNCTION_BLOCK
