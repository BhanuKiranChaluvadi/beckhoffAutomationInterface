PROGRAM PRG_MFC
VAR
    // Hardware IO - MFC Controllers (Bronkhorst EtherCAT)
    // Declared as VAR (not VAR_INPUT) so TwinCAT I/O linker exposes AT %I*/%Q*
    // members for EtherCAT PDO mapping. PRG_SIMULATION writes to these directly
    // via PRG_MFC.IO_MFC_*.MeasuredValue — this works identically for VAR.
    IO_MFC_Precursor : ST_BronkhorstController_IO;  // Range: 0-25 sccm
    IO_MFC_Reactant  : ST_BronkhorstController_IO;  // Range: 0-25 sccm
    IO_MFC_Inert     : ST_BronkhorstController_IO;  // Range: 0-500 sccm
END_VAR
VAR
    // Subsystem and Manual Panel
    fbFlowSubsystem : FB_FlowSubsystem;
    fbManualPanel   : FB_FlowManualPanel;

    // Adapters
    adapterPrecursor : FB_BronkhorstEtherCATAdapter;
    adapterReactant  : FB_BronkhorstEtherCATAdapter;
    adapterInert     : FB_BronkhorstEtherCATAdapter;

    // Controllers
    mfcPrecursor : FB_BronkhorstController;
    mfcReactant  : FB_BronkhorstController;
    mfcInert     : FB_BronkhorstController;

    bInitialized : BOOL := FALSE;
END_VAR

    IF NOT bInitialized THEN
        _Init();
    END_IF

    // Execute Controllers
    mfcPrecursor.Execute();
    mfcReactant.Execute();
    mfcInert.Execute();

    // Execute Subsystem (Error Aggregation)
    fbFlowSubsystem.Execute();

    // Manual Panel
    fbManualPanel(ipSubsystem := fbFlowSubsystem, nMaxZone := GVL_FlowSystem.FLOW_ZONE_COUNT);

METHOD PRIVATE _Init
VAR
    aipControllers : ARRAY[1..GVL_FlowSystem.FLOW_ZONE_COUNT] OF I_Controller;
END_VAR
    _InitMfc(
        eId          := E_MFCIdentifier.MFCT_A_01,
        stIo         := IO_MFC_Precursor,
        fbAdapter    := adapterPrecursor,
        fbController := mfcPrecursor,
        rMinValue    := GVL_FlowSystem.MIN_FLOW_PRECURSOR_SCCM,
        rMaxValue    := GVL_FlowSystem.MAX_FLOW_PRECURSOR_SCCM
    );
    _InitMfc(
        eId          := E_MFCIdentifier.MFCT_B_01,
        stIo         := IO_MFC_Reactant,
        fbAdapter    := adapterReactant,
        fbController := mfcReactant,
        rMinValue    := GVL_FlowSystem.MIN_FLOW_REACTANT_SCCM,
        rMaxValue    := GVL_FlowSystem.MAX_FLOW_REACTANT_SCCM
    );
    _InitMfc(
        eId          := E_MFCIdentifier.MFCT_IG_01,
        stIo         := IO_MFC_Inert,
        fbAdapter    := adapterInert,
        fbController := mfcInert,
        rMinValue    := GVL_FlowSystem.MIN_FLOW_INERT_SCCM,
        rMaxValue    := GVL_FlowSystem.MAX_FLOW_INERT_SCCM
    );

    // Subsystem Initialization
    aipControllers[E_MFCIdentifier.MFCT_A_01]  := mfcPrecursor;
    aipControllers[E_MFCIdentifier.MFCT_B_01]  := mfcReactant;
    aipControllers[E_MFCIdentifier.MFCT_IG_01] := mfcInert;

    fbFlowSubsystem.Init(aipControllers := aipControllers, nCount := GVL_FlowSystem.FLOW_ZONE_COUNT);
    bInitialized := TRUE;
END_METHOD

METHOD PRIVATE _InitMfc
VAR_INPUT
    eId       : E_MFCIdentifier;
    rMinValue : REAL;
    rMaxValue : REAL;
END_VAR
VAR_IN_OUT
    stIo         : ST_BronkhorstController_IO;
    fbAdapter    : FB_BronkhorstEtherCATAdapter;
    fbController : FB_BronkhorstController;
END_VAR
    fbAdapter.Link(
        refMeasuredValue     := stIo.MeasuredValue,
        refActualSetpoint    := stIo.ActualSetpoint,
        refValveOutput       := stIo.ValveOutput,
        refCommandedSetpoint := stIo.CommandedSetpoint,
        refControlByte       := stIo.ControlByte,
        refControlMode       := stIo.ControlMode,
        rMinValue            := REAL_TO_LREAL(rMinValue),
        rMaxValue            := REAL_TO_LREAL(rMaxValue)
    );
    fbController.Init(
        sInstanceName      := TO_STRING(eId),
        ipHardware         := fbAdapter,
        rMinValue          := rMinValue,
        rMaxValue          := rMaxValue,
        bInverseControl    := FALSE,
        rBoundaryTolerance := GVL_FlowSystem.SETPOINT_TOLERANCE
    );
END_METHOD

END_PROGRAM
