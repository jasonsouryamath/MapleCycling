// Wahoo TICKR / generic BLE heart-rate strap transport.
//
// The Bluetooth work lives in a NATIVE plugin, Assets/Plugins/MapleRideBleNative.dll
// (source: tools/ble_plugin/MapleRideBleNative.cpp), built with C++/WinRT. This is deliberate:
// Unity's desktop Mono cannot load the WinRT "Windows" metadata assembly, so a MANAGED plugin that
// references Windows.Devices.Bluetooth throws TypeLoadException at load. A native DLL that uses
// WinRT internally and exposes plain C functions is P/Invoked cleanly by Mono - it never sees a
// WinRT type. On non-Windows platforms this file is a harmless stub.

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>Windows adapter that P/Invokes the native C++/WinRT heart-rate plugin.</summary>
public sealed class BleHeartRateSource : IHeartRateSource
{
    private const string Dll = "MapleRideBleNative";

    [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern void mrhr_start(string nameFilter, double timeout);
    [DllImport(Dll)] private static extern void mrhr_stop();
    [DllImport(Dll)] private static extern int mrhr_bpm();
    [DllImport(Dll)] private static extern int mrhr_connected();
    [DllImport(Dll)] private static extern int mrhr_adv_count();
    [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern int mrhr_status(StringBuilder buf, int len);
    [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern int mrhr_seen(StringBuilder buf, int len);
    [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern int mrhr_device(StringBuilder buf, int len);

    private readonly string _nameFilter;
    private readonly float _timeoutSeconds;
    private bool _failed;
    private float _logTimer;

    public BleHeartRateSource(string nameFilter, float timeoutSeconds = 5f)
    {
        _nameFilter = nameFilter ?? "";
        _timeoutSeconds = Mathf.Max(1f, timeoutSeconds);
    }

    private static string GetStatus() { var sb = new StringBuilder(160); try { mrhr_status(sb, sb.Capacity); } catch { } return sb.ToString(); }
    private static string GetSeen() { var sb = new StringBuilder(256); try { mrhr_seen(sb, sb.Capacity); } catch { } return sb.ToString(); }
    private static string GetDevice() { var sb = new StringBuilder(160); try { mrhr_device(sb, sb.Capacity); } catch { } return sb.ToString(); }

    public string Label
    {
        get
        {
            if (_failed) return "BLE HR (native plugin missing)";
            if (IsConnected)
            {
                string d = GetDevice();
                return string.IsNullOrEmpty(d) ? "HR strap (BLE)" : d + " (BLE)";
            }
            return "BLE HR (" + GetStatus() + ")";
        }
    }

    public bool IsConnected { get { try { return mrhr_connected() != 0; } catch { return false; } } }
    public int BeatsPerMinute { get { try { return mrhr_bpm(); } catch { return 0; } } }

    public string Diagnostics
    {
        get
        {
            if (_failed) return "native plugin missing (MapleRideBleNative.dll)";
            int adv = 0; try { adv = mrhr_adv_count(); } catch { }
            string d = "status: " + GetStatus() + "   adv: " + adv;
            string seen = GetSeen();
            if (!string.IsNullOrEmpty(seen)) d += "\nseen: " + seen;
            return d;
        }
    }

    public void Start()
    {
        try
        {
            mrhr_start(_nameFilter, _timeoutSeconds);
            _failed = false;
            _logTimer = 0f;
            Debug.Log("[ride][hr] native scan started for '" +
                      (string.IsNullOrEmpty(_nameFilter) ? "any HR strap" : _nameFilter) + "'");
        }
        catch (DllNotFoundException e)
        {
            _failed = true;
            Debug.LogWarning("[ride] MapleRideBleNative.dll not found (build it with " +
                             "tools/ble_plugin/build.ps1): " + e.Message);
        }
        catch (Exception e)
        {
            _failed = true;
            Debug.LogWarning("[ride] BLE native start failed: " + e.Message);
        }
    }

    public void Poll(float dt)
    {
        if (_failed) return;
        _logTimer += dt;
        if (_logTimer < 2f) return;
        _logTimer = 0f;
        if (IsConnected) return;
        int adv = 0; try { adv = mrhr_adv_count(); } catch { }
        Debug.Log("[ride][hr] status=" + GetStatus() + " adv=" + adv + " seen=[" + GetSeen() + "]");
    }

    public void Stop() { try { mrhr_stop(); } catch { } }
}
#else
using UnityEngine;

/// <summary>
/// Non-Windows stub: keeps the heart-rate path type-complete so gameplay code never has to
/// <c>#if</c> around it, and is honest about being unavailable rather than faking a connection.
/// </summary>
public sealed class BleHeartRateSource : IHeartRateSource
{
    private readonly string _nameFilter;
    private bool _warned;

    public BleHeartRateSource(string nameFilter, float timeoutSeconds = 5f)
    {
        _nameFilter = nameFilter;
    }

    public string Label => "BLE HR (Windows only)";
    public bool IsConnected => false;
    public int BeatsPerMinute => 0;
    public string Diagnostics => "Windows only";

    public void Start()
    {
        if (_warned) return;
        _warned = true;
        Debug.LogWarning("[ride] BleHeartRateSource: the Bluetooth transport is Windows-only. " +
                         "On this platform the HUD shows simulated heart rate.");
    }

    public void Poll(float dt) { }
    public void Stop() { }
}
#endif
