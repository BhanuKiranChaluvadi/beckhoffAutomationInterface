(* CONFIG_TIER: 4-PLATFORM -- Do not edit; identical on all machines. *)
{attribute 'qualified_only'}
VAR_GLOBAL
    // Toggle TRUE to activate the background physics simulator and override physical values.
    // False disables physics logic (letting real terminals update memory naturally).
    bActive : BOOL := TRUE;
END_VAR
