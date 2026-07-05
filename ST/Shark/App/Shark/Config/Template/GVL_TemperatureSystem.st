(* CONFIG_TIER: 2-TEMPLATE -- Copy as-is; tune values after commissioning. *)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    // ============================================================================
    // TEMPERATURE SENSOR (PT100)
    // ============================================================================
    SENSOR_MAX_TEMP         : LREAL := 850.0;   // PT100 physical limit (°C) - all sensors use this

    // ============================================================================
    // AMBIENT TEMPERATURE SENSOR (MDP5001_300_7E2119CA — temperature channel)
    // Scaling for the TH sensor temperature channel used as SENS_CHAMBER.
    // EL32xx-compatible raw format: verified 2026-06-22, raw 242 → 24.2 °C.
    // ============================================================================
    AMBT_TEMP_RAW_MIN    : LREAL := 0.0;       // Raw at lower end of sensor range
    AMBT_TEMP_RAW_MAX    : LREAL := 30518.0;   // Raw at upper end of sensor range
    AMBT_TEMP_SCALED_MIN : LREAL := -40.0;     // Minimum ambient temperature [°C]
    AMBT_TEMP_SCALED_MAX : LREAL := 60.0;      // Maximum ambient temperature [°C]
    SENSOR_MIN_RANGE        : REAL  := -50.0;   // Minimum sensor range (°C)
    SENSOR_MAX_DEVIATION    : LREAL := 15.0;    // Max acceptable deviation between dual sensors (°C)
    SCALE_BECKHOFF_TEMP     : LREAL := 0.1;    // Beckhoff sensor scaling (0.1°C resolution)
    TEMP_RAW_AT_MAX_SENSOR  : LREAL := 8500.0; // EL32xx raw value at SENSOR_MAX_TEMP (= 850°C * 0.1)

    // ============================================================================
    // OUTPUT LIMITS
    // ============================================================================
    OUTPUT_MIN              : LREAL := 0.0;     // Minimum heater output (%)
    OUTPUT_MAX              : LREAL := 80.0;    // Maximum heater output (%) - Limited to 80% duty cycle
    OUTPUT_SAFE_DEFAULT     : LREAL := 0.0;     // Safe default output (%) - OFF
    OUTPUT_MANUAL_DEFAULT   : LREAL := 0.0;     // Manual mode output (%)

    // ============================================================================
    // CONTROLLER PARAMETERS
    // ============================================================================
    CTRL_CYCLE_TIME         : TIME := T#10MS;   // PID algorithm cycle interval
    CTRL_TASK_CYCLE_TIME    : TIME := T#10MS;   // PLC task cycle (must match task config)
    CTRL_PWM_CYCLE_TIME     : TIME := T#1S;     // PWM cycle period
    CTRL_PWM_MIN_TIME       : TIME := T#0S;     // Minimum ON/OFF time per cycle
    CTRL_ALARM_SUPPRESSION  : DWORD := 16#0000EFFF;  // Alarm suppression mask
    CTRL_SCALING_FACTOR     : LREAL := 1.0;     // Scaling for heating/cooling interactions
    CTRL_SETPOINT_TOLERANCE : LREAL := 2.0;     // At-setpoint tolerance (°C)
    CTRL_MAX_CONTINUOUS_ON  : TIME := T#10M;    // Max continuous heater-on time

    // ============================================================================
    // SAFETY & ALARM THRESHOLDS
    // ============================================================================
    TEMP_SAFETY_MARGIN      : LREAL := 10.0;    // Safety alarm margin above zone max (°C)
    TEMP_ABSOLUTE_MARGIN    : LREAL := 20.0;    // Hard shutdown margin above zone max (°C)
    ALARM_TEMP_HIGH         : LREAL := 5.0;     // Warning above setpoint (°C, relative)
    ALARM_TEMP_HIGH_HIGH    : LREAL := 10.0;    // Alarm above setpoint (°C, relative)
    ALARM_TEMP_LOW          : LREAL := -5.0;    // Warning below setpoint (°C, relative)
    ALARM_TEMP_LOW_LOW      : LREAL := -10.0;   // Alarm below setpoint (°C, relative)

    // ============================================================================
    // TUNING PARAMETERS
    // ============================================================================
    CTRL_TUNE_STABILIZATION  : TIME := T#20S;    // Stabilization wait before tuning
    TUNE_END_PERCENT_HEATING : LREAL := 80.0;    // Stop tuning at 80% of setpoint
    TUNE_Y_HEATING           : LREAL := 100.0;   // Control output during heating tune step
    TUNE_Y_STABLE            : LREAL := 0.0;     // Output to maintain stable temp before tune
    TUNE_KP                  : LREAL := 1.2;     // Multiplier for Proportional gain
    TUNE_TN                  : LREAL := 2.0;     // Multiplier for Integral time
    TUNE_TV                  : LREAL := 0.42;    // Multiplier for Derivative time
    TUNE_TD                  : LREAL := 0.25;    // Multiplier for Damping time

END_VAR
