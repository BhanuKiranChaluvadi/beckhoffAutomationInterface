(* Type alias for the Beckhoff EL3214 (PT100/RTD) channel type.
   The MDP5001_320_<suffix> name is a config-hash TwinCAT computes from the
   terminal's actual PDO/revision config: this project's terminals (ESI rev
   0x0012) generate 5D7E181C — confirmed empirically 2026-07-14 by letting
   TwinCAT regenerate and save the .tsproj. The reference project's hardware
   (rev 0x0011) generated A369A904 instead; that name does NOT exist here. *)
TYPE T_Beckhoff_TempSensor_Chuck : MDP5001_320_5D7E181C;
END_TYPE
