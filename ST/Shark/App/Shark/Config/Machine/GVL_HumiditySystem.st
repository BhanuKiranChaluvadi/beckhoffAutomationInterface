(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build.
   Scaling for the MDP5001_300_7E2119CA humidity channel on the Shark TH sensor pair.
   Humidity channel uses standard 0-30518 full-scale.
   Temperature channel has moved to GVL_TemperatureSystem *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // AMBT_HUMID: Ambient relative humidity [%RH]
    AMBT_HUMID_RAW_MIN    : LREAL := 0.0;
    AMBT_HUMID_RAW_MAX    : LREAL := 30518.0;
    AMBT_HUMID_SCALED_MIN : LREAL := 0.0;    // %RH
    AMBT_HUMID_SCALED_MAX : LREAL := 100.0;  // %RH

END_VAR
