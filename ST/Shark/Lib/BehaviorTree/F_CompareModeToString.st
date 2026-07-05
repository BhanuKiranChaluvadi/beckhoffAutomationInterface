(* F_CompareModeToString - Converts E_CompareMode to a human-readable string.
   Used in error messages and status descriptions (DRY). *)
FUNCTION F_CompareModeToString : STRING(20)
VAR_INPUT
    eMode : E_CompareMode;
END_VAR

    CASE eMode OF
        E_CompareMode.WITHIN_TOLERANCE:
        F_CompareModeToString := 'within';

        E_CompareMode.GREATER_THAN:
        F_CompareModeToString := '>';

        E_CompareMode.LESS_THAN:
        F_CompareModeToString := '<';
    ELSE
        F_CompareModeToString := '?';
    END_CASE
END_FUNCTION
