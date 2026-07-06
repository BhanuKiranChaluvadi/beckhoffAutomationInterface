function Get-ScriptDirectory
{
	$Invocation = (Get-Variable MyInvocation -Scope 1).Value
	Split-Path $Invocation.MyCommand.Path
}

$tpyPath = Join-Path(Get-ScriptDirectory)"Sample.tpy"
$tsmPath = Join-Path(Get-ScriptDirectory)"Sample.tsm"

$systemManager = new-object -comobject TCatSysManager.TcSysManager
$systemManager.NewConfiguration()

$plcNode = $systemManager.LookupTreeItem("TIPC")
$plc = $plcNode.CreateChild($tpyPath, 0, "", $null)
$plcProject = $systemManager.LookupTreeItem("TIPC^Sample")
$systemManager.SaveConfiguration($tsmPath)