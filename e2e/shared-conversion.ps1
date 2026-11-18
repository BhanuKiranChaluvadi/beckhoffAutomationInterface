# Explicit TwinCAT acceptance for shared conversion. Never activates a runtime.
param(
    [Parameter(Mandatory = $true)][string] $Bai,
    [string] $Work = 'C:/bai/work/shared-conversion-acceptance'
)
$ErrorActionPreference = 'Stop'
$run = Join-Path $Work (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
$src = Join-Path $run 'source'
New-Item -ItemType Directory -Path $src -Force | Out-Null
$previousCache = $env:BAI_CONVERSION_CACHE
try {
    $env:BAI_CONVERSION_CACHE = Join-Path $run 'cache'
    @'
INTERFACE I_Probe
METHOD Run : INT
END_METHOD
END_INTERFACE
'@ | Set-Content -LiteralPath "$src/I_Probe.st" -Encoding UTF8
    @'
FUNCTION_BLOCK FB_Probe IMPLEMENTS I_Probe
VAR
    _value : INT;
END_VAR
METHOD PUBLIC Run : INT
Run := Value;
END_METHOD
PROPERTY PUBLIC Value : INT
GET
    Value := _value;
END_GET
SET
    _value := Value;
END_SET
END_PROPERTY
END_FUNCTION_BLOCK
'@ | Set-Content -LiteralPath "$src/FB_Probe.st" -Encoding UTF8
    @'
VAR_GLOBAL
{IF defined (FIRST_MACHINE)}
    nFirstOnly : INT;
{ELSE}
    nSecondOnly : INT;
{END_IF}
END_VAR
'@ | Set-Content -LiteralPath "$src/GVL_Probe.st" -Encoding UTF8
    @'
PROGRAM MAIN
VAR
    fb : FB_Probe;
    n : INT;
END_VAR
fb.Value := 7;
n := fb.Run();
'@ | Set-Content -LiteralPath "$src/MAIN.st" -Encoding UTF8
    & $Bai convert $src
    if ($LASTEXITCODE -ne 0) { throw 'Source conversion failed.' }
    $cacheFiles = @(Get-ChildItem -LiteralPath $env:BAI_CONVERSION_CACHE -Filter '*.json' -Recurse)
    if ($cacheFiles.Count -ne 4) { throw 'Expected four complete source objects.' }
    foreach ($machine in @('First', 'Second')) {
        $define = if ($machine -eq 'First') { 'FIRST_MACHINE' } else { 'SECOND_MACHINE' }
        & $Bai init $src --into $run --name $machine --define $define
        if ($LASTEXITCODE -ne 0) { throw "$machine build failed." }
        $tmc = Get-ChildItem -LiteralPath (Join-Path $run $machine) -Filter '*.tmc' -Recurse |
            Where-Object { $_.Name -eq "$machine.tmc" } | Select-Object -First 1
        if (-not $tmc) { throw "$machine TMC was not produced." }
        $text = [IO.File]::ReadAllText($tmc.FullName)
        $expected = if ($machine -eq 'First') { 'nFirstOnly' } else { 'nSecondOnly' }
        $absent = if ($machine -eq 'First') { 'nSecondOnly' } else { 'nFirstOnly' }
        if ($text -notmatch $expected -or $text -match $absent) { throw "$machine compiled the wrong feature flags." }
    }
    $after = @(Get-ChildItem -LiteralPath $env:BAI_CONVERSION_CACHE -Filter '*.json' -Recurse)
    if ($after.Count -ne 4) { throw 'The projects did not reuse the four prepared conversions.' }
    "PASS: independent flag configurations, interfaces and properties; evidence: $run"
} finally {
    $env:BAI_CONVERSION_CACHE = $previousCache
}
