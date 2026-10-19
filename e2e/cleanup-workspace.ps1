# Filesystem acceptance; never creates, moves or deletes real PLC work folders.
$ErrorActionPreference='Stop'
$script=Join-Path $PSScriptRoot '../scripts/Manage-BaiWorkspace.ps1'
$base=Join-Path ([IO.Path]::GetTempPath()) ('bai-cleanup-test-'+[Guid]::NewGuid().ToString('N'))
$root=Join-Path $base 'root'
$collection=Join-Path $root 'bai'
New-Item -ItemType Directory -Force $root | Out-Null
$fixtureName='bai-boot-transaction-'+[Guid]::NewGuid().ToString('N')
$fixture=Join-Path $root $fixtureName
New-Item -ItemType Directory $fixture | Out-Null
'{"status":"Passed","scope":"filesystem transaction; not runtime activation"}' | Set-Content "$fixture/validation.json"
'keep these bytes' | Set-Content "$fixture/payload.txt"
$expected=(Get-FileHash "$fixture/payload.txt").Hash
foreach($name in 'bai-experiment','bai-source','bai-unfinished','bai-offline-completion','bai-linked'){
    New-Item -ItemType Directory -Path "$root/$name" | Out-Null
    'valuable content' | Set-Content "$root/$name/data.txt"
}
New-Item -ItemType Directory "$root/bai-source/.git" | Out-Null
New-Item -ItemType Directory "$base/outside" | Out-Null
'outside content' | Set-Content "$base/outside/keep.txt"
New-Item -ItemType Junction -Path "$root/bai-linked/junction" -Target "$base/outside" | Out-Null
function Check([bool]$Condition,[string]$Message){if(-not $Condition){throw "FAIL: $Message"};Write-Host "PASS: $Message"}
function Run([string[]]$Extra,[int]$Expected=0){
    $log=Join-Path $base ([Guid]::NewGuid().ToString('N')+'.log')
    $old=$ErrorActionPreference
    try{$ErrorActionPreference='Continue'; & powershell -NoProfile -File $script -Root $root -CollectionRoot $collection -MinimumAgeDays 0 @Extra *> $log; $code=$LASTEXITCODE}
    finally{$ErrorActionPreference=$old}
    if(($Expected -eq 0 -and $code -ne 0) -or ($Expected -ne 0 -and $code -eq 0)){throw "Unexpected exit $code. Evidence: $log"}
}
Run @('-Action','Consolidate')
Check ((Test-Path "$fixture/payload.txt") -and -not (Test-Path "$collection/fixtures/$fixtureName")) 'preview does not move content'
Run @('-Action','Consolidate','-Apply')
Check ((Get-FileHash "$collection/fixtures/$fixtureName/payload.txt").Hash -eq $expected) 'move preserves fixture bytes'
Check ((Test-Path "$collection/archive/bai-experiment/data.txt") -and -not (Test-Path "$root/bai-experiment")) 'inactive experiment is consolidated'
Check ((Test-Path "$root/bai-source/.git") -and (Test-Path "$root/bai-offline-completion/data.txt")) 'Git source and release evidence remain in place'
Check ((Test-Path "$root/bai-linked/junction") -and (Test-Path "$base/outside/keep.txt")) 'junction tree and external target remain untouched'
$reports=@(Get-ChildItem "$collection/cleanup" -Filter *.json)
$moves=@($reports | ForEach-Object {(Get-Content $_.FullName -Raw | ConvertFrom-Json).folders} | Where-Object {$_.Result -eq 'Moved'})
Check (@($moves | Where-Object {$_.Path -eq $fixture -and $_.Destination -eq "$collection\fixtures\$fixtureName"}).Count -eq 1) 'old/new paths are recorded'
New-Item -ItemType Directory "$root/bai-experiment" | Out-Null
'new source' | Set-Content "$root/bai-experiment/data.txt"
Run @('-Action','Consolidate','-Names','bai-experiment','-Apply') 1
Check ((Get-Content "$root/bai-experiment/data.txt") -eq 'new source') 'existing destination refuses overwrite and preserves source'
Run @('-Action','DeleteFixtures')
Check (Test-Path "$collection/fixtures/$fixtureName/payload.txt") 'delete preview leaves fixture intact'
Run @('-Action','DeleteFixtures','-Apply')
Check (-not (Test-Path "$collection/fixtures/$fixtureName")) 'only completed synthetic fixture is deleted'
Check ((Test-Path "$root/bai-experiment/data.txt") -and (Test-Path "$collection/archive/bai-unfinished/data.txt")) 'deletion never removes arbitrary experiments'
Run @('-Action','DeleteFixtures','-Names','../outside','-Apply') 1
Check (Test-Path "$base/outside/keep.txt") 'traversal is rejected'
@{status='Passed';scope='filesystem cleanup fixtures';checks=11} | ConvertTo-Json | Set-Content "$base/result.json"
Write-Host "PASS: cleanup acceptance. Evidence: $base"
