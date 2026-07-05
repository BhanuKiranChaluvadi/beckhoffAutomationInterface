(* Unified command identifiers for both PRO and Shark machines.
   PRO uses: 0-304 (motion, ozone, pin lifter commands)
   Shark uses: 0-201, 1000-1001 (spectrometer commands)
   Both use: 0, 1, 9, 10, 40-57, 100, 200, 9999
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_Command :
(
    None                     := 0,
    CancelProcess            := 1,      // Exit from the deposition / System stop

    StartSpotDeposition      := 9,      // Start the spot deposition process

    SetHeaterZone            := 10,     // ST_CmdSetHeatZone — whole zone by E_HeatZone (zone is multiple channels)
    SetHeaterChannel         := 11,     // ST_CmdSetHeatChannel — single channel by E_HeatChannel
    SetInertFlow             := 40,
    SetMFCFlowRate           := 41,
    SetPressureController    := 42,

    SetValve                 := 50,
    SetTemperatureGradients  := 51,     // Start the pre-heating
    SetSwitch                := 53,     // Turn on/off the chuck pump
    StartBubblerFlow         := 54,     // Start the precursor or reactant sequence
    StopBubblerFlow          := 56,     // Stop the precursor sequence
    StartBubblerLeakTest     := 57,     // Start bubbler leak test

    HoldReleaseSample        := 100,
    StartPurge               := 200,

    // PRO-specific motion and hardware commands
    OzoneGenerator           := 300,    // TODO: also present for Shark, but currently only implemented for PRO(maybe) 
    PinLifter                := 301,    // ST_CmdPinLifter
    HeadActuatorsHoming      := 302,    // empty struct only header
    HeadMoveActuators        := 303,    // ST_CmdHeadMoveActuators
    HeadMoveKinematic        := 304,    // ST_CmdHeadMoveKinematic
    StopAllActuation         := 305,    // Stop pin lifter and head actuators motion. empty struct only header

    // Shark-specific spectrometer commands
    SetSpectrometerData      := 1000,
    SetSpectrometerDark      := 1001,

    ErrorAcknowledge         := 9999
) INT;
END_TYPE
