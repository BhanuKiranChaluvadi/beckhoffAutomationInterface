(* Heat CHANNEL identifier — one value per PHYSICAL CHANNEL on the Shark machine.

   A "channel" is the smallest unit of heater control: ONE RTD + ONE SSR +
   ONE PID loop (i.e. one FB_BeckhoffTempController instance). Multiple
   channels are grouped into a logical zone (E_HeatZone) for aggregation,
   delta-safety, and a single I_Controller surface to the rest of the system.

   Why this enum exists:
     * Telemetry consumers and internal PLC logic want per-channel readback —
       individual temperature, enable, and setpoint for every band — not just
       the zone aggregate.
     * Each entry's NUMERIC VALUE is the flat global channel index used by
       FB_TemperatureSubsystem._anZoneChannelOffset, i.e. the same index
       returned by GetChannelGlobalIndex(eZone, nChannelIdx). So a client
       (or PLC code) can write:
            stTelemetry.HETR_[E_HeatChannel.HETR_A_STR_01]
       and stay in sync with internal addressing.

   Layout (matches GVL_TemperatureZones.CHANNEL_MAX = 15):

       Zone (E_HeatZone) | nChannels | Global index range  | Channel entries
       ------------------+-----------+---------------------+--------------------------
       HETR_HEAD    (1)  |     1     |  1                  | HETR_HEAD_01
       HETR_A_STR   (2)  |     1     |  2                  | HETR_A_STR_01
       HETR_A_CMB   (3)  |     1     |  3                  | HETR_A_CMB_01
       HETR_A_B1    (4)  |     1     |  4                  | HETR_A_B1_01
       HETR_A_B2    (5)  |     1     |  5                  | HETR_A_B2_01
       HETR_A_B3    (6)  |     1     |  6                  | HETR_A_B3_01
       HETR_B_STR   (7)  |     1     |  7                  | HETR_B_STR_01
       HETR_B_CMB   (8)  |     1     |  8                  | HETR_B_CMB_01
       HETR_B_B1    (9)  |     1     |  9                  | HETR_B_B1_01
       HETR_B_B2   (10)  |     1     | 10                  | HETR_B_B2_01
       HETR_B_B3   (11)  |     1     | 11                  | HETR_B_B3_01
       HETR_EXH    (12)  |     2     | 12..13              | HETR_EXH_01..02
       HETR_CHK    (13)  |     2     | 14..15              | HETR_CHK_01..02

   Total: 15 channels.

   Add new entries when adding a channel to a zone. Numeric values must
   remain a contiguous 1..N sequence in the order above so they continue
   to match _anZoneChannelOffset[zone] + channelIdx.

   See also:
     * E_HeatZone        — per-ZONE enum (13 entries) for control / sequences.
     * E_TemperatureMonitorZone — monitor-only RTDs (no heater).
     * Documentation/HEAT_ZONE_ARCHITECTURE.md — vocabulary + layering.

   Naming note: this enum is plural (`E_HeatChannel`) to mirror PRO's
   per-channel enum and to keep singular `E_HeatZone` reserved for the
   per-ZONE identifier. The plural form here means "the set of all heater
   channels".

   Relationship to the legacy enum:
     `Telemetry/LegacyClientCompat/E_HeatZones` is an alias of this enum
     (same 15 numeric values, different name) kept ONLY for the C# DALP
     client's symbol expectation. New Shark code should reference
     `E_HeatChannel`. When the legacy DALP path is decommissioned the
     alias goes away and PRO + Shark share the same name end-to-end.

   Note on telemetry binding:
     Unlike PRO \u2014 where `ST_MachineTelemetry.HETR_` is bounded by
     `E_HeatChannel` extent \u2014 Shark's existing `ST_MachineTelemetry.HETR_`
     is fixed at `[0..23]` by the legacy DALP frame layout and cannot be
     re-bound without breaking the C# client. Adopt the enum-bound form
     when Shark's telemetry struct is modernized.
*)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_HeatChannel :
(
    // ─── A-side (Precursor) ────────────────────────────────────────────
    HETR_HEAD_01    := 1,
    HETR_A_STR_01   := 2,
    HETR_A_CMB_01   := 3,
    HETR_A_B1_01    := 4,
    HETR_A_B2_01    := 5,
    HETR_A_B3_01    := 6,

    // ─── B-side (Reactant) ─────────────────────────────────────────────
    HETR_B_STR_01   := 7,
    HETR_B_CMB_01   := 8,
    HETR_B_B1_01    := 9,
    HETR_B_B2_01    := 10,
    HETR_B_B3_01    := 11,

    // ─── Shared (Exhaust + Chuck) ──────────────────────────────────────
    HETR_EXH_01     := 12,
    HETR_EXH_02     := 13,
    HETR_CHK_01     := 14,
    HETR_CHK_02     := 15
);
END_TYPE
