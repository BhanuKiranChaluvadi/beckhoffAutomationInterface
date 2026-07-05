(* Per-CHANNEL PID gains for one heater channel (one RTD + one SSR + one PID).

   Indexed by the flat global channel index (1..CHANNEL_MAX) in
   GVL_TemperatureZoneConfig.PID_GAINS, using the SAME ordering as
   PRG_TEMPERATURE_SENSOR / PRG_HEAT_ZONE (zone-major, channel-minor).

   Channels grouped into one logical zone (E_HeatZone) may each need their OWN
   gains: they are physically distinct bands, and the Beckhoff autotune produces
   a per-channel result. This struct is the single place to record those gains.

   Field names mirror the legacy per-zone PID fields so commissioning notes and
   autotune output map across unchanged. *)
TYPE ST_HeatChannelPIDGains :
STRUCT
    rKp : LREAL := 2.0;
    tTn : TIME := T#200S;
    tTv : TIME := T#50S;
    tTd : TIME := T#250MS;
END_STRUCT
END_TYPE
