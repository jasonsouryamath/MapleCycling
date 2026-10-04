# Runs Unity batch steps in sequence, safely shared between several agents.
#  * Two labs: Lab 1 is this project; Lab 2 is a hard-linked copy (see lab_sync.ps1 / lab_setup.ps1).
#    A batch takes whichever lab is free and keeps it for ALL its steps (Apply -> Capture stay together).
#  * Fair queue: batches get a lab first come, first served (ticket files in tools/unity/queue).
#  * An exclusive lock FILE per lab serialises the runners of that lab.
#  * Only Unity processes on the SAME project block a lab, so an interactive Editor open on this
#    project just sends batches to Lab 2.
#  * Lab 2's Assets changes are copied back into this project when the batch ends. Logs, captures and
#    other outputs always land in this project.
#  * WeatherMathValidation.Run (the "does it compile?" step) first runs the offline Roslyn compile check
#    BEFORE taking a lab: a failed compile stops the batch without using a lab (errors go to that step's log).
#    If later steps launch Unity anyway, the Unity compile step is skipped (those steps compile everything);
#    a compile-only batch still runs it in Unity, because the offline check has missed a real error before.
#    Set $env:UNITY_REAL_COMPILE = "1" to always run it in Unity.
#  * Time limit per step (default 30 min, or a 4th field: "Method|log|quit|minutes"). A step that runs
#    past it is stopped (only the Unity process this runner started) and reported as TIMEOUT.
#  * Retries a step that dies immediately (tiny log, non-zero exit) - the project was still locked.
#  * PLAYTIME (tools/unity/PLAYTIME.json, set by the Play button in Agent HQ): the user wants to play. No batch takes
#    a lab while it exists (queued batches wait and start by themselves afterwards), and a running batch stops
#    after its current step (never mid-step) and exits 3, printing the skipped steps to re-run.
# Usage: run_steps.ps1 "Method|log|quit[|minutes]" ...   (quit = 1 adds -quit; 0 for play-mode captures)
#        $env:UNITY_LAB = "1" or "2" forces one lab.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Steps)
$R  = "C:\Users\jason\OneDrive\Desktop\MapleRide"
$UN = "C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Unity.exe"
$DefaultTimeoutMin = 30
. (Join-Path $PSScriptRoot "lab_sync.ps1")

$Labs = @(
    @{ Name = "Lab 1"; Id = "1"; Path = $R;       Lock = (Join-Path $PSScriptRoot "unity.lock");      Clone = $false }
    @{ Name = "Lab 2"; Id = "2"; Path = $LabPath; Lock = (Join-Path $PSScriptRoot "unity_lab2.lock"); Clone = $true }
) | Where-Object { -not $_.Clone -or (Test-Path (Join-Path $_.Path ".lab_ready")) -or $env:UNITY_LAB -eq $_.Id }
if ($env:UNITY_LAB) { $Labs = @($Labs | Where-Object { $_.Id -eq $env:UNITY_LAB }) }
if (-not $Labs) { throw "no Unity lab available (UNITY_LAB=$env:UNITY_LAB)" }

function Unity-Running($path) {
    foreach ($p in Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue) {
        $c = $p.CommandLine
        if (-not $c) { if ($path -eq $R) { return $true } else { continue } }
        if ($c.Replace('/', '\').IndexOf($path, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------- compile checks without Unity
$Batch = @($Steps | ForEach-Object { $m, $l, $q, $t = $_.Split("|"); @{ Method = $m; Log = $l; Quit = $q; Timeout = $(if ($t) { [double]$t } else { $DefaultTimeoutMin }) } })
if ($env:UNITY_REAL_COMPILE -ne "1") {
    $compile = @($Batch | Where-Object { $_.Method -eq "WeatherMathValidation.Run" })
    if ($compile) {
        $out = & pwsh -NoProfile -File (Join-Path $PSScriptRoot "offline_compile.ps1") *>&1 | ForEach-Object { "$_" }
        $ok = $out -contains "OFFLINE COMPILE OK"
        $failed = $out -contains "OFFLINE COMPILE FAILED"
        if ($ok -or $failed) {
            # the offline check has missed a real Unity error before (board N rule 6): only skip the Unity compile
            # when a later step launches Unity anyway (it compiles everything and its `error CS` lines are reported)
            $rest = @($Batch | Where-Object { $_.Method -ne "WeatherMathValidation.Run" })
            $skip = $failed -or $rest.Count -gt 0
            foreach ($c in $compile) {
                $how = if ($skip) { "no Unity launch" } else { "Unity check still runs next (compile-only batch)" }
                [IO.File]::WriteAllLines("$R\$($c.Log)", @("[runner] offline compile check before WeatherMathValidation.Run ($how).") + $out)
                "[WeatherMathValidation.Run] offline compile $(if ($ok) { 'OK' } else { 'FAILED' }) ($how, log: $($c.Log))$(if ($skip) { ' exit=' + $(if ($ok) { 0 } else { 1 }) })"
            }
            $out | Where-Object { $_ -match 'error CS' } | Select-Object -First 12 | ForEach-Object { "   $_" }
            if ($failed) { "[runner] compile errors: batch stopped before taking a Unity lab. Fix them and re-run."; exit 1 }
            if ($rest) { $Batch = $rest }
        } else {
            "[runner] offline compile could not run; running WeatherMathValidation.Run in Unity instead"
        }
    }
    if (-not $Batch) { exit 0 }
}

# ---------------------------------------------------------------- fair queue
# PLAYTIME.json = the user pressed the Play button in Agent HQ: they want to play, so every agent leaves the labs.
$PlayFlag = Join-Path $PSScriptRoot "PLAYTIME.json"
function Say-Playtime($what) {
    Write-Host "[runner] $(Get-Date -Format HH:mm:ss) PLAYTIME: the user is playing MapleRide and the Unity labs are closed - $what."
    Write-Host "[runner] Save your work now: finish the edit you're in and post a CHECKPOINT line in COORDINATION.md (done / next / files)."
    Write-Host "[runner] Then do only non-Unity work (or pause). Don't start or kill Unity. The labs reopen when the user presses the button again."
}
$QueueDir = Join-Path $PSScriptRoot "queue"
New-Item -ItemType Directory -Force $QueueDir | Out-Null
$Ticket = Join-Path $QueueDir ("{0:D19}_{1}.ticket" -f [DateTime]::UtcNow.Ticks, $PID)
if (-not $env:UNITY_LAB) { Set-Content $Ticket ($Batch | ForEach-Object { $_.Method }) -Encoding utf8 }

function First-InLine {
    if ($env:UNITY_LAB) { return $true }   # forced to one lab: outside the shared line, so it never blocks the other lab
    $live = foreach ($t in Get-ChildItem $QueueDir -Filter *.ticket | Sort-Object Name) {
        $owner = [int]($t.BaseName.Split("_")[1])
        if (Get-Process -Id $owner -ErrorAction SilentlyContinue) { $t.FullName } else { Remove-Item $t.FullName -ErrorAction SilentlyContinue }
    }
    return (@($live)[0] -eq $Ticket)
}

function Acquire-Lab {
    $waits = 0
    $playSaid = $false
    while ($true) {
        if (Test-Path $PlayFlag) {
            if (-not $playSaid) { Say-Playtime "this batch has NOT started and waits (no lab taken)"; $playSaid = $true }
            Start-Sleep -Seconds 10
            continue
        }
        if ($playSaid) { Write-Host "[runner] $(Get-Date -Format HH:mm:ss) PLAYTIME OVER - the labs are open again; this batch is back in the queue."; $playSaid = $false }
        if (First-InLine) {
            foreach ($lab in $Labs) {
                try { $h = [System.IO.File]::Open($lab.Lock, 'OpenOrCreate', 'ReadWrite', 'None') } catch { continue }
                if (Unity-Running $lab.Path) { $h.Close(); continue }
                return @{ Lab = $lab; Handle = $h }
            }
        }
        if (++$waits -eq 4) {
            Write-Host "[runner] $(Get-Date -Format HH:mm:ss) ALL UNITY LABS ARE BUSY - you are queued and this batch will start by itself."
            Write-Host "[runner] Do NOT wait idle: leave this runner going in the background, grab another task from COORDINATION.md"
            Write-Host "[runner] (or a non-Unity part of your own task) and work on it; check this runner again later."
        }
        Start-Sleep -Seconds 3
    }
}

function Stop-Tree([int]$id) {
    foreach ($c in Get-CimInstance Win32_Process -Filter "ParentProcessId=$id" -ErrorAction SilentlyContinue) { Stop-Tree ([int]$c.ProcessId) }
    Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
}

try { $got = Acquire-Lab } finally { Remove-Item $Ticket -ErrorAction SilentlyContinue }
$lab = $got.Lab
$HolderFile = Join-Path $PSScriptRoot "lab$($lab.Id).holder"   # tells the dashboard who owns the lab, also between steps
@{ pid = $PID; steps = $Steps; since = [DateTime]::UtcNow.ToString("o") } | ConvertTo-Json -Compress | Set-Content $HolderFile -Encoding utf8
"[runner] $(Get-Date -Format HH:mm:ss) using $($lab.Name) ($($lab.Path))"
$sync = $null
try {
    if ($lab.Clone) { $sync = Sync-LabIn }
    $done = 0
    foreach ($s in $Batch) {
        $method, $log, $quit = $s.Method, $s.Log, $s.Quit
        if (Test-Path $PlayFlag) {
            $left = @($Batch | Select-Object -Skip $done | ForEach-Object { "`"$($_.Method)|$($_.Log)|$($_.Quit)`"" })
            Say-Playtime "this batch stopped after $done step(s) and left $($lab.Name)"
            "[runner] Skipped (re-run after playtime): run_steps.ps1 $($left -join ' ')"
            $script:PlaytimeStop = $true
            break
        }
        $done++
        $timedOut = $false
        for ($attempt = 1; $attempt -le 6; $attempt++) {
            while (Unity-Running $lab.Path) { Start-Sleep 5 }
            Start-Sleep 3
            $args = @("-projectPath", $lab.Path, "-batchmode")
            if ($quit -ne "0") { $args += "-quit" }
            $args += @("-executeMethod", $method, "-logFile", "$R\$log")
            $p = Start-Process -FilePath $UN -ArgumentList $args -PassThru
            if (-not $p.WaitForExit([int]($s.Timeout * 60000))) {
                $timedOut = $true
                Stop-Tree $p.Id
                $p.WaitForExit(30000) | Out-Null
                Add-Content "$R\$log" "`n[runner] TIMEOUT: stopped after $($s.Timeout) min (raise it with a 4th field: `"$method|$log|$quit|<minutes>`")."
                break
            }
            $lines = (Get-Content "$R\$log" -ErrorAction SilentlyContinue | Measure-Object -Line).Lines
            if ($p.ExitCode -ne 0 -and $lines -lt 60) {
                "[$method] exit=$($p.ExitCode) after $lines log lines - project busy? retry $attempt"
                Start-Sleep 20
                continue
            }
            break
        }
        if ($timedOut) { "[$method] TIMEOUT after $($s.Timeout) min ($($lab.Name)) - stopped; the rest of this batch is skipped"; $script:BatchTimedOut = $true; break }
        "[$method] exit=$($p.ExitCode) ($($lab.Name))"
        Select-String -Path "$R\$log" -Pattern "skin-tone|baked .* greeting|kuro-closeup|minato-crowd\] prepared|port apron|staged 30|error CS|Shader error|Exception:" |
            Select-Object -Last 8 | ForEach-Object { "   " + $_.Line }
    }
}
finally {
    if ($sync) { Sync-LabOut $sync }
    Remove-Item $HolderFile -ErrorAction SilentlyContinue
    $got.Handle.Close()
}
if ($script:BatchTimedOut) { exit 1 }
if ($script:PlaytimeStop) { exit 3 }
