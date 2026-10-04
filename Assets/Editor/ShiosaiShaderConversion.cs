using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Spec section 4.4 shader/material conversion for Shiosai Coast.
///
/// Every MapleRide surface material is a .mat ASSET (see ShiosaiCoastEnvironment.LoadOrCreate),
/// so conversion never needs to open or write the scene - which matters, because this project
/// already carries a routed defect where a capture tool dirties the 200 MB coast scene.
///
/// The Built-in shaders are deliberately left on disk and unreferenced rather than deleted
/// (spec 4.1): if an HDRP replacement fails its visual test, reverting is a re-run of this pass
/// with the map inverted, not a restore from the checkpoint.
/// </summary>
public static class ShiosaiShaderConversion
{
    // Built-in shader name -> HDRP replacement. Only entries whose HDRP shader actually
    // exists AND compiles are applied; a missing or broken replacement is reported and the
    // material is left on its Built-in shader, because a half-converted material that
    // silently loses its properties is worse than a catalogued magenta one.
    private static readonly (string from, string to)[] Map =
    {
        ("MapleRide/SakuraCel",           "MapleRide/HDRP/CelLit"),
        // The road was the one gap in this table. With no entry, SakuraPass_Asphalt fell through
        // to the generic CelLit conversion and lost the ENTIRE section-26 layer stack (macro
        // variation, wheel tracks, shoulder, verge), which is what turned the carriageway into a
        // flat purple-grey strip. MapleRide/HDRP/Road is the real replacement.
        ("MapleRide/SakuraRoad",          "MapleRide/HDRP/Road"),
        ("MapleRide/SakuraTerrain",       "MapleRide/HDRP/Terrain"),
        ("MapleRide/SakuraWater",         "MapleRide/HDRP/Ocean"),
        ("MapleRide/ShiosaiShallows",     "MapleRide/HDRP/Shallows"),
        ("MapleRide/SakuraFoliage",       "MapleRide/HDRP/Foliage"),
        ("MapleRide/SakuraSky",           "MapleRide/HDRP/Sky"),
        // Maple City was the last Built-in region left after the coast migration. Its facades
        // get their OWN HDRP target, not CelLit: the city is one combined mesh whose
        // per-building tint lives in vertex colour, which CelLit does not read, and its flat
        // walls need continuous light rather than a cel ramp. See MapleRideCityFacade.shader.
        ("MapleRide/MapleCityFacade",     "MapleRide/HDRP/CityFacade"),
        ("Unlit/Color",                   "HDRP/Unlit"),
        ("Particles/Standard Unlit",      "HDRP/Unlit"),
    };

    private const string GltfShader = "glTF/PbrMetallicRoughness";
    private const string ArchitectureShader = "MapleRide/ShiosaiArchitecture";
    private const string HdrpLit = "HDRP/Lit";

    [MenuItem("MapleRide/Environment/Convert Shiosai Materials To HDRP")]
    public static void Run()
    {
        ExtractModelMaterials();
        ConvertMaterials();
        ConvertSceneMaterials();
    }

    /// <summary>
    /// Converts one material in place. Returns true if it changed.
    ///
    /// Shared by the asset pass and the scene pass so a material gets identical treatment
    /// wherever it happens to live - the two passes disagreeing is exactly how the village
    /// ended up half-converted.
    /// </summary>
    private static bool ConvertOne(Material mat, Dictionary<string, Shader> replacements, Shader litShader, List<string> log)
    {
        if (mat == null || mat.shader == null) return false;
        string current = mat.shader.name;

        if (replacements.TryGetValue(current, out var target))
        {
            // HDRP/Unlit does not share the Built-in unlit property NAMES: colour lives in
            // _UnlitColor and the albedo map in _UnlitColorMap. A bare shader swap therefore
            // silently drops both and repaints the surface default white - which is how Maple
            // City's warm window glow, authored at an over-bright (1.62, 1.28, 0.74), would have
            // come back as flat white lamps. Carry the authored values across explicitly.
            if (target.name == "HDRP/Unlit")
            {
                var srcColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                var srcTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                var srcScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
                var srcOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
                bool wasTransparent = mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent
                                      || current == "Particles/Standard Unlit";

                mat.shader = target;
                mat.SetColor("_UnlitColor", srcColor);
                if (srcTex != null)
                {
                    mat.SetTexture("_UnlitColorMap", srcTex);
                    mat.SetTextureScale("_UnlitColorMap", srcScale);
                    mat.SetTextureOffset("_UnlitColorMap", srcOffset);
                }

                // The leaf-drift particle sheet is an alpha cutout card; left opaque it draws
                // as a grid of solid black quads. HDRP expresses transparency through
                // _SurfaceType plus the matching blend/queue state, so all of it is set here.
                if (wasTransparent)
                {
                    mat.SetFloat("_SurfaceType", 1f);                 // Transparent
                    mat.SetFloat("_BlendMode", 0f);                   // Alpha
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.SetFloat("_AlphaCutoffEnable", 1f);
                    mat.SetFloat("_AlphaCutoff", 0.35f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }

                EditorUtility.SetDirty(mat);
                log.Add($"[sc-convert] '{mat.name}' {current} -> HDRP/Unlit " +
                        $"(colour {srcColor}, map={(srcTex != null ? srcTex.name : "none")}, transparent={wasTransparent})");
                return true;
            }

            mat.shader = target;
            EditorUtility.SetDirty(mat);
            return true;
        }

        if (current == GltfShader && litShader != null)
        {
            ConvertGltfToLit(mat, litShader, log);
            return true;
        }

        if (current == ArchitectureShader && litShader != null)
        {
            ConvertArchitectureToLit(mat, litShader);
            return true;
        }

        // Recovery path for architecture materials that an earlier run of this pass had
        // already routed to HDRP/Lit. Those surfaces render black or blown-out here because
        // the scene's lights were authored for Built-in at intensity ~1 while HDRP/Lit
        // expects physical lux, and HDRP's static-sky ambient is off. Re-routing them to the
        // binder-driven CelLit puts the buildings back in the same light as the road.
        if (current == HdrpLit && litShader != null && mat.name.StartsWith("Shiosai_", StringComparison.Ordinal))
        {
            var tint = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
            var albedo = mat.HasProperty("_BaseColorMap") ? mat.GetTexture("_BaseColorMap") : null;
            float smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.2f;

            mat.shader = litShader;
            mat.SetColor("_Color", tint);
            if (albedo != null) mat.SetTexture("_MainTex", albedo);
            mat.SetFloat("_Gloss", Mathf.Clamp(smooth, 0.01f, 1f));
            EditorUtility.SetDirty(mat);
            log.Add($"[sc-convert] '{mat.name}' HDRP/Lit -> CelLit (baseline-parity lighting)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// The other half of the gap that made the first conversion lie.
    ///
    /// The region builders create materials with `new Material(...)` at build time and the
    /// result is serialised INTO the scene file, not into an .mat asset. Those materials are
    /// invisible to AssetDatabase.FindAssets("t:Material") just as model sub-assets are, which
    /// is why the harbour village, the lake village and the lane markings were still on the
    /// Built-in glTF shader - rendering blown-out white - after a pass that reported success.
    ///
    /// This walks every renderer in the scene and converts what the asset pass could not see.
    /// It deliberately saves the scene: unlike a diagnostic capture (which carries a routed
    /// defect for writing the scene as a side effect), persisting this conversion IS the point.
    /// </summary>
    private static void ConvertSceneMaterials()
    {
        const string scenePath = "Assets/Scenes/SakuraPass.unity";
        var log = new List<string>();

        var replacements = BuildReplacements(log);
        var litShader = Shader.Find("MapleRide/HDRP/CelLit");

        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        var seen = new HashSet<Material>();
        int converted = 0;
        var byShader = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || !seen.Add(mat)) continue;
                    // Assets are already handled by the pass above; only scene-embedded
                    // materials (no asset path) need converting here.
                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat))) continue;

                    string before = mat.shader != null ? mat.shader.name : "<null>";
                    if (ConvertOne(mat, replacements, litShader, log))
                    {
                        converted++;
                        byShader.TryGetValue(before, out int c);
                        byShader[before] = c + 1;
                    }
                }
            }
        }

        if (converted > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }

        foreach (var l in log) Debug.Log(l);
        foreach (var kv in byShader) Debug.Log($"[sc-convert] scene: {kv.Value} material(s) left '{kv.Key}'");
        Debug.Log($"[sc-convert] SCENE SUMMARY converted={converted} scene-embedded material(s)");
    }

    private static Dictionary<string, Shader> BuildReplacements(List<string> log)
    {
        var replacements = new Dictionary<string, Shader>(StringComparer.Ordinal);
        foreach (var (from, to) in Map)
        {
            var sh = Shader.Find(to);
            if (sh == null) { log.Add($"[sc-convert] MISSING replacement '{to}' for '{from}' - materials left as-is"); continue; }
            if (ShaderUtil.ShaderHasError(sh)) { log.Add($"[sc-convert] BROKEN replacement '{to}' (compile errors) - materials left as-is"); continue; }
            replacements[from] = sh;
        }
        return replacements;
    }

    /// <summary>
    /// Materials that live INSIDE an imported model (.glb/.gltf/.fbx) are sub-assets owned by
    /// the importer. AssetDatabase.FindAssets("t:Material") never returns them, and assigning a
    /// shader to one would be discarded on the next reimport - which is exactly why the first
    /// conversion pass reported success while every road marking, house and prop was still on
    /// the Built-in glTF shader and rendering wrong.
    ///
    /// Extracting them turns each into a real, editable .mat asset that the importer then
    /// remaps to, so the conversion below can reach them and the result survives reimport.
    /// This is a one-way but fully reversible-by-checkpoint operation; it is idempotent because
    /// an already-extracted model has no material sub-assets left to extract.
    /// </summary>
    private static void ExtractModelMaterials()
    {
        const string dir = "Assets/Materials/Extracted";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        var modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
        var modelPaths = new List<string>(modelGuids.Select(AssetDatabase.GUIDToAssetPath));

        // FindAssets("t:Model") only matches assets handled by Unity's ModelImporter. This
        // project's riders, bikes, props and road models are .glb brought in by a glTF
        // ScriptedImporter, so the filter returned nothing and the pass reported "0 models"
        // while 125 materials sat broken inside them. Scanning by extension is what actually
        // finds them.
        string assetsRoot = Application.dataPath;
        foreach (var ext in new[] { "*.glb", "*.gltf", "*.fbx" })
        {
            foreach (var full in System.IO.Directory.GetFiles(assetsRoot, ext, System.IO.SearchOption.AllDirectories))
            {
                string rel = "Assets" + full.Substring(assetsRoot.Length).Replace('\\', '/');
                if (!modelPaths.Contains(rel)) modelPaths.Add(rel);
            }
        }

        int extracted = 0, models = 0;
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (string modelPath in modelPaths)
        {
            var reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath);
            bool touched = false;

            foreach (var rep in reps)
            {
                if (!(rep is Material m)) continue;
                if (m.shader == null) continue;
                if (m.shader.name != GltfShader && !Map.Any(x => x.from == m.shader.name) &&
                    m.shader.name != ArchitectureShader) continue;

                // Names collide across models (every road model has a "LineWhite"); suffix on
                // collision so one model's material can never silently overwrite another's.
                string baseName = string.IsNullOrEmpty(m.name) ? "Material" : m.name;
                string target = $"{dir}/{baseName}.mat";
                int n = 1;
                while (used.Contains(target) || AssetDatabase.LoadAssetAtPath<Material>(target) != null)
                    target = $"{dir}/{baseName}_{n++}.mat";
                used.Add(target);

                string err = AssetDatabase.ExtractAsset(m, target);
                if (string.IsNullOrEmpty(err))
                {
                    extracted++; touched = true;
                }
                else
                {
                    // ExtractAsset is only implemented by some importers; the glTF
                    // ScriptedImporter is not one of them. The general mechanism that does
                    // work for every importer is an explicit remap: author a real .mat asset
                    // and tell the importer to use it in place of the embedded one. This is
                    // what makes the shader swap survive a reimport.
                    var copy = new Material(m);
                    copy.name = System.IO.Path.GetFileNameWithoutExtension(target);
                    AssetDatabase.CreateAsset(copy, target);

                    var importer = AssetImporter.GetAtPath(modelPath);
                    if (importer != null)
                    {
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(m), copy);
                        extracted++; touched = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[sc-convert] no importer for {modelPath}; '{m.name}' left embedded");
                    }
                }
            }

            if (touched)
            {
                models++;
                AssetDatabase.WriteImportSettingsIfDirty(modelPath);
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
            }
        }

        if (extracted > 0)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }
        Debug.Log($"[sc-convert] extracted {extracted} embedded material(s) from {models} model(s) -> {dir}");
    }

    private static void ConvertMaterials()
    {
        var log = new List<string>();
        int converted = 0, skipped = 0, gltf = 0, arch = 0;

        var replacements = BuildReplacements(log);

        var litShader = Shader.Find("MapleRide/HDRP/CelLit");
        if (litShader == null) log.Add("[sc-convert] MapleRide/HDRP/CelLit not found - imported materials left as-is");

        var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            string current = mat.shader.name;
            bool wasBuiltin = replacements.ContainsKey(current);
            bool wasGltf = current == GltfShader;
            bool wasArch = current == ArchitectureShader;

            // Routed through the SAME ConvertOne the scene pass uses. Keeping a second,
            // inline copy of these branches here is what let the HDRP/Lit -> CelLit recovery
            // silently never run on asset materials, leaving the harbour houses white.
            if (ConvertOne(mat, replacements, litShader, log))
            {
                if (wasGltf) gltf++;
                else if (wasArch) arch++;
                else converted++;
                continue;
            }

            if (Map.Any(m => m.from == current)) skipped++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        foreach (var l in log) Debug.Log(l);
        Debug.Log($"[sc-convert] SUMMARY converted={converted} gltf_remapped={gltf} architecture_remapped={arch} " +
                  $"left_on_builtin={skipped} (replacements available: {replacements.Count}/{Map.Length})");
    }

    /// <summary>
    /// MapleRide/ShiosaiArchitecture -> HDRP/Lit.
    ///
    /// The spec's 4.4 matrix names a MapleRide/HDRP/ShiosaiArchitecture replacement, but that
    /// shader was already a plain Standard PBR surface shader whose stated design intent is
    /// "continuous light response and texture contrast, NOT stepped bands" - the exact thing
    /// HDRP/Lit does natively, and better, with real HDRP shadows, SSAO and reflections that a
    /// hand-written pass cannot yet receive. Wrapping it in a custom shader would have cost the
    /// buildings their shadow response to gain nothing but a matching asset name. Recorded as a
    /// deliberate deviation in SC_MIGRATION_AUDIT.md.
    /// </summary>
    private static void ConvertArchitectureToLit(Material mat, Shader lit)
    {
        Color tint = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        Texture albedo = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
        float bumpStrength = mat.HasProperty("_BumpStrength") ? mat.GetFloat("_BumpStrength") : 0.65f;
        float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
        float smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.35f;

        mat.shader = lit;

        // Same reasoning as the imported-material path: CelLit, not HDRP/Lit. The spec's 4.4
        // acceptance line for architecture asks for "cel-controlled shadow response", and
        // routing the buildings through the same binder-driven lighting as the rest of the kit
        // is what keeps the town in the same light as the road it stands beside.
        mat.SetColor("_Color", tint);
        if (albedo != null) mat.SetTexture("_MainTex", albedo);
        mat.SetFloat("_Gloss", Mathf.Clamp(smoothness, 0.01f, 1f));
        mat.SetFloat("_SpecStrength", Mathf.Lerp(0.05f, 0.35f, Mathf.Clamp01(metallic)));
        // Buildings are hard-surface props: the weathering channel is what section 30 asks for
        // and it costs nothing when left at the shader default, so it stays off here rather
        // than being switched on speculatively during a pipeline migration.

        EditorUtility.SetDirty(mat);
    }

    /// <summary>
    /// glTF/PbrMetallicRoughness -> HDRP/Lit. This shader is NOT in the spec's 4.4 matrix but
    /// backs 83 materials - every imported rider, bicycle and prop - so it is the single
    /// largest magenta group. Values must be read BEFORE the shader swap: assigning .shader
    /// drops any property the new shader does not declare, and glTF's names do not match
    /// HDRP's, so a naive swap would silently reset every character to white plastic.
    /// </summary>
    private static void ConvertGltfToLit(Material mat, Shader lit, List<string> log)
    {
        Color baseColor = Color.white;
        Texture baseMap = null, normalMap = null, emissiveMap = null, occlusionMap = null;
        float metallic = 1f, roughness = 1f;
        Color emissive = Color.black;

        // The importer has shipped both camelCase glTF names and underscore-prefixed Unity
        // names across versions; probe both rather than assuming one.
        foreach (var n in new[] { "baseColorFactor", "_BaseColorFactor", "_Color", "_BaseColor" })
            if (mat.HasProperty(n)) { baseColor = mat.GetColor(n); break; }
        foreach (var n in new[] { "baseColorTexture", "_BaseColorTexture", "_MainTex", "_BaseColorMap" })
            if (mat.HasProperty(n)) { baseMap = mat.GetTexture(n); break; }
        foreach (var n in new[] { "metallicFactor", "_MetallicFactor", "_Metallic" })
            if (mat.HasProperty(n)) { metallic = mat.GetFloat(n); break; }
        foreach (var n in new[] { "roughnessFactor", "_RoughnessFactor", "_Roughness" })
            if (mat.HasProperty(n)) { roughness = mat.GetFloat(n); break; }
        foreach (var n in new[] { "normalTexture", "_NormalTexture", "_BumpMap", "_NormalMap" })
            if (mat.HasProperty(n)) { normalMap = mat.GetTexture(n); break; }
        foreach (var n in new[] { "emissiveFactor", "_EmissiveFactor", "_EmissionColor" })
            if (mat.HasProperty(n)) { emissive = mat.GetColor(n); break; }
        foreach (var n in new[] { "emissiveTexture", "_EmissiveTexture", "_EmissionMap" })
            if (mat.HasProperty(n)) { emissiveMap = mat.GetTexture(n); break; }
        foreach (var n in new[] { "occlusionTexture", "_OcclusionTexture", "_OcclusionMap" })
            if (mat.HasProperty(n)) { occlusionMap = mat.GetTexture(n); break; }

        mat.shader = lit;

        // Target is MapleRide/HDRP/CelLit, not HDRP/Lit. Two reasons, both load-bearing:
        // (1) the game's art direction is cel-shaded chibi, and these materials back the riders,
        //     bikes and props that most need to read that way;
        // (2) HDRP/Lit is lit by HDRP's own light loop in PHYSICAL units, and this project's
        //     lights were authored for Built-in at intensity ~1-3, which in lux is essentially
        //     darkness - converted HDRP/Lit surfaces came back black. CelLit is lit by the
        //     MapleRideSunBinder globals, so it is immune to that unit mismatch and needs no
        //     scene edits to the existing lights.
        mat.SetColor("_Color", baseColor);
        if (baseMap != null) mat.SetTexture("_MainTex", baseMap);
        // Roughness maps onto the cel gloss control inverted: glTF stores roughness, the cel
        // shader stores gloss, so a matte surface must end up with LOW gloss, not high.
        mat.SetFloat("_Gloss", Mathf.Clamp(1f - Mathf.Clamp01(roughness), 0.01f, 1f));
        mat.SetFloat("_SpecStrength", Mathf.Lerp(0.05f, 0.45f, Mathf.Clamp01(metallic)));
        if (emissive.maxColorComponent > 0.001f)
        {
            // CelLit has no emissive channel; lifting the base colour keeps a lantern or sign
            // reading as self-lit rather than dropping the effect silently.
            mat.SetColor("_Color", baseColor + emissive * 0.5f);
        }

        EditorUtility.SetDirty(mat);
    }

}
