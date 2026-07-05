(* ST_GasSideOutput - Simulated pressure/flow output for one gas side

DESIGN PRINCIPLES:
- Single Responsibility: Groups all simulated outputs for one side
- DRY: Single structure returned from simulation method
- KISS: Simple data structure, no logic
*)
TYPE ST_GasSideOutput :
STRUCT
    rCMPressure : LREAL;            // Cold manifold pressure (bar)
    rHMPressure : LREAL;            // Hot manifold pressure (bar)
    rMFCActual : LREAL;             // MFC actual flow (sccm)
END_STRUCT
END_TYPE
