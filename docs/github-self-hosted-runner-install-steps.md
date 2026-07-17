# Installing the GitHub Actions Self-Hosted Runner — Exact Steps

One numbered step = one action. Do them in order. This sets up the test
bench PC to automatically compile the PLC project on every push to
`main`/`master`.

**Everything in Part 1 happens in your browser, on the PLC project's GitHub
repo** (the one with `.tsproj`/`.plcproj`/`.sln` — not this tool's repo).
**Everything from Part 2 onward happens on the test bench PC**, in
PowerShell.

> Why not the plain "install as a Windows service" method GitHub's own
> quickstart shows? Visual Studio's automation (which this tool uses to
> drive TwinCAT) needs a real interactive desktop. A normal Windows service
> runs with no desktop and Visual Studio hangs when the runner tries to open
> it. Part 4 below sets the runner up to run in a real logged-in desktop
> session automatically instead — a few extra steps, done once.

---

## Part 1 — Register the runner (browser, on GitHub)

1. Go to the **PLC project repo** on GitHub.
2. Click the **Settings** tab at the top of the repo page.
3. In the left sidebar, click **Actions**.
4. Click **Runners**.
5. Click the green **New self-hosted runner** button.
6. Under "Choose the operating system", click **Windows**.
7. Leave the architecture dropdown as **x64**.
8. GitHub now shows three grey boxes of PowerShell commands: **Download**,
   **Configure**, and **Using your self-hosted runner**.
9. **Leave this browser tab open.** You'll copy from the Download and
   Configure boxes in the next steps — the token in the Configure box
   expires after about an hour, so don't do this part 1 the night before.

---

## Part 2 — Download the runner (test bench PC)

10. On the test bench PC, open **PowerShell** (admin not required for this part).
11. Run:
    ```powershell
    mkdir C:\actions-runner
    cd C:\actions-runner
    ```
12. Go back to the GitHub browser tab from Part 1. Copy the entire contents
    of the **Download** box (it's 3-4 lines: an `Invoke-WebRequest`, then
    `Add-Type`, then `[System.IO.Compression.ZipFile]::ExtractToDirectory(...)`).
13. Paste that copied text into the PowerShell window and press Enter. Wait
    for it to finish (downloads + unzips the runner).

    > Use the exact text from **your** browser tab — the version number in
    > the URL changes over time, so don't reuse an old version number from
    > anywhere else, including an earlier run of this same guide.

14. Confirm it worked:
    ```powershell
    dir C:\actions-runner
    ```
    You should see `config.cmd`, `run.cmd`, and a `bin` folder.

---

## Part 3 — Connect the runner to the repo (test bench PC)

15. Go back to the GitHub browser tab. Copy the entire contents of the
    **Configure** box — one line, starting with `.\config.cmd --url ...`.
16. Paste it into the same PowerShell window and press Enter.
17. It will ask a few questions — press **Enter** to accept the default for
    each one, unless you have a specific reason not to:
    - `Enter the name of the runner group to add this runner to` → Enter
    - `Enter the name of runner` → Enter (or type something like `bench-pc`)
    - `Enter any additional labels` → Enter (skip)
    - `Enter name of work folder` → Enter
18. Confirm you see `√ Runner successfully added` and `√ Settings Saved`
    near the end of the output. If you see an error instead, the token from
    step 15 likely expired — go back to Part 1 and generate a fresh one.

**Do not run `.\svc.cmd install` at this point.** That's the standard next
step GitHub shows you, but skip it — see Part 4.

---

## Part 4 — Make it start automatically in a real desktop session

> **Adjust for your actual runner folder path.** These steps assume the
> runner lives at `C:\Users\Administrator\actions-runner` (confirmed via
> `ls`/`dir` in that folder) and that you're using the built-in
> `Administrator` account for autologon — not a separate dedicated account.
> That's a deliberate simplification: the runner folder already sits inside
> `Administrator`'s own profile, and a separate low-privilege account
> wouldn't have permission to reach into it without extra ACL changes. If
> your runner folder is at a different path, swap it in everywhere below.
>
> Trade-off: the bench PC will auto-login as a full admin account
> continuously. Acceptable for a dedicated, physically-secured test bench;
> if you'd rather keep a separate low-privilege account, move the runner
> folder to a neutral path first (e.g. `Move-Item
> C:\Users\Administrator\actions-runner C:\actions-runner`), then create a
> dedicated local user and substitute its name everywhere `Administrator`
> appears below.

### 4a. Confirm you have what autologon needs

19. Make sure you know the **`Administrator` account's actual Windows login
    password** — you'll reuse it in the next step. No new account needs to
    be created.

### 4b. Make Windows auto-login as that account on every boot

20. Open PowerShell **as Administrator**, then run these lines one at a
    time (replace `<PASSWORD>` with `Administrator`'s real password):
    ```powershell
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
    Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value "1"
    Set-ItemProperty -Path $regPath -Name "DefaultUserName" -Value "Administrator"
    ```
21. Run:
    ```powershell
    Set-ItemProperty -Path $regPath -Name "DefaultPassword" -Value "<PASSWORD>"
    ```

    > This stores the password in the registry in plain text. Keep the
    > bench PC physically secured. If you'd rather not store a plaintext
    > value, Microsoft's Sysinternals **Autologon.exe** tool does the same
    > thing but encrypts it — swap it in for steps 20-21 if you prefer.

### 4c. Make the runner start the moment that account logs in

22. Still as Administrator, run:
    ```powershell
    $action = New-ScheduledTaskAction -Execute "C:\Users\Administrator\actions-runner\run.cmd"
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User "Administrator"
    ```
23. Run:
    ```powershell
    $principal = New-ScheduledTaskPrincipal -UserId "Administrator" -LogonType Interactive -RunLevel Highest
    Register-ScheduledTask -TaskName "GitHubActionsRunner" -Action $action -Trigger $trigger -Principal $principal
    ```

### 4d. Reboot and confirm

24. Run:
    ```powershell
    Restart-Computer
    ```
25. Wait for the PC to come back up. It should log in to `Administrator`
    automatically (no one needs to type anything) and a console window
    running `run.cmd` should appear on screen, ending with the line
    `Listening for Jobs`.
26. Go back to the GitHub browser tab: **Settings → Actions → Runners**.
    Refresh the page. The runner should show a green dot and **Idle**.

    If it doesn't show up: RDP/console into the PC, confirm you're logged
    in as `Administrator`, and check whether the `run.cmd` window is open.
    If not, double-click `C:\Users\Administrator\actions-runner\run.cmd`
    manually once to see any error text directly.

---

## Part 5 — Add the workflow file (this part is in the PLC repo's source, any machine)

27. In the **PLC project repo**, create the file
    `.github/workflows/plc-build.yml` with this content:
    ```yaml
    name: PLC Build

    on:
      push:
        branches: [main, master]

    jobs:
      build:
        runs-on: [self-hosted, Windows, X64]
        steps:
          - name: Checkout PLC project
            uses: actions/checkout@v4

          - name: Compile PLC project
            shell: powershell
            run: |
              $tool = "C:\tools\beckhoffAutomationInterface\beckhoffAutomationInterface\bin\Debug\net48\beckhoffAutomationInterface.exe"
              $plcName = "<RealPlcName>"   # omit --plc-name below entirely if not needed

              & $tool build "$env:GITHUB_WORKSPACE" --plc-name $plcName
              if ($LASTEXITCODE -ne 0) {
                throw "PLC build failed (exit code $LASTEXITCODE)"
              }
    ```
28. `build` finds the checked-out repo's single `.tsproj` automatically —
    only replace `<RealPlcName>` (or drop `--plc-name` entirely if the
    `.tsproj`'s own base name already matches). If you need to check: on the
    bench PC, inside a checkout of the PLC repo, run
    `Get-ChildItem -Recurse -Filter *.plcproj` — `--plc-name` is that file's
    name **without** the extension.
29. Confirm `C:\tools\beckhoffAutomationInterface\...\beckhoffAutomationInterface.exe`
    already exists on the bench PC (this tool, built once — see
    [plc-ci-setup-guide.md](plc-ci-setup-guide.md) §3 if not).
30. Commit and push this file to `main`/`master`:
    ```powershell
    git add .github/workflows/plc-build.yml
    git commit -m "Add PLC compile CI workflow"
    git push
    ```
    This push is itself the first CI trigger.

---

## Part 6 — Verify it actually worked

31. On GitHub, open the PLC repo's **Actions** tab.
32. You should see a **PLC Build** run in progress, picked up by your
    runner (its name shows next to the job).
33. Click into it. It should show `Checkout PLC project`, then
    `Compile PLC project` — this step takes a minute or two (Visual
    Studio opening TwinCAT), same as a manual build would.
34. Green check = build passed. Red X = build failed, with the compiler
    errors printed in that step's log.
35. Optional but recommended: deliberately break something small in the PLC
    project, push it, confirm the job goes red with a readable error, then
    revert — proves the failure path works, not just the happy path.

---

Done. For troubleshooting and the manual pre-CI validation build, see
[plc-ci-setup-guide.md](plc-ci-setup-guide.md) — this document only covers
the runner install itself in more granular steps.
