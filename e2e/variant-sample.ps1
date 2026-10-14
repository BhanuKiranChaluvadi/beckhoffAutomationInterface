<#
.SYNOPSIS
    End-to-end closed loop for ST/VariantSample on a real TwinCAT runtime:
    init (sync all) -> build all variants -> for each variant: activate, verify it, verify the
    other one (must fail). Every step's exit code is compared with the expected one.

.DESCRIPTION
    Needs TwinCAT XAE and a running UmRT_Default with a valid license (run 'doctor' first).
    Activation REPLACES UmRT_Default's boot project. The project is created in a fresh
    temporary folder and never touches ST/ except for reading it.

.EXAMPLE
    .\e2e\variant-sample.ps1
    .\e2e\variant-sample.ps1 -Exe .\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe
#>
param(
    [string] $Exe    = (Join-Path $PSScriptRoot '..\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe'),
    [string] $Target = 'umrt',
    [string] $Work   = (Join-Path ([IO.Path]::GetTempPath()) ('bai-e2e-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$Exe    = (Resolve-Path $Exe).Path
$source = (Resolve-Path (Join-Path $PSScriptRoot '..\ST\VariantSample')).Path
$expect = Join-Path $source 'expect.json'
New-Item -ItemType Directory -Force $Work | Out-Null

$results = New-Object System.Collections.Generic.List[object]
function Step([string] $name, [int] $expectedExit, [string[]] $argv) {
    Write-Host "`n=== $name" -ForegroundColor Cyan
    $start = Get-Date
    # Capture everything first: piping a native exe into Select-Object -First kills it early.
    $out = & $Exe @argv 2>&1
    $code = $LASTEXITCODE
    $out | Where-Object { "$_" -match 'PLC MESSAGE|PASSED|FAILED|ACTIVATED|NOT ACTIVATED|OK  |FAIL |ERROR' } | ForEach-Object { "  $_" }
    $ok = $code -eq $expectedExit
    $results.Add([pscustomobject]@{ Step = $name; Expected = $expectedExit; Actual = $code; AsExpected = $ok;
                                     Seconds = [int]((Get-Date) - $start).TotalSeconds })
    Write-Host ("  exit {0} (expected {1}) -> {2}" -f $code, $expectedExit, $(if ($ok) { 'AS EXPECTED' } else { 'UNEXPECTED' })) `
        -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

Push-Location $Work
try {
    Step 'doctor'                       0 @('doctor')
    Step 'init (sync all + build all)'  0 @('init', $source, '--dest', $Work)
    $tsproj = (Get-ChildItem $Work -Recurse -Filter 'VariantSample.tsproj' | Select-Object -First 1).FullName
    Step 'build (all variants)'         0 @('build', $tsproj)
    Step 'activate without --yes'       1 @('activate', $tsproj, '--target', $Target, '--variant', 'MachineA')
    foreach ($pair in @(@('MachineA', 'MachineB'), @('MachineB', 'MachineA'))) {
        $run, $other = $pair
        Step "activate $run"                     0 @('activate', $tsproj, '--target', $Target, '--variant', $run, '--yes')
        Step "verify $run (running)"             0 @('verify', '--target', $Target, '--expect', $expect, '--variant', $run)
        Step "verify $other (must fail)"         1 @('verify', '--target', $Target, '--expect', $expect, '--variant', $other)
    }
}
finally {
    Pop-Location
}

Write-Host "`n=== Summary ($Work)" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host
$bad = @($results | Where-Object { -not $_.AsExpected })
if ($bad.Count -eq 0) { Write-Host "E2E PASSED: all $($results.Count) steps as expected." -ForegroundColor Green; exit 0 }
Write-Host "E2E FAILED: $($bad.Count) of $($results.Count) steps not as expected." -ForegroundColor Red
exit 1
