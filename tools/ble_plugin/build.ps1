# Rebuilds Assets/Plugins/MapleRideBleNative.dll from MapleRideBleNative.cpp.
#
# NATIVE plugin (C++/WinRT): does the BLE heart-rate work and exposes plain C functions the Unity
# side P/Invokes. It is native on purpose - Unity's desktop Mono cannot load the managed WinRT
# "Windows" metadata assembly (TypeLoadException), but it P/Invokes a native DLL with no trouble.
#
# Requires Visual Studio 2022 Build Tools (cl.exe) + Windows 10/11 SDK with C++/WinRT headers.

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..\..")

# Locate vcvars64.bat (prefer vswhere, fall back to the known BuildTools path).
$vcvars = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vsPath) { $vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat" }
}
if (-not $vcvars -or -not (Test-Path $vcvars)) {
    $vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
}
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found - install VS 2022 Build Tools with the C++ workload." }

$src = Join-Path $here "MapleRideBleNative.cpp"
$outDir = Join-Path $repo "Assets\Plugins"
$out = Join-Path $outDir "MapleRideBleNative.dll"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$tmp = Join-Path $env:TEMP "mrhr_build"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$cl = "cl /nologo /std:c++20 /EHsc /O2 /MD /LD `"$src`" /Fe:MapleRideBleNative.dll /link windowsapp.lib"
$cmd = "call `"$vcvars`" >nul && cd /d `"$tmp`" && $cl"
& $env:ComSpec /c $cmd
if ($LASTEXITCODE -ne 0) { throw "Native build failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $tmp "MapleRideBleNative.dll") $out -Force
Write-Host "Built OK -> $out ($((Get-Item $out).Length) bytes)"
