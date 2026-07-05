(* CONFIG_TIER: 1-MACHINE  -- Update all values for every new machine build. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // Pressure Sensor Count (analog read-only sensors)
    SENSOR_COUNT     : INT := 4;

    // Pressure Controller Count (Bronkhorst Modbus, managed by PRG_BronkhorstPressure)
    CONTROLLER_COUNT : INT := 2;

    // Pressure Limits
    MIN_PRESSURE_BAR : REAL := 0.0;
    MAX_PRESSURE_BAR : REAL := 3.0;

    // Pressure Controller Constants (Bronkhorst Modbus needle valves)
    MIN_PRESSURE_CTRL_BAR  : REAL := 0.0;
    MAX_PRESSURE_CTRL_BAR  : REAL := 3.0;
    SETPOINT_TOLERANCE     : REAL := 0.01;
    MODBUS_FIRST_SLAVE_ADDRESS : BYTE := 1;

    // Sensor-specific raw scaling (from old FB_PressureSensorManager)
    // PSNS_A_DST: 0-30518 raw → 0.0-3.0 bar(a)
    PSNS_A_DST_RAW_MIN      : LREAL := 0.0;
    PSNS_A_DST_RAW_MAX      : LREAL := 30518.0;
    PSNS_A_DST_SCALED_MIN   : LREAL := 0.0;
    PSNS_A_DST_SCALED_MAX   : LREAL := 3.0;

    // PSNS_B_DST: 0-30518 raw → 0.0-3.0 bar(a)
    PSNS_B_DST_RAW_MIN      : LREAL := 0.0;
    PSNS_B_DST_RAW_MAX      : LREAL := 30518.0;
    PSNS_B_DST_SCALED_MIN   : LREAL := 0.0;
    PSNS_B_DST_SCALED_MAX   : LREAL := 3.0;

    // PSNS_AB_EXH: 0-30518 raw → 0.0-1.0 bar(a)
    PSNS_AB_EXH_RAW_MIN     : LREAL := 0.0;
    PSNS_AB_EXH_RAW_MAX     : LREAL := 30518.0;
    PSNS_AB_EXH_SCALED_MIN  : LREAL := 0.0;
    PSNS_AB_EXH_SCALED_MAX  : LREAL := 1.0;

    // PSNS_CHK_VAC: 3052-15259 raw → 1.01325-0.00325 bar(a) [INVERTED]
    // NOTE: Physical relationship is inverted — higher raw = lower pressure (more vacuum).
    //       FB_AnalogSensor requires ascending ranges, so we store ascending params here.
    //       Reading will be physically inverted: actual_bar = 1.0165 - sensor_reading
    PSNS_CHK_VAC_RAW_MIN    : LREAL := 3052.0;
    PSNS_CHK_VAC_RAW_MAX    : LREAL := 15259.0;
    PSNS_CHK_VAC_SCALED_MIN : LREAL := 0.00325;
    PSNS_CHK_VAC_SCALED_MAX : LREAL := 1.01325;
END_VAR
