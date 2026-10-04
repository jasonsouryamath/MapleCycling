using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Collapses the shared Colnago GLB's ~78 individual mesh parts into a handful of combined
/// renderers per rider, and bakes a single-renderer "far" bike for distance LOD.
///
/// WHY THIS EXISTS (measured, not guessed):
/// A play-mode isolation sweep on Shiosai at the 1300 m station measured the full frame at
/// 59.2 ms with 4,740 draw calls. Switching the traffic root off took it to 6.5 ms / 598 draws,
/// and merely disabling the traffic's RENDERERS (leaving all thirty rigs solving every frame)
/// took it to 6.8 ms. So the cost was never the IK, the post-FX or the landscape - it was the
/// draw calls, and an inventory showed where they came from: 60 skinned body renderers and
/// 2,340 MESH renderers, i.e. thirty bicycles x seventy-eight parts, every part its own
/// draw call, every one of them submitted again for the shadow pass.
///
/// The parts cannot simply all be merged: KuroBikeRig spins Axle_R / Axle_F / Crank_L / Crank_R
/// and steers SteerPivot, so anything merged across those sockets would freeze. The bake
/// therefore groups parts by their nearest DRIVEN ancestor - one combined renderer per moving
/// group plus one for the static frame, ~7 renderers instead of 78, with the sockets' own
/// transforms untouched so the drivetrain still turns. Beyond a few dozen metres the moving
/// parts are indistinguishable, so a single fully-merged mesh replaces the lot.
///
/// The combined meshes are saved ONCE as shared assets and reused by all thirty riders - the
/// geometry is identical between them, only the materials differ - so the scene does not gain
/// thirty copies of a merged bicycle.
/// </summary>
public static class ShiosaiBikeLod
{
    const string MeshDir = "Assets/Environment/ShiosaiCoast/Generated/BikeLod";

    /// <summary>
    /// Sockets KuroBikeRig rotates at runtime. Order matters: a part is assigned to the FIRST
    /// of these found walking up from itself, and Axle_F lives under SteerPivot while the
    /// cranks live under BB, so the deeper socket has to win.
    /// </summary>
    static readonly string[] DrivenSockets = { "Axle_R", "Axle_F", "Crank_L", "Crank_R", "SteerPivot", "BB" };

    const string FrameGroup = "";   // everything not under a driven socket

    class Baked
    {
        public Mesh mesh;
        public string[] materialNames;   // one per submesh, in the baked order
    }

    static Dictionary<string, Baked> _detail;
    static Baked _far;
    static string _bakedFrom;

    /// <summary>Drops the cached bake so a rebuild re-reads the GLB.</summary>
    public static void Reset() { _detail = null; _far = null; _bakedFrom = null; }

    /// <summary>
    /// Replaces a freshly-instantiated bike's per-part renderers with the combined ones.
    /// Call AFTER the livery repaint, so the clone map is available to re-apply.
    /// </summary>
    /// <param name="bikeModel">the instantiated GLB root ("BikeMesh")</param>
    /// <param name="clones">source material name -> this rider's cloned material</param>
    public static void Apply(GameObject bikeModel, Dictionary<string, Material> clones,
                             out Renderer[] detail, out Renderer far)
    {
        detail = System.Array.Empty<Renderer>();
        far = null;
        if (bikeModel == null) return;

        // Name -> material for every material the ORIGINAL parts used, with the rider's own
        // clones overlaid. The bake only stores names, so this is how each rider gets its
        // own livery back onto the merged geometry.
        var byName = new Dictionary<string, Material>();
        foreach (var mr in bikeModel.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var m in mr.sharedMaterials)
            {
                if (m == null) continue;
                string src = m.name.Replace(" (Instance)", "");
                if (!byName.ContainsKey(src)) byName[src] = m;
            }
        if (clones != null)
            foreach (var kv in clones) byName[kv.Key] = kv.Value;

        // Bake material names CANONICALLY, i.e. under the shared GLB's own material names.
        //
        // This pass runs after the livery repaint, so by now this rider's paint materials are
        // named "Shiosai07_Kanon_Colnago_Racing_Red", not "Colnago_Racing_Red". The bake is
        // cached and therefore performed once, from whichever rider came first - so every
        // LATER rider used to look its baked names up in its own map, miss, and get a NULL
        // material, which Unity draws as magenta. That is the "purple bikes" defect: rider 00
        // kept its livery and the other twenty-nine turned magenta.
        var canonical = new Dictionary<string, string>();
        if (clones != null)
            foreach (var kv in clones)
                if (kv.Value != null) canonical[kv.Value.name] = kv.Key;

        Bake(bikeModel, canonical);

        var made = new List<Renderer>();
        foreach (var kv in _detail)
        {
            var space = kv.Key == FrameGroup
                ? bikeModel.transform
                : FindChild(bikeModel.transform, kv.Key);
            if (space == null)
            {
                // Silently skipping this used to just mean "one fewer merged renderer" - which
                // reads on screen as a bike missing its frame, its wheels or its cranks for
                // THIS rider only, with nothing in the log to say why. Every rider instantiates
                // the same shared bike GLB, so this should never actually miss; if it does, the
                // rider needs a full bike, not a silently thinner one.
                Debug.LogError($"[shiosai-traffic] {bikeModel.transform.root.name}: bike LOD " +
                               $"socket group '{kv.Key}' not found on this rider's bike instance " +
                               "- that group's renderer was NOT built (missing bike parts).");
                continue;
            }
            made.Add(Spawn(space, "BikeLod_" + (kv.Key == FrameGroup ? "Frame" : kv.Key),
                           kv.Value, byName, true));
        }
        detail = made.ToArray();

        far = Spawn(bikeModel.transform, "BikeLod_Far", _far, byName, false);
        if (far == null)
            Debug.LogError($"[shiosai-traffic] {bikeModel.transform.root.name}: bike LOD 'far' " +
                           "renderer failed to build - this rider will show NO bicycle beyond " +
                           "the detail range.");

        // The source parts stay in the hierarchy (KuroBikeRig and the sockets rely on those
        // transforms) but never draw again. Disabling rather than deleting keeps this
        // reversible and keeps the GLB prefab instance intact.
        foreach (var mr in bikeModel.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.gameObject.name.StartsWith("BikeLod_")) continue;
            mr.enabled = false;
            EditorUtility.SetDirty(mr);
        }
    }

    static Renderer Spawn(Transform space, string name, Baked baked,
                          Dictionary<string, Material> byName, bool startEnabled)
    {
        var existing = space.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject(name);
        go.transform.SetParent(space, false);
        go.AddComponent<MeshFilter>().sharedMesh = baked.mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var resolved = new Material[baked.materialNames.Length];
        for (int i = 0; i < resolved.Length; i++)
        {
            if (byName.TryGetValue(baked.materialNames[i], out var m) && m != null)
            {
                resolved[i] = m;
                continue;
            }
            // Never leave the slot null: Unity draws a null material as magenta, which reads
            // in-game as a purple bicycle and gives no clue where it came from.
            Debug.LogError($"[shiosai-traffic] {space.root.name}: LOD submesh {i} wants material " +
                           $"'{baked.materialNames[i]}' but this rider has no such material. " +
                           "Bike would render magenta.");
        }
        mr.sharedMaterials = resolved;
        mr.enabled = startEnabled;
        return mr;
    }

    // ------------------------------------------------------------------ bake

    static void Bake(GameObject bikeModel, Dictionary<string, string> canonical)
    {
        string key = bikeModel.transform.childCount + ":" + bikeModel.name;
        if (_detail != null && _bakedFrom == key) return;

        EnsureFolder(MeshDir);

        var byGroup = new Dictionary<string, List<MeshFilter>>();
        var all = new List<MeshFilter>();
        foreach (var mf in bikeModel.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.gameObject.name.StartsWith("BikeLod_")) continue;
            all.Add(mf);
            string g = GroupOf(mf.transform, bikeModel.transform);
            if (!byGroup.TryGetValue(g, out var list)) byGroup[g] = list = new List<MeshFilter>();
            list.Add(mf);
        }

        _detail = new Dictionary<string, Baked>();
        foreach (var kv in byGroup)
        {
            var space = kv.Key == FrameGroup
                ? bikeModel.transform
                : FindChild(bikeModel.transform, kv.Key);
            if (space == null) continue;
            _detail[kv.Key] = Combine(kv.Value, space,
                                      "bike_lod_" + (kv.Key == FrameGroup ? "frame" : kv.Key),
                                      canonical);
        }
        _far = Combine(all, bikeModel.transform, "bike_lod_far", canonical);
        _bakedFrom = key;

        Debug.Log($"[shiosai-traffic] bike LOD baked: {all.Count} parts -> {_detail.Count} " +
                  $"detail renderers + 1 far renderer.");
    }

    static string GroupOf(Transform t, Transform root)
    {
        for (var p = t; p != null && p != root.parent; p = p.parent)
            for (int i = 0; i < DrivenSockets.Length; i++)
                if (p.name == DrivenSockets[i]) return DrivenSockets[i];
        return FrameGroup;
    }

    static Baked Combine(List<MeshFilter> parts, Transform space, string assetName,
                         Dictionary<string, string> canonical)
    {
        var perMaterial = new Dictionary<string, List<CombineInstance>>();
        var w2l = space.worldToLocalMatrix;

        foreach (var mf in parts)
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            var mats = mr.sharedMaterials;
            var mesh = mf.sharedMesh;
            var m = w2l * mf.transform.localToWorldMatrix;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string k = (s < mats.Length && mats[s] != null)
                    ? mats[s].name.Replace(" (Instance)", "") : "__none";
                if (canonical != null && canonical.TryGetValue(k, out var src)) k = src;
                if (!perMaterial.TryGetValue(k, out var list))
                    perMaterial[k] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
            }
        }

        // Deterministic submesh order, so every rider's material array lines up with the
        // shared mesh regardless of the order Unity happened to walk the hierarchy in.
        var order = perMaterial.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToArray();

        var subs = new List<CombineInstance>();
        foreach (var k in order)
        {
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            m.CombineMeshes(perMaterial[k].ToArray(), true, true);
            subs.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
        }

        var final = new Mesh { name = assetName, indexFormat = IndexFormat.UInt32 };
        final.CombineMeshes(subs.ToArray(), false, false);
        final.RecalculateBounds();

        // OVERWRITE IN PLACE - never DeleteAsset + CreateAsset. These meshes are shared by EVERY
        // region that stages traffic through this class (Shiosai AND Minato). Deleting the asset
        // minted a new GUID on each re-bake, so re-staging one region silently nulled the
        // merged-bike MeshFilters of the other: its riders kept their bodies but drew no bicycle
        // (the "riding invisible bikes" defect - all 30 Minato riders after a Shiosai rebuild).
        string path = $"{MeshDir}/{assetName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(final, existing);
            existing.name = assetName;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(final);
            final = existing;
        }
        else
        {
            AssetDatabase.DeleteAsset(path);   // a non-Mesh squatter at this path
            AssetDatabase.CreateAsset(final, path);
        }
        AssetDatabase.SaveAssetIfDirty(final);

        return new Baked { mesh = final, materialNames = order };
    }

    // ------------------------------------------------------------------ helpers

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var f = FindChild(c, name);
            if (f != null) return f;
        }
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
