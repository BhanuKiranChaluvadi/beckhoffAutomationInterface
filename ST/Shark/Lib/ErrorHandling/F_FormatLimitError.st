(* Formats a limit error message with value and limit. *)
FUNCTION F_FormatLimitError : STRING(255)
VAR_INPUT
    sLabel  : STRING(20);   // Error label (e.g., 'Temp', 'Deviation')
    rActual : LREAL;        // Actual value
    rLimit  : LREAL;        // Limit value
    sUnit   : STRING(10);   // Unit (e.g., 'C', '%')
END_VAR
VAR
    sResult : STRING(255);
END_VAR
    sResult := CONCAT(sLabel, ' ');
    sResult := CONCAT(sResult, LREAL_TO_FMTSTR(rActual, 2, TRUE));
    sResult := CONCAT(sResult, sUnit);
    sResult := CONCAT(sResult, ' > limit ');
    sResult := CONCAT(sResult, LREAL_TO_FMTSTR(rLimit, 2, TRUE));
    sResult := CONCAT(sResult, sUnit);

    F_FormatLimitError := sResult;
END_FUNCTION
