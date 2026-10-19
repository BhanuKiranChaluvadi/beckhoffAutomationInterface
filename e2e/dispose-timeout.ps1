<# Reproduce a COM server that accepts no more calls during shutdown.
   Only the new XAE process created by this test is suspended. The session's
   shutdown watchdog must release the blocked setter and close that process.
   No project, PLC target, configuration or license is changed. #>
param(
    [Parameter(Mandatory=$true)][string]$Bai,
    [Parameter(Mandatory=$true)][string]$Evidence
)
$ErrorActionPreference='Stop'
if(@(Get-Process TcXaeShell -ErrorAction SilentlyContinue).Count -ge 2){throw 'Two XAE processes are already active.'}
if(Test-Path -LiteralPath $Evidence){throw 'Use a new evidence directory.'}
New-Item -ItemType Directory -Path $Evidence | Out-Null
$bin=Split-Path (Get-Item -LiteralPath $Bai).FullName -Parent
foreach($dll in 'EnvDTE.dll','EnvDTE80.dll','Interop.TCatSysManager.dll'){
    [void][Reflection.Assembly]::LoadFrom("$bin/$dll")
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DisposeTimeoutFixture {
    [DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] public static extern int NtResumeProcess(IntPtr process);
}
'@
$assembly=[Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $Bai).FullName)
$before=@(Get-Process TcXaeShell -ErrorAction SilentlyContinue | ForEach-Object Id)
$session=$assembly.GetType('BeckhoffAutomationInterface.VisualStudioSession').GetMethod('Start').Invoke($null,[object[]]@('xae'))
$ownedPid=0
$process=$null
$suspended=$false
$disposed=$false
try{
    $ownedPid=$session.GetType().GetField('_devenvPid',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($session)
    if($ownedPid -le 0 -or $ownedPid -in $before){throw 'Cannot prove ownership of the test shell.'}
    $process=Get-Process -Id $ownedPid
    if($process.ProcessName -ne 'TcXaeShell'){throw 'Unexpected owned process.'}
    if([DisposeTimeoutFixture]::NtSuspendProcess($process.Handle) -ne 0){throw 'Could not suspend the owned test shell.'}
    $suspended=$true
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $session.Dispose()
    $disposed=$true
    $timer.Stop()
    if(-not $process.HasExited -or $timer.Elapsed.TotalSeconds -gt 45){throw 'Shutdown watchdog did not release the blocked COM call.'}
    @{status='Passed';scope='Owned XAE shutdown with a suspended COM server';pid=$ownedPid;seconds=$timer.Elapsed.TotalSeconds;
      baiSha256=(Get-FileHash -LiteralPath $Bai).Hash;runtimeChanged=$false} |
        ConvertTo-Json | Set-Content "$Evidence/validation.json" -Encoding UTF8
    Write-Host "PASS: blocked shutdown completed in $($timer.Elapsed.TotalSeconds) s."
}finally{
    if($process -and -not $process.HasExited){
        if($suspended){[void][DisposeTimeoutFixture]::NtResumeProcess($process.Handle)}
    }
    if(-not $disposed){$session.Dispose()}
    if($process){$process.Dispose()}
}
