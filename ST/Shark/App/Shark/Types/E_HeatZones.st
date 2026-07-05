(*
E_HeatZones — Legacy per-CHANNEL heater identifier expected by the C# DALP
Manager. The client iterates this enum dynamically and binds each entry to
HETR_[entryValue] in ST_Atlant3d_Telemetry.

**Alias for the modern `E_HeatChannel`** (same 15 numeric values). Kept
under this name and in this folder ONLY for legacy client name compatibility.
New Shark code should use `E_HeatChannel` (Subsystems/Temperature/Config/)
so naming stays synchronized with PRO. When the legacy DALP path is
decommissioned, this file should be deleted.

Distinct from `E_HeatZone` (the new per-zone enum, 13 entries). This enum
has 15 entries because the legacy client treats every RTD/SSR channel as a
"heat zone". Entry numbering MUST match the per-channel global index used
by FB_TemperatureSubsystem._anZoneChannelOffset:

   Zone (E_HeatZone) | Channel(s) | E_HeatZones entry/value
   ------------------+------------+--------------------------------
   HETR_HEAD   (1)   | 1          | HETR_HEAD_01   = 1
   HETR_A_STR  (2)   | 1          | HETR_A_STR_01  = 2
   HETR_A_CMB  (3)   | 1          | HETR_A_CMB_01  = 3
   HETR_A_B1   (4)   | 1          | HETR_A_B1_01   = 4
   HETR_A_B2   (5)   | 1          | HETR_A_B2_01   = 5
   HETR_A_B3   (6)   | 1          | HETR_A_B3_01   = 6
   HETR_B_STR  (7)   | 1          | HETR_B_STR_01  = 7
   HETR_B_CMB  (8)   | 1          | HETR_B_CMB_01  = 8
   HETR_B_B1   (9)   | 1          | HETR_B_B1_01   = 9
   HETR_B_B2  (10)   | 1          | HETR_B_B2_01   = 10
   HETR_B_B3  (11)   | 1          | HETR_B_B3_01   = 11
   HETR_EXH   (12)   | 1, 2       | HETR_EXH_01    = 12, HETR_EXH_02 = 13
   HETR_CHK   (13)   | 1, 2       | HETR_CHK_01    = 14, HETR_CHK_02 = 15
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_HeatZones :
(
    HETR_HEAD_01   := 1,
    HETR_A_STR_01  := 2,
    HETR_A_CMB_01  := 3,
    HETR_A_B1_01   := 4,
    HETR_A_B2_01   := 5,
    HETR_A_B3_01   := 6,
    HETR_B_STR_01  := 7,
    HETR_B_CMB_01  := 8,
    HETR_B_B1_01   := 9,
    HETR_B_B2_01   := 10,
    HETR_B_B3_01   := 11,
    HETR_EXH_01    := 12,
    HETR_EXH_02    := 13,
    HETR_CHK_01    := 14,
    HETR_CHK_02    := 15
) INT;
END_TYPE
