(* Legacy alias for the C# DALP Manager client - top-level telemetry struct.
   Field-for-field identical to ST_MachineTelemetry (HETR_, VALV_, MFCT_,
   PSNS_, PCTR_, DIOS_, STAT_ arrays preserved). *)
{attribute 'pack_mode' := '0'}
TYPE ST_Atlant3d_Telemetry : ST_MachineTelemetry; END_TYPE
