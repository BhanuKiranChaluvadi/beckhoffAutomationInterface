<#
.SYNOPSIS
    End-to-end check of per-variant I/O for ST/VariantIoSample: init (sync all) -> sync io again
    (no change) -> export io -> sync the export (no change) -> export again (identical) -> build
    all variants. Every step's exit code and key output line is compared with the expected one.

.DESCRIPTION
    Needs TwinCAT XAE. No runtime and no activation: it only creates a project in a fresh
    temporary folder. ST/ is only read.

.EXAMPLE
    .\e2e\variant-io-sample.ps1
#>
param(
    [string] $Exe  = (Join-Path $PSScriptRoot '..\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe'),
    [string] $Work = (Join-Path ([IO.Path]::GetTempPath()) ('bai-e2e-io-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$Exe    = (Resolve-Path $Exe).Path
$source = (Resolve-Path (Join-Path $PSScriptRoot '..\ST\VariantIoSample')).Path
$name   = 'VariantIoSample'
New-Item -ItemType Directory -Force $Work, "$Work\export1", "$Work\export2" | Out-Null

$results = New-Object System.Collections.Generic.List[object]
function Step([string] $label, [int] $expectedExit, [string] $mustContain, [string[]] $argv) {
    Write-Host "`n=== $label" -ForegroundColor Cyan
    # Capture everything first: piping a native exe into Select-Object -First kills it early.
    $out = & $Exe @argv 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $out | Where-Object { $_ -match 'Per-variant IO|~ variants|some variants only|PASSED|FAILED|VARIANTS FAILED' } | ForEach-Object { "  $_" }
    $found = [string]::IsNullOrEmpty($mustContain) -or ($out | Where-Object { $_ -like "*$mustContain*" })
    $ok = ($code -eq $expectedExit) -and [bool]$found
    $results.Add([pscustomobject]@{ Step = $label; ExpectedExit = $expectedExit; Exit = $code; Expected = $mustContain; AsExpected = $ok })
    Write-Host ("  exit {0} (expected {1}), '{2}' found: {3} -> {4}" -f $code, $expectedExit, $mustContain, [bool]$found, $(if ($ok) { 'AS EXPECTED' } else { 'UNEXPECTED' })) `
        -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

Push-Location $Work
try {
    Step 'init (sync all + build all)'   0 'Per-variant IO: 1 item(s), 1 changed'   @('init', $source, '--into', $Work, '--name', $name)
    Step 'sync io again'                 0 'Per-variant IO: 1 item(s), 0 changed'   @('sync', 'io', $source, '--project', (Join-Path $Work $name))
    $tsproj = (Get-ChildItem $Work -Recurse -Filter "$name.tsproj" | Select-Object -First 1).FullName
    Step 'export io'                     0 '1 I/O item(s) exist in some variants only' @('export', 'io', "$Work\export1", '--project', $tsproj)
    Copy-Item (Join-Path $source 'variants.xml') "$Work\export1"
    Step 'sync io from the export'       0 'Per-variant IO: 1 item(s), 0 changed'   @('sync', 'io', "$Work\export1", '--project', (Join-Path $Work $name))
    Step 'export io again'               0 'Exported IO tree'                       @('export', 'io', "$Work\export2", '--project', $tsproj)
    Step 'build (all variants)'          0 'ALL 2 VARIANT(S) PASSED'                @('build', $tsproj)
}
finally {
    Pop-Location
}

$same = (Get-FileHash "$Work\export1\io-devices.xml").Hash -eq (Get-FileHash "$Work\export2\io-devices.xml").Hash
$attr = Select-String -Path "$Work\export1\io-devices.xml" -Pattern 'Term 3 \(EL2008\)" Product="EL2008" Variants="MachineA"' -Quiet
$results.Add([pscustomobject]@{ Step = 'export1 == export2 (byte-identical)'; ExpectedExit = '-'; Exit = '-'; Expected = 'identical'; AsExpected = $same })
$results.Add([pscustomobject]@{ Step = 'export has EL2008 Variants="MachineA"'; ExpectedExit = '-'; Exit = '-'; Expected = 'attribute'; AsExpected = $attr })

Write-Host "`n=== Summary ($Work)" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host
$bad = @($results | Where-Object { -not $_.AsExpected })
if ($bad.Count -eq 0) { Write-Host "E2E PASSED: all $($results.Count) checks as expected." -ForegroundColor Green; exit 0 }
Write-Host "E2E FAILED: $($bad.Count) of $($results.Count) checks not as expected." -ForegroundColor Red
exit 1
