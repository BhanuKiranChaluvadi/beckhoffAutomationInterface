(* Returns the current system time as a Date-Time (DT) value. *)
FUNCTION F_GetCurrentDT : DT
VAR
    fbGetTime  : GETSYSTEMTIME;
    stFileTime : T_FILETIME;
END_VAR
    // Get current system time (100ns ticks since 1601)
    fbGetTime(timeLoDW => stFileTime.dwLowDateTime, timeHiDW => stFileTime.dwHighDateTime);

    // Convert to Date-Time (Seconds since 1970)
    F_GetCurrentDT := FILETIME_TO_DT(stFileTime);
END_FUNCTION
