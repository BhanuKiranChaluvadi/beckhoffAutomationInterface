(* F_EvaluateCompareMode - Evaluates a value against a target using the specified compare mode.
   Centralizes comparison logic used by sensor action nodes (DRY). *)
FUNCTION F_EvaluateCompareMode : BOOL
VAR_INPUT
    rValue     : LREAL;
    rTarget    : LREAL;
    rTolerance : LREAL;         // Only used for WITHIN_TOLERANCE mode
    eMode      : E_CompareMode;
END_VAR

    CASE eMode OF
        E_CompareMode.WITHIN_TOLERANCE:
        F_EvaluateCompareMode := ABS(rValue - rTarget) <= rTolerance;

        E_CompareMode.GREATER_THAN:
        F_EvaluateCompareMode := rValue > rTarget;

        E_CompareMode.LESS_THAN:
        F_EvaluateCompareMode := rValue < rTarget;
    ELSE
        F_EvaluateCompareMode := FALSE;
    END_CASE
END_FUNCTION
