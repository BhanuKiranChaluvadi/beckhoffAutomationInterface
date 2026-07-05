(* Positive modulo — returns a value in [0, rModulus).
   Standard MOD can return negative results for negative inputs;
   this function wraps into the positive range. *)
FUNCTION F_ModAbs : LREAL
VAR_INPUT
    rValue   : LREAL;
    rModulus : LREAL;
END_VAR
VAR
    rResult : LREAL;
END_VAR
    IF rModulus = 0.0 THEN
        F_ModAbs := 0.0;
        RETURN;
    END_IF

    rResult := rValue - FLOOR(rValue / rModulus) * rModulus;
    F_ModAbs := rResult;
END_FUNCTION
