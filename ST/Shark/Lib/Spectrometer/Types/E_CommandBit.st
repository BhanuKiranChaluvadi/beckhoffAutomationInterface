{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_CommandBit :
    (
        NONE      := 0,
        FREQUENCY := 1,
        LIGHT     := 2,
        AVERAGE   := 3,
        SPECTRUM  := 4,
        QUALITY   := 5
    )UINT;
END_TYPE
