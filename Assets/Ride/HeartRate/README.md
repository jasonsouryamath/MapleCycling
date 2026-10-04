# Heart-rate monitor (Wahoo TICKR / any BLE strap)

MapleRide reads heart rate over the standard Bluetooth **Heart Rate Service** (`0x180D`,
characteristic `0x2A37`). A TICKR only produces **heart rate** — power and cadence still come from
the trainer / mash model — so HR is a presentation signal (the HUD `♥` readout and heart tile).

## How to use it
Click the **heart tile** (top-right, under World Map) or press **H**. It scans and connects; the
tile shows `SEARCHING`, then live `### BPM` with a beating heart. Target strap is set on the
`MapleRide Ride` object → DeviceManager → **Heart rate monitor** (Device Name `TICKR`, or empty to
take the first HR strap).

## Why a NATIVE plugin
Unity's desktop **Mono cannot load the WinRT `Windows` metadata assembly** — a *managed* DLL that
references `Windows.Devices.Bluetooth` throws `TypeLoadException` at load (that was the original
"can't connect" bug). The fix is a **native** plugin:

- **`Assets/Plugins/MapleRideBleNative.dll`** — C++/WinRT, does all the Bluetooth work and exposes
  plain C functions (`mrhr_start/stop/bpm/connected/adv_count/status/seen/device`). Mono P/Invokes
  these and never sees a WinRT type. Source: `tools/ble_plugin/MapleRideBleNative.cpp`.
- `BleHeartRateSource.cs` — Windows adapter that P/Invokes the plugin (stub on other platforms).
- It finds the strap two ways: **paired-device enumeration** (bonded straps don't advertise) and
  **advertisement watching** (matched on the HR service uuid or the name filter).

## Rebuilding the native plugin
Run `tools/ble_plugin/build.ps1` (needs VS 2022 Build Tools + Windows SDK with C++/WinRT). It
compiles `MapleRideBleNative.cpp` to `Assets/Plugins/MapleRideBleNative.dll` (x64).

## Diagnostics
While searching, the pairing popover and the Unity Console (`[ride][hr]`) show:
`status: … adv: N seen: …`
- `adv: 0` → no advertisements reaching the app (Bluetooth off/blocked, or nothing advertising).
- `adv > 0`, strap not in `seen` → strap asleep or held by the Wahoo app / a phone — free it.
- `status: no service / no characteristic / error: …` → connected but a GATT step failed.

## Testing checklist
1. Pair the TICKR in Windows Bluetooth (or just wake it); close the Wahoo app / phone if it holds it.
2. Enter play mode, click the heart tile (or press **H**).
3. Watch `SEARCHING` → `### BPM`.
