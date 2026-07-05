(* Tunable controller interface.

A tunable is a controller that, in addition to the normal I_Controller surface
(Enable / SetSetpoint / GetStatus / Reset), supports switching its control mode
at runtime — most importantly into the Beckhoff PID self-tuning (autotune) mode.

Implemented by both a single channel (FB_BeckhoffTempController) and a whole
heat zone (FB_HeatZone). A zone cascades the mode to every channel it owns, so
the same panel can tune one channel or an entire zone interchangeably
(Liskov substitution). *)
INTERFACE I_Tunable EXTENDS I_Controller

(* Switch the controller (or every channel of a zone) into the given control
   mode, e.g. E_CTRL_MODE.eCTRL_MODE_TUNE to start autotuning or
   E_CTRL_MODE.eCTRL_MODE_ACTIVE to return to normal closed-loop control. *)
METHOD SetControlMode : BOOL
VAR_INPUT
    eMode : E_CTRL_MODE;
END_VAR
END_METHOD

(* Current control mode (for a zone: the last mode commanded to its channels). *)
METHOD GetControlMode : E_CTRL_MODE
END_METHOD

END_INTERFACE
