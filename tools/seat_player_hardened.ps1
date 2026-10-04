<#
seat_player_hardened.ps1 - ENFORCED, NON-DESTRUCTIVE order for seating the approved Kuro GLB
onto the bike in SakuraPass.

WHY THIS EXISTS
The clean rider (Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb) renders perfectly on its own but
has repeatedly DEGRADED when seated onto the bike. Seating itself is non-destructive - Unity's
KuroBikeRig poses the rigged skeleton with bone rotation + two-bone IK and never touches mesh,
normals, materials or scale. The degradation came entirely from DOWNSTREAM material passes running
in the wrong order (or re-matching an approved material):

  * KuroCharacterCelLitFix clones the shared red frame into Shared_Colnago_Racing_Red_CelLit and
    re-points the player's frame at it  -> frame goes RED  (guarded in code now; enforced here too)
  * PlayerFidelityFix's bn.Contains("Helmet") caught the body atlas clone
    PlayerBody_KuroHelmetMatte_CelLit and flattened the head to black (guarded in code now)
  * a re-instantiate leaves the body on the raw glTF metallic shader -> chrome (asserted in swap)

The invariant that makes seating reliable: the LIVERY / FRAME / body passes must run AFTER any
CelLit exposure conversion, and the frame-black pass runs LAST. This script runs the passes in
exactly that order, one batchmode launch each with the 20s project-lock settle between them, and
finishes with the in-engine chase capture so the result is judged by a RENDER, not a log line.

USAGE
  1. CLOSE the Unity editor (an open editor holds the project lock; a batch capture fired against a
     locked project silently renders the PREVIOUS build and still exits 0).
  2. pwsh -File tools/seat_player_hardened.ps1
#>

param(
  [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Unity.exe",
  [string]$Project = "C:\Users\jason\OneDrive\Desktop\MapleRide",
  [switch]$SkipBackup
)

$ErrorActionPreference = "Stop"
$glb = Join-Path $Project "Assets\Kuro\NPC\KuroNPC_KuroAnime_Rigged.glb"
$logDir = Join-Path $Project "Logs\seat_hardened"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# --- Guard 1: refuse to run against a LOCKED project (the "renders the previous build" trap) ----
$lock = Join-Path $Project "Temp\UnityLockfile"
$unityOpen = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object {
  (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)").CommandLine -match [regex]::Escape($Project) -and
  (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)").CommandLine -notmatch "AssetImportWorker"
}
if ($unityOpen) {
  Write-Host "ABORT: a Unity editor is open on this project (PID $($unityOpen.Id -join ', ')). Close it first;" -ForegroundColor Red
  Write-Host "       a batch capture against a locked project silently renders the PREVIOUS build." -ForegroundColor Red
  exit 2
}

# --- Guard 2: keep a timestamped backup of the approved GLB before the run (insurance) ----------
if (-not $SkipBackup) {
  $bakDir = Join-Path $Project "design_assets\3d\kuro\_backups"
  New-Item -ItemType Directory -Force -Path $bakDir | Out-Null
  $bak = Join-Path $bakDir ("KuroNPC_KuroAnime_Rigged_{0}.glb" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
  Copy-Item $glb $bak
  Write-Host "backup -> $bak" -ForegroundColor DarkGray
}

# ENFORCED ORDER. Each entry is one batchmode launch. Frame-black is LAST; every livery/body pass
# runs after the CelLit exposure conversion.
$steps = @(
  @{ name = "1-swap";        method = "KuroPlayerModelSwap.Apply" },       # re-instantiate GLB, body->matte CelLit, assert no glTF body
  @{ name = "2-refit";       method = "KuroBikeRefit.Run" },              # bike scale + serialized riding pose + ForceSolveOnce
  @{ name = "3-cellit";      method = "KuroCharacterCelLitFix.Run" },     # rider/bike glTF|HDRP-Lit -> CelLit (frame-red revert now guarded)
  @{ name = "4-body-cellit"; method = "KuroPlayerBodyCelLit.Run" },       # reaffirm matte body (reimport-safe)
  @{ name = "5-fidelity";    method = "PlayerFidelityFix.Run" },          # helmet/body/kit matte + black-stays-black clamp (PlayerBody_ now skipped)
  @{ name = "6-framefix";    method = "PlayerBikeFrameBlackFix.Run" },    # LAST WORD on the frame: matte black, per-player clone
  @{ name = "6b-saddle";     method = "KuroSaddleFix.Run" },             # saddle -> matte frame-black + forward tuck (kills the rear "black box"); idempotent
  @{ name = "7-verify";      method = "AnimeKuroChaseShot.RunPlayer" }    # in-engine chase (rear) + helmet + red-pixel report
)

$signal = "\[fidelity\]|\[framefix\]|\[saddlefix\]|\[kuro-cellit\]|\[player-body-cellit\]|\[kuro-swap\]|\[kuro-refit\]|\[chase-shot\]|ASSERTION|error CS|Exception"

foreach ($s in $steps) {
  $log = Join-Path $logDir ("{0}.log" -f $s.name)
  Write-Host "`n=== $($s.name): -executeMethod $($s.method) ===" -ForegroundColor Cyan
  & $Unity -batchmode -projectPath $Project -executeMethod $s.method -logFile $log -quit | Out-Null
  $code = $LASTEXITCODE
  if (Test-Path $log) {
    Get-Content $log | Select-String -Pattern $signal | ForEach-Object { "   $($_.Line)" }
  }
  Write-Host "   exit=$code" -ForegroundColor (@{ $true = "Green"; $false = "Red" }[$code -eq 0])
  if ($code -ne 0) { Write-Host "   STOP: '$($s.method)' failed - fix before continuing (order matters)." -ForegroundColor Red; exit $code }
  Start-Sleep -Seconds 20   # let the batch process release the project lock before the next launch
}

Write-Host "`nDONE. Look at the RENDER (not just the log):" -ForegroundColor Green
Write-Host "  reference/good_graphics/shiosai_real_rider/player_kuro_behind.png   (chase / rear)"
Write-Host "  reference/good_graphics/shiosai_real_rider/player_kuro_helmet.png   (matte head)"
Write-Host "Confirm: spiky hair, matte head, matte-black frame (redPixels ~0%), correct proportions."
