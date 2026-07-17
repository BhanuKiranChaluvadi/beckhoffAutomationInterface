# Worker script invoked (detached) by githooks/post-commit. Runs the incremental sync
# and logs everything to githooks/logs/<timestamp>.log, since nothing here is visible
# to the developer's terminal (the hook launches this hidden/detached so `git commit`
# returns immediately).
#
# Machine-specific: set $env:BECKHOFF_TWINCAT_DEST / $env:BECKHOFF_ST_SOURCE if your
# TwinCAT project or ST source don't live at the defaults below (also used to point
# this script at a disposable scratch project for testing, without editing the script).
#
# IMPORTANT: no --name is passed, so the project name defaults to the source folder's
# own directory name (RunOptions.Parse's convention) - this MUST match whatever name
# the project was originally bootstrapped with (e.g. "Shark" for ST/Shark), or this
# script will silently bootstrap a SEPARATE new project instead of updating the
# existing one. If you ever bootstrap with an explicit --name that differs from the
# source folder's name, add the matching --name below.
#
# Uses `sync all` (not `init`) - a missing project here is a hard error, not a silent
# bootstrap, exactly like the old --incremental-without---init behavior.

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $RepoRoot "beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe"
$Source = if ($env:BECKHOFF_ST_SOURCE) { $env:BECKHOFF_ST_SOURCE } else { Join-Path $RepoRoot "ST\Shark" }
$Dest = if ($env:BECKHOFF_TWINCAT_DEST) { $env:BECKHOFF_TWINCAT_DEST } else { "C:\Users\$env:USERNAME\Documents\TwinCAT" }

$LogDir = Join-Path $PSScriptRoot "logs"
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$LogFile = Join-Path $LogDir ("{0:yyyy-MM-dd_HHmmss}.log" -f (Get-Date))

if (-not (Test-Path $Exe)) {
    "SKIPPED: built exe not found at '$Exe' (build the project first)." | Out-File $LogFile
    exit 0
}

& $Exe sync all $Source --dest $Dest --incremental *> $LogFile
$exitCode = $LASTEXITCODE

Add-Content $LogFile ""
if ($exitCode -eq 0) {
    Add-Content $LogFile "RESULT: SUCCESS (exit code 0)"
} else {
    Add-Content $LogFile "RESULT: FAILED (exit code $exitCode) - see above for details."
}
