(*
PRG_HUMIDITY_SENSOR — Ambient Humidity Sensor (Shark)

Manages the MDP5001_300_7E2119CA humidity channel from the Shark TH sensor pair.
Consumers read the scaled value via ipSensor.GetMeasurement().

The temperature channel of the same TH sensor is wired in PRG_TEMPERATURE_SENSOR
as E_TemperatureMonitorZone.SENS_CHAMBER.

Scaling constants are in GVL_HumiditySystem.
*)
PROGRAM PRG_HUMIDITY_SENSOR
VAR_INPUT
    // Hardware IO — linked to EtherCAT process image at %I* offset matching terminal position.
    // VAR_INPUT allows PRG_SIMULATION to write raw values for simulation mode.
    IO_Humid : T_Beckhoff_AmbientSensor;
END_VAR
VAR
    fbAdapter    : FB_AnalogInputAdapter;
    fbSensor     : FB_AnalogSensor;
    ipSensor     : I_Sensor;
    bInitialized : BOOL := FALSE;
END_VAR

    IF NOT bInitialized THEN
        _Init();
    END_IF

    fbAdapter.Execute();
    fbSensor.Execute();

METHOD PRIVATE _Init
    fbAdapter.Link(ADR(IO_Humid));
    fbSensor.Init(
        sInstanceName := 'AMBT_HUMID',
        ipRawInput    := fbAdapter,
        rRawMin       := GVL_HumiditySystem.AMBT_HUMID_RAW_MIN,
        rRawMax       := GVL_HumiditySystem.AMBT_HUMID_RAW_MAX,
        rScaledMin    := GVL_HumiditySystem.AMBT_HUMID_SCALED_MIN,
        rScaledMax    := GVL_HumiditySystem.AMBT_HUMID_SCALED_MAX
    );
    ipSensor     := fbSensor;
    bInitialized := TRUE;
END_METHOD

END_PROGRAM
