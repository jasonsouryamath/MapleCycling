using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Headless scene check for SakuraPassEnvironment's authored wind exposure.</summary>
public static class SakuraWindVolumeValidation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RootPath = "Sakura Pass Environment/Wind Volumes";

    [MenuItem("MapleRide/Validation/Sakura Wind Volumes")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int pass = 0, fail = 0;
        void Check(string label, bool ok, string detail = "")
        {
            if (ok)
            {
                pass++;
                Debug.Log($"[sakura-wind] PASS {label}{(detail.Length > 0 ? ": " + detail : "")}");
            }
            else
            {
                fail++;
                Debug.LogError($"[sakura-wind] FAIL {label}{(detail.Length > 0 ? ": " + detail : "")}");
            }
        }

        var parent = GameObject.Find(RootPath);
        Check("volume root exists", parent != null, RootPath);
        if (parent == null)
            throw new InvalidOperationException("Sakura wind volume root is missing. Rebuild Sakura Pass first.");

        var volumes = parent.GetComponentsInChildren<WeatherWindVolume>(true);
        var sheltered = volumes.Where(v => Mathf.Approximately(v.windMultiplier, 0.6f)).ToArray();
        var exposed = volumes.Where(v => Mathf.Approximately(v.windMultiplier, 1.3f)).ToArray();

        Check("27 route volumes staged", volumes.Length == 27, volumes.Length.ToString());
        Check("Kawabe valley count", sheltered.Length == 15, sheltered.Length.ToString());
        Check("ridge count", exposed.Length == 12, exposed.Length.ToString());
        Check("all volumes are direct region descendants",
              volumes.All(v => v.transform.IsChildOf(parent.transform)));
        Check("all colliders are triggers",
              volumes.All(v => v.TryGetComponent<BoxCollider>(out var box) && box.isTrigger));
        Check("all edge blends are authored",
              volumes.All(v => Mathf.Approximately(v.edgeBlendM, 18f)));

        // Ordinary MonoBehaviour OnEnable messages do not run in edit mode. Register exactly as
        // play mode does so this check exercises WeatherWindVolume.TryExposureAt, then leave the
        // static registry clean for any later editor work in the same process.
        foreach (var volume in volumes) CallLifecycle(volume, "OnDisable");
        foreach (var volume in volumes) CallLifecycle(volume, "OnEnable");
        try
        {
            CheckFunctionalCentre("Kawabe centre resolves x0.60", sheltered, 0.6f, ref pass, ref fail);
            CheckFunctionalCentre("ridge centre resolves x1.30", exposed, 1.3f, ref pass, ref fail);
        }
        finally
        {
            foreach (var volume in volumes) CallLifecycle(volume, "OnDisable");
        }

        Debug.Log($"[sakura-wind] RESULT {pass} passed, {fail} failed");
        if (fail > 0)
            throw new InvalidOperationException($"Sakura wind validation failed: {fail} check(s).");
    }

    private static void CheckFunctionalCentre(string label, WeatherWindVolume[] volumes,
                                              float expected, ref int pass, ref int fail)
    {
        if (volumes.Length == 0)
        {
            fail++;
            Debug.LogError($"[sakura-wind] FAIL {label}: no matching volume");
            return;
        }

        var sample = volumes[volumes.Length / 2];
        bool found = WeatherWindVolume.TryExposureAt(sample.transform.position, out float actual);
        bool ok = found && Mathf.Abs(actual - expected) < 0.02f;
        if (ok)
        {
            pass++;
            Debug.Log($"[sakura-wind] PASS {label}: {actual:0.000}");
        }
        else
        {
            fail++;
            Debug.LogError($"[sakura-wind] FAIL {label}: found={found}, actual={actual:0.000}");
        }
    }

    private static void CallLifecycle(WeatherWindVolume volume, string method)
    {
        typeof(WeatherWindVolume).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(volume, null);
    }
}
