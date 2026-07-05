(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
(*
Project version — Shark system.
Replaces old Global_Version.st + F_GetVersion.st.
*)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    stLibVersion : ST_LibVersion := (
        iMajor   := 1,
        iMinor   := 2,
        iBuild   := 0,
        iRevision := 0,
        sVersion := '1.2.0'
    );
END_VAR
