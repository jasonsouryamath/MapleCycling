using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shop fitment (2026-09-25). Splits the player's body mesh (KuroNPC_KuroAnime_Rigged.glb) into
/// garment material slots by TRIANGLE, so bought kit is drawn exactly on the jersey / bibs /
/// helmet geometry. The old per-texel mask bled kit colour into hair, gloves and skin, because
/// Kuro's atlas is a baked projection whose fragments overlap.
///
/// Labels come from the Blender segmentation (design_assets/3d/kuro/kuro_npc_segment.py), exported
/// per triangle by kuro_shop_trilabels.py into Assets/Editor/Data/kuro_tri_labels.bytes. Triangles
/// are matched by rest-pose CENTROID, not by index, so the bake does not depend on the importer
/// keeping the triangle order. The axis convention is found automatically (48 signed axis
/// permutations, bounding-box normalised, the best mean nearest-neighbour distance wins).
///
/// Output: Assets/Resources/Shop/KuroKitSplit.asset. It is the same vertices, bones, bind poses
/// and blend shapes, with the original submeshes kept at their indices (the body submesh loses
/// its kit triangles) plus three appended submeshes: jersey, bibs, helmet. KitAppearance swaps
/// it in at runtime.
///
///   run_steps.ps1 "KuroKitSplitBake.Bake|claude_kitsplit.log|1"
/// </summary>
public static class KuroKitSplitBake
{
    const string GlbPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
    const string LabelsPath = "Assets/Editor/Data/kuro_tri_labels.bytes";
    public const string OutPath = "Assets/Resources/Shop/KuroKitSplit.asset";

    const int Helmet = 0, Jersey = 3, Shorts = 4;

    /// <summary>Adaptive PN refinement of the player body (see <see cref="KuroMeshRefine"/>).
    /// Edges above RefineMaxEdge metres are split, up to RefinePasses times.</summary>
    const bool RefinePlayer = true;
    const float RefineMaxEdge = 0.010f;
    const int RefinePasses = 2;
    const float RefineBulge = 0.15f;

    [MenuItem("MapleRide/Shop/Bake Kuro Kit Split Mesh")]
    public static void Bake()
    {
        var src = AssetDatabase.LoadAllAssetsAtPath(GlbPath).OfType<Mesh>()
                               .OrderByDescending(m => TriCount(m)).FirstOrDefault();
        if (src == null) { Debug.LogError($"[kitsplit] no mesh in {GlbPath}"); return; }

        var raw = File.ReadAllBytes(LabelsPath);
        int n = raw.Length / 16;
        var bc = new Vector3[n];
        var bl = new int[n];
        for (int i = 0; i < n; i++)
        {
            bc[i] = new Vector3(System.BitConverter.ToSingle(raw, i * 16),
                                System.BitConverter.ToSingle(raw, i * 16 + 4),
                                System.BitConverter.ToSingle(raw, i * 16 + 8));
            bl[i] = Mathf.RoundToInt(System.BitConverter.ToSingle(raw, i * 16 + 12));
        }

        var verts = src.vertices;
        // body submesh = the largest one; the others (if any) are kept untouched
        int body = 0;
        for (int s = 1; s < src.subMeshCount; s++)
            if (src.GetTriangles(s).Length > src.GetTriangles(body).Length) body = s;
        var tris = src.GetTriangles(body);
        int tn = tris.Length / 3;
        var uc = new Vector3[tn];
        for (int t = 0; t < tn; t++)
            uc[t] = (verts[tris[t * 3]] + verts[tris[t * 3 + 1]] + verts[tris[t * 3 + 2]]) / 3f;
        Debug.Log($"[kitsplit] mesh '{src.name}' {src.vertexCount} verts, {src.subMeshCount} submeshes, " +
                  $"body submesh {body} = {tn} tris; blender labels {n} tris");

        // ---- axis fit
        var bMin = Min(bc); var bMax = Max(bc);
        float bestScore = float.MaxValue; Matrix4x4 best = Matrix4x4.identity; string bestName = "";
        var grid = new Grid(bc, 0.02f);
        var sample = Enumerable.Range(0, tn).Where(i => i % Mathf.Max(1, tn / 3000) == 0).ToArray();
        foreach (var (perm, name) in Perms())
        {
            var mapped = uc.Select(p => perm.MultiplyVector(p)).ToArray();
            var uMin = Min(mapped); var uMax = Max(mapped);
            var fit = BoxFit(uMin, uMax, bMin, bMax) * perm;
            float sum = 0f;
            foreach (int i in sample) sum += grid.Nearest(fit.MultiplyPoint3x4(uc[i]), out _);
            float score = sum / sample.Length;
            if (score < bestScore) { bestScore = score; best = fit; bestName = name; }
        }
        Debug.Log($"[kitsplit] axis fit {bestName}: mean centroid distance {bestScore * 1000f:0.0} mm");
        if (bestScore > 0.01f) Debug.LogWarning("[kitsplit] fit is poor (>10 mm) - check the labels match this GLB.");

        // ---- label every triangle, then a 2-pass majority smooth over shared-position neighbours
        var lab = new int[tn];
        var bpos = new Vector3[tn];
        for (int t = 0; t < tn; t++) { grid.Nearest(best.MultiplyPoint3x4(uc[t]), out int j); lab[t] = bl[j]; bpos[t] = bc[j]; }
        Smooth(lab, tris, verts, 2);
        ClaimStraysInGarmentZones(lab, bpos);
        StripByBone(src, lab, tris);

        var keep = new List<int>(); var jer = new List<int>(); var bib = new List<int>(); var hel = new List<int>();
        for (int t = 0; t < tn; t++)
        {
            var dst = lab[t] == Jersey ? jer : lab[t] == Shorts ? bib : lab[t] == Helmet ? hel : keep;
            dst.Add(tris[t * 3]); dst.Add(tris[t * 3 + 1]); dst.Add(tris[t * 3 + 2]);
        }

        var outMesh = Object.Instantiate(src);
        outMesh.name = "KuroKitSplit";
        int orig = src.subMeshCount;
        outMesh.subMeshCount = orig + 3;
        for (int s = 0; s < orig; s++)
            outMesh.SetTriangles(s == body ? keep.ToArray() : src.GetTriangles(s), s, false);
        outMesh.SetTriangles(jer.ToArray(), orig + 0, false);
        outMesh.SetTriangles(bib.ToArray(), orig + 1, false);
        outMesh.SetTriangles(hel.ToArray(), orig + 2, false);
        outMesh.RecalculateBounds();

        // PLAYER DETAIL (2026-09-26): this mesh is the player's runtime body (KitAppearance swaps it
        // in at Start), so it is where Kuro gets his extra geometry - the shared GLB, and every NPC
        // built from it, stay at the original density. Original vertices are kept first and
        // unchanged, so the labels above, the rig and the baked grip all still hold.
        if (RefinePlayer)
        {
            var refined = KuroMeshRefine.Refine(outMesh, RefineMaxEdge, RefinePasses, RefineBulge, out string rep);
            Debug.Log("[kitsplit] refine " + rep);
            if (refined != outMesh) { Object.DestroyImmediate(outMesh); outMesh = refined; outMesh.name = "KuroKitSplit"; }
        }

        AssetDatabase.DeleteAsset(OutPath);
        AssetDatabase.CreateAsset(outMesh, OutPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[kitsplit] wrote {OutPath}: base {keep.Count / 3}, jersey {jer.Count / 3}, bibs {bib.Count / 3}, " +
                  $"helmet {hel.Count / 3} tris; kit submeshes start at index {orig}");
    }

    /// <summary>
    /// Garments stop where the skin/gear starts: a "jersey" triangle whose vertices are mostly
    /// skinned to a forearm or hand bone is a GLOVE cuff (Kuro has bare forearms), and a "bibs"
    /// triangle on a shin/foot bone is a sock or shoe. Bone names come from the GLB's own
    /// SkinnedMeshRenderer. Play test 2026-09-25: the gloves came out jersey-teal without this.
    /// </summary>
    static void StripByBone(Mesh src, int[] lab, int[] tris)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        var smr = go != null ? go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.sharedMesh == src) : null;
        if (smr == null || smr.bones == null || smr.bones.Length == 0) { Debug.LogWarning("[kitsplit] no bones - skipped the glove/sock strip"); return; }
        var names = smr.bones.Select(b => b != null ? b.name.ToLowerInvariant() : "").ToArray();
        bool Arm(string n) => n.Contains("hand") || n.Contains("forearm") || n.Contains("lowerarm") || n.Contains("finger") || n.Contains("thumb");
        bool Shin(string n) => n.Contains("foot") || n.Contains("toe") || n.Contains("calf") || n.Contains("lowerleg") || n.Contains("shin") ||
                               (n.EndsWith("leg") && !n.Contains("up") && !n.Contains("thigh"));
        var bw = src.boneWeights;
        int glove = 0, sock = 0;
        for (int t = 0; t < lab.Length; t++)
        {
            if (lab[t] != Jersey && lab[t] != Shorts) continue;
            int hits = 0;
            for (int k = 0; k < 3; k++)
            {
                var w = bw[tris[t * 3 + k]];
                int b = w.boneIndex0;   // dominant influence
                if (b < 0 || b >= names.Length) continue;
                // bibs: shins/feet AND forearms/hands (the segmentation labelled some hanging
                // forearm triangles "shorts": a black band at the wrist in the shop)
                if (lab[t] == Jersey ? Arm(names[b]) : (Shin(names[b]) || Arm(names[b]))) hits++;
            }
            if (hits >= 2) { if (lab[t] == Jersey) glove++; else sock++; lab[t] = -1; }
        }
        Debug.Log($"[kitsplit] bone strip: {glove} jersey triangles on forearm/hand bones -> base (gloves), " +
                  $"{sock} bibs triangles on shin/foot bones -> base");
    }

    /// <summary>
    /// Triangles the segmentation called hair / face / glove / leg but that sit INSIDE a garment
    /// zone (the same clean 3D cuts as kuro_shop_mask.py v3, Blender rest pose) belong to the
    /// garment. Play test 2026-09-25: a white jersey showed black flecks on the shoulders and
    /// under the arm, which were exactly such triangles still drawing Kuro's black kit.
    /// </summary>
    static void ClaimStraysInGarmentZones(int[] lab, Vector3[] p)
    {
        const float ArmX = 0.19f, SleeveZ = 0.705f, WaistZ = 0.558f, LegHemZ = 0.335f, TopZ = 0.80f;
        const int Hair = 1, Face = 2, Glove = 5, Leg = 6;
        int toJersey = 0, toBibs = 0;
        for (int t = 0; t < lab.Length; t++)
        {
            int l = lab[t];
            // Helmet is included: the segmentation's helmet region (27k tris) reaches down onto the
            // shoulders, and those triangles drew black flecks on a white jersey. The real shell is
            // far above TopZ, so the z test only ever claims the strays.
            if (l != Helmet && l != Hair && l != Face && l != Glove && l != Leg) continue;
            float ax = Mathf.Abs(p[t].x), z = p[t].z;
            bool arm = ax >= ArmX;
            if (z < TopZ && ((!arm && z >= WaistZ) || (arm && z >= SleeveZ))) { lab[t] = Jersey; toJersey++; }
            // bibs: only leg triangles (hands hang beside the thighs in the rest pose)
            else if (l == Leg && !arm && z >= LegHemZ && z < WaistZ) { lab[t] = Shorts; toBibs++; }
        }
        Debug.Log($"[kitsplit] zone claim: {toJersey} stray triangles -> jersey, {toBibs} -> bibs");
    }

    static int TriCount(Mesh m) { int c = 0; for (int s = 0; s < m.subMeshCount; s++) c += (int)m.GetIndexCount(s) / 3; return c; }

    /// <summary>Majority vote over triangles sharing a vertex POSITION (the mesh is a soup, so
    /// index adjacency misses most neighbours). Only flips a triangle when a clear majority
    /// disagrees, which removes single-triangle specks without eroding real boundaries.</summary>
    static void Smooth(int[] lab, int[] tris, Vector3[] verts, int passes)
    {
        var byPos = new Dictionary<Vector3Int, List<int>>();
        Vector3Int Key(Vector3 v) => Vector3Int.RoundToInt(v * 2000f);   // 0.5 mm buckets
        for (int t = 0; t < lab.Length; t++)
            for (int k = 0; k < 3; k++)
            {
                var key = Key(verts[tris[t * 3 + k]]);
                if (!byPos.TryGetValue(key, out var l)) byPos[key] = l = new List<int>();
                l.Add(t);
            }
        var votes = new Dictionary<int, int>();
        for (int p = 0; p < passes; p++)
        {
            var next = (int[])lab.Clone();
            int flips = 0;
            for (int t = 0; t < lab.Length; t++)
            {
                votes.Clear(); int total = 0;
                for (int k = 0; k < 3; k++)
                    foreach (int o in byPos[Key(verts[tris[t * 3 + k]])])
                    {
                        if (o == t) continue;
                        votes[lab[o]] = votes.TryGetValue(lab[o], out int c) ? c + 1 : 1; total++;
                    }
                if (total == 0) continue;
                var top = votes.OrderByDescending(kv => kv.Value).First();
                if (top.Key != lab[t] && top.Value * 3 >= total * 2) { next[t] = top.Key; flips++; }
            }
            System.Array.Copy(next, lab, lab.Length);
            Debug.Log($"[kitsplit] smooth pass {p + 1}: {flips} triangles relabelled");
        }
    }

    static IEnumerable<(Matrix4x4, string)> Perms()
    {
        int[][] ps = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        foreach (var p in ps)
            for (int s = 0; s < 8; s++)
            {
                var m = Matrix4x4.zero; m.m33 = 1f;
                for (int r = 0; r < 3; r++) m[r, p[r]] = ((s >> r) & 1) == 0 ? 1f : -1f;
                yield return (m, $"perm{p[0]}{p[1]}{p[2]} signs{s}");
            }
    }

    static Matrix4x4 BoxFit(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
    {
        // uniform scale (height-driven) + translation that maps box a onto box b
        float sa = (aMax - aMin).magnitude, sb = (bMax - bMin).magnitude;
        float k = sa > 1e-6f ? sb / sa : 1f;
        var t = (bMin + bMax) * 0.5f - (aMin + aMax) * 0.5f * k;
        return Matrix4x4.TRS(t, Quaternion.identity, Vector3.one * k);
    }

    static Vector3 Min(Vector3[] a) { var m = a[0]; foreach (var v in a) m = Vector3.Min(m, v); return m; }
    static Vector3 Max(Vector3[] a) { var m = a[0]; foreach (var v in a) m = Vector3.Max(m, v); return m; }

    sealed class Grid
    {
        readonly Dictionary<Vector3Int, List<int>> _cells = new Dictionary<Vector3Int, List<int>>();
        readonly Vector3[] _p; readonly float _s;
        public Grid(Vector3[] p, float cell)
        {
            _p = p; _s = cell;
            for (int i = 0; i < p.Length; i++)
            {
                var k = Cell(p[i]);
                if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>();
                l.Add(i);
            }
        }
        Vector3Int Cell(Vector3 v) => Vector3Int.FloorToInt(v / _s);
        public float Nearest(Vector3 q, out int idx)
        {
            idx = 0; float best = float.MaxValue; var c = Cell(q);
            for (int r = 1; r <= 6; r++)
            {
                for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z)) != r && r > 1) continue;
                    if (!_cells.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var l)) continue;
                    foreach (int i in l) { float d = (_p[i] - q).sqrMagnitude; if (d < best) { best = d; idx = i; } }
                }
                if (best < (r * _s) * (r * _s)) break;
            }
            return best == float.MaxValue ? 1f : Mathf.Sqrt(best);
        }
    }
}
