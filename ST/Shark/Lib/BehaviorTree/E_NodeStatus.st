{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_NodeStatus :
    (
        IDLE := 0,
        RUNNING := 1,
        SUCCESS := 2,
        FAILURE := 3
    ) UDINT;
END_TYPE
