# Gas Physics Simulation

## Priority Order
1. **Vacuum (exhaust)** - Always wins when active
2. **Gas inlet (MFC)** - Pressurizes when flowing
3. **Atmosphere (nozzle)** - 1.0 bar reference

## Important Hardware Facts

### HM Pressure Sensor = Pressure Controller
There is **NO separate physical HM pressure sensor**. The pressure controller (PCV) 
located in the hot manifold serves dual purposes:
- **As a Controller**: Regulates HM pressure toward its setpoint
- **As a Sensor**: Its `MeasuredValue` IS the HM pressure reading

Since `I_Controller` extends `I_Sensor`, calling `GetMeasurement()` on the controller
returns the current HM pressure (the process variable it's controlling).

### Exhaust Valve Hierarchy
The exhaust system has three valves in series:

1. **VALV_EX_PUMP (ID 27)** - Main exhaust pump isolation valve
   - Controls vacuum on the exhaust manifold
   - Must be OPEN for exhaust pressure to drop

2. **VALV_A_EXH (ID 10)** - A-Side exhaust valve
   - Connects A-Side manifolds to exhaust line

3. **VALV_B_EXH (ID 21)** - B-Side exhaust valve
   - Connects B-Side manifolds to exhaust line

## HM Pressure Logic (with PCV)

| Gas Input | Nozzle Iso | PCV Setpoint     | HM Pressure Result           |
|-----------|------------|------------------|------------------------------|
| Yes       | Closed     | Any              | = CM (no outlet)             |
| Yes       | Open       | 0 (purge)        | ~1.4 bar (flow-stabilized)   |
| Yes       | Open       | 1.4 (regulating) | 1.4 bar (tracks PCV setpoint)|
| Yes       | Open       | 3 (closed)       | = CM (no outlet)             |
| No        | Open       | 0 (purge)        | ~1.0 bar (atmospheric)       |
| No        | -          | -                | ~1.0 bar (leak to atm)       |

**Key Insight**: When PCV is regulating, HM pressure **ramps toward the PCV setpoint**.
The controller's MeasuredValue reflects this - it will eventually match the setpoint
(unless PCV is fully open or fully closed).

## CM Pressure Logic (depends on HM outlet)

| Gas Flow | Path to HM | HM Has Outlet | CM Pressure Result           |
|----------|------------|---------------|------------------------------|
| Yes      | Open       | Yes           | = HM (connected vessels)     |
| Yes      | Open       | No            | = Max (3.0 bar, builds up)   |
| Yes      | Closed     | -             | = Max (3.0 bar, builds up)   |
| No       | -          | -             | ~1.0 bar (atmospheric)       |
