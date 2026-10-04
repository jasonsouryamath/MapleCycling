# One-time setup of Unity "Lab 2" (see lab_sync.ps1). Safe to re-run.
# Takes the main project's Unity lock while it copies Library, so the databases aren't copied mid-write.
# Usage: pwsh -NoProfile -File tools/unity/lab_setup.ps1 [-NoWait]   (-NoWait: copy Library without waiting for the lock)
param([switch]$NoWait)
. (Join-Path $PSScriptRoot "lab_sync.ps1")
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $LabPath | Out-Null

function Unity-OnMain {
    Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
        Where-Object { -not $_.CommandLine -or $_.CommandLine.Replace('/', '\').IndexOf($MainPath, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
}

if (-not (Test-Path (Join-Path $LabPath "Library\ArtifactDB"))) {
    "[setup] waiting for the main project's Unity lock (queued behind current jobs)..."
    $lockPath = Join-Path $PSScriptRoot "unity.lock"
    $lock = $null
    while (-not $lock -and -not $NoWait) { try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') } catch { Start-Sleep 5 } }
    try {
        while (-not $NoWait -and (Unity-OnMain)) { Start-Sleep 5 }
        "[setup] $(Get-Date -Format HH:mm:ss) lock held, linking Library"
        $src = Join-Path $MainPath "Library"; $dst = Join-Path $LabPath "Library"
        $n1 = [LabSync]::LinkTree("$src\Artifacts", "$dst\Artifacts")
        $n2 = [LabSync]::LinkTree("$src\PackageCache", "$dst\PackageCache")
        robocopy $src $dst /E /XD "$src\Artifacts" "$src\PackageCache" "$src\Bee" /XF *.lock /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        "[setup] Library: $n1 artifacts + $n2 package files hard-linked, the rest copied"
    }
    finally { if ($lock) { $lock.Close() } }
}

foreach ($d in "UserSettings") {
    if (Test-Path (Join-Path $MainPath $d)) { robocopy (Join-Path $MainPath $d) (Join-Path $LabPath $d) /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null }
}
$null = Sync-LabIn  # prints its own summary
"[setup] $(Get-Date -Format HH:mm:ss) Lab 2 ready at $LabPath"
