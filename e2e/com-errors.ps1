# Offline negative check: collide a function block with the template's MAIN program.
# Native CreateChild must fail, with context and exit 1. No target is activated.
param(
    [Parameter(Mandatory=$true)] [string] $Bai,
    [Parameter(Mandatory=$true)] [string] $Work
)
$ErrorActionPreference = 'Stop'
$Work = [IO.Path]::GetFullPath($Work)
if (Test-Path -LiteralPath $Work) { throw 'Use a new folder to retain earlier evidence.' }
New-Item -ItemType Directory -Path "$Work\source" -Force | Out-Null
"FUNCTION_BLOCK MAIN`r`nVAR`r`n    nCount : UDINT;`r`nEND_VAR`r`nEND_FUNCTION_BLOCK" |
    Set-Content "$Work\source\MAIN.st" -Encoding UTF8
$previous = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $output = @(& $Bai init "$Work\source" --into "$Work\project" --name ComFailure 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
} finally { $ErrorActionPreference = $previous }
$output | Set-Content "$Work\expected-failure.log" -Encoding UTF8
$log = $output -join "`n"
if ($code -ne 1 -or $log -notmatch "ERROR: TwinCAT CreateChild failed for '[^']+\^MAIN'" -or
    $log -notmatch 'HRESULT 0x80004005' -or $log -match 'Unhandled Exception') {
    throw "Expected a controlled native creation failure; exit $code. Inspect $Work."
}
@{status='Passed'; expectedExitCode=1; physicalActivation=$false
  baiSha256=(Get-FileHash -LiteralPath $Bai -Algorithm SHA256).Hash
  completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json |
    Set-Content "$Work\validation.json" -Encoding UTF8
Write-Host "PASS native COM failure reports context and exit 1: $Work"
