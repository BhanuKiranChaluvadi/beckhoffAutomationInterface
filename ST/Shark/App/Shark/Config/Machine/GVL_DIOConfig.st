(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(* Digital I/O channel counts for the Shark system.
   Values must match the extent of the corresponding E_*Identifier enums.
   Absorbs the old GVL_DigitalInputSystem and GVL_DigitalOutputSystem files. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // Digital I/O channel counts (must match the extent of their E_*Identifier enums)
    DI_COUNT : INT := 12;   // E_DigitalInputIdentifier  (was GVL_DigitalInputSystem.INPUT_COUNT)
    DO_COUNT : INT := 16;   // E_DigitalOutputIdentifier (was GVL_DigitalOutputSystem.OUTPUT_COUNT)

    // Outputs activated once on first boot (section 8 one-time init)
    // To add a new auto-start output: increment AUTO_START_DO_COUNT and add the identifier.
    AUTO_START_DO_COUNT   : INT := 3;
    AUTO_START_DO_OUTPUTS : ARRAY[1..3] OF INT := [
        E_DigitalOutputIdentifier.LGHT_CMR_TOP,    // Top camera light
        E_DigitalOutputIdentifier.LGHT_CMR_SIDE,   // Side camera light
        E_DigitalOutputIdentifier.CHIL_PWR         // Chiller power
    ];
END_VAR