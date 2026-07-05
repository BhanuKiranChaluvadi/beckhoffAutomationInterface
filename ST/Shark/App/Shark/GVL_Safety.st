(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(* TwinSAFE safety I/O mapping — Shark system.
   All signals are linked to the TwinSAFE safety project via EtherCAT.
   The orchestrator (PRG_SHARK) gates subsystems behind SafetyOk.

   From old FB_SafetyManager:
   - lnkSF_bSafeState  → SafetyOk (aggregate safe signal from TwinSAFE)
   - lnkSF_bSafetyRunning / lnkSF_bRestart / lnkSF_bErrorAckoledge → handshake outputs
   - HeaterVoltage (EL3443) → monitored in old code with 200V threshold
*)
{attribute 'qualified_only'}
VAR_GLOBAL
    // --- TwinSAFE Aggregate State ---
    SafetyOk                        AT %I* : BOOL;     // TRUE when TwinSAFE reports safe state

    // --- TwinSAFE Handshake (PLC → Safety PLC) ---
    SafetyRunning                   AT %Q* : BOOL;     // Heartbeat — PLC sets TRUE while running
    SafetyRestart                   AT %Q* : BOOL;     // Restart request to safety project
    SafetyErrorAcknowledge          AT %Q* : BOOL;     // Error acknowledge to safety project

    // --- Heater Voltage Monitoring (EL3443 Power Measurement) ---
    HeaterVoltage                   AT %I* : REAL;      // Mains voltage (V) from EL3443
END_VAR
