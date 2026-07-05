(*
Valve Identifier Enumeration - Shark System

Preserved from old E_ValveIdentifier with identical names and values.
Organized by subsystem:
- A-Side Gas System (1-13): Precursor gas distribution
- B-Side Gas System (14-27): Reactant gas distribution
- Inert Gas System (28): Inert gas control
- Exhaust System (29): Exhaust pump valve
- Chuck System (30-35): Chuck vacuum zones, vent, and cooling water (solenoid via DO)
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_ValveIdentifier :
    (
        // A-Side Gas System Valves
        VALV_A_MFC01           := 1,     // Reactant A Before MFC
        VALV_A_DST_B1          := 2,     // Reactant A Distributor Bubbler 1
        VALV_A_DST_B2          := 3,     // Reactant A Distributor Bubbler 2
        VALV_A_DST_B3          := 4,     // Reactant A Distributor Bubbler 3
        VALV_A_DST_PRG         := 5,     // Reactant A Distributor Purge
        VALV_A_DST_EXH         := 6,     // Reactant A Distributor Exhaust
        VALV_A_CMB_B1          := 7,     // Reactant A Combiner Bubbler 1
        VALV_A_CMB_B2          := 8,     // Reactant A Combiner Bubbler 2
        VALV_A_CMB_B3          := 9,     // Reactant A Combiner Bubbler 3
        VALV_A_CMB_PRG         := 10,    // Reactant A Combiner Purge
        VALV_A_CMB_EXH         := 11,    // Reactant A Combiner Exhaust
        VALV_A_STR             := 12,    // Reactant A Start
        VALV_A_MIX             := 13,    // Reactant A Mixing

        // B-Side Gas System Valves
        VALV_B_MFC01           := 14,    // Reactant B Before MFC
        VALV_B_GAS01           := 15,    // Reactant B External Gas
        VALV_B_DST_B1          := 16,    // Reactant B Distributor Bubbler 1
        VALV_B_DST_B2          := 17,    // Reactant B Distributor Bubbler 2
        VALV_B_DST_B3          := 18,    // Reactant B Distributor Bubbler 3
        VALV_B_DST_PRG         := 19,    // Reactant B Distributor Purge
        VALV_B_DST_EXH         := 20,    // Reactant B Distributor Exhaust
        VALV_B_CMB_B1          := 21,    // Reactant B Combiner Bubbler 1
        VALV_B_CMB_B2          := 22,    // Reactant B Combiner Bubbler 2
        VALV_B_CMB_B3          := 23,    // Reactant B Combiner Bubbler 3
        VALV_B_CMB_PRG         := 24,    // Reactant B Combiner Purge
        VALV_B_CMB_EXH         := 25,    // Reactant B Combiner Exhaust
        VALV_B_STR             := 26,    // Reactant B Start
        VALV_B_MIX             := 27,    // Reactant B Mixing

        // Inert Gas System Valves
        VALV_IG_MFC01          := 28,    // Inert Gas MFC Valve

        // Exhaust System Valves
        VALV_EX_PUMP           := 29,    // Exhaust Pump Valve

        // Chuck System Valves (solenoid via DO injection)
        VALV_CHK_LZ1           := 30,    // Chuck Zone 1 Vacuum
        VALV_CHK_LZ2           := 31,    // Chuck Zone 2 Vacuum
        VALV_CHK_LZ3           := 32,    // Chuck Zone 3 Vacuum
        VALV_CHK_VENT          := 33,    // Chuck Vent
        VALV_CHK_WIN           := 34,    // Chuck Chiller Water In
        VALV_CHK_WOUT          := 35     // Chuck Chiller Water Out
    )INT;
END_TYPE
