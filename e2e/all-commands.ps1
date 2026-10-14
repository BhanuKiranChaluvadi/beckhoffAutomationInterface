<#
.SYNOPSIS
    Closed-loop check of EVERY command on real fixtures. Each step runs the exe and compares
    its exit code and key output lines with the expected ones. Full output of each step is
    kept in <Work>\logs\ for review.

.DESCRIPTION
    Groups:
      A  command line: usage, --help, removed flags, wrong paths, .stconfig rules (no TwinCAT)
      B  read-only checks on ST/sample, plus generated failures: an unlinked %I variable and
         one object defined in two files (no TwinCAT)
      C  ST/sample life cycle: init, every sync mode (second run = no change), check events,
         build --clean, every export mode, overwrite guard, export -> sync round trip
      D  the same project: compile error mapped to the .st file and line, then fixed again;
         incremental sync after a git commit; the post-commit worker script
      E  runtime, read-only only: activate preview on both targets, verify on both targets

    Needs TwinCAT XAE. Group E needs UmRT_Default running the VariantSample project (run
    e2e\variant-sample.ps1 first; it leaves MachineB running). Nothing is ever activated
    here. ST/ is only read: every source tree is copied into a fresh temporary folder.

.EXAMPLE
    .\e2e\all-commands.ps1
    .\e2e\all-commands.ps1 -Running MachineA      # if MachineA is the variant now on UmRT
#>
param(
    [string] $Exe     = (Join-Path $PSScriptRoot '..\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe'),
    [string] $Running = 'MachineB',
    [string] $Work    = (Join-Path ([IO.Path]::GetTempPath()) ('bai-e2e-all-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$Exe  = (Resolve-Path $Exe).Path
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$st   = Join-Path $repo 'ST'
New-Item -ItemType Directory -Force $Work, "$Work\logs", "$Work\src", "$Work\tc", "$Work\cfg" | Out-Null

$results = New-Object System.Collections.Generic.List[object]
$n = 0
# $must: every entry must appear in the output (literal substring). $mustNot: none may.
function Step([string] $group, [string] $label, [int] $expectedExit, [string[]] $must, [string[]] $argv, [string[]] $mustNot = @()) {
    $script:n++
    Write-Host ("`n=== {0}{1:00} {2}" -f $group, $script:n, $label) -ForegroundColor Cyan
    $start = Get-Date
    # Capture everything first: piping a native exe into Select-Object -First kills it early.
    # 'Continue': PowerShell 5.1 would turn the exe's first stderr line into a terminating error.
    $ErrorActionPreference = 'Continue'
    $out = @(& $Exe @argv 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $log = Join-Path "$Work\logs" ('{0:00}-{1}.log' -f $script:n, ($label -replace '[^\w]+', '_'))
    (@("> $Exe $($argv -join ' ')", "exit $code", '') + $out) | Set-Content $log -Encoding utf8
    # Literal substring match: -like would read '[format-check]' as a wildcard set.
    $missing = @($must | Where-Object { $m = $_; -not ($out | Where-Object { $_.Contains($m) }) })
    $present = @($mustNot | Where-Object { $m = $_; $out | Where-Object { $_.Contains($m) } })
    $ok = ($code -eq $expectedExit) -and $missing.Count -eq 0 -and $present.Count -eq 0
    $out | Where-Object { $_ -match 'PASSED|FAILED|ERROR|Sync complete|complete \(|Exported|parsed OK|issue\(s\)|check-links\]|Per-variant|changed|NOT ACTIVATED|OK  |FAIL |refus|Removed -|Usage:' } |
        Select-Object -First 8 | ForEach-Object { "  $_" }
    $why = @()
    if ($code -ne $expectedExit) { $why += "exit $code, expected $expectedExit" }
    if ($missing.Count) { $why += "missing: " + ($missing -join ' | ') }
    if ($present.Count) { $why += "unwanted: " + ($present -join ' | ') }
    $results.Add([pscustomobject]@{ Id = ('{0}{1:00}' -f $group, $script:n); Step = $label; Exit = $code; Expected = $expectedExit;
                                     AsExpected = $ok; Seconds = [int]((Get-Date) - $start).TotalSeconds; Why = ($why -join '; ') })
    Write-Host ("  -> {0} {1}" -f $(if ($ok) { 'AS EXPECTED' } else { 'UNEXPECTED' }), ($why -join '; ')) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
function Check([string] $group, [string] $label, [bool] $ok, [string] $why) {
    $script:n++
    $results.Add([pscustomobject]@{ Id = ('{0}{1:00}' -f $group, $script:n); Step = $label; Exit = '-'; Expected = '-';
                                     AsExpected = $ok; Seconds = 0; Why = $(if ($ok) { '' } else { $why }) })
    Write-Host ("`n=== {0}{1:00} {2}`n  -> {3} {4}" -f $group, $script:n, $label, $(if ($ok) { 'AS EXPECTED' } else { 'UNEXPECTED' }), $(if ($ok) { '' } else { $why })) `
        -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
function Edit([string] $file, [string] $old, [string] $new) {   # exact, once; keeps ASCII + CRLF
    $text = [IO.File]::ReadAllText($file)
    if ($text.IndexOf($old) -lt 0) { throw "e2e fixture: '$old' not in $file" }
    [IO.File]::WriteAllText($file, $text.Replace($old, $new))
}
function Snapshot([string] $dir) {   # hash of every project file TwinCAT must not change
    Get-ChildItem $dir -Recurse -File | Where-Object { $_.FullName -notmatch '\\(_Boot|_CompileInfo|\.vs)\\' } |
        ForEach-Object { '{0} {1}' -f $_.FullName.Substring($dir.Length), (Get-FileHash $_.FullName).Hash } | Sort-Object
}
# Not named 'Git': PowerShell resolves functions before applications, case-insensitively.
function InRepo([string] $dir) {
    # PowerShell 5.1 turns any stderr line of a native exe into an error; judge git by its exit code.
    $ErrorActionPreference = 'Continue'
    & git.exe -C $dir -c user.name=e2e -c user.email=e2e@example.invalid -c core.hooksPath=NUL -c core.autocrlf=false @args 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "e2e fixture: git $($args -join ' ') failed in $dir" }
}
function NewGitCopy([string] $from, [string] $to) {
    Copy-Item $from $to -Recurse
    # Left by earlier runs on ST/ itself (git-ignored there); a copy must start without a baseline.
    Remove-Item (Join-Path $to '.st-sync-state'), (Join-Path $to '.st-known-names') -ErrorAction SilentlyContinue
    InRepo $to init -q; InRepo $to add -A; InRepo $to commit -q -m 'fixture'
}

$sample    = "$Work\src\sample"; NewGitCopy "$st\sample" $sample
$sampleSln = "$Work\tc\sample"
$expect  = "$st\VariantSample\expect.json"

Push-Location $Work
try {
    # ------------------------------------------------------------ A: command line
    Step A 'no arguments prints usage, exit 1'        1 @('Usage:', 'Commands:') @()
    Step A '--help, exit 0'                           0 @('Commands:', "'<command> --help'") @('--help')
    Step A 'build --help'                             0 @('Usage: beckhoffAutomationInterface build') @('build', '--help')
    Step A 'activate --help'                          0 @('activate <path', '--plc <name>') @('activate', '--help')
    Step A 'verify --help'                            0 @('verify --target') @('verify', '--help')
    Step A 'unknown command'                          1 @('unknown command') @('deploy', $sample)
    Step A 'old --dest names --project'               1 @('Removed - sync: --project') @('sync', 'all', $sample, '--dest', $Work)
    Step A 'old --plc-name on build names --plc'      1 @('Removed - use --plc') @('build', $sample, '--plc-name', 'X')
    Step A 'old --plc-name on activate names --plc'   1 @('use --plc') @('activate', $sample, '--target', 'umrt', '--plc-name', 'X')
    Step A 'old --parse-only names check parse'       1 @('check parse') @('check', 'format', $sample, '--parse-only')
    Step A 'unknown --shell'                          1 @("unknown --shell 'eclipse'") @('--shell', 'eclipse', 'build', $sample)
    Step A 'sync without --project'                   1 @('needs --project') @('sync', 'code', $sample, '--no-config')
    Step A 'sync with a missing project'              1 @('project not found') @('sync', 'code', $sample, '--project', "$Work\Nope")
    Step A 'build with a missing project'             1 @('ERROR') @('build', "$Work\Nope.tsproj")
    Step A 'init needs --into'                        1 @('needs --into') @('init', $sample)
    Copy-Item $sample "$Work\src\bad-name" -Recurse
    Step A 'init refuses a hyphen in the PLC name'    1 @("'bad-name' is not valid") @('init', "$Work\src\bad-name", '--into', "$Work\tc")
    Check A '...and created nothing' (-not (Test-Path "$Work\tc\bad-name")) "$Work\tc\bad-name exists"
    Step A 'verify needs --target and --expect'       1 @('needs --target') @('verify', '--expect', $expect)
    Step A 'verify: unknown target'                   1 @('ERROR') @('verify', '--target', 'plc9', '--expect', $expect, '--variant', 'MachineA')
    Step A 'verify: variant not in expect.json'       1 @("no entry for variant 'MachineC'") @('verify', '--target', 'umrt', '--expect', $expect, '--variant', 'MachineC')
    Step A 'verify: missing expect file'              1 @('ERROR') @('verify', '--target', 'umrt', '--expect', "$Work\none.json", '--variant', 'MachineA')
    Step A 'activate: unknown target'                 1 @('ERROR') @('activate', "$st\VariantSample", '--target', 'plc9')
    Set-Content "$Work\cfg\.stconfig" "dest=C:\X`nsync-code=true" -Encoding ascii
    Step A '.stconfig: removed keys name their fix'   1 @("'dest' is no longer supported", "'sync-code' is no longer supported") @('check', 'parse', $sample, '--config', "$Work\cfg")
    Set-Content "$Work\cfg\.stconfig" 'confirm-delete=true' -Encoding ascii
    Step A '.stconfig: safety flag is refused'        1 @("unknown key 'confirm-delete'") @('check', 'parse', $sample, '--config', "$Work\cfg")
    Step A '--no-config skips a bad .stconfig'        0 @('parsed OK') @('check', 'parse', $sample, '--config', "$Work\cfg", '--no-config')

    # ------------------------------------------------------------ B: read-only checks
    Step B 'doctor'                                   0 @('UmRT_Default', 'License:') @('doctor')
    Step B 'check parse sample'                       0 @('9 file(s) parsed OK', ', 0 failed') @('check', 'parse', $sample) @('DUPLICATE')
    Step B 'check format sample (clean)'              0 @('0 issue(s) found') @('check', 'format', $sample)
    Step B 'check links sample'                       0 @('2 declared, 2 linked, 0 unlinked') @('check', 'links', $sample)
    Step B 'check --ignore drops files'               0 @('7 file(s) parsed OK') @('check', 'parse', $sample, '--ignore', 'FB_Motor*.st')
    Copy-Item $sample "$Work\src\nolinks" -Recurse; Remove-Item "$Work\src\nolinks\io-devices.xml"
    Step B 'check links: unlinked %I/%Q -> exit 1'     1 @('2 declared, 0 linked, 2 unlinked', 'UNLINKED') @('check', 'links', "$Work\src\nolinks")
    Copy-Item $sample "$Work\src\dup" -Recurse; Copy-Item "$Work\src\dup\FB_Motor.st" "$Work\src\dup\FB_Motor_old.st"
    Step B 'check parse: one object in two files'     1 @('DUPLICATE FB_Motor: FB_Motor.st, FB_Motor_old.st') @('check', 'parse', "$Work\src\dup")

    # ------------------------------------------------------------ C: life cycle
    Step C 'init sample (sync all + build)'           0 @('BUILD PASSED') @('init', $sample, '--into', "$Work\tc") @('BUILD FAILED')
    $tsproj = (Get-ChildItem $sampleSln -Recurse -Filter 'sample.tsproj' | Select-Object -First 1).FullName
    Check C 'init created sample.sln and sample.tsproj' ((Test-Path "$sampleSln\sample.sln") -and $tsproj) "no project under $sampleSln"
    Step C 'sync code again: no change'               0 @('Sync complete (0 created, 0 updated, 0 deleted, ', ' unchanged)') @('sync', 'code', $sample, '--project', $sampleSln)
    Step C 'sync libs again: no change'               0 @('Library sync complete (0 added, 0 removed)') @('sync', 'libs', $sample, '--project', "$sampleSln\sample.sln")
    Step C 'sync io again: no change'                 0 @('IO sync complete (0 created, 0 deleted') @('sync', 'io', $sample, '--project', $tsproj)
    Step C 'sync events: no events.xml'               0 @() @('sync', 'events', $sample, '--project', $sampleSln) @('ERROR', 'FAILED')
    Step C 'sync variants: no variants.xml'           0 @() @('sync', 'variants', $sample, '--project', $sampleSln) @('VARIANTS FAILED', 'ERROR')
    Step C 'check events against the project'         0 @() @('check', 'events', $sample, '--project', $tsproj) @('MISSING', 'ERROR')
    Step C 'build --clean'                            0 @('BUILD PASSED') @('build', $tsproj, '--clean')
    Step C 'build folder (finds the one .tsproj)'     0 @('BUILD PASSED') @('build', $sampleSln)
    Step C 'build the .sln'                           0 @('BUILD PASSED') @('build', "$sampleSln\sample.sln")
    Step C 'build --variant on a project without'     1 @('the project has no variants') @('build', $tsproj, '--variant', 'MachineA')
    $before = Snapshot $sampleSln
    Step C 'export code'                              0 @('Code export complete') @('export', 'code', "$Work\exp\code", '--project', $tsproj)
    Step C 'export code again: refused'               1 @('--overwrite') @('export', 'code', "$Work\exp\code", '--project', $tsproj)
    Step C 'export code --overwrite'                  0 @('Code export complete') @('export', 'code', "$Work\exp\code", '--project', $tsproj, '--overwrite')
    Step C 'export libs'                              0 @('library reference(s)') @('export', 'libs', "$Work\exp\libs", '--project', $tsproj)
    Step C 'export io'                                0 @('Exported IO tree') @('export', 'io', "$Work\exp\io", '--project', $tsproj)
    Step C 'export events (project has none)'         0 @('No event classes found') @('export', 'events', "$Work\exp\events", '--project', $tsproj)
    Step C 'export links'                             0 @('Exported current variable links') @('export', 'links', "$Work\exp\links", '--project', $tsproj)
    Step C 'export object MAIN'                       0 @("Exported 'MAIN'") @('export', 'object', 'MAIN', "$Work\exp\object", '--project', $tsproj)
    Step C 'export object FB_Motor'                   0 @("Exported 'FB_Motor'") @('export', 'object', 'FB_Motor', "$Work\exp\object", '--project', $tsproj)
    Step C 'export object: unknown name'              1 @('ERROR') @('export', 'object', 'FB_DoesNotExist', "$Work\exp\object2", '--project', $tsproj)
    Step C 'export all'                               0 @('Exported IO tree', 'library reference(s)') @('export', 'all', "$Work\exp\all", '--project', $tsproj)
    $srcCount = @(Get-ChildItem $sample -Recurse -Filter *.st).Count
    $expCount = @(Get-ChildItem "$Work\exp\all" -Recurse -Filter *.st).Count
    Check C "export all wrote every .st ($expCount of $srcCount)" ($expCount -eq $srcCount) "exported $expCount, source has $srcCount"
    $diff = @(Compare-Object $before (Snapshot $sampleSln))
    Check C 'project files unchanged by 10 exports' ($diff.Count -eq 0) ("changed: " + (($diff | ForEach-Object InputObject) -join ', '))
    Step C 'round trip: sync the export back, no change' 0 @('Sync complete (0 created, 0 updated, 0 deleted, ') @('sync', 'code', "$Work\exp\all", '--project', $sampleSln)
    Step C 'round trip: build after it'               0 @('BUILD PASSED') @('build', $tsproj)

    # ------------------------------------------------------------ D: errors, incremental, hook
    $motor = Join-Path $sample 'FB_Motor.st'
    $orig = [IO.File]::ReadAllBytes($motor)
    Edit $motor '// Main cyclic logic of the motor function block' "// Main cyclic logic of the motor function block`r`nnUndeclared := 1;"
    Step D 'compile error is mapped to FB_Motor.st'   1 @('BUILD FAILED', 'nUndeclared', 'FB_Motor.st') @('sync', 'all', $sample, '--project', $sampleSln)
    [IO.File]::WriteAllBytes($motor, $orig)
    Step D 'error fixed: build passes again'          0 @('BUILD PASSED') @('sync', 'all', $sample, '--project', $sampleSln)
    InRepo $sample add -A; InRepo $sample commit -q -m 'baseline'
    Step D 'baseline recorded (full sync)'            0 @('Recorded sync baseline') @('sync', 'code', $sample, '--project', $sampleSln)
    Edit (Join-Path $sample 'F_ClampSpeed.st') '// Requested speed' '// Requested speed (e2e incremental)'
    InRepo $sample add -A; InRepo $sample commit -q -m 'one file'
    Step D 'incremental: only the changed file'       0 @('1 file(s) changed, 0 file(s) deleted') @('sync', 'all', $sample, '--project', $sampleSln, '--incremental')
    Edit (Join-Path $sample 'F_ClampSpeed.st') '// Maximum allowed speed' '// Maximum allowed speed (e2e hook)'
    InRepo $sample add -A; InRepo $sample commit -q -m 'hook'
    $env:BECKHOFF_ST_SOURCE = $sample; $env:BECKHOFF_TWINCAT_DEST = "$Work\tc"
    $hookStart = Get-Date
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'githooks\run-incremental-sync.ps1')
    Remove-Item Env:BECKHOFF_ST_SOURCE, Env:BECKHOFF_TWINCAT_DEST
    $hookLog = Get-ChildItem (Join-Path $repo 'githooks\logs') -Filter *.log | Where-Object { $_.LastWriteTime -ge $hookStart } |
               Sort-Object LastWriteTime | Select-Object -Last 1
    $hookText = if ($hookLog) { Get-Content $hookLog.FullName -Raw } else { '' }
    Check D 'git hook worker: incremental sync + build' (($hookText -like '*RESULT: SUCCESS*') -and ($hookText -like '*1 file(s) changed*') -and ($hookText -like '*BUILD PASSED*')) "log: $($hookLog.FullName)"
    if ($hookLog) { Copy-Item $hookLog.FullName "$Work\logs\hook.log"; Remove-Item $hookLog.FullName }
    $env:BECKHOFF_ST_SOURCE = $sample; $env:BECKHOFF_TWINCAT_DEST = "$Work\Nope"
    $hookStart = Get-Date
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'githooks\run-incremental-sync.ps1')
    Remove-Item Env:BECKHOFF_ST_SOURCE, Env:BECKHOFF_TWINCAT_DEST
    $hookLog = Get-ChildItem (Join-Path $repo 'githooks\logs') -Filter *.log | Where-Object { $_.LastWriteTime -ge $hookStart } |
               Sort-Object LastWriteTime | Select-Object -Last 1
    $hookText = if ($hookLog) { Get-Content $hookLog.FullName -Raw } else { '' }
    Check D 'git hook worker: wrong dest fails loudly' (($hookText -like '*RESULT: FAILED*') -and ($hookText -like '*project not found*') -and -not (Test-Path "$Work\Nope")) "log: $($hookLog.FullName)"
    if ($hookLog) { Copy-Item $hookLog.FullName "$Work\logs\hook-wrong-dest.log"; Remove-Item $hookLog.FullName }

    # ------------------------------------------------------------ E: runtime (read-only)
    $other = if ($Running -eq 'MachineA') { 'MachineB' } else { 'MachineA' }
    Step E 'activate preview on UmRT (no --yes)'      1 @('Target umrt', 'boot project now: VariantSample', 'NOT ACTIVATED') @('activate', $tsproj, '--target', 'umrt')
    Step E 'activate preview on local (no --yes)'     1 @('Target local', 'NOT ACTIVATED') @('activate', $tsproj, '--target', 'local')
    Step E "verify $Running on UmRT (running)"        0 @('VERIFY PASSED') @('verify', '--target', 'umrt', '--expect', $expect, '--variant', $Running)
    Step E "verify $other on UmRT (must fail)"        1 @('VERIFY FAILED') @('verify', '--target', 'umrt', '--expect', $expect, '--variant', $other)
    Step E 'verify on local (Config, no PLC)'         1 @() @('verify', '--target', 'local', '--expect', $expect, '--variant', $Running, '--timeout', '5') @('VERIFY PASSED')
}
finally {
    Pop-Location
}

Write-Host "`n=== Summary ($Work)" -ForegroundColor Cyan
$results | Format-Table Id, Step, Exit, Expected, AsExpected, Seconds, Why -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$results | Export-Csv "$Work\logs\summary.csv" -NoTypeInformation
$bad = @($results | Where-Object { -not $_.AsExpected })
if ($bad.Count -eq 0) { Write-Host "E2E PASSED: all $($results.Count) checks as expected." -ForegroundColor Green; exit 0 }
Write-Host "E2E FAILED: $($bad.Count) of $($results.Count) checks not as expected." -ForegroundColor Red
exit 1
