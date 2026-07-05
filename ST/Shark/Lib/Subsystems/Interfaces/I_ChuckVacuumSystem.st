// Chuck Vacuum Subsystem Interface
//
// Abstracts machine-specific chuck-vacuum hardware topology behind a stable
// operation-level interface. Per-machine concrete implementations encapsulate:
//   - How the vacuum source is started/stopped (dedicated vs shared pump)
//   - Whether and how a vent valve is operated
//   - How "vacuum held" / "vacuum released" is sensed
//     (digital pressure switches vs analog vacuum sensor)
//
// Lib sequences (FB_ChuckVacuumHoldSequence / FB_ChuckVacuumReleaseSequence)
// own the state-machine, timeout, and error-reporting logic. They invoke
// this interface for all hardware actuation, so the sequences themselves
// contain no machine-specific code.
//
// Implementations live in App/<Machine>/Subsystems/ChuckVacuum/.
INTERFACE I_ChuckVacuumSystem

    // True once Init() has succeeded and all required hardware references
    // have been resolved.
    METHOD IsInitialized : BOOL
    END_METHOD

    // True if any internal hardware actuation has reported an error since
    // the last Reset().
    METHOD IsError : BOOL
    END_METHOD

    // Number of zone valves this system supports (typically 3 for 4"/6"/8").
    METHOD GetMaxZoneCount : INT
    END_METHOD

    // Idempotent: ensure the vacuum source is active and any vent path is
    // closed. Returns TRUE if commanded successfully (does not wait for
    // mechanical settling — caller can add its own stabilization timer).
    METHOD PrepareForHold : BOOL
    END_METHOD

    // Idempotent: open the vent path (if any) to begin atmospheric release.
    // The pump is NOT yet stopped — release requires waiting for the vacuum
    // sensor to confirm before shutting the pump down.
    METHOD BeginRelease : BOOL
    END_METHOD

    // Idempotent: stop the vacuum source. Called only after IsVacuumReleased
    // returns TRUE. For machines whose pump is shared with another subsystem
    // this is a no-op.
    METHOD FinishRelease : BOOL
    END_METHOD

    // Open the zone valve for the specified zone (1-based). Returns FALSE
    // if the zone index is out of range.
    METHOD OpenZone : BOOL
    VAR_INPUT
        nZone : INT;
    END_VAR
    END_METHOD

    // Close the zone valve for the specified zone (1-based). Returns FALSE
    // if the zone index is out of range.
    METHOD CloseZone : BOOL
    VAR_INPUT
        nZone : INT;
    END_VAR
    END_METHOD

    // TRUE when vacuum is achieved on all zones 1..nActiveZoneCount.
    // rThreshold meaning is implementation-specific:
    //   - Analog sensor: vacuum is held when measurement < rThreshold
    //   - Digital pressure switches: rThreshold is ignored (hardware-defined)
    METHOD IsVacuumHeld : BOOL
    VAR_INPUT
        nActiveZoneCount : INT;
        rThreshold       : LREAL;
    END_VAR
    END_METHOD

    // TRUE when vacuum is fully released (atmosphere reached on all zones).
    //   - Analog sensor: released when measurement > rThreshold
    //   - Digital pressure switches: rThreshold is ignored
    METHOD IsVacuumReleased : BOOL
    VAR_INPUT
        rThreshold : LREAL;
    END_VAR
    END_METHOD

    // Reset internal error state and reset any zone valves whose drivers
    // report errors. Does not change actuator commands.
    METHOD Reset : BOOL
    END_METHOD

END_INTERFACE
