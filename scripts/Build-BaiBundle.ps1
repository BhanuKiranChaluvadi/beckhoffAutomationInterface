param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify the source commit.' }
if (& git -C $repo status --porcelain --untracked-files=no) { throw 'Build only committed source.' }
$project = Join-Path $repo 'beckhoffAutomationInterface'
$version = ([xml](Get-Content "$project/beckhoffAutomationInterface.csproj" -Raw)).Project.PropertyGroup.Version
$version = @($version | Where-Object { $_ })[0]
$built = Join-Path $project 'bin/Release/net48'
$exe = Join-Path $built 'beckhoffAutomationInterface.exe'
$reported = & $exe --version
$reported | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'Packaged executable cannot start.' }
if ($reported -notmatch [regex]::Escape("$version+$commit")) { throw 'Executable was not built from the current commit. Build Release again.' }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$files = [ordered]@{}
Get-ChildItem $built -File | Where-Object { $_.Extension -in '.exe','.dll','.config' } | Sort-Object Name | ForEach-Object {
    $files[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
foreach ($required in 'beckhoffAutomationInterface.exe','Interop.TCatSysManager.dll','EnvDTE.dll','EnvDTE80.dll') {
    if (-not $files.Contains($required)) { throw "Missing dependency: $required" }
}
$archive = Join-Path $Output 'bai.zip'
Compress-Archive -LiteralPath @($files.Keys | ForEach-Object { Join-Path $built $_ }) -DestinationPath $archive
$metadata = [ordered]@{
    schemaVersion = 1
    repository = 'BhanuKiranChaluvadi/beckhoffAutomationInterface'
    commit = $commit
    version = $version
    tag = "build-$commit"
    sha256 = $files['beckhoffAutomationInterface.exe']
    archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    files = $files
}
[IO.File]::WriteAllText((Join-Path $Output 'bai-release.json'), ($metadata | ConvertTo-Json -Depth 5) + "`n")
