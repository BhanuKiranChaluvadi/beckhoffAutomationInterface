(* Type alias for Beckhoff MDP5001_300_7E2119CA analog input terminal.
   Used for ambient temperature and humidity sensors (4-20 mA analog output).
   Same underlying hardware as T_Beckhoff_PressureSensor; separate alias
   for semantic clarity at the declaration site. *)
TYPE T_Beckhoff_AmbientSensor : MDP5001_300_7E2119CA;
END_TYPE
