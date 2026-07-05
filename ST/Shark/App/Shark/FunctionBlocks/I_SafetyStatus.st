(*
I_SafetyStatus — Interface for reading safety system state.

Implemented by FB_SafetyManager.
Consumed by PRG_DIGITAL_INPUT to feed safety-derived signals into the
digital input subsystem without coupling to the concrete manager type
(Dependency Inversion Principle).
*)
INTERFACE I_SafetyStatus

(* TRUE when mains heater voltage exceeds threshold (HETR_VOLT_ON). *)
PROPERTY HeaterVoltagePresence : BOOL

(* TRUE when the safety state machine has reached E_SafetyState.OK (SAFE_STO_ON). *)
PROPERTY IsOk : BOOL
