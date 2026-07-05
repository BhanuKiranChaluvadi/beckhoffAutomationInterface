# Simulation Module — I/O Hijacking Architecture

This folder contains pure physics engines used for hardware-less testing.
No mock interfaces or dependency injection — simulation works by directly
overriding `%I*` process image memory based on `%Q*` outputs.

## Folder Structure

```
Lib/Simulation/
├── Physics/
│   ├── FB_GasPhysicsSimulator.st    # Gas pressure/flow/vacuum physics
│   ├── FB_SimulateHeaterZone.st     # Thermal ramp simulation
│   ├── FB_SimulateAnalogActuator.st # Generic setpoint-tracking actuator
│   ├── ST_GasSideState.st           # Valve state input structure
│   ├── ST_GasSideOutput.st          # Simulated pressure/flow output
│   └── physics.md                   # Gas physics rules
├── info.md                          # Beckhoff terminal raw type reference
└── README.md
```

## Architecture

```
  Production Code (unchanged, simulation-unaware)
  ┌──────────────────────────────────────────────┐
  │  PRG_HEAT_ZONE   PRG_MFC   PRG_VALVE  ...   │
  │       │ writes %Q*     │ reads %I*           │
  └───────┼────────────────┼─────────────────────┘
          │                ▲
          │   TwinCAT Process Image boundary
          ▼                │
  ┌──────────────────────────────────────────────┐
  │  App/<Machine>/Simulation/PRG_SIMULATION     │
  │  ──────────────────────────────────────────  │
  │  1. READ: Spy on %Q* outputs                 │
  │  2. CALCULATE: Run physics engines            │
  │  3. WRITE: Force results into %I* inputs      │
  └──────────────────────────────────────────────┘
          │                ▲
          ▼                │
  ┌──────────────────────────────────────────────┐
  │  Lib/Simulation/Physics/                     │
  │  ──────────────────────────────────────────  │
  │  FB_SimulateHeaterZone     (thermal math)    │
  │  FB_SimulateAnalogActuator (ramp math)       │
  │  FB_GasPhysicsSimulator    (gas/vacuum)      │
  └──────────────────────────────────────────────┘
```

## Key Design Principles

- **Production code is simulation-blind.** No `IF bSimulationMode` anywhere in application PRGs.
- **Physics engines are pure functions.** Inputs in, outputs out, no I/O side effects.
- **PRG_SIMULATION is the only wiring point.** Machine-specific, lives in `App/<Machine>/Simulation/`.
- **Toggle via GVL_Simulation.bActive.** When FALSE, PRG_SIMULATION returns immediately.

## Usage

PRG_SIMULATION is called unconditionally from `PRG_MAIN` / `PRG_PRO`.
It self-guards with `GVL_Simulation.bActive`:

```st
// Inside PRG_SIMULATION:
IF NOT GVL_Simulation.bActive THEN RETURN; END_IF

// Read %Q, run physics, write %I
aSimHeaters[1](bHeaterOn := GVL_IO.bHeaterOut, ...);
GVL_IO.nTempRawInput := F_ScaleRealToInt(aSimHeaters[1].rCurrentTemp, ...);
```
