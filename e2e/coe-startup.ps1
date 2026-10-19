# Offline XAE acceptance: creates only disabled I/O and never activates a target.
param(
    [Parameter(Mandatory=$true)] [string] $Bai,
    [Parameter(Mandatory=$true)] [string] $Work
)
$ErrorActionPreference = 'Stop'
$Work = [IO.Path]::GetFullPath($Work)
if (Test-Path -LiteralPath $Work) { throw 'Use a new work folder; previous evidence is retained.' }
New-Item -ItemType Directory -Path "$Work\source" -Force | Out-Null
function Invoke-Bai([string] $Name, [string[]] $Arguments) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $Bai @Arguments 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    $output | Set-Content -LiteralPath "$Work\$Name.log" -Encoding UTF8
    if ($code -ne 0) { throw "BAI $Name failed: $Work\$Name.log" }
    Write-Host "PASS $Name"
}
function Commands([string] $File) {
    [xml] $xml = Get-Content -LiteralPath $File -Raw
    $coe = $xml.SelectSingleNode('//Terminal/CoE')
    if (-not $coe) { throw "No exported CoE section: $File" }
    return $coe.OuterXml
}
$manifest = @'
<IoTree><Device Name="Offline EtherCAT" Disabled="true"><Box Name="Coupler (EK1100)" Product="EK1100">
<Terminal Name="Analog (EL3174)" Product="EL3174"><CoE>
<InitCmd><Transition>PS</Transition><Comment>offline test 1</Comment><Timeout>1000</Timeout><Ccs>1</Ccs><Index>#x8000</Index><SubIndex>19</SubIndex><Data>0500</Data></InitCmd>
<InitCmd><Transition>PS</Transition><Comment>offline test 2</Comment><Timeout>1000</Timeout><Ccs>1</Ccs><Index>#x8010</Index><SubIndex>19</SubIndex><Data>0600</Data></InitCmd>
</CoE></Terminal></Box></Device></IoTree>
'@
$manifest | Set-Content -LiteralPath "$Work\source\io-devices.xml" -Encoding UTF8
"PROGRAM MAIN`r`nVAR`r`n    nCount : UDINT;`r`nEND_VAR`r`n    nCount := nCount + 1;`r`nEND_PROGRAM" |
    Set-Content -LiteralPath "$Work\source\MAIN.st" -Encoding UTF8
Invoke-Bai 'initial-build' @('init',"$Work\source",'--into',"$Work\first",'--name','CoeFirst')
$first = "$Work\first\CoeFirst\CoeFirst.sln"
Invoke-Bai 'initial-export' @('export','io',"$Work\export-first",'--project',$first)
$expected = Commands "$Work\export-first\io-devices.xml"
[xml] $exported = Get-Content "$Work\export-first\io-devices.xml" -Raw
if ($exported.SelectNodes('//Terminal/CoE/InitCmd').Count -ne 2) { throw 'Two user commands were not retained.' }
Invoke-Bai 'unchanged-import' @('sync','io',"$Work\export-first",'--project',$first)
if ((Get-Content "$Work\unchanged-import.log" -Raw) -notmatch '0 state change\(s\)') { throw 'Repeated import changed the project.' }
Copy-Item -LiteralPath "$Work\source\MAIN.st" -Destination "$Work\export-first\MAIN.st"
Invoke-Bai 'rebuilt' @('init',"$Work\export-first",'--into',"$Work\second",'--name','CoeSecond')
Invoke-Bai 'rebuilt-export' @('export','io',"$Work\export-second",'--project',"$Work\second\CoeSecond\CoeSecond.sln")
if ((Commands "$Work\export-second\io-devices.xml") -cne $expected) { throw 'Fresh project changed command order or content.' }
New-Item -ItemType Directory -Path "$Work\empty" | Out-Null
$exported.SelectSingleNode('//Terminal/CoE').RemoveAll()
$exported.Save("$Work\empty\io-devices.xml")
Invoke-Bai 'clear-import' @('sync','io',"$Work\empty",'--project',$first)
Invoke-Bai 'clear-export' @('export','io',"$Work\export-empty",'--project',$first)
[xml] $empty = Get-Content "$Work\export-empty\io-devices.xml" -Raw
if ($empty.SelectNodes('//Terminal/CoE/InitCmd').Count -ne 0 -or -not $empty.SelectSingleNode('//Terminal/CoE')) { throw 'Explicit empty list did not clear commands.' }
@{status='Passed'; physicalActivation=$false; userCommands=2; baiSha256=(Get-FileHash $Bai -Algorithm SHA256).Hash;
  completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content "$Work\validation.json" -Encoding UTF8
Write-Host "PASS CoE import/export, idempotence, fresh rebuild and explicit removal: $Work"
