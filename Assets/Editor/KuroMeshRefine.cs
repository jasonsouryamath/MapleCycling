using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Adds geometry to a skinned character mesh WITHOUT changing its rig, its baked grip or any
/// existing vertex: every edge longer than <c>maxEdge</c> is split, repeatedly, and the new
/// midpoints are lifted onto a PN (curved point-normal) surface so the added density rounds the
/// silhouette instead of just subdividing flat facets.
///
/// Why adaptive and not a uniform subdivision (measured on KuroNPC_KuroAnime_Rigged.glb,
/// 2026-09-26): 34.5k of its 41.5k vertices sit on the Head bone (hair + helmet). The body is
/// 1.4 tris/cm2 against the head's 3.7, with 13 mm median edges against 8 mm. A uniform level
/// would quadruple the already-dense hair; splitting only the long edges fills the sparse torso,
/// arms and legs up to the head's density.
///
/// Invariants the rest of the pipeline relies on:
///  * the first N output vertices ARE the input vertices, in order and unchanged;
///  * a triangle's children stay in the parent's submesh (garment splits survive);
///  * seams never crack: the split decision and the midpoint position depend only on the two
///    POSITIONS (welded ids), so the duplicated vertices on both sides of a UV seam agree;
///  * new vertices get interpolated UVs / normals / tangents / colours and the top-4 merge of
///    both endpoints' bone weights, renormalised.
/// Hard edges (coincident normals disagreeing) and open/non-manifold borders stay straight.
/// </summary>
public static class KuroMeshRefine
{
    /// <summary>Vertex budget. This project skins on the CPU (PlayerSettings.meshDeformation = CPU),
    /// whose cost scales with vertex count, so the refine stops here: 65k is ~1.57x the GLB, and the
    /// budget is spent on the LONGEST edges first (the sparse torso / arms / legs), not the dense hair.
    /// NOTE (2026-09-26): an 89.6k-vert / 147k-tri refine renders fine skinned. It only "vanished" in
    /// editor captures that swapped sharedMesh on an SMR already skinned in the same editor frame -
    /// capture on a fresh SkinnedMeshRenderer instead (see KuroDetailProbe.Capture).</summary>
    public static int MaxVerts = 65000;

    public static Mesh Refine(Mesh src, float maxEdge, int passes, float maxBulge, out string report)
    {
        report = "";
        if (src.blendShapeCount > 0)
        {
            report = $"skipped: '{src.name}' has {src.blendShapeCount} blend shapes (not interpolated)";
            return src;
        }

        var P = new List<Vector3>(src.vertices);
        var N = new List<Vector3>(src.normals);
        var T = new List<Vector4>(src.tangents);
        var C = new List<Color>(src.colors);
        var UV = new List<List<Vector4>>();
        for (int ch = 0; ch < 8; ch++)
        {
            var l = new List<Vector4>();
            src.GetUVs(ch, l);
            if (l.Count != src.vertexCount) break;
            UV.Add(l);
        }
        var BW = new List<BoneWeight>(src.boneWeights);
        bool hasN = N.Count == P.Count, hasT = T.Count == P.Count, hasC = C.Count == P.Count;
        bool hasW = BW.Count == P.Count;

        int subs = src.subMeshCount;
        var S = new List<int>[subs];
        for (int s = 0; s < subs; s++) S[s] = new List<int>(src.GetTriangles(s));

        int trisBefore = 0;
        foreach (var l in S) trisBefore += l.Count / 3;
        int vertsBefore = P.Count, splitTotal = 0;
        float budgetEdge = 0f;
        float eps = Mathf.Max(src.bounds.size.x, Mathf.Max(src.bounds.size.y, src.bounds.size.z)) * 1e-6f;

        for (int pass = 0; pass < passes; pass++)
        {
            // ---- weld by position; smooth normal and "hard" flag per welded id
            var idOf = new Dictionary<Vector3Int, int>();
            var pid = new int[P.Count];
            for (int v = 0; v < P.Count; v++)
            {
                var q = new Vector3Int(Mathf.RoundToInt(P[v].x / eps), Mathf.RoundToInt(P[v].y / eps),
                                       Mathf.RoundToInt(P[v].z / eps));
                if (!idOf.TryGetValue(q, out int id)) { id = idOf.Count; idOf[q] = id; }
                pid[v] = id;
            }
            int ids = idOf.Count;
            var nSum = new Vector3[ids];
            var firstN = new Vector3[ids];
            var hard = new bool[ids];
            var seen = new bool[ids];
            var pos = new Vector3[ids];
            for (int v = 0; v < P.Count; v++)
            {
                int id = pid[v];
                pos[id] = P[v];
                if (!hasN) continue;
                var nv = N[v].normalized;
                nSum[id] += nv;
                if (!seen[id]) { seen[id] = true; firstN[id] = nv; }
                else if (Vector3.Dot(firstN[id], nv) < 0.94f) hard[id] = true;   // > ~20 deg split
            }
            for (int i = 0; i < ids; i++) nSum[i] = nSum[i].sqrMagnitude > 1e-12f ? nSum[i].normalized : Vector3.up;

            // ---- manifold test per welded edge (borders / non-manifold edges stay straight)
            var edgeUse = new Dictionary<long, int>();
            foreach (var l in S)
                for (int t = 0; t < l.Count; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        long key = Key(pid[l[t + k]], pid[l[t + (k + 1) % 3]], ids);
                        edgeUse.TryGetValue(key, out int c); edgeUse[key] = c + 1;
                    }

            float max2 = maxEdge * maxEdge;
            var midByVerts = new Dictionary<long, int>();
            var midPos = new Dictionary<long, Vector3>();
            int splitThisPass = 0;

            int Mid(int a, int b)
            {
                long vk = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midByVerts.TryGetValue(vk, out int m)) return m;
                int pa = pid[a], pb = pid[b];
                long ek = Key(pa, pb, ids);
                if (!midPos.TryGetValue(ek, out var mp))
                {
                    // Always evaluate from the lower welded id, so both seam sides get the SAME floats.
                    int lo = Mathf.Min(pa, pb), hi = Mathf.Max(pa, pb);
                    Vector3 p1 = pos[lo], p2 = pos[hi], n1 = nSum[lo], n2 = nSum[hi];
                    mp = (p1 + p2) * 0.5f;
                    bool curved = hasN && !hard[lo] && !hard[hi] && Vector3.Dot(n1, n2) > 0.5f &&
                                  edgeUse.TryGetValue(ek, out int use) && use == 2;
                    if (curved)
                    {
                        float w12 = Vector3.Dot(p2 - p1, n1), w21 = Vector3.Dot(p1 - p2, n2);
                        var d = -(w12 * n1 + w21 * n2) / 8f;
                        float lim = maxBulge * (p2 - p1).magnitude;
                        if (d.magnitude > lim) d = d.normalized * lim;
                        mp += d;
                    }
                    midPos[ek] = mp;
                    splitThisPass++;
                }
                m = P.Count;
                P.Add(mp);
                if (hasN) { var nn = N[a] + N[b]; N.Add(nn.sqrMagnitude > 1e-12f ? nn.normalized : N[a]); }
                if (hasT)
                {
                    var ta = T[a]; var tb = T[b];
                    var tv = new Vector3(ta.x + tb.x, ta.y + tb.y, ta.z + tb.z);
                    if (tv.sqrMagnitude < 1e-12f) tv = new Vector3(ta.x, ta.y, ta.z);
                    tv.Normalize();
                    T.Add(new Vector4(tv.x, tv.y, tv.z, ta.w));
                }
                if (hasC) C.Add(Color.Lerp(C[a], C[b], 0.5f));
                foreach (var l in UV) l.Add((l[a] + l[b]) * 0.5f);
                if (hasW) BW.Add(Merge(BW[a], BW[b]));
                midByVerts[vk] = m;
                return m;
            }

            // Budget: raise this pass's threshold (globally, so shared edges still split on both
            // sides and seams never crack) until the mesh stays <= MaxVerts.
            var pairSet = new HashSet<long>();
            bool Over(float thr2)
            {
                pairSet.Clear();
                foreach (var l in S)
                    for (int t = 0; t < l.Count; t += 3)
                        for (int k = 0; k < 3; k++)
                        {
                            int a = l[t + k], b = l[t + (k + 1) % 3];
                            if ((P[a] - P[b]).sqrMagnitude > thr2)
                                pairSet.Add(a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a);
                        }
                return P.Count + pairSet.Count > MaxVerts;
            }
            if (Over(max2))
            {
                float lo = maxEdge, hi = maxEdge * 8f;
                while (Over(hi * hi) && hi < 10f) hi *= 2f;
                for (int it = 0; it < 24; it++)
                {
                    float midT = (lo + hi) * 0.5f;
                    if (Over(midT * midT)) lo = midT; else hi = midT;
                }
                max2 = hi * hi;
                budgetEdge = Mathf.Max(budgetEdge, hi);
            }
            bool Long(int a, int b) => (P[a] - P[b]).sqrMagnitude > max2;

            for (int s = 0; s < subs; s++)
            {
                var src3 = S[s];
                var dst = new List<int>(src3.Count * 2);
                for (int t = 0; t < src3.Count; t += 3)
                {
                    int v0 = src3[t], v1 = src3[t + 1], v2 = src3[t + 2];
                    int mask = (Long(v0, v1) ? 1 : 0) | (Long(v1, v2) ? 2 : 0) | (Long(v2, v0) ? 4 : 0);
                    if (mask == 0) { dst.Add(v0); dst.Add(v1); dst.Add(v2); continue; }
                    if (mask == 7)
                    {
                        int m0 = Mid(v0, v1), m1 = Mid(v1, v2), m2 = Mid(v2, v0);
                        Tri(dst, v0, m0, m2); Tri(dst, m0, v1, m1); Tri(dst, m2, m1, v2); Tri(dst, m0, m1, m2);
                        continue;
                    }
                    // rotate so the pattern is canonical, keeping winding
                    int a = v0, b = v1, c = v2;
                    bool one = mask == 1 || mask == 2 || mask == 4;
                    if (one)
                    {
                        if (mask == 2) { a = v1; b = v2; c = v0; }
                        else if (mask == 4) { a = v2; b = v0; c = v1; }
                        int m = Mid(a, b);
                        Tri(dst, a, m, c); Tri(dst, m, b, c);
                    }
                    else
                    {
                        // two long edges: rotate so the SHORT edge is (c, a)
                        if (mask == 6) { a = v1; b = v2; c = v0; }        // short = e0 (v0,v1)
                        else if (mask == 5) { a = v2; b = v0; c = v1; }   // short = e1 (v1,v2)
                        int m0 = Mid(a, b), m1 = Mid(b, c);
                        Tri(dst, m0, b, m1);
                        if ((P[a] - P[m1]).sqrMagnitude < (P[m0] - P[c]).sqrMagnitude)
                        { Tri(dst, a, m0, m1); Tri(dst, a, m1, c); }
                        else
                        { Tri(dst, a, m0, c); Tri(dst, m0, m1, c); }
                    }
                }
                S[s] = dst;
            }
            splitTotal += splitThisPass;
            if (splitThisPass == 0) break;
        }

        var outMesh = new Mesh { name = src.name, indexFormat = IndexFormat.UInt32 };
        outMesh.SetVertices(P);
        if (hasN) outMesh.SetNormals(N);
        if (hasT) outMesh.SetTangents(T);
        if (hasC) outMesh.SetColors(C);
        for (int ch = 0; ch < UV.Count; ch++) outMesh.SetUVs(ch, UV[ch]);
        if (hasW) outMesh.boneWeights = BW.ToArray();
        outMesh.bindposes = src.bindposes;
        outMesh.subMeshCount = subs;
        int trisAfter = 0;
        for (int s = 0; s < subs; s++) { outMesh.SetTriangles(S[s], s, false); trisAfter += S[s].Count / 3; }
        outMesh.RecalculateBounds();

        report = $"'{src.name}': {vertsBefore} -> {P.Count} verts, {trisBefore} -> {trisAfter} tris " +
                 $"(x{(float)trisAfter / Mathf.Max(1, trisBefore):0.00}), {splitTotal} edges split, " +
                 $"maxEdge {maxEdge * 1000f:0.0} mm-units, {passes} passes" +
                 (budgetEdge > 0f ? $" (vertex budget {MaxVerts} raised it to {budgetEdge * 1000f:0.0})" : "");
        int worstSub = 0;
        for (int s = 0; s < subs; s++) worstSub = Mathf.Max(worstSub, S[s].Count / 3);
        report += $", largest submesh {worstSub} tris";
        return outMesh;
    }

    static long Key(int a, int b, int n) => a < b ? (long)a * n + b : (long)b * n + a;

    static void Tri(List<int> l, int a, int b, int c) { l.Add(a); l.Add(b); l.Add(c); }

    static BoneWeight Merge(BoneWeight a, BoneWeight b)
    {
        var acc = new Dictionary<int, float>(8);
        void Add(int i, float w) { if (w <= 0f) return; acc.TryGetValue(i, out float c); acc[i] = c + w * 0.5f; }
        Add(a.boneIndex0, a.weight0); Add(a.boneIndex1, a.weight1); Add(a.boneIndex2, a.weight2); Add(a.boneIndex3, a.weight3);
        Add(b.boneIndex0, b.weight0); Add(b.boneIndex1, b.weight1); Add(b.boneIndex2, b.weight2); Add(b.boneIndex3, b.weight3);
        var top = new List<KeyValuePair<int, float>>(acc);
        top.Sort((x, y) => y.Value.CompareTo(x.Value));
        float sum = 0f;
        for (int i = 0; i < top.Count && i < 4; i++) sum += top[i].Value;
        if (sum <= 0f) return a;
        var r = new BoneWeight();
        if (top.Count > 0) { r.boneIndex0 = top[0].Key; r.weight0 = top[0].Value / sum; }
        if (top.Count > 1) { r.boneIndex1 = top[1].Key; r.weight1 = top[1].Value / sum; }
        if (top.Count > 2) { r.boneIndex2 = top[2].Key; r.weight2 = top[2].Value / sum; }
        if (top.Count > 3) { r.boneIndex3 = top[3].Key; r.weight3 = top[3].Value / sum; }
        return r;
    }
}
