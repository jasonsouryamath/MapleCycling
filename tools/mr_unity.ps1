param(
  [Parameter(Mandatory = $true)][string]$Method,
  [Parameter(Mandatory = $true)][string]$Log,
  [int]$Retries = 3
)
# Unity batchmode on this machine reliably fails its FIRST launch after an idle period: it
# prints the banner, logs "Successfully changed project path", then exits rc=1 with a ~1.0 KB
# log WITHOUT ever running -executeMethod. A retry succeeds. This wrapper makes that
# deterministic instead of a per-call surprise, and enforces the 20 s batchmode lock gap.
#
# Note: tools/run_unity.ps1 already exists but is pinned to editor 6000.3.17f1, which is not
# the version this project uses (6000.4.11f1). Left untouched deliberately.
$UN   = "C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Unity.exe"
$PROJ = "C:\Users\jason\OneDrive\Desktop\MapleRide"
$logPath = Join-Path $PROJ $Log

# Some passes (notably the *Diagnostics.Capture ones) do not honour -quit and leave an
# Editor process alive holding the project lock. Every later launch then dies during
# bootstrap with the ~1 KB stub log and rc=1, which looks identical to the transient
# first-launch failure. Clear any leftover Editor before starting so the two are not
# confused. There is no interactive Editor session to protect here - the project is driven
# entirely from batchmode in this workflow.
function Clear-StaleUnity {
  foreach ($p in @(Get-Process Unity -ErrorAction SilentlyContinue)) {
    Write-Host "  clearing stale Unity pid=$($p.Id) started=$($p.StartTime)"
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
  }
  if (@(Get-Process Unity -ErrorAction SilentlyContinue).Count -gt 0) { Start-Sleep -Seconds 10 }
}

for ($i = 1; $i -le $Retries; $i++) {
  Clear-StaleUnity
  & $UN -batchmode -quit -projectPath $PROJ -executeMethod $Method -logFile $logPath | Out-Null
  $rc   = $LASTEXITCODE
  $size = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
  Write-Host "attempt $i : rc=$rc size=$size"
  # A real run always produces a log far larger than the ~1 KB bootstrap stub.
  if ($rc -eq 0 -and $size -gt 5000) { Write-Host "OK  $Method"; Clear-StaleUnity; exit 0 }
  if ($size -gt 5000) {
    # Ran for real but reported failure -> surface the reason rather than blindly retrying.
    Write-Host "--- ran but rc=$rc; errors: ---"
    Get-Content $logPath | Select-String "error CS|Exception|ABORT|Aborting" | Select-Object -First 20
    exit $rc
  }
  Start-Sleep -Seconds 20
}
Write-Host "FAILED after $Retries attempts: $Method"
exit 1
