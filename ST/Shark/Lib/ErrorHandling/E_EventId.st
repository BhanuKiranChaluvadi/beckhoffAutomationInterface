(* Non-error event identifiers for the library (operational / informational logs). *)
{attribute 'qualified_only'}
{attribute 'strict'}
{attribute 'to_string'}
TYPE E_EventId :
    (
        NONE := 0,

        // Generic informational / diagnostic event; free text carried in the log
        // context. Use when a message does not need its own filterable ID.
        // Specific IDs (command lifecycle, state changes, etc.) are added per
        // call site during migration, in non-overlapping bands above 10000
        // (E_ErrorId occupies 0..7999).
        EVT_INFO := 10000
    ) UDINT;
END_TYPE
