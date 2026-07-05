(*
MFC Identifier Enumeration - Shark System

Preserved from old E_MFCIdentifier.
3 Bronkhorst EtherCAT mass flow controllers.
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_MFCIdentifier :
    (
        MFCT_A_01              := 1,     // Precursor MFC [25 sccm]
        MFCT_B_01              := 2,     // Reactant MFC [25 sccm]
        MFCT_IG_01             := 3      // Inert Gas MFC [500 sccm]
    )INT;
END_TYPE
