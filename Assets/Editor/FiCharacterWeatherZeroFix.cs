using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot repair pass for the Minato helmet/jersey "tear" finding's TRUE root cause: see the
/// detailed writeup in NpcCelLitConversion.ApplyCelLitRecipe and KuroPlayerBodyCelLit.BuildCelLitBody
/// (both now zero these fields for every NEWLY converted material). This class does the same
/// zeroing directly on every ALREADY-PERSISTED character material asset on disk, because the
/// idempotent conversion helpers only ever touch a renderer slot that is still on the raw glTF
/// shader - a slot already redirected to a persisted CelLit clone in a previous session is (by
/// design) never revisited, so those existing clones would otherwise keep carrying the stale,
/// non-zero _DetailAmount/_TintVariation/_WearAmount/_MossAmount/_GrimeAmount/_DappleStrength
/// values forever.
///
/// SCOPE, deliberately narrow: MapleRideCelLit (formerly MR_Weather's static-prop weathering) is
/// shared by hundreds of environment materials (rock/bark/timber/render walls) that legitimately
/// want moss/grime/wear - zeroing it everywhere would flatten every intentionally-weathered prop
/// in the game. This only ever touches material assets that are clearly CHARACTER body/skin/hair/
/// smile materials, by an explicit include list of path/name patterns, with an explicit exclude
/// list for bike-hardware clones that happen to live alongside them (frame/tyre/metal/carbon) and
/// are NOT part of this finding.
/// </summary>
public static class FiCharacterWeatherZeroFix
{
    static readonly string[] IncludeRoots =
    {
        "Assets/Kuro/NPC/Materials",
        "Assets/Kuro/Materials",
    };

    // A material is treated as a CHARACTER material if its asset path/name matches ANY of these.
    static bool IsCharacterMaterial(string path)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        if (file.EndsWith("_Body")) return true;
        if (file.Contains("Skin_CelLit")) return true;
        if (file.Contains("CoralSmile")) return true;
        if (file == "Shared_Material_0_CelLit") return true;
        if (file == "Player_Material_0") return true;
        if (file.StartsWith("PlayerBody_") || file.StartsWith("PlayerOnly_")) return true;
        if (file == "Kuro_Bib_Shorts_Black" || file == "Kuro_Body_Matte" || file == "Kuro_Helmet_Black") return true;
        if (file.StartsWith("PocRider_RR_") || file.StartsWith("PocRider_Material_0")
            || file == "PocRider_KuroSmile") return true;
        return false;
    }

    // Bike-hardware clones that happen to share the include roots/naming prefixes above but are
    // NOT part of this finding (frame/tyre/metal) - never touch these even if a future rename
    // made one accidentally match an include rule above.
    static bool IsExcludedBikePart(string path)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        foreach (var bad in new[] { "MR_Aero_Frame", "MR_Aero_Highlight", "MR_Maple_Accent",
                                     "Colnago", "Brushed_Metal", "Road_Tyre", "Bike_Frame", "Bike_Rim" })
            if (file.Contains(bad)) return true;
        return false;
    }

    static readonly string[] Fields =
        { "_DetailAmount", "_TintVariation", "_WearAmount", "_MossAmount", "_GrimeAmount", "_DappleStrength" };

    [MenuItem("MapleRide/Diagnostics/FI - Zero Character Weathering On All Persisted Materials")]
    public static void Run()
    {
        var touched = new List<string>();
        var skipped = 0;
        var scanned = 0;

        foreach (var root in IncludeRoots)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                scanned++;
                if (IsExcludedBikePart(path)) { skipped++; continue; }
                if (!IsCharacterMaterial(path)) { skipped++; continue; }

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                bool changed = false;
                foreach (var f in Fields)
                {
                    if (!mat.HasProperty(f)) continue;
                    float v = mat.GetFloat(f);
                    if (Mathf.Abs(v) < 1e-6f) continue;
                    mat.SetFloat(f, 0f);
                    changed = true;
                }
                if (changed)
                {
                    EditorUtility.SetDirty(mat);
                    touched.Add(path);
                }
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[fi-weather-zero] scanned={scanned} touched={touched.Count} skipped={skipped}");
        foreach (var p in touched) Debug.Log($"[fi-weather-zero]   fixed: {p}");

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
