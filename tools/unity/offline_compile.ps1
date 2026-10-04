# Offline C# type-check using Unity's bundled Roslyn, for when the Unity editor is busy/locked.
# Rebuilds Assembly-CSharp and Assembly-CSharp-Editor from the CURRENT Assets/**/*.cs, using the
# last Unity-generated response files for defines/references. Output goes to $env:TEMP only.
# Usage: pwsh -NoProfile -File tools/unity/offline_compile.ps1
$ErrorActionPreference = "Stop"
$R = "C:\Users\jason\OneDrive\Desktop\MapleRide"
$E = "C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Data"
$dag = Get-ChildItem "$R\Library\Bee\artifacts" -Directory -Filter "*.dag" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$out = Join-Path $env:TEMP "mr_offline_compile"
New-Item -ItemType Directory -Force $out | Out-Null
Set-Location $R

$all = Get-ChildItem Assets -Recurse -Filter *.cs | ForEach-Object { $_.FullName.Substring($R.Length + 1).Replace('\', '/') }
# asmdef folders belong to other assemblies
$asm = Get-ChildItem Assets -Recurse -Filter *.asmdef | ForEach-Object { $_.DirectoryName.Substring($R.Length + 1).Replace('\', '/') + "/" }
$all = $all | Where-Object { $f = $_; -not ($asm | Where-Object { $f.StartsWith($_) }) }
$editor = $all | Where-Object { $_ -match '(^|/)Editor/' }
$runtime = $all | Where-Object { $_ -notmatch '(^|/)Editor/' }

function Build($name, $files, $extraRef) {
    $rsp = Get-Content (Join-Path $dag.FullName "$name.rsp") | Where-Object { $_ -notmatch '^"Assets/' -and $_ -notmatch '^-out:|^-refout:' }
    $rsp = $rsp | ForEach-Object { if ($extraRef -and $_ -match 'Assembly-CSharp\.(ref\.)?dll"$' -and $_ -match '^-r:') { "-r:`"$extraRef`"" } else { $_ } }
    $rsp += "-out:`"$out\$name.dll`""
    $rsp += "-refout:`"$out\$name.ref.dll`""
    $rsp += $files | ForEach-Object { "`"$_`"" }
    $rspPath = "$out\$name.rsp"
    Set-Content $rspPath $rsp
    $log = & "$E\NetCoreRuntime\dotnet.exe" exec "$E\DotNetSdkRoslyn\csc.dll" /nostdlib /noconfig "@$rspPath" 2>&1
    $errs = $log | Where-Object { $_ -match 'error CS' }
    Write-Host "[$name] $($files.Count) files, $($errs.Count) errors"
    $errs | Select-Object -First 30 | ForEach-Object { Write-Host $_ }
    if ($errs.Count -eq 0 -and -not (Test-Path "$out\$name.dll")) { $log | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }; return 1 }
    return $errs.Count
}
$e1 = Build "Assembly-CSharp" $runtime $null
$e2 = Build "Assembly-CSharp-Editor" $editor "$out\Assembly-CSharp.ref.dll"
if ($e1 + $e2 -eq 0) { "OFFLINE COMPILE OK" } else { "OFFLINE COMPILE FAILED" }
