<#
Inventory legacy bai-* folders. Preview is the default for every action.
Consolidate moves inactive folders without deleting their contents. DeleteFixtures
only deletes completed synthetic test fixtures, never arbitrary generated projects.
Absolute paths in archived projects/logs remain historical; rebuild before reuse.
#>
[CmdletBinding()]
param(
    [ValidateSet('Report','Consolidate','DeleteFixtures')][string]$Action='Report',
    [string]$Root='C:\',
    [string]$CollectionRoot='C:\bai',
    [string[]]$Names=@(),
    [ValidateRange(0,3650)][int]$MinimumAgeDays=1,
    [string[]]$ProtectPaths=@(),
    [switch]$Apply
)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$CollectionRoot=[IO.Path]::GetFullPath($CollectionRoot).TrimEnd('\','/')
function Is-Within([string]$Path,[string]$Parent) {
    $p=[IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $r=[IO.Path]::GetFullPath($Parent).TrimEnd('\','/')
    return $p.Equals($r,[StringComparison]::OrdinalIgnoreCase) -or
        $p.StartsWith($r+'\',[StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoLinkAncestors([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor){
        if(Test-Path -LiteralPath $cursor){
            $item=Get-Item -LiteralPath $cursor -Force
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked path refused: $cursor"}
        }
        $parent=Split-Path $cursor -Parent
        if($parent -eq $cursor){break}
        $cursor=$parent
    }
}
Assert-NoLinkAncestors $Root
Assert-NoLinkAncestors $CollectionRoot
if(-not (Test-Path -LiteralPath $Root -PathType Container)){throw 'Root must exist.'}
if(-not (Is-Within $CollectionRoot $Root) -or $CollectionRoot -ieq $Root.TrimEnd('\','/')){throw 'CollectionRoot must be a subfolder of Root.'}
if((Split-Path $CollectionRoot -Leaf) -like 'bai-*'){throw 'Use a collection folder such as C:\bai, not another bai-* folder.'}
foreach($name in $Names){if($name -notmatch '^bai-[A-Za-z0-9_-]+$'){throw "Use an exact bai-* folder name: $name"}}
$protected=@{
    'bai-command-migration'='Tool migration checkout, pinned executables and test evidence'
    'bai-offline-completion'='Retained release bundles, toolchain archive and validation receipts'
    'bai-test'='Retained scenario, package and identity evidence'
    'bai-warning-fix-20261019'='Separate warning-fix work; retain until its owner finishes'
    'bai-lock'='Shared runtime lock; its path must remain stable'
}
$protectedPaths=@($ProtectPaths)+@($env:BAI_EXE,$PSScriptRoot,(Get-Location).Path)
# PATH entries may refer to a retained BAI binary directory.
$protectedPaths+=@($env:PATH -split ';' | Where-Object {$_ -and [IO.Path]::IsPathRooted($_)})
$processes=@(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {$_.ProcessId -ne $PID})
$busy=@($processes | Where-Object {$_.Name -match '^(beckhoffAutomationInterface|TcXaeShell|devenv)\.exe$'})
if($Apply -and $Action -ne 'Report' -and $busy.Count){throw 'Close BAI and XAE before applying cleanup. Report/preview is still available.'}
function Inspect-Tree([string]$Path) {
    $stack=New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push($Path)
    $bytes=0L; $files=0; $links=$false; $git=$false
    $latest=(Get-Item -LiteralPath $Path -Force).LastWriteTimeUtc
    while($stack.Count){
        $dir=$stack.Pop()
        foreach($entry in Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop){
            if($entry.LastWriteTimeUtc -gt $latest){$latest=$entry.LastWriteTimeUtc}
            if($entry.Name -eq '.git'){$git=$true}
            if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){$links=$true;continue}
            if($entry.PSIsContainer){$stack.Push($entry.FullName)}else{$bytes+=$entry.Length;$files++}
        }
    }
    [pscustomobject]@{Bytes=$bytes;Files=$files;HasLinks=$links;HasGit=$git;LatestUtc=$latest}
}
function Is-CompletedFixture([string]$Path,[string]$Name) {
    if($Name -notmatch '^bai-(boot-transaction|bsd-coordinator)-[0-9a-f]{32}$'){return $false}
    $file=Join-Path $Path 'validation.json'
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){return $false}
    try{$v=Get-Content -LiteralPath $file -Raw | ConvertFrom-Json}catch{return $false}
    if($v.status -cne 'Passed'){return $false}
    if($Name -like 'bai-boot-transaction-*'){
        return $v.scope -ceq 'filesystem transaction; not runtime activation'
    }
    return $v.scope -ceq 'synthetic ADS/SSH coordinator with real file transaction' -and $v.physicalAcceptance -eq $false
}
$candidates=@(Get-ChildItem -LiteralPath $Root -Force -Directory | Where-Object {$_.Name -like 'bai-*'})
if($Action -eq 'DeleteFixtures' -and (Test-Path -LiteralPath "$CollectionRoot/fixtures")){
    Assert-NoLinkAncestors "$CollectionRoot/fixtures"
    $candidates+=@(Get-ChildItem -LiteralPath "$CollectionRoot/fixtures" -Directory -Force)
}
$cutoff=[DateTime]::UtcNow.AddDays(-$MinimumAgeDays)
$rows=@(foreach($item in $candidates){
    $reason='';$category='Archive candidate';$tree=$null
    $source=[IO.Path]::GetFullPath($item.FullName)
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){$reason='Link/junction; no traversal'}
    elseif($protected.ContainsKey($item.Name)){$reason=$protected[$item.Name]}
    elseif(@($protectedPaths | Where-Object {$_ -and (Is-Within $_ $source)}).Count){$reason='Current directory, script, PATH or explicitly protected path'}
    elseif(@($processes | Where-Object {$_.CommandLine -and $_.CommandLine.Replace('/','\').IndexOf($source,[StringComparison]::OrdinalIgnoreCase) -ge 0}).Count){$reason='Referenced by a running process'}
    # Even protected folders are sized if they are ordinary directories.
    if(-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){
        try{$tree=Inspect-Tree $source}catch{$reason="Cannot safely inspect: $($_.Exception.Message)"}
    }
    if($tree){
        if($tree.HasLinks){$reason='Contains a link/junction; manual review required'}
        if($tree.HasGit){$reason='Contains a Git checkout/worktree; preserve source and Git metadata'}
        if(-not $reason -and $tree.LatestUtc -gt $cutoff){$reason="Changed within $MinimumAgeDays day(s)"}
    }
    $fixture=Is-CompletedFixture $source $item.Name
    if($fixture){$category='Completed synthetic fixture'}
    if($reason){$category='Keep'}
    $selected=(-not $Names.Count -or $item.Name -in $Names)
    $operation='None';$destination=''
    if(-not $reason -and $selected){
        if($Action -eq 'Consolidate'){
            $operation='Move'
            $group=if($fixture){'fixtures'}else{'archive'}
            $destination=Join-Path "$CollectionRoot/$group" $item.Name
        }elseif($Action -eq 'DeleteFixtures' -and $fixture){$operation='Delete'}
    }
    [pscustomobject]@{Name=$item.Name;Path=$source;Category=$category;Reason=$reason;Bytes=$(if($tree){$tree.Bytes}else{0L});Files=$(if($tree){$tree.Files}else{0});LatestUtc=$(if($tree){$tree.LatestUtc.ToString('o')}else{''});Operation=$operation;Destination=$destination;Result='Preview'}
})
foreach($name in $Names){if($name -notin $rows.Name){throw "Folder not found in cleanup scope: $name"}}
Assert-NoLinkAncestors $CollectionRoot
$reports=Join-Path $CollectionRoot 'cleanup'
Assert-NoLinkAncestors $reports
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$report=Join-Path $reports ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'.json')
function Save-Report {
    [ordered]@{action=$Action;apply=[bool]$Apply;root=$Root;home=$CollectionRoot;createdUtc=[DateTime]::UtcNow.ToString('o');folders=$rows} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
Save-Report
foreach($row in $rows | Where-Object {$_.Operation -ne 'None'}){
    if(-not $Apply){continue}
    try{
        $source=[IO.Path]::GetFullPath($row.Path)
        $parent=Split-Path $source -Parent
        # Exact direct children only; never operate on Root, CollectionRoot or arbitrary nested paths.
        if($parent.TrimEnd('\') -ine $Root.TrimEnd('\') -and
           ($Action -ne 'DeleteFixtures' -or $parent -ine [IO.Path]::GetFullPath("$CollectionRoot/fixtures"))){throw 'Source is outside the permitted cleanup directories.'}
        if((Split-Path $source -Leaf) -cne $row.Name){throw 'Source name changed.'}
        Assert-NoLinkAncestors $source
        $again=Inspect-Tree $source
        if($again.HasGit -or $again.HasLinks -or $again.LatestUtc -gt $cutoff){throw 'Folder changed or is no longer eligible.'}
        if($row.Operation -eq 'Move'){
            $destination=[IO.Path]::GetFullPath($row.Destination)
            if(-not (Is-Within $destination $CollectionRoot) -or (Is-Within $CollectionRoot $source)){throw 'Unsafe archive destination.'}
            Assert-NoLinkAncestors $destination
            if(Test-Path -LiteralPath $destination){throw 'Destination exists; refusing to merge or overwrite.'}
            New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            Move-Item -LiteralPath $source -Destination $destination -ErrorAction Stop
            $row.Result='Moved'
        }else{
            if(-not (Is-CompletedFixture $source $row.Name)){throw 'Not a completed synthetic fixture.'}
            Remove-Item -LiteralPath $source -Recurse -Force -ErrorAction Stop
            $row.Result='Deleted'
        }
    }catch{$row.Result='Failed: '+$_.Exception.Message}
    Save-Report
}
$rows | Select-Object Name,Category,@{n='MiB';e={[Math]::Round($_.Bytes/1MB,1)}},Operation,Result | Format-Table -AutoSize
Write-Host "Report and old/new path map: $report"
Write-Host 'Archive folders are retained evidence. Rebuild projects before reuse; embedded absolute paths are not rewritten.'
if(-not $Apply -and $Action -ne 'Report'){Write-Host 'PREVIEW ONLY. Add -Apply to perform the listed operations.'}
if(@($rows | Where-Object {$_.Result -like 'Failed:*'}).Count){throw 'Some operations failed. See the report; other recorded operations may have completed.'}
