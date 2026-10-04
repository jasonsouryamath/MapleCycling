using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ground-truth probe: what mesh is the scene player actually rendering, and when baked in its
/// CURRENT pose, are the head-region (local-space upper) verts dispersed (exploded) or tight?
/// Distinguishes "reimport didn't take / broken ref" from "shredding is below the surgery cutoff".
/// </summary>
public static class FiPlayerBakeProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[bake] player root missing"); return; }

        var sb = new StringBuilder();
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) { sb.AppendLine($"[bake] {smr.name} sharedMesh=NULL"); continue; }
            var m = smr.sharedMesh;
            sb.AppendLine($"[bake] renderer='{smr.name}' mesh='{m.name}' verts={m.vertexCount} bones={smr.bones.Length} rootBone='{(smr.rootBone? smr.rootBone.name : "null")}'");
            sb.AppendLine($"[bake]   materials=[{string.Join(", ", smr.sharedMaterials.Select(x => x ? x.name : "NULL"))}]");

            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            if (verts.Length == 0) { sb.AppendLine("[bake]   baked EMPTY"); Object.DestroyImmediate(baked); continue; }

            // local-space bounds of baked (posed) mesh
            var min = verts[0]; var max = verts[0];
            foreach (var v in verts) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            float top = max.y; float span = max.y - min.y;
            // "head region" = top 30% of the posed height
            float cut = max.y - span * 0.30f;
            var head = verts.Where(v => v.y >= cut).ToArray();
            var hmin = head[0]; var hmax = head[0];
            foreach (var v in head) { hmin = Vector3.Min(hmin, v); hmax = Vector3.Max(hmax, v); }
            Vector3 hsize = hmax - hmin;
            // dispersion metric: stddev of head verts from their centroid
            Vector3 c = Vector3.zero; foreach (var v in head) c += v; c /= head.Length;
            float var2 = 0f; foreach (var v in head) var2 += (v - c).sqrMagnitude; var2 /= head.Length;
            float rms = Mathf.Sqrt(var2);
            sb.AppendLine($"[bake]   posed bounds y {min.y:0.000}->{max.y:0.000} (span {span:0.000})");
            sb.AppendLine($"[bake]   HEAD region (top30%) verts={head.Length} bbox={hsize.x:0.000}x{hsize.y:0.000}x{hsize.z:0.000} rmsFromCentroid={rms:0.000}");

            // Identify OUTLIER verts (posed >2*rms from head centroid) and tally the dominant
            // bone that drags them, plus their REST-pose local Y range.
            var bw = m.boneWeights;
            var rest = m.vertices;
            var names = smr.bones.Select(b => b ? b.name : "?").ToArray();
            var tally = new System.Collections.Generic.Dictionary<string, int>();
            float outThresh = rms * 2f;
            int nOut = 0; float restMin = 1e9f, restMax = -1e9f;
            // recompute per-original-vertex: baked verts align 1:1 with mesh verts
            for (int vi = 0; vi < verts.Length; vi++)
            {
                if (verts[vi].y < cut) continue;
                if ((verts[vi] - c).magnitude <= outThresh) continue;
                nOut++;
                var w = bw[vi];
                int bi = w.weight0 >= w.weight1 && w.weight0 >= w.weight2 && w.weight0 >= w.weight3 ? w.boneIndex0
                       : w.weight1 >= w.weight2 && w.weight1 >= w.weight3 ? w.boneIndex1
                       : w.weight2 >= w.weight3 ? w.boneIndex2 : w.boneIndex3;
                string bn = (bi >= 0 && bi < names.Length) ? names[bi] : "?";
                tally[bn] = tally.TryGetValue(bn, out var cc) ? cc + 1 : 1;
                restMin = Mathf.Min(restMin, rest[vi].y); restMax = Mathf.Max(restMax, rest[vi].y);
            }
            sb.AppendLine($"[bake]   OUTLIERS (>2rms) count={nOut} restLocalY {restMin:0.000}->{restMax:0.000}");
            foreach (var kv in tally.OrderByDescending(k => k.Value))
                sb.AppendLine($"[bake]     outlier dominant bone {kv.Key,-14} {kv.Value}");
            Object.DestroyImmediate(baked);
        }
        Debug.Log(sb.ToString());
        MapleRideSceneBootstrap.DiscardChanges();
    }
}
