# Offline acceptance against retained compiled artifacts. Never activates a PLC.
param(
    [Parameter(Mandatory=$true)][string]$Bai,
    [Parameter(Mandatory=$true)][string]$Package,
    [Parameter(Mandatory=$true)][string]$PackageSha256,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$Machine,
    [string]$Variant='',
    [Parameter(Mandatory=$true)][string]$Platform,
    [Parameter(Mandatory=$true)][string]$LibraryLock,
    [Parameter(Mandatory=$true)][string]$LibraryLockSha256,
    [Parameter(Mandatory=$true)][string]$Work
)
$ErrorActionPreference='Stop'
$Work=[IO.Path]::GetFullPath($Work)
if(Test-Path -LiteralPath $Work){throw 'Use a new work folder to retain earlier evidence.'}
New-Item -ItemType Directory $Work | Out-Null
function Invoke-Artifact([string]$Name,[string[]]$Arguments,[int]$Expected=0){
    $previous=$ErrorActionPreference
    try{$ErrorActionPreference='Continue';$output=@(& $Bai @Arguments 2>&1 | ForEach-Object {"$_"});$code=$LASTEXITCODE}
    finally{$ErrorActionPreference=$previous}
    $output | Set-Content "$Work/$Name.log" -Encoding UTF8
    if($code -ne $Expected){throw "$Name returned $code; expected $Expected. See $Work/$Name.log"}
    Write-Host "PASS: $Name"
    if($Expected -eq 0){return ($output -join "`n" | ConvertFrom-Json)}
}
$verified=Invoke-Artifact 'verify-original' @('package','verify','--package',$Package,'--sha256',$PackageSha256,'--json')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($Package,"$Work/payload")
$metadata=Get-Content "$Work/payload/release.json" -Raw | ConvertFrom-Json
$metadata.PSObject.Properties.Remove('files')
$metadata | ConvertTo-Json -Depth 30 | Set-Content "$Work/metadata.json" -Encoding UTF8
# Only the just-extracted manifest is removed; the retained input package stays unchanged.
Remove-Item -LiteralPath "$Work/payload/release.json"
$created=Invoke-Artifact 'create' @('package','create','--payload',"$Work/payload",'--metadata',"$Work/metadata.json",'--output',"$Work/repacked.zip",'--json')
$repacked=Invoke-Artifact 'verify-repacked' @('package','verify','--package',"$Work/repacked.zip",'--sha256',$created.sha256,'--json')
$old=@($verified.files | Sort-Object path | ForEach-Object {"$($_.path)|$($_.bytes)|$($_.sha256.ToLowerInvariant())"})
$new=@($repacked.files | Sort-Object path | ForEach-Object {"$($_.path)|$($_.bytes)|$($_.sha256.ToLowerInvariant())"})
if(Compare-Object $old $new){throw 'Repackaged payload differs from retained package.'}
Invoke-Artifact 'reject-wrong-package-hash' @('package','verify','--package',"$Work/repacked.zip",'--sha256',('0'*64),'--json') 1
$identity=@('--project',$Project,'--machine',$Machine,'--platform',$Platform)
if($Variant){$identity+=@('--variant',$Variant)}
Invoke-Artifact 'libraries-verify' (@('libraries','verify')+$identity+@('--library-lock',$LibraryLock,'--library-lock-sha256',$LibraryLockSha256,'--json')) | Out-Null
$captured=Invoke-Artifact 'libraries-lock' (@('libraries','lock')+$identity+@('--output',"$Work/libraries.lock.json",'--json'))
Invoke-Artifact 'libraries-roundtrip' (@('libraries','verify')+$identity+@('--library-lock',"$Work/libraries.lock.json",'--library-lock-sha256',$captured.sha256,'--json')) | Out-Null
Invoke-Artifact 'reject-wrong-library-hash' (@('libraries','verify')+$identity+@('--library-lock',$LibraryLock,'--library-lock-sha256',('0'*64),'--json')) 1
@{status='Passed';scope='Offline artifact migration';sourcePackageSha256=$PackageSha256;repackedSha256=$created.sha256;
    baiSha256=(Get-FileHash $Bai).Hash;payloadFiles=$verified.files.Count;physicalDeployment=$false} |
    ConvertTo-Json | Set-Content "$Work/result.json" -Encoding UTF8
Write-Host "PASS: artifact migration acceptance. Evidence: $Work"
