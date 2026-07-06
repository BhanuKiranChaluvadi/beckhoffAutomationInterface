function Get-ScriptDirectory
{
	$Invocation = (Get-Variable MyInvocation -Scope 1).Value
	Split-Path $Invocation.MyCommand.Path
}

$tpyPath = Join-Path(Get-ScriptDirectory)"Sample.tpy"
$tsmPath = Join-Path(Get-ScriptDirectory)"Sample.tsm"

$systemManager = new-object -comobject TCatSysManager.TcSysManager
$systemManager.OpenConfiguration($tsmPath)

$systemManager.LinkVariables("TIPC^Sample^Standard^Outputs^MAIN.bOutput", "TIID^Device 1 (CX1100)^Box 1 (CX1100-BK)^Term 2 (KL2404)^Channel 1^Output")

$systemManager.SaveConfiguration($tsmPath)