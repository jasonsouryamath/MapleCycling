using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// SURGICAL, bike-frame-only livery fix for the player ("Kuro on Sakura Pass").
///
/// Why this exists separately from PlayerFidelityFix: the player's FRAME tubes ship on
/// Colnago_Racing_Red, and a later CelLit conversion (KuroCharacterCelLitFix) re-points the
/// player's frame renderer back at the SHARED Shared_Colnago_Racing_Red_CelLit asset, so the
/// frame renders bright red again while the reference sheet (Assets/Kuro/Reference.png) shows an
/// all-black matte KURO carbon frame.
///
/// This pass ONLY touches the renderers under the player's "Bike" child, and ONLY the material
/// slots that resolve to the red frame material. It clones the shared red material into a
/// per-player asset (Assets/Kuro/Materials/KuroBike_Frame_Black.mat) and recolours THAT to matte
/// black with the same low rim/spec finish as the player's carbon-black bike parts. It never
/// writes to the shared imported/CelLit material, so every NPC that legitimately rides a red
/// Colnago is unaffected, and it never touches the rider's body/helmet/hair/kit materials, so the
/// already-approved rider livery is not regressed. Idempotent: re-runs re-affirm the same clone.
public static class PlayerBikeFrameBlackFix
{
    const string ScenePath  = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string MatDir     = "Assets/Kuro/Materials";
    const string FrameAsset = "KuroBike_Frame_Black";

    // Carbon frame, matte. Same value PlayerFidelityFix uses (#151518).
    static readonly Color FrameBlack = new Color32(0x15, 0x15, 0x18, 0xFF);

    // Low, cool rim/spec so the black reads as FORM (not a flat void that vanishes into shadow)
    // WITHOUT the warm wet-plastic gloss the shader ships by default. Matched to the player's
    // carbon-black bike parts (Player_Shared_Colnago_Carbon_Black_CelLit: rim 0.16 / spec 0.06)
    // and the head materials (~0.16), per the fix brief. The sun this rim multiplies is warm, so
    // the tint itself is cool to cancel the khaki cast (the "brown helmet" clamp).
    const float FrameRimStrength  = 0.16f;
    const float FrameSpecStrength = 0.06f;
    static readonly Color FrameRimColor  = new Color(0.50f, 0.54f, 0.68f, 1f);
    static readonly Color FrameSpecTint  = new Color(0.82f, 0.86f, 1.00f, 1f);

    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    [MenuItem("MapleRide/Fix Player Bike Frame (matte black)")]
    public static void RunMenu() => Run();

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[framefix] no player object."); EditorApplication.Exit(1); return; }

        var bikeT = FindChild(player.transform, "Bike");
        if (bikeT == null) { Debug.LogError("[framefix] no 'Bike' child under player."); EditorApplication.Exit(1); return; }

        int touchedSlots = 0;
        foreach (var r in bikeT.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string bn = BaseName(mats[i]);
                // Reproduction evidence: log exactly what each frame-candidate slot resolves to now.
                bool isRed   = bn.Contains("Racing_Red");
                bool isFrame = bn.Contains("Frame_Black");
                if (isRed || isFrame)
                    Debug.Log($"[framefix] BEFORE '{r.gameObject.name}' slot{i} -> '{mats[i].name}' " +
                              $"shader='{mats[i].shader.name}' color={ReadColor(mats[i])}");

                if (!isRed && !isFrame) continue;

                var clone = CloneMat(mats[i], FrameAsset);
                SetColor(clone, FrameBlack);
                if (clone.HasProperty("_RimStrength"))  clone.SetFloat("_RimStrength", FrameRimStrength);
                if (clone.HasProperty("_SpecStrength")) clone.SetFloat("_SpecStrength", FrameSpecStrength);
                if (clone.HasProperty("_RimColor"))     clone.SetColor("_RimColor", FrameRimColor);
                if (clone.HasProperty("_SpecTint"))     clone.SetColor("_SpecTint", FrameSpecTint);
                if (clone.HasProperty("_Metallic"))     clone.SetFloat("_Metallic", 0f);
                if (clone.HasProperty("metallicFactor"))clone.SetFloat("metallicFactor", 0f);
                if (clone.HasProperty("_Smoothness"))   clone.SetFloat("_Smoothness", 0.12f);
                if (clone.HasProperty("_Glossiness"))   clone.SetFloat("_Glossiness", 0.12f);
                if (clone.HasProperty("roughnessFactor"))clone.SetFloat("roughnessFactor", 0.85f);
                EditorUtility.SetDirty(clone);
                mats[i] = clone;
                touched = true; touchedSlots++;
                Debug.Log($"[framefix] AFTER  '{r.gameObject.name}' slot{i} -> '{clone.name}' color={ReadColor(clone)}");
            }
            if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }

        if (touchedSlots == 0)
            Debug.LogWarning("[framefix] no red/frame material slots found under the player's Bike.");
        else
            Debug.Log($"[framefix] recoloured {touchedSlots} frame slot(s) -> matte black (per-player clone, shared red untouched).");

        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[framefix] done + saved scene.");
        EditorApplication.Exit(0);
    }

    // --- helpers (mirrors PlayerFidelityFix so the finish stays consistent) ----------------

    static string BaseName(Material m) => m.name.Replace(" (Instance)", "");

    static string ReadColor(Material m)
    {
        if (m.HasProperty("_Color")) return m.GetColor("_Color").ToString("F3");
        if (m.HasProperty("baseColorFactor")) return m.GetColor("baseColorFactor").ToString("F3");
        return "n/a";
    }

    static Material CloneMat(Material src, string assetName)
    {
        string path = $"{MatDir}/{assetName}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            if (src != existing)
            {
                existing.shader = src.shader;
                existing.CopyPropertiesFromMaterial(src);
                existing.name = assetName;
            }
            return existing;
        }
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Kuro", "Materials");
        var m = new Material(src);
        m.CopyPropertiesFromMaterial(src);
        m.name = assetName;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void SetColor(Material m, Color c)
    {
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, p == "baseColorFactor" ? c.linear : c);
    }

    static Transform FindChild(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindChild(c, name); if (r != null) return r; }
        return null;
    }
}
