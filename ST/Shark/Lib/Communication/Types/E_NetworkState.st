(* Network coordinator state. *)
{attribute 'qualified_only'}
{attribute 'strict'}
{attribute 'to_string'}
TYPE E_NetworkState :
    (
        SELECT_SLAVE,
        INITIATE_REQUEST,
        AWAIT_COMPLETION,
        SETTLING
    );
END_TYPE
