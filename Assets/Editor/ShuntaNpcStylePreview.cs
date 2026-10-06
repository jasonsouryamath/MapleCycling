using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Export;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds standalone, bike-rigged style previews for review BEFORE anything is wired into the game:
/// each library rider (already rigged to its KuroBikeRig bike) is cloned, given a different hair + eyes by
/// ShuntaNpcStyle, and saved as Assets/Ride/ShuntaMetro/Actors/StylePreview/StyleRider_NN.prefab with every
/// generated mesh / material / texture persisted next to it. The source prefabs and the game are not touched.
/// Run: Unity -batchmode -quit -executeMethod ShuntaNpcStylePreview.Build
/// </summary>
public static class ShuntaNpcStylePreview
{
    const string Folder = "Assets/Ride/ShuntaMetro/Actors/StylePreview";
    const string GlbFolder = Folder + "/GLB";
    const float LineupSpacingM = 2.6f;

    public static void Build()
    {
        var library = Resources.Load<ShuntaNpcLibrary>(ShuntaNpcLibrary.ResourcePath);
        if (library == null || library.riders == null || library.riders.Length == 0)
        { Debug.LogError("[shunta-style] no NPC library riders; run ShuntaNpcLightingValidation.BuildLibrary first"); Fail(); return; }
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        int made = 0; var prefabs = new List<string>();
        for (int i = 0; i < ShuntaNpcStyle.EyeStyleCount; i++)
        {
            var source = library.riders[i % library.riders.Length];
            if (source == null) continue;
            var parking = new GameObject("~style preview") { hideFlags = HideFlags.HideAndDontSave }; parking.SetActive(false);
            try
            {
                var clone = UnityEngine.Object.Instantiate(source, parking.transform);
                clone.name = "StyleRider_" + i.ToString("D2");
                ShuntaNpcLibrary.Sanitize(clone, false);                    // keeps the KuroBikeRig: the rider stays rigged to its bike
                var bodyBefore = BodySignature(clone);
                bool styled = ShuntaNpcStyle.Apply(clone, i, true);
                Debug.Log($"[shunta-style] {clone.name}: source={source.name} styled={styled} eyes={ShuntaNpcStyle.EyeName(ShuntaNpcStyle.Pick(i, 8, 8).eye)} body {bodyBefore} -> {BodySignature(clone)}");
                if (!styled) continue;
                Persist(clone, clone.name);
                var path = Folder + "/" + clone.name + ".prefab";
                clone.transform.SetParent(null);
                PrefabUtility.SaveAsPrefabAsset(clone, path);
                made++; prefabs.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(parking); }
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log($"[shunta-style] built {made} styled bike riders in {Folder}");
        if (made == 0) { Fail(); return; }
        ExportGlbs(prefabs);
    }

    static void Fail() { if (Application.isBatchMode) EditorApplication.Exit(2); }

    /// <summary>Vertex count + bone count of the first enabled body mesh, to show the body itself is unchanged.</summary>
    public static string BodySignature(GameObject go)
    {
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.name == "Mesh_0" && smr.enabled && smr.sharedMesh != null)
                return $"verts={smr.sharedMesh.vertexCount} bones={smr.bones.Length} mats={smr.sharedMaterials.Length}";
        return "none";
    }

    static void Persist(GameObject root, string prefix)
    {
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials; bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                foreach (string prop in m.GetTexturePropertyNames())
                {
                    var t = m.GetTexture(prop);
                    if (t != null && !AssetDatabase.Contains(t) && !(t is RenderTexture)) AssetDatabase.CreateAsset(t, Unique(prefix + "_tex_" + (n++), "asset"));
                }
                if (!AssetDatabase.Contains(m)) { AssetDatabase.CreateAsset(m, Unique(prefix + "_mat_" + (n++), "mat")); }
                mats[i] = m; changed = true;
            }
            if (changed) r.sharedMaterials = mats;
            Mesh mesh = null; var smr = r as SkinnedMeshRenderer; var mf = r.GetComponent<MeshFilter>();
            mesh = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
            if (mesh != null && !AssetDatabase.Contains(mesh)) AssetDatabase.CreateAsset(mesh, Unique(prefix + "_mesh_" + (n++), "asset"));
        }
    }

    static string Unique(string name, string ext) => AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + name + "." + ext);

    // ------------------------------------------------------------------ GLB preview export
    /// <summary>One GLB per styled rider plus a lineup GLB with all of them side by side, posed on their bikes.</summary>
    static void ExportGlbs(List<string> prefabPaths)
    {
        Directory.CreateDirectory(GlbFolder);
        int ok = 0;
        var lineup = new List<GameObject>();
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            var single = PreparedInstance(prefabPaths[i]);
            if (single == null) continue;
            try { if (Export(new[] { single }, GlbFolder + "/" + Path.GetFileNameWithoutExtension(prefabPaths[i]) + ".glb")) ok++; }
            catch (Exception e) { Debug.LogError("[shunta-style] GLB export failed for " + prefabPaths[i] + ": " + e); }
            finally { UnityEngine.Object.DestroyImmediate(single); }
        }
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            var inst = PreparedInstance(prefabPaths[i]);
            if (inst == null) continue;
            inst.transform.position = new Vector3((i - (prefabPaths.Count - 1) * .5f) * LineupSpacingM, 0f, 0f);
            lineup.Add(inst);
        }
        try { if (lineup.Count > 0 && Export(lineup.ToArray(), GlbFolder + "/StyleRiders_Lineup.glb")) ok++; }
        catch (Exception e) { Debug.LogError("[shunta-style] lineup GLB export failed: " + e); }
        finally { foreach (var g in lineup) if (g != null) UnityEngine.Object.DestroyImmediate(g); }
        Debug.Log($"[shunta-style] exported {ok} GLB file(s) to {GlbFolder}");
        if (ok == 0) Fail();
    }

    /// <summary>Fresh prefab instance, posed on its bike, LOD0 only, with export-friendly materials. Never touches the saved prefab.</summary>
    static GameObject PreparedInstance(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.SetActive(true);
        foreach (var rig in inst.GetComponentsInChildren<KuroBikeRig>(true))
        { try { rig.enabled = true; rig.ForceSolveOnce(); } catch (Exception e) { Debug.LogWarning("[shunta-style] bike pose solve failed: " + e.Message); } }
        foreach (var group in inst.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = group.GetLODs();
            for (int l = 1; l < lods.Length; l++)
                foreach (var r in lods[l].renderers) if (r != null) { r.enabled = false; if (r.GetComponents<Component>().Length <= 4) r.gameObject.SetActive(false); }
            group.enabled = false;
        }
        MakeExportable(inst);
        return inst;
    }

    static void MakeExportable(GameObject root)
    {
        var standard = Shader.Find("Standard") ?? Shader.Find("HDRP/Lit");
        var cache = new Dictionary<Material, Material>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || standard == null) continue;
                if (!cache.TryGetValue(m, out var export))
                {
                    export = new Material(standard) { name = m.name };
                    Color c = Color.white;
                    if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor"); else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
                    export.color = c;
                    Texture tex = null;
                    foreach (string prop in new[] { "_MainTex", "_BaseColorMap", "_BaseMap" }) if (m.HasProperty(prop) && (tex = m.GetTexture(prop)) != null) break;
                    var readable = ReadableCopy(tex);
                    if (readable != null) export.mainTexture = readable;
                    else if (tex != null) Debug.LogWarning($"[shunta-style] texture {tex.name} on {m.name} is not readable in batch mode; exporting colour only");
                    cache[m] = export;
                }
                mats[i] = export;
            }
            r.sharedMaterials = mats;
        }
    }

    static Texture2D ReadableCopy(Texture t)
    {
        if (t == null) return null;
        string path = AssetDatabase.GetAssetPath(t), ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
        {
            try { var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (tex.LoadImage(File.ReadAllBytes(path))) { tex.name = t.name; return tex; } }
            catch (Exception) { /* fall through */ }
        }
        return t is Texture2D t2 && t2.isReadable ? t2 : null;
    }

    static bool Export(GameObject[] roots, string path)
    {
        var export = new GameObjectExport(new ExportSettings { Format = GltfFormat.Binary });
        export.AddScene(roots);
        var task = export.SaveToFileAndDispose(path);
        Pump(task);
        bool done = task.IsCompleted && !task.IsFaulted && task.Result && File.Exists(path);
        Debug.Log($"[shunta-style] GLB {(done ? "OK" : "FAILED")}: {path}" + (done ? $" ({new FileInfo(path).Length / 1024} KB)" : ""));
        return done;
    }

    /// <summary>Batch mode has no player loop to resume the exporter's awaits on the main thread, so drive Unity's own queue until the task ends.</summary>
    static void Pump(Task task)
    {
        MethodInfo exec = null; object target = null;
        var type = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.UnitySynchronizationContext");
        if (type != null)
        {
            exec = type.GetMethod("ExecuteTasks", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (exec == null) { exec = type.GetMethod("Exec", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); target = SynchronizationContext.Current; }
        }
        if (exec == null) Debug.LogWarning("[shunta-style] could not find Unity's sync-context pump; waiting without it");
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 180)
        {
            try { exec?.Invoke(target, null); } catch (Exception e) { Debug.LogWarning("[shunta-style] pump: " + e.InnerException?.Message); }
            Thread.Sleep(2);
        }
    }
}
