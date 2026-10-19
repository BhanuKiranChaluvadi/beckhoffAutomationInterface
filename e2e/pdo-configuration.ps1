# Creates disabled third-party I/O, maps raw UINT measurements/setpoints, and
# rebuilds from reverse export. No target activation or online mailbox access.
param([Parameter(Mandatory=$true)][string]$Bai, [Parameter(Mandatory=$true)][string]$Work)
$ErrorActionPreference='Stop'
$Work=[IO.Path]::GetFullPath($Work)
if (Test-Path -LiteralPath $Work) { throw 'Use a new work folder.' }
New-Item -ItemType Directory -Path "$Work/source" -Force | Out-Null
function Run([string]$Stage,[string[]]$Arguments) {
    $previous=$ErrorActionPreference
    try { $ErrorActionPreference='Continue'; $output=@(& $Bai @Arguments 2>&1 | ForEach-Object { "$_" }); $code=$LASTEXITCODE }
    finally { $ErrorActionPreference=$previous }
    $output | Set-Content "$Work/$Stage.log" -Encoding UTF8
    if ($code -ne 0) { throw "$Stage failed; see $Work/$Stage.log" }
    Write-Host "PASS $Stage"
}
@'
PROGRAM MAIN
VAR
    nMeasure AT %I* : UINT;
    nActual AT %I* : UINT;
    nCommand AT %Q* : UINT;
    nControl AT %Q* : USINT;
    aValves AT %Q* : ARRAY[0..3] OF BYTE;
END_VAR
    nCommand := 0;
END_PROGRAM
'@ | Set-Content "$Work/source/MAIN.st" -Encoding UTF8
@'
<IoTree><Device Name="Offline EtherCAT" Disabled="true">
<Box Name="MFC" VendorId="1387" ProductCode="7" RevisionNo="8"/>
<Box Name="Valves" VendorId="276" ProductCode="#x01000001" RevisionNo="#x00010002"/>
</Device></IoTree>
'@ | Set-Content "$Work/source/io-devices.xml" -Encoding UTF8
Run 'create-devices' @('init',"$Work/source",'--into',"$Work/first",'--name','PdoFirst','--plc','NFLite')
$project="$Work/first/PdoFirst/PdoFirst.sln"
Run 'export-defaults' @('export','io',"$Work/defaults",'--project',$project)
[xml]$xml=Get-Content "$Work/defaults/io-devices.xml" -Raw
$data=$xml.SelectSingleNode('//*[@Name="MFC"]/ProcessData')
if (-not $data) { throw 'No native MFC ProcessData exported.' }
function Entry([string]$Name,[int]$Sub) {
    $e=$xml.CreateElement('Entry')
    foreach ($pair in @(@('Index','#x2001'),@('SubIndex',"$Sub"),@('BitLen','16'),@('Name',$Name),@('DataType','UINT'))) {
        $child=$xml.CreateElement($pair[0]);$child.InnerText=$pair[1];[void]$e.AppendChild($child)
    }
    return $e
}
$tx=$data.SelectSingleNode('TxPdo[Name="TxPDO"]');$rx=$data.SelectSingleNode('RxPdo[Name="RxPDO"]')
foreach ($pdo in @($tx,$rx)) { foreach ($entry in @($pdo.SelectNodes('Entry'))) { [void]$pdo.RemoveChild($entry) } }
[void]$tx.AppendChild((Entry 'Measure' 1));[void]$tx.AppendChild((Entry 'Setpoint' 2));[void]$rx.AppendChild((Entry 'Setpoint' 2))
$links=$xml.CreateElement('Links');[void]$xml.DocumentElement.AppendChild($links)
foreach ($pair in @(@('Inputs^MAIN.nMeasure','MFC^TxPDO^Measure'),@('Inputs^MAIN.nActual','MFC^TxPDO^Setpoint'),@('Outputs^MAIN.nCommand','MFC^RxPDO^Setpoint'),@('Outputs^MAIN.nControl','MFC^CO RxPDO^CO Control byte'))) {
    $link=$xml.CreateElement('Link');$link.SetAttribute('PlcVar',"NFLite Instance^PlcTask $($pair[0])");$link.SetAttribute('IoChannel',"Offline EtherCAT^$($pair[1])");[void]$links.AppendChild($link)
}
foreach ($i in 0..3) {
    $link=$xml.CreateElement('Link');$link.SetAttribute('PlcVar',"NFLite Instance^PlcTask Outputs^MAIN.aValves[$i]");$link.SetAttribute('IoChannel',"Offline EtherCAT^Valves^Byte $i^Output");[void]$links.AppendChild($link)
}
$xml.Save("$Work/source/io-devices.xml")
Run 'apply-and-link' @('sync','all',"$Work/source",'--project',$project)
if ((Get-Content "$Work/apply-and-link.log" -Raw) -notmatch '8 linked, 0 unresolved') { throw 'Not all eight PDO links resolved.' }
Run 'export-configured' @('export','io',"$Work/configured",'--project',$project)
Run 'unchanged-import' @('sync','io',"$Work/configured",'--project',$project)
if ((Get-Content "$Work/unchanged-import.log" -Raw) -notmatch '0 state change\(s\)') { throw 'PDO import was not idempotent.' }
Copy-Item "$Work/source/MAIN.st" "$Work/configured/MAIN.st"
# io export and links export are separate: copy the checked links into the rebuild source.
[xml]$configured=Get-Content "$Work/configured/io-devices.xml" -Raw
[void]$configured.DocumentElement.AppendChild($configured.ImportNode($links,$true));$configured.Save("$Work/configured/io-devices.xml")
Run 'rebuild' @('init',"$Work/configured",'--into',"$Work/second",'--name','PdoSecond','--plc','NFLite')
Run 'rebuild-export' @('export','io',"$Work/rebuilt",'--project',"$Work/second/PdoSecond/PdoSecond.sln")
[xml]$rebuilt=Get-Content "$Work/rebuilt/io-devices.xml" -Raw
if ($configured.SelectSingleNode('//*[@Name="MFC"]/ProcessData').OuterXml -cne $rebuilt.SelectSingleNode('//*[@Name="MFC"]/ProcessData').OuterXml) { throw 'PDO configuration differs after a fresh rebuild.' }
@{status='Passed';linked=8;physicalActivation=$false;baiSha256=(Get-FileHash $Bai).Hash;completedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content "$Work/validation.json"
Write-Host "PASS PDO import/export and fresh rebuild: $Work"
