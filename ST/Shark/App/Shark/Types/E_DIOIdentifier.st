(*
E_DIOIdentifier — Combined DIO index for telemetry DIOS_ array.
Preserves the old E_DIOIdentifier layout for DALP Manager ADS compatibility.
Maps both digital inputs and outputs to sequential telemetry indices 1..25.
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_DIOIdentifier :
    (
        LGHT_CMR_SIDE   := 1,
        LGHT_CMR_TOP    := 2,
        CHIL_PWR        := 3,
        LGHT_SGN_RED    := 4,
        LGHT_SGN_YEL    := 5,
        LGHT_SGN_GRN    := 6,
        LOCK_DOOR_FL    := 7,
        LOCK_DOOR_FR    := 8,
        PRXS_DOOR_LT    := 9,
        PRXS_DOOR_LM    := 10,
        PRXS_DOOR_LB    := 11,
        PRXS_DOOR_RT    := 12,
        PRXS_DOOR_RM    := 13,
        PRXS_DOOR_RB    := 14,
        PRXS_DOOR_BML   := 15,
        PRXS_DOOR_BMR   := 16,
        PRXS_DOOR_BMB   := 17,
        PUMP_CHK_VAC    := 18,
        VALV_CHK_LZ1    := 19,
        VALV_CHK_LZ2    := 20,
        VALV_CHK_LZ3    := 21,
        VALV_CHK_VENT   := 22,
        HETR_VOLT_ON    := 23, 
        SAFE_STO_ON     := 24,
        SAFE_OVERRIDE   := 25
    )INT;
END_TYPE
