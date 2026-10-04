using UnityEditor;
using UnityEngine;

/// <summary>Creates/refreshes the stock weather presets in Assets/Resources/Weather (editable
/// afterwards; re-running only fills presets that do not exist yet).
/// Run: -executeMethod WeatherPresetAuthoring.CreateDefaults</summary>
public static class WeatherPresetAuthoring
{
    [MenuItem("MapleRide/Weather/Create Default Presets")]
    public static void CreateDefaults()
    {
        const string dir = "Assets/Resources/Weather";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Resources", "Weather");
        // name, windTo, sustained, gust, gustHz, turb, cover, density, precip, humidity, fog, tempC, wet, transition
        Make(dir, "ClearCoastalBreeze",    60f,  3.5f, 1.5f, 0.06f, 0.20f, 0.30f, 0.45f, 0.00f, 0.50f, 0.05f, 20f, 0.0f, 240f);
        Make(dir, "MarineHaze",            80f,  2.0f, 0.8f, 0.04f, 0.10f, 0.45f, 0.35f, 0.00f, 0.85f, 0.35f, 17f, 0.1f, 300f);
        Make(dir, "BuildingAfternoonWind", 70f,  6.0f, 3.0f, 0.08f, 0.35f, 0.40f, 0.55f, 0.00f, 0.45f, 0.08f, 22f, 0.0f, 360f);
        Make(dir, "PassingCloudFront",     95f,  7.0f, 3.5f, 0.10f, 0.45f, 0.70f, 0.75f, 0.10f, 0.70f, 0.15f, 16f, 0.2f, 300f);
        Make(dir, "SunShower",             90f,  4.0f, 2.0f, 0.08f, 0.30f, 0.50f, 0.60f, 0.45f, 0.80f, 0.12f, 18f, 0.7f, 180f);
        Make(dir, "StrongBridgeCrosswind", 150f, 9.5f, 4.5f, 0.09f, 0.40f, 0.35f, 0.50f, 0.00f, 0.50f, 0.06f, 19f, 0.0f, 240f);
        Make(dir, "DistantOffshoreStorm",  110f, 6.5f, 3.0f, 0.07f, 0.35f, 0.65f, 0.85f, 0.05f, 0.75f, 0.18f, 17f, 0.1f, 420f);
        Make(dir, "PostRainClearing",      40f,  4.5f, 2.0f, 0.06f, 0.25f, 0.40f, 0.50f, 0.00f, 0.75f, 0.10f, 17f, 0.8f, 360f);
        // WP-E snow (Azora Highlands winter). Cold air turns precipitation to snow (WeatherState.SnowFraction).
        Make(dir, "AlpineSnowfall",        75f,  3.0f, 1.8f, 0.06f, 0.30f, 0.80f, 0.70f, 0.55f, 0.85f, 0.32f, -4f, 0.3f, 240f, snow: 1.00f);
        Make(dir, "LightSnowFlurries",     60f,  2.5f, 1.4f, 0.07f, 0.25f, 0.55f, 0.50f, 0.22f, 0.75f, 0.18f, -3f, 0.2f, 240f, snow: 0.95f);
        Make(dir, "SnowClearing",          40f,  1.8f, 1.0f, 0.05f, 0.15f, 0.30f, 0.40f, 0.04f, 0.60f, 0.10f, -5f, 0.1f, 300f, snow: 1.00f);
        MakeSnowflakeMaterial(dir);
        AssetDatabase.SaveAssets();
        Debug.Log("[weather] default presets ready in " + dir);
    }

    private static void Make(string dir, string name, float to, float sus, float gust, float hz, float turb,
                             float cover, float dens, float precip, float hum, float fog, float t, float wet, float trans,
                             float snow = 0f)
    {
        string path = $"{dir}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<WeatherPreset>(path) != null) return;
        var p = ScriptableObject.CreateInstance<WeatherPreset>();
        p.windToDeg = to; p.sustainedMps = sus; p.gustMps = gust; p.gustFrequencyHz = hz; p.turbulence = turb;
        p.cloudCoverage = cover; p.cloudDensity = dens; p.precipitation = precip; p.humidity = hum;
        p.fog = fog; p.airTempC = t; p.wetness = wet; p.transitionSeconds = trans; p.snowCover = snow;
        AssetDatabase.CreateAsset(p, path);
    }

    /// <summary>Resources/Weather/Snowflake.mat - keeps the snowflake shader in player builds
    /// (WeatherEffects loads it by name; Shader.Find alone would strip it).</summary>
    private static void MakeSnowflakeMaterial(string dir)
    {
        string path = $"{dir}/Snowflake.mat";
        var sh = Shader.Find("MapleRide/HDRP/Snowflake");
        if (sh == null) { Debug.LogWarning("[weather] MapleRide/HDRP/Snowflake shader missing"); return; }
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
    }
}
