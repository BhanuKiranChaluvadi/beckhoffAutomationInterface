# Worker script invoked (detached) by githooks/post-commit. Runs the incremental sync
# and logs everything to githooks/logs/<timestamp>.log, since nothing here is visible
# to the developer's terminal (the hook launches this hidden/detached so `git commit`
# returns immediately).
#
# Machine-specific: $env:BECKHOFF_ST_SOURCE (required: the .st source folder to sync) and
# $env:BECKHOFF_TWINCAT_DEST (default below). Also used to point this script at a
# disposable scratch project for testing, without editing the script.
#
# The project is <Dest>\<name>\<name>.sln, where <name> is the source folder's own name
# (what `init` uses unless it was given --name). If the project was bootstrapped with a
# different --name, set $Project to <Dest>\<that name>. A wrong path fails loudly
# ("project not found") - it never bootstraps a new project.
#
# Uses `sync all` (not `init`) - a missing project here is a hard error, not a silent
# bootstrap, exactly like the old --incremental-without---init behavior.

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $RepoRoot "beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe"
$Source = $env:BECKHOFF_ST_SOURCE
$Dest = if ($env:BECKHOFF_TWINCAT_DEST) { $env:BECKHOFF_TWINCAT_DEST } else { "C:\Users\$env:USERNAME\Documents\TwinCAT" }

$LogDir = Join-Path $PSScriptRoot "logs"
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$LogFile = Join-Path $LogDir ("{0:yyyy-MM-dd_HHmmss}.log" -f (Get-Date))

if (-not $Source) {
    "SKIPPED: set BECKHOFF_ST_SOURCE to the .st source folder to sync." | Out-File $LogFile
    exit 0
}
if (-not (Test-Path $Exe)) {
    "SKIPPED: built exe not found at '$Exe' (build the project first)." | Out-File $LogFile
    exit 0
}

$Project = Join-Path $Dest (Split-Path $Source -Leaf)
& $Exe sync all $Source --project $Project --incremental *> $LogFile
$exitCode = $LASTEXITCODE

Add-Content $LogFile ""
if ($exitCode -eq 0) {
    Add-Content $LogFile "RESULT: SUCCESS (exit code 0)"
} else {
    Add-Content $LogFile "RESULT: FAILED (exit code $exitCode) - see above for details."
}
