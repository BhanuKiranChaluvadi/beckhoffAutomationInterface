function Get-ScriptDirectory
{
	$Invocation = (Get-Variable MyInvocation -Scope 1).Value
	Split-Path $Invocation.MyCommand.Path
}

$tsmPath = Join-Path(Get-ScriptDirectory)"Sample.tsm"

$systemManager = new-object -comobject TCatSysManager.TcSysManager
$systemManager.OpenConfiguration($tsmPath)
$systemManager.ActivateConfiguration()
$systemManager.StartRestartTwinCAT()
