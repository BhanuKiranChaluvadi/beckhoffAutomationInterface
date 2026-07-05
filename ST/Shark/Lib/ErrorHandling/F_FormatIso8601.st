(* Formats a Date-Time (DT) value as an ISO-8601 string 'YYYY-MM-DDThh:mm:ss'.
   Used for the tTimestamp field of the EventLogger JSON attribute so clients
   get the same timestamp shape the legacy logger emitted.

   Implementation: TwinCAT DT_TO_STRING yields the fixed-width IEC form
   'DT#YYYY-MM-DD-hh:mm:ss'. We strip the 'DT#' prefix and replace the single
   '-' separator between the date and the time (position 11) with a 'T'.
   The DT resolution is one second, matching the legacy timestamp. *)
FUNCTION F_FormatIso8601 : STRING(19)
VAR_INPUT
    dtIn : DT;
END_VAR
VAR
    sTmp  : STRING(40);
    nHash : INT;
END_VAR
    sTmp := DT_TO_STRING(dtIn);          // 'DT#YYYY-MM-DD-hh:mm:ss'
    nHash := FIND(sTmp, '#');
    IF nHash > 0 THEN
        sTmp := DELETE(sTmp, nHash, 1);  // remove the 'DT#' prefix -> 'YYYY-MM-DD-hh:mm:ss'
    END_IF
    sTmp := REPLACE(sTmp, 'T', 1, 11);   // date/time separator '-' -> 'T'
    F_FormatIso8601 := sTmp;
END_FUNCTION
