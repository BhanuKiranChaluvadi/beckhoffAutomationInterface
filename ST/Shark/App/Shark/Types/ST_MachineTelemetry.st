(*
ST_MachineTelemetry — Top-level telemetry aggregator for ADS readback.
Matches old ST_Atlant3d_Telemetry array layout for DALP Manager compatibility.
All sub-structs use pack_mode := '0'.
*)
{attribute 'pack_mode' := '0'}
TYPE ST_MachineTelemetry :
STRUCT
    HETR_ : ARRAY[0..GVL_TemperatureZones.CHANNEL_MAX] OF ST_HeaterTelemetry;            // slot 0 unused; data at [1..15]
    // Per-ZONE heater telemetry aggregated from per-channel controllers.
    // Slot 0 unused (mirrors HETR_ convention); data written at [1..CONTROLLABLE_ZONE_MAX].
    ZHETR_ : ARRAY[0..GVL_TemperatureZones.CONTROLLABLE_ZONE_MAX] OF ST_HeaterTelemetry; // slot 0 unused; data at [1..13]
    VALV_ : ARRAY[1..GVL_ValveSystem.VALVE_COUNT] OF ST_ValveTelemetry;
    MFCT_ : ARRAY[1..GVL_FlowSystem.FLOW_ZONE_COUNT] OF ST_MFCTelemetry;
    PSNS_ : ARRAY[1..GVL_PressureSystem.SENSOR_COUNT] OF ST_PressureSensorTelemetry;
    PCTR_ : ARRAY[1..GVL_PressureSystem.CONTROLLER_COUNT] OF ST_PressureControllerTelemetry;
    DIOS_ : ARRAY[1..25] OF ST_DIOTelemetry;
    MACH_ : ARRAY[1..1]  OF ST_MachineStateTelemetry;
    STAT_ : ARRAY[1..1]  OF ST_SequenceStateTelemetry;
END_STRUCT
END_TYPE
