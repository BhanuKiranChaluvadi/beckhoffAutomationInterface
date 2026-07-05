(* Heat zone (zone) identifier - indexes into the zone array.
   Shark layout: HEAD is shared (single heater), the rest mirror per-side.
   EXH and CHK are 2-channel composite zones managed by FB_HeatZone. *)
{attribute 'qualified_only'}
{attribute 'to_string'}
TYPE E_HeatZone :
(
    HETR_HEAD   := 1,    // Shared head heater           (1 channel)
    HETR_A_STR  := 2,    // A-side start (umbilical)     (1 channel)
    HETR_A_CMB  := 3,    // A-side hot manifold          (1 channel)
    HETR_A_B1   := 4,    // A-side bubbler 1             (1 channel)
    HETR_A_B2   := 5,    // A-side bubbler 2             (1 channel)
    HETR_A_B3   := 6,    // A-side bubbler 3             (1 channel)
    HETR_B_STR  := 7,    // B-side start (umbilical)     (1 channel)
    HETR_B_CMB  := 8,    // B-side hot manifold          (1 channel)
    HETR_B_B1   := 9,    // B-side bubbler 1             (1 channel)
    HETR_B_B2   := 10,   // B-side bubbler 2             (1 channel)
    HETR_B_B3   := 11,   // B-side bubbler 3             (1 channel)
    HETR_EXH    := 12,   // Exhaust  (inner + outer)     (2 channels, AVG)
    HETR_CHK    := 13    // Chuck    (inner + outer)     (2 channels, AVG, tight delta)
);
END_TYPE
