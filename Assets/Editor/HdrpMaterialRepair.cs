using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Audit + repair for the "magenta world / blown-out white rider" regression.
///
/// ROOT CAUSE (measured, not guessed - see Audit()):
///  * The project runs HDRP, but SakuraPassEnvironment.LoadOrCreate re-assigned
///    <c>mat.shader = Shader.Find("MapleRide/SakuraCel|Terrain|Foliage|Water|Sky")</c> on every
///    region build. Those are Built-in render pipeline shaders with no HDRP pass, so HDRP drew
///    them with the error shader: 35,767 of the scene's renderers came back MAGENTA
///    (19,787 foliage + 15,818 cel + 161 terrain + 1 water) after the most recent rebuild,
///    while the road - the one family SakuraRoadPass had already migrated - rendered correctly.
///    The permanent half of that fix is in SakuraPassEnvironment/MapleRideShaderNames; this
///    pass repairs the materials the broken builds already wrote to disk and into the scene.
///  * Every player/NPC/GLB-prop material sat on "Shader Graphs/glTF-pbrMetallicRoughness",
///    which is lit by HDRP's real (lux) light loop. At this project's fixed EV100 = 0 exposure
///    that is calibrated orders of magnitude too bright, so the rider, the bike and the GLB
///    signage rendered blown out to flat white/black. KuroCharacterCelLitFix established the
///    proven fix (route them to MapleRide/HDRP/CelLit, which self-compensates for exposure);
///    the player asset switch to KuroNPC_KuroAnime_Rigged.glb re-introduced raw glTF materials
///    that the earlier, player-object-scoped pass no longer covered.
///
/// The MapleRide/HDRP/* ports declare a strict SUPERSET of their Built-in originals' property
/// names, so the conversion is a plain shader swap that preserves every authored value.
///
/// Idempotent: materials already on the correct shader are skipped, shared GLB sub-assets are
/// cloned under a fixed name and reused, and no user setting is reset.
/// </summary>
public static class HdrpMaterialRepair
{
    const string ScenePath  = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    static readonly List<string> Report = new List<string>();
    static void R(string s) { Report.Add(s); Debug.Log(s); }

    static void Flush(string name)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../" + name));
        File.WriteAllLines(path, Report);
        Debug.Log("[hdrp] report -> " + path);
    }

    // ------------------------------------------------------------------ audit

    [MenuItem("MapleRide/Environment/Audit HDRP Materials", priority = 30)]
    public static void Audit()
    {
        Report.Clear();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Census("AUDIT");
        Flush("zz_hdrp_audit_report.txt");
    }

    static void Census(string label)
    {
        R($"[{label}] currentRenderPipeline={(GraphicsSettings.currentRenderPipeline == null ? "<NULL - Built-in>" : GraphicsSettings.currentRenderPipeline.GetType().Name)}");

        var census = new Dictionary<string, int>(StringComparer.Ordinal);
        var samples = new Dictionary<string, string>(StringComparer.Ordinal);
        var scene = EditorSceneManager.GetActiveScene();
        int nullMat = 0, bad = 0;

        foreach (var root in scene.GetRootGameObjects())
        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
        foreach (var m in rend.sharedMaterials)
        {
            if (m == null) { nullMat++; continue; }
            string key = m.shader == null ? "<null shader>" : m.shader.name;
            census.TryGetValue(key, out int c);
            census[key] = c + 1;
            if (!samples.ContainsKey(key)) samples[key] = $"{rend.name} / {m.name}";
            if (IsPipelineIncompatible(key)) bad++;
        }

        foreach (var kv in census.OrderByDescending(k => k.Value))
            R($"[{label}] {(IsPipelineIncompatible(kv.Key) ? "BAD " : "ok  ")}{kv.Value,6} x '{kv.Key}'   e.g. {samples[kv.Key]}");
        R($"[{label}] null materials: {nullMat}");
        R($"[{label}] TOTAL pipeline-incompatible renderer/material slots: {bad}");

        var player = GameObject.Find(PlayerName);
        if (player == null) R($"[{label}] PLAYER not found!");
        else
        {
            var shaders = player.GetComponentsInChildren<Renderer>(true)
                                .SelectMany(x => x.sharedMaterials)
                                .Where(m => m != null && m.shader != null)
                                .GroupBy(m => m.shader.name)
                                .OrderByDescending(g => g.Count());
            R($"[{label}] PLAYER '{player.name}' renderers={player.GetComponentsInChildren<Renderer>(true).Length}");
            foreach (var g in shaders) R($"[{label}] PLAYER {g.Count(),5} x '{g.Key}'  e.g. {g.First().name}");
        }
    }

    /// <summary>
    /// A shader is incompatible when it is neither an HDRP shader nor one of this project's
    /// HDRP ports - i.e. it has no HDRP pass and will draw magenta - or when it is the raw
    /// glTF shader, which draws blown out at this project's fixed exposure.
    /// </summary>
    static bool IsPipelineIncompatible(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName)) return true;
        if (shaderName == KuroCharacterCelLitFix.OldGltfShader) return true;
        if (shaderName.StartsWith("MapleRide/HDRP/", StringComparison.Ordinal)) return false;
        if (shaderName.StartsWith("HDRP/", StringComparison.Ordinal)) return false;
        return true;
    }

    // ----------------------------------------------------------------- repair

    [MenuItem("MapleRide/Environment/Repair HDRP Materials", priority = 31)]
    public static void Repair()
    {
        Report.Clear();
        var celLit = Shader.Find(KuroCharacterCelLitFix.CelLitName);
        if (celLit == null || ShaderUtil.ShaderHasError(celLit))
        {
            R($"ABORT: '{KuroCharacterCelLitFix.CelLitName}' missing or has compile errors - refusing to convert.");
            Flush("zz_hdrp_repair_report.txt");
            return;
        }

        if (!AssetDatabase.IsValidFolder(KuroCharacterCelLitFix.SharedMatDir))
            AssetDatabase.CreateFolder("Assets/Kuro", "Materials");

        int assetSwaps = RepairMaterialAssets();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Census("BEFORE");

        int sceneSwaps = 0, gltfInPlace = 0, gltfCloned = 0, remapped = 0;
        var clones = new Dictionary<Material, Material>();
        var handled = new HashSet<Material>();

        foreach (var root in scene.GetRootGameObjects())
        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = rend.sharedMaterials;
            bool touched = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null) continue;
                string shaderName = m.shader.name;

                if (shaderName == KuroCharacterCelLitFix.OldGltfShader)
                {
                    string assetPath = AssetDatabase.GetAssetPath(m);
                    bool ownedMat = string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
                    Material target;
                    if (ownedMat)
                    {
                        // Scene-embedded instances and real .mat assets are owned by this scene
                        // or this project and are safe to convert in place.
                        if (handled.Add(m)) { KuroCharacterCelLitFix.ConvertToCelLit(m, celLit); gltfInPlace++; }
                        target = m;
                    }
                    else
                    {
                        // Embedded sub-asset of an imported .glb: SHARED by every rider and
                        // reverted by the next reimport, so it must be cloned once and reused.
                        if (!clones.TryGetValue(m, out target))
                        {
                            target = KuroCharacterCelLitFix.CloneSharedToCelLit(m, celLit);
                            clones[m] = target;
                            gltfCloned++;
                        }
                    }
                    if (mats[i] != target) { mats[i] = target; touched = true; }
                    continue;
                }

                if (!handled.Contains(m) && ConvertPipelineShader(m)) { handled.Add(m); sceneSwaps++; if (m.shader.name == "HDRP/Unlit") remapped++; }
            }

            if (touched) { rend.sharedMaterials = mats; EditorUtility.SetDirty(rend); }
        }

        // RenderSettings.skybox is ignored by HDRP (the sky comes from the Gradient Sky volume
        // override), but a Built-in shader left on it still shows up in any legacy code path, so
        // it is converted for consistency rather than left as a landmine.
        if (RenderSettings.skybox != null && ConvertPipelineShader(RenderSettings.skybox))
            R($"skybox material '{RenderSettings.skybox.name}' -> {RenderSettings.skybox.shader.name}");

        R($"REPAIR asset materials swapped: {assetSwaps}");
        R($"REPAIR scene materials swapped to HDRP ports: {sceneSwaps} (of which unlit remaps: {remapped})");
        R($"REPAIR glTF materials converted to CelLit in place: {gltfInPlace}");
        R($"REPAIR shared GLB materials cloned to CelLit: {gltfCloned}");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Census("AFTER");
        Flush("zz_hdrp_repair_report.txt");
    }

    /// <summary>Every .mat asset in the project, so a rebuild does not re-import a broken one.</summary>
    static int RepairMaterialAssets()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (ConvertPipelineShader(mat)) { R($"asset {path}: -> {mat.shader.name}"); n++; }
        }
        return n;
    }

    /// <summary>
    /// Swaps a Built-in shader for the HDRP port the project already ships. Returns true when a
    /// swap happened. The MapleRide ports share the originals' property names exactly, so no
    /// value marshalling is needed; the stock unlit shaders do not, so albedo colour/texture are
    /// carried across explicitly and the HDRP surface state is set up.
    /// </summary>
    static bool ConvertPipelineShader(Material mat)
    {
        string current = mat.shader.name;
        string resolved = MapleRideShaderNames.Resolve(current);
        if (resolved == current) return false;

        var target = Shader.Find(resolved);
        if (target == null || ShaderUtil.ShaderHasError(target))
        {
            R($"LEFT AS-IS '{mat.name}': replacement '{resolved}' missing or broken.");
            return false;
        }

        Color color = Color.white;
        foreach (var p in new[] { "_Color", "_UnlitColor", "_BaseColor" })
            if (mat.HasProperty(p)) { color = mat.GetColor(p); break; }
        Texture tex = null;
        foreach (var p in new[] { "_MainTex", "_UnlitColorMap", "_BaseColorMap" })
            if (mat.HasProperty(p)) { var t = mat.GetTexture(p); if (t != null) { tex = t; break; } }
        bool wasInstanced = mat.enableInstancing;

        mat.shader = target;
        MapleRideShaderNames.SetBaseColor(mat, color);
        MapleRideShaderNames.SetBaseTexture(mat, tex);
        mat.enableInstancing = wasInstanced;

        if (resolved == "HDRP/Unlit")
        {
            // These are the tunnel sodium luminaires and the petal quads: emissive/transparent
            // surfaces that were deliberately unlit under Built-in and must stay unlit.
            if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", color);
            if (mat.HasProperty("_UseEmissiveIntensity")) mat.SetFloat("_UseEmissiveIntensity", 0f);
        }

        EditorUtility.SetDirty(mat);
        return true;
    }
}
