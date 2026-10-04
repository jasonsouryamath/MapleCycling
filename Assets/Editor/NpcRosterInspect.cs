using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Inspection pass for the NPC roster: measures each rig's seated height from BAKED skinned
/// meshes (Renderer.bounds is the import-time estimate and lies), dumps material names so the
/// livery repaint can target them, and renders a contact sheet so the sculpts can be compared
/// by eye rather than by file size.
public static class NpcRosterInspect
{
    static readonly string[] Rigs =
    {
        "Assets/Kuro/NPC/KuroNPC_Aoi.glb",
        "Assets/Kuro/NPC/KuroNPC_Mika.glb",
        "Assets/Kuro/NPC/KuroNPC_Ren.glb",
        "Assets/Kuro/NPC/KuroNPC_Sora.glb",
        "Assets/Kuro/NPC/KuroNPC_Yuki.glb",
        "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb",
    };

    static readonly string[] Bikes =
    {
        "Assets/Kuro/kuro_bike_colnago.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Bianchi.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Cannondale.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Canyon.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Pinarello.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Trek.glb",
    };

    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        foreach (var p in Rigs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (asset == null) { Debug.LogWarning($"[roster] MISSING {p}"); continue; }
            var go = Object.Instantiate(asset);
            float h = BakedHeight(go);
            var mats = go.GetComponentsInChildren<Renderer>(true)
                         .SelectMany(r => r.sharedMaterials).Where(m => m != null)
                         .Select(m => m.name).Distinct().ToArray();
            var tex = go.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r => r.sharedMaterials).Where(m => m != null && m.HasProperty("baseColorTexture"))
                        .Select(m => m.GetTexture("baseColorTexture")).Where(t => t != null)
                        .Select(t => t.name).Distinct().ToArray();
            Debug.Log($"[roster] RIG {Path.GetFileName(p)} height={h:F3} mats=[{string.Join(", ", mats)}] tex=[{string.Join(", ", tex)}]");
            Object.DestroyImmediate(go);
        }

        foreach (var p in Bikes)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (asset == null) { Debug.LogWarning($"[roster] MISSING {p}"); continue; }
            var go = Object.Instantiate(asset);
            var mats = go.GetComponentsInChildren<Renderer>(true)
                         .SelectMany(r => r.sharedMaterials).Where(m => m != null)
                         .Select(m => m.name).Distinct().ToArray();
            var b = Bounds(go);
            Debug.Log($"[roster] BIKE {Path.GetFileName(p)} size={b.size:F3} mats=[{string.Join(", ", mats)}]");
            Object.DestroyImmediate(go);
        }

        EditorApplication.Exit(0);
    }

    /// Skinned bounds are the import-time estimate; bake the posed mesh instead.
    public static float BakedHeight(GameObject go)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var s in skins)
        {
            var m = new Mesh();
            s.BakeMesh(m, true);
            foreach (var v in m.vertices)
            {
                var w = s.transform.TransformPoint(v);
                lo = Mathf.Min(lo, w.y); hi = Mathf.Max(hi, w.y);
            }
            Object.DestroyImmediate(m);
        }
        if (skins.Length == 0) { var b = Bounds(go); return b.size.y; }
        return hi - lo;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
