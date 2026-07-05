INTERFACE I_TemperatureSubsystem

METHOD IsInitialized : BOOL
END_METHOD

METHOD HasAnyError : BOOL
END_METHOD

METHOD AllAtSetpoint : BOOL
END_METHOD

METHOD GetFirstError : ST_Error
END_METHOD

METHOD GetZoneCount : INT
END_METHOD

METHOD GetTemperatureByIndex : I_Sensor
VAR_INPUT
    nZone : INT;
END_VAR
END_METHOD

METHOD GetControllerByIndex : I_Controller
VAR_INPUT
    nZone : INT;
END_VAR
END_METHOD

(* Channel-level controller addressed by its flat global channel index
   (1..CHANNEL_MAX). Returns 0 when out of range. *)
METHOD GetChannelControllerByGlobalIndex : I_Controller
VAR_INPUT
    nGlobalIdx : INT;
END_VAR
END_METHOD

(* Channel-level controller addressed by (zone, channel-in-zone). The channel
   index is 1..GetChannelCountByIndex(nZone). Returns 0 when out of range. *)
METHOD GetChannelControllerByZoneIndex : I_Controller
VAR_INPUT
    nZone       : INT;
    nChannelIdx : INT;
END_VAR
END_METHOD

(* Number of channels in a zone (0 when the zone index is out of range). *)
METHOD GetChannelCountByIndex : INT
VAR_INPUT
    nZone : INT;
END_VAR
END_METHOD

END_INTERFACE
