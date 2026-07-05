INTERFACE I_PressureSubsystem

METHOD IsInitialized : BOOL
END_METHOD

METHOD HasAnyError : BOOL
END_METHOD

METHOD GetSensorByIndex : I_Sensor
VAR_INPUT
    nSensor : INT;
END_VAR
END_METHOD

METHOD GetSensorCount : INT
END_METHOD

METHOD GetControllerByIndex : I_Controller
VAR_INPUT
    nCtrl : INT;
END_VAR
END_METHOD

METHOD GetControllerCount : INT
END_METHOD

END_INTERFACE
