// Runs the sequence orchestrator and the sequence tester side-by-side.
// Tester is wired to the orchestrator during Configure — no separate PRG_SequenceTester needed.
PROGRAM PRG_Sequence
VAR
    fbOrchestrator : FB_SequenceOrchestrator;
    fbTester       : FB_SequenceTester;
END_VAR

    fbOrchestrator();
    fbTester();

END_PROGRAM


METHOD PUBLIC Configure : BOOL
VAR_INPUT
    ipProvider : I_HardwareProvider;
END_VAR
    Configure := fbOrchestrator.Configure(ipProvider := ipProvider);
    fbTester.Configure(refOrchestrator := fbOrchestrator);
END_METHOD


METHOD PUBLIC GetStatus : ST_SequenceStatus
    GetStatus := fbOrchestrator.GetStatus();
END_METHOD
