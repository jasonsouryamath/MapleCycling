using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// PEDESTRIAN MODEL-SWAP PIPELINE, editor side (claude-peds, 2026-09-26). Guide:
/// docs/PEDESTRIAN_MODEL_IMPORT.md.
///
///  * <see cref="PedestrianModelPostprocessor"/>: every model dropped in
///    Assets/Characters/Pedestrians/Import/ imports as Humanoid (avatar from the model), readable
///    (sole calibration + prop fitting read the skin), file scale, no cameras/lights. First import only,
///    so later hand changes in the Inspector stick.
///  * <see cref="PedestrianModelLibraryBuilder.Build"/>: scans that folder (FBX / GLB / VRM), groups
///    Mixamo-style "Name@Clip" files per character, measures height, detects the rig, makes cel
///    materials and writes Assets/Resources/Pedestrians/PedestrianModelLibrary.asset.
///      run_steps.ps1 "PedestrianModelLibraryBuilder.Build|claude_peds_lib.log|1"
///  * <see cref="PedestrianModelCapture"/>: play-mode proof, frames to reference/good_graphics/pedestrians_v2/.
///      run_steps.ps1 "PedestrianModelCapture.Run|claude_peds_on.log|0"     (model set ON)
///      run_steps.ps1 "PedestrianModelCapture.RunOff|claude_peds_off.log|0" (flag OFF, donor look)
/// </summary>
public sealed class PedestrianModelPostprocessor : AssetPostprocessor
{
    public const string Folder = "Assets/Characters/Pedestrians/Import/";

    public override uint GetVersion() => 1;

    static bool InFolder(string path) => path.Replace('\\', '/').StartsWith(Folder, StringComparison.OrdinalIgnoreCase);

    void OnPreprocessModel()
    {
        if (!InFolder(assetPath)) return;
        var mi = assetImporter as ModelImporter;
        if (mi == null || !mi.importSettingsMissing) return;   // first import only
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.isReadable = true;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importBlendShapes = true;
        mi.optimizeGameObjects = false;
        mi.importAnimation = true;
        if (PedestrianModelLibraryBuilder.IsHairFile(assetPath))
        {
            // hair attachments: plain meshes parented to the Head bone at run time, no avatar
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
        }
        else
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }
        Debug.Log($"[peds-import] {assetPath}: Humanoid, readable, file scale");
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!InFolder(assetPath)) return;
        var anim = root.GetComponentInChildren<Animator>(true);
        var av = anim != null ? anim.avatar : null;
        int bones = root.GetComponentsInChildren<Transform>(true).Length;
        Debug.Log($"[peds-import] {assetPath}: {bones} transforms, avatar {(av == null ? "none" : av.isValid && av.isHuman ? "HUMANOID valid" : "INVALID (name aliases will be used)")}");
    }
}

public static class PedestrianModelLibraryBuilder
{
    const string LibPath = "Assets/Resources/Pedestrians/PedestrianModelLibrary.asset";
    const string MatDir = "Assets/Characters/Pedestrians/Materials";
    static readonly string[] Exts = { ".fbx", ".glb", ".gltf", ".vrm", ".obj", ".blend" };

    [MenuItem("MapleRide/Pedestrians/Rebuild Pedestrian Model Library", priority = 40)]
    public static void Build()
    {
        AssetDatabase.Refresh();
        Directory.CreateDirectory(MatDir);
        var lib = AssetDatabase.LoadAssetAtPath<PedestrianModelLibrary>(LibPath);
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<PedestrianModelLibrary>();
            AssetDatabase.CreateAsset(lib, LibPath);
        }
        var files = Directory.GetFiles(PedestrianModelPostprocessor.Folder.TrimEnd('/'))
            .Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant()) && !IsHairFile(f))
            .Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToList();
        var groups = files.GroupBy(f => { var n = Path.GetFileNameWithoutExtension(f); int at = n.IndexOf('@'); return at > 0 ? n.Substring(0, at) : n; });
        var template = FindTemplate();
        var seen = new HashSet<string>();
        foreach (var g in groups)
        {
            string id = Sanitize(g.Key);
            seen.Add(id);
            // the mesh carrier: plain file, else @Idle, else the first
            string main = g.FirstOrDefault(f => !Path.GetFileNameWithoutExtension(f).Contains("@"))
                       ?? g.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).EndsWith("@Idle", StringComparison.OrdinalIgnoreCase))
                       ?? g.First();
            // embedded media (e.g. Kaito's jpg inside the FBX): extract once so the cel material can use it
            if (AssetImporter.GetAtPath(main) is ModelImporter mImp &&
                AssetDatabase.LoadAllAssetsAtPath(main).OfType<Material>().Any(m => SourceTexture(m) == null))
            {
                string texDir = $"{Path.GetDirectoryName(main).Replace('\\', '/')}/Textures";
                Directory.CreateDirectory(texDir);
                if (mImp.ExtractTextures(texDir))
                {
                    AssetDatabase.Refresh();
                    AssetDatabase.ImportAsset(main, ImportAssetOptions.ForceUpdate);
                    Debug.Log($"[peds-lib] {main}: extracted embedded textures to {texDir}");
                }
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(main);
            if (prefab == null || prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
            {
                Debug.LogWarning($"[peds-lib] {main}: no skinned mesh (not importable as a pedestrian) - skipped");
                continue;
            }
            var e = lib.models.FirstOrDefault(m => m.id == id);
            bool fresh = e == null;
            if (fresh) { e = new PedestrianModelLibrary.Entry { id = id }; lib.models.Add(e); }
            e.prefab = prefab;
            e.sourcePath = main;
            var names = prefab.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
            e.rig = names.Any(n => n.StartsWith("mixamorig", StringComparison.OrdinalIgnoreCase)) ? PedestrianModelLibrary.Rig.Mixamo
                  : names.Any(n => n.StartsWith("J_Bip_", StringComparison.Ordinal)) ? PedestrianModelLibrary.Rig.VRoid
                  : names.Contains("Hips") && names.Contains("LeftUpLeg") ? PedestrianModelLibrary.Rig.Plain
                  : PedestrianModelLibrary.Rig.Other;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(main).OfType<Avatar>().FirstOrDefault();
            e.humanoid = avatar != null && avatar.isValid && avatar.isHuman;
            e.nativeHeight = MeasureHeight(prefab, out float footY);
            // clips (kept for reference / a future Animator path)
            foreach (var f in g)
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(f).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    string lower = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (lower.Contains("@idle")) e.idleClip = clip;
                    else if (lower.Contains("@walk") || (lower.Contains("@run") && e.walkClip == null)) e.walkClip = clip;
                }
            // bone check with the runtime alias map
            var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
            var map = PedestrianBoneMap.Resolve(inst.transform, out string src);
            var missing = PedestrianBoneMap.MissingForWalk(map);
            UnityEngine.Object.DestroyImmediate(inst);
            // cel materials: one per source slot, created once (hand edits survive a rebuild)
            e.materials = MakeCelMaterials(id, prefab, template);
            if (e.variants == null || e.variants.Length == 0) e.variants = MakeTintVariants(id, e.materials);
            if (fresh)
            {
                e.tags = GuessTags(id);
                // Kaito / X Bot are PROOF stand-ins; real pedestrians replace them
                e.notes = "auto-registered " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            }
            Debug.Log($"[peds-lib] {id}: rig {e.rig}, humanoid {e.humanoid}, bones via {src}, missing [{string.Join(",", missing)}], " +
                      $"native height {e.nativeHeight:0.000} m (feet at {footY:0.000}), {e.materials.Length} cel materials, " +
                      $"idle '{(e.idleClip ? e.idleClip.name : "-")}' walk/run '{(e.walkClip ? e.walkClip.name : "-")}', enabled {e.enabled}");
        }
        BuildKuroRiderSet(lib);
        foreach (var e in lib.models.Where(m => !seen.Contains(m.id)))
            Debug.LogWarning($"[peds-lib] {e.id}: its file is gone from the Import folder (entry kept; prefab {(e.prefab ? "ok" : "MISSING")})");
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log($"[peds-lib] RESULT library {LibPath}: {lib.models.Count} models ({string.Join(", ", lib.models.Select(m => m.id + (m.enabled ? "" : " (off)")))}); enabledGlobally {lib.enabledGlobally}, regions [{string.Join(",", lib.regions)}]");
    }

    public static bool IsHairFile(string path)
    {
        var n = Path.GetFileName(path);
        return n.StartsWith("PedHair_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Hair_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Kuro-rider pedestrian parts dropped by the model generator: PedKit_*.png street kits,
    /// PedHair_*/Hair_* meshes, and an optional manifest *.json (anywhere under the Import folder).</summary>
    static void BuildKuroRiderSet(PedestrianModelLibrary lib)
    {
        if (lib.kuroRider == null) lib.kuroRider = new PedestrianModelLibrary.KuroRiderSet();
        string root = PedestrianModelPostprocessor.Folder.TrimEnd('/');
        var all = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => f.Replace('\\', '/')).Where(f => !f.EndsWith(".meta")).OrderBy(f => f).ToList();
        lib.kuroRider.streetKits = all.Where(f => Path.GetFileName(f).StartsWith("PedKit_", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(f).ToLowerInvariant() == ".png")
            .Select(f => AssetDatabase.LoadAssetAtPath<Texture2D>(f)).Where(t => t != null).ToArray();
        lib.kuroRider.hairStyles = all.Where(f => IsHairFile(f) && Exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => AssetDatabase.LoadAssetAtPath<GameObject>(f)).Where(g => g != null).ToArray();
        var manifest = all.FirstOrDefault(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        lib.kuroRider.hairStrands = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/PedHair_Strands.png");
        if (lib.kuroRider.hairColours == null || lib.kuroRider.hairColours.Length == 0)
            lib.kuroRider.hairColours = new PedestrianModelLibrary.KuroRiderSet().hairColours;
        lib.kuroRider.manifestPath = manifest ?? "";
        Debug.Log($"[peds-lib] kuro-rider set: enabled {lib.kuroRider.enabled}, {lib.kuroRider.streetKits.Length} street kits [{string.Join(",", lib.kuroRider.streetKits.Select(t => t.name))}], " +
                  $"{lib.kuroRider.hairStyles.Length} hair styles [{string.Join(",", lib.kuroRider.hairStyles.Select(g => g.name))}], manifest {(manifest ?? "none")}" +
                  (manifest != null ? $" ({new FileInfo(manifest).Length} bytes)" : ""));
    }

    static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());

    static string[] GuessTags(string id)
    {
        var l = id.ToLowerInvariant();
        var t = new List<string>();
        if (l.Contains("female") || l.Contains("woman") || l.Contains("girl") || l.Contains("ybot")) t.Add("female");
        if (l.Contains("male") && !l.Contains("female") || l.Contains("man") && !l.Contains("woman") || l.Contains("xbot") || l.Contains("x_bot") || l.Contains("kaito")) t.Add("male");
        if (l.Contains("senior") || l.Contains("elder")) t.Add("senior");
        if (l.Contains("child") || l.Contains("kid")) t.Add("child");
        if (l.StartsWith("ped_")) t.Add("custom-chibi");
        return t.ToArray();
    }

    /// <summary>Feet-to-crown height of the prefab at scale 1 (baked skin, metres).</summary>
    public static float MeasureHeight(GameObject prefab, out float footY)
    {
        var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
        inst.transform.position = Vector3.zero;
        float lo = float.MaxValue, hi = float.MinValue;
        var skins = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        PedestrianModelSwap.BakedYRange(skins, out lo, out hi);
        float boneTop = float.MinValue;
        foreach (var smr in skins) foreach (var b in smr.bones) if (b != null) boneTop = Mathf.Max(boneTop, b.position.y);
        Debug.Log($"[peds-lib] {prefab.name}: baked skin y {lo:0.0000}..{hi:0.0000}, top bone y {boneTop:0.0000}, root scale {inst.transform.localScale.x:0.####}, first skin lossy scale {(skins.Length > 0 ? skins[0].transform.lossyScale.x : 0f):0.####}");
        foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
        { lo = Mathf.Min(lo, mr.bounds.min.y); hi = Mathf.Max(hi, mr.bounds.max.y); }
        UnityEngine.Object.DestroyImmediate(inst);
        footY = lo;
        return hi > lo ? hi - Mathf.Min(0f, lo) : 1.8f;
    }

    static Material FindTemplate()
    {
        foreach (var guid in AssetDatabase.FindAssets("MapleLife_Crowd_ t:Material"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.EndsWith("_Body.mat")) continue;
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null && m.shader != null && m.shader.name == "MapleRide/HDRP/CelLit") return m;
        }
        return null;
    }

    static Texture SourceTexture(Material m)
    {
        if (m == null) return null;
        foreach (var p in new[] { "_BaseColorMap", "_MainTex", "baseColorTexture", "_BaseMap", "_Albedo" })
            if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
        return null;
    }

    static Color SourceColor(Material m)
    {
        if (m == null) return Color.white;
        foreach (var p in new[] { "_BaseColor", "_Color", "baseColorFactor" })
            if (m.HasProperty(p)) return m.GetColor(p);
        return Color.white;
    }

    /// <summary>
    /// CelLit (the riders' / crowd's shader) clone of the Maple crowd body material, per source slot,
    /// with the pedestrian-only lighting lift: a view-independent edge rim, more ambient, more
    /// ambient kept in shadow and a small matte floor (fixes the underlit look without touching any
    /// shared shader or region grade).
    /// </summary>
    static Material[] MakeCelMaterials(string id, GameObject prefab, Material template)
    {
        var result = new List<Material>();
        var shader = Shader.Find("MapleRide/HDRP/CelLit");
        int slot = 0;
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var src in r.sharedMaterials)
            {
                string path = $"{MatDir}/{id}_{slot:00}_{Sanitize(src != null ? src.name : "mat")}_Cel.mat";
                slot++;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = template != null ? new Material(template) : new Material(shader);
                    var tex = SourceTexture(src);
                    var col = SourceColor(src);
                    mat.SetTexture("_MainTex", tex);
                    mat.SetColor("_Color", tex != null ? Color.white : new Color(col.r, col.g, col.b, 1f));
                    ApplyPedestrianLighting(mat);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else if (mat.GetTexture("_MainTex") == null && SourceTexture(src) != null)
                {
                    // texture arrived later (extracted embedded media): fill it in, keep other tweaks
                    mat.SetTexture("_MainTex", SourceTexture(src));
                    mat.SetColor("_Color", Color.white);
                    EditorUtility.SetDirty(mat);
                }
                Debug.Log($"[peds-lib]   slot {slot - 1} '{(src ? src.name : "-")}' ({(src && src.shader ? src.shader.name : "-")}) tex '{(SourceTexture(src) ? SourceTexture(src).name : "none")}' -> {Path.GetFileName(path)}");
                result.Add(mat);
            }
        return result.ToArray();
    }

    /// <summary>
    /// Starter outfit variants (only when an entry has none): the default look plus two tints of
    /// the LAST slot (usually the main surface/body). Replace with real outfit materials by hand.
    /// </summary>
    static PedestrianModelLibrary.Variant[] MakeTintVariants(string id, Material[] mats)
    {
        if (mats == null || mats.Length == 0) return Array.Empty<PedestrianModelLibrary.Variant>();
        var tints = new (string name, Color c)[] { ("navy", new Color(0.55f, 0.65f, 1.0f)), ("sage", new Color(0.75f, 0.95f, 0.7f)) };
        var list = new List<PedestrianModelLibrary.Variant> { new PedestrianModelLibrary.Variant { id = "default", materials = mats } };
        foreach (var (name, c) in tints)
        {
            var arr = (Material[])mats.Clone();
            int k = arr.Length - 1;
            string path = $"{MatDir}/{id}_{k:00}_variant_{name}_Cel.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(mats[k]);
                m.SetColor("_Color", mats[k].GetColor("_Color") * c);
                AssetDatabase.CreateAsset(m, path);
            }
            arr[k] = m;
            list.Add(new PedestrianModelLibrary.Variant { id = name, materials = arr });
        }
        return list.ToArray();
    }

    public static void ApplyPedestrianLighting(Material mat)
    {
        void F(string p, float v) { if (mat.HasProperty(p)) mat.SetFloat(p, v); }
        void C(string p, Color v) { if (mat.HasProperty(p)) mat.SetColor(p, v); }
        F("_AmbientStrength", 1.35f);
        F("_ShadowAmbient", 0.55f);
        F("_ShadeStrength", 0.6f);
        F("_EdgeRimStrength", 0.55f);
        F("_EdgeRimPower", 2.6f);
        C("_EdgeRimColor", new Color(0.85f, 0.9f, 1f, 1f));
        F("_RimStrength", 0.35f);
        C("_MatteFloor", new Color(0.07f, 0.07f, 0.08f, 1f));
        F("_HighlightRolloff", 0.5f);
    }
}

/// <summary>Play-mode proof for the model swap (see <see cref="PedestrianModelPlaymodeRunner"/>).</summary>
[InitializeOnLoad]
public static class PedestrianModelCapture
{
    const string PrefKey = "mapleride.pedmodels.playcapture";
    const string OnKey = "mapleride.pedmodels.on";
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string OutDir = "../reference/good_graphics/pedestrians_v2";
    const double TimeoutSeconds = 900.0;
    static double _deadline;

    static PedestrianModelCapture() { EditorApplication.playModeStateChanged += OnPlayModeChanged; }

    [MenuItem("MapleRide/Pedestrians/Play-test model swap (ON)", priority = 41)]
    public static void Run() { _set = "default"; Go(true); }
    /// <summary>ON with the "fbx" set: forces the FBX model swap (X Bot / Kaito test models) instead of the Kuro-rider look.</summary>
    [MenuItem("MapleRide/Pedestrians/Play-test model swap (ON, FBX test models)", priority = 43)]
    public static void RunFbx() { _set = PedestrianModelLibrary.FbxTestSet; Go(true); }
    static string _set = "default";
    [MenuItem("MapleRide/Pedestrians/Play-test model swap (OFF)", priority = 42)]
    public static void RunOff() => Go(false);

    static void Go(bool on)
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        EditorPrefs.SetBool(OnKey, on);
        PlayerPrefs.SetString(PedestrianModelLibrary.ModelSetPref, on ? _set : "");
        PlayerPrefs.SetInt(PedestrianDirector.DisabledPref, 0);
        PlayerPrefs.Save();
        Debug.Log($"[peds-model-play] entering play mode (model set {(on ? "ON" : "OFF")})...");
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~PedestrianModelRunner").AddComponent<PedestrianModelPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.onMode = EditorPrefs.GetBool(OnKey, true);
            runner.framePrefix = PlayerPrefs.GetString(PedestrianModelLibrary.ModelSetPref, "") == PedestrianModelLibrary.FbxTestSet ? "fbx_" : "";
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            PlayerPrefs.SetString(PedestrianModelLibrary.ModelSetPref, "");   // never leave it on
            PlayerPrefs.Save();
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[peds-model-play] left play mode.");
            if (Application.isBatchMode) EditorApplication.Exit(PedestrianModelPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[peds-model-play] TIMED OUT waiting for the runner.");
            PedestrianModelPlaymodeRunner.Failed = true;
            PedestrianModelPlaymodeRunner.Finished = true;
        }
        if (!PedestrianModelPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}
