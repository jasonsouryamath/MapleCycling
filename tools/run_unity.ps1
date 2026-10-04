<#
Runs a Unity batchmode -executeMethod pass against the MapleRide project.

Why this exists:
  * Batchmode logs used to be written with -logFile into the project tree
    (unity_build.log etc). They are build artefacts, they are large, and they
    kept ending up in the working tree. This script always writes them to
    $env:LOCALAPPDATA\MapleRide\logs, which is outside the repo entirely.
  * Unity.exe returns BEFORE it releases the project lock, and more than one agent/editor can
    be working in this project. A pass that loses the race logs "another Unity instance is
    running" and does nothing at all - while still exiting 0. This retries on that.

Usage:
  .\run_unity.ps1 -Method SakuraPassEnvironment.BuildEnvironmentPass
  .\run_unity.ps1 -Method SakuraPassDiagnostics.Capture -Graphics
#>
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [string]$Tag = "",
    # Capture passes must NOT pass -nographics or every render comes back black.
    [switch]$NoGraphics,
    [int]$Retries = 6,
    [int]$RetryWait = 45,
    [string]$Editor = "C:\Program Files\Unity\Hub\Editor\6000.3.17f1\Editor\Unity.exe",
    [string]$Project = "C:\Users\jason\OneDrive\Desktop\MapleRide"
)

$logDir = Join-Path $env:LOCALAPPDATA "MapleRide\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
if ($Tag -eq "") { $Tag = ($Method -replace '[^A-Za-z0-9]', '_') }
$log = Join-Path $logDir ("{0}_{1:yyyyMMdd_HHmmss}.log" -f $Tag, (Get-Date))

for ($attempt = 1; $attempt -le $Retries; $attempt++) {
    $args = @("-projectPath", $Project, "-batchmode", "-quit", "-executeMethod", $Method,
              "-logFile", $log)
    if ($NoGraphics) { $args += "-nographics" }
    & $Editor @args 2>&1 | Tee-Object -Variable console | Out-Null
    $code = $LASTEXITCODE
    Start-Sleep -Seconds 5   # Unity returns before flushing the log
    $text = if (Test-Path $log) { Get-Content $log -Raw } else { "" }
    # A contended lock aborts before the log file is even opened, so the message only ever
    # appears on stdout - check both.
    $text += "`n" + ($console -join "`n")

    if ($text -match "another Unity instance|Multiple Unity instances|lock.*held") {
        Write-Host "[run_unity] attempt ${attempt}: project lock contended, retrying in $RetryWait s"
        Start-Sleep -Seconds $RetryWait
        continue
    }
    Write-Host "[run_unity] $Method exit=$code log=$log"
    ($text -split "`n") | Select-String "error CS|could not be found|Sakura Pass: saved|\[diag\] wrote" |
        Select-Object -First 40 | ForEach-Object { $_.Line.Trim() }
    exit $code
}

Write-Error "[run_unity] gave up after $Retries attempts - project lock never freed. Log: $log"
exit 1
