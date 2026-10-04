using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// FUJI RIDGE - copilot session 2 (2026-09-26), region queue F1/F2/F4/F5.
///
/// F1 PEOPLE LOD. The ~485 cloned Minato crowd figures kept MapleCityLife's LODGroup: the live
/// skinned LOD0 (shadow-casting) out to ~110 m and the static LOD1 (also shadow-casting) out to
/// ~330 m, about 50M tris in the root. MinatoCrowdActor.OnEnable re-imposes those thresholds on
/// its own LODGroup in play mode (ApplyDimensionalLodPolicy, a file this region must not edit), so
/// the clone's own LODGroup is REMOVED and a wrapper parent carries the Fuji LODGroup instead:
///   LOD0 live skin + hair cap + hat + contact shadow, shadows ON   ..  PeopleSkinnedM
///   LOD1 static bake, shadows OFF                                   ..  PeopleStaticM
///   LOD2 vertex-clustered proxy of the bake (same look materials)    ..  PeopleCullM, then culled.
///
/// F2 PAVEMENT. Continuous strips (PavementStrip) and a height lookup (PaveY) so every town prop
/// stands on the sloping pavement / piazza instead of on its house's flat station height.
///
/// F4 HATS. The old lookup took the first "Head" transform and the bounds of every renderer under
/// it, and rejected any radius outside 0.05-0.2 m; chibi heads + the hair cap exceed 0.2 m. The
/// head is now the Head bone of the live skin (as MapleCityLife does) and its size comes from the
/// head-weighted vertices of the baked skin plus the hair cap.
///
/// F5 RIFUGIO RIDERS. The six roster riders' own bikes (rider body stripped) lean on a hitching
/// rail; a dismounted cyclist (a Minato donor in its own kit and helmet, materials cloned into
/// Fuji-owned CelLit copies) stands beside each bike.
///
/// Every number below is PROVISIONAL art/perf tuning, not a requirement.
/// </summary>
public static partial class FujiRidgeEnvironment
{
    // ---- F1 tunables (PROVISIONAL) --------------------------------------------------------------
    /// <summary>Live skinned figure (and its shadow) only this close to the camera, metres.</summary>
    private const float PeopleSkinnedM = 25f;
    /// <summary>Static baked figure, shadowless, out to this distance, metres.</summary>
    private const float PeopleStaticM = 55f;
    /// <summary>Clustered proxy out to this distance; beyond it people are culled, metres.</summary>
    private const float PeopleCullM = 120f;
    /// <summary>Vertex-cluster cell for the far proxy, metres (figures are ~1.3 m tall).</summary>
    private const float PeopleProxyCellM = 0.055f;
    /// <summary>Unity LOD relative height = size / (distance * 2 tan(fov/2)); fov 60 deg.</summary>
    private const float LodFovK = 1.1547f;

    // ---- F2 tunables (PROVISIONAL) --------------------------------------------------------------
    /// <summary>Pavement kerb line, metres from the centreline: the carriageway edge + 5 cm.</summary>
    private const float KerbOffsetM = RoadHalfWidth + 0.05f;
    /// <summary>Pavement outer edge, under the house fronts (facade line is WalkTopOffset).</summary>
    private const float PaveOuterM = WalkTopOffset + 1.2f;
    /// <summary>Kerb height above the station height (PavementY uses the same 0.12 m).</summary>
    private const float PaveTopM = 0.12f;
    private const float KerbStoneWidthM = 0.3f;
    private const float PaveSkirtM = 1.8f;
    private const float PiazzaHalfLenM = 38f;
    private const float PiazzaDepthM = CorridorClearM + 31.5f;
    /// <summary>Piazza paving sits this far above the pavement where the two overlap.</summary>
    private const float PiazzaLiftM = 0.02f;

    // ---- F5 tunables (PROVISIONAL) --------------------------------------------------------------
    private const float RifugioBikeLineM = 6.85f;   // from the deck centre toward the road
    private const float RifugioBikeLeanDeg = 11f;
    private const float RifugioStandOffsetM = 0.85f; // rider stands this far from the bike, deck side
    private const float RifugioBikeCullM = 160f;

    private static Vector3 _rifugioSf, _rifugioFwd;
    private static float _rifugioTop;

    // =================================================================== F2 pavement

    /// <summary>Pavement height at <paramref name="q"/>: the route height interpolated along the
    /// nearest segment, + PaveTopM + <paramref name="lift"/>. Matches PavementStrip's surface.</summary>
    private static float PaveY(FujiRoute route, Vector3 q, float lift, bool overTerrain = false)
    {
        if (overTerrain) return Mathf.Max(PaveY(route, q, lift), GroundMeshY(route, q) + PiazzaOverTerrainM);
        int i = NearestStation(q.x, q.z, out _);
        if (i < 0) return q.y;
        float bestErr = float.MaxValue, y = route.Position[i].y;
        for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(i, route.Count - 2); j++)
        {
            var a = route.Position[j]; var b = route.Position[j + 1];
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            var aq = new Vector2(q.x - a.x, q.z - a.z);
            float u = Vector2.Dot(aq, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f);
            float err = u < 0f ? -u : (u > 1f ? u - 1f : 0f);
            if (err < bestErr) { bestErr = err; y = Mathf.Lerp(a.y, b.y, Mathf.Clamp01(u)); }
        }
        return y + PaveTopM + lift;
    }
    /// <summary>The piazza paving never dips under the hillside: it rides this far above the terrain.</summary>
    private const float PiazzaOverTerrainM = 0.05f;
    private static readonly float[] CorridorNodesM =
        { -90f, -70f, -55f, -46f, -34f, -24f, -16f, -10f, -5.2f, 0f, 5.2f, 10f, 16f, 24f, 34f, 46f, 55f, 70f, 90f };

    /// <summary>
    /// Height of the RENDERED ground (the "Fuji Verge" corridor mesh, BuildCorridor: a row every
    /// CorridorStride stations, columns at CorridorNodesM, triangles a-d-b / b-d-e) at q. The
    /// paving has to clear the mesh, not Height(): between the 16/24/34 m columns the mesh chord
    /// sits well above the curved Height() on the hillside, which showed as dark terrain stripes
    /// through the piazza. Falls back to Height() outside the ribbon.
    /// </summary>
    private static float GroundMeshY(FujiRoute route, Vector3 q)
    {
        int i = NearestStation(q.x, q.z, out _);
        if (i < 0) return Height(q.x, q.z);
        int S = CorridorStride, last = route.Count - 1;
        int r = (i / S) * S;
        Vector3 Node(int row, int c)
        {
            var p = route.Position[row]; var s = route.SideFlat(row); float o = CorridorNodesM[c];
            float px = p.x + s.x * o, pz = p.z + s.z * o;
            return new Vector3(px, Height(px, pz) + 0.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 55f, Mathf.Abs(o))), pz);
        }
        bool InTri(Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-6f) return false;
            float l1 = ((b.z - c.z) * (q.x - c.x) + (c.x - b.x) * (q.z - c.z)) / d;
            float l2 = ((c.z - a.z) * (q.x - c.x) + (a.x - c.x) * (q.z - c.z)) / d;
            float l3 = 1f - l1 - l2;
            const float Eps = -1e-3f;
            if (l1 < Eps || l2 < Eps || l3 < Eps) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }
        foreach (int ra in new[] { r, r - S, r + S, r - 2 * S })
        {
            if (ra < 0 || ra >= last) continue;
            int rb = Mathf.Min(ra + S, last);
            for (int c = 0; c < CorridorNodesM.Length - 1; c++)
            {
                Vector3 a = Node(ra, c), b = Node(ra, c + 1), d = Node(rb, c), e = Node(rb, c + 1);
                if (InTri(a, d, b, out float y) || InTri(b, d, e, out y)) return y;
            }
        }
        return Height(q.x, q.z);
    }

    /// <summary>
    /// A continuous paved strip on side <paramref name="s"/> from station i0 to i1, between the
    /// lateral offsets inner..outer: top follows the station heights (no steps), consecutive
    /// segments share their station edges (no wedge gaps on bends). Optional kerb stone band
    /// and a kerb face down past the road surface; a skirt on the outer edge and both ends.
    /// </summary>
    private static void PavementStrip(PMesh m, FujiRoute route, int i0, int i1, int s, float inner, float outer,
                                      float lift, int row, float br, float topBr, bool kerb, bool overTerrain = false)
    {
        Vector3 At(int i, float off, float dy)
        {
            var p = route.Position[i] + route.SideFlat(i) * (s * off);
            p.y = route.Position[i].y + PaveTopM + lift;
            if (overTerrain) p.y = Mathf.Max(p.y, GroundMeshY(route, p) + PiazzaOverTerrainM);
            p.y += dy;
            return p;
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float bright)
        {
            int ia = m.Vert(a, row, bright), ib = m.Vert(b, row, bright), ic = m.Vert(c, row, bright), id = m.Vert(d, row, bright);
            m.Tri(ia, ib, ic, n); m.Tri(ia, ic, id, n);
        }
        float kerbOuter = kerb ? inner + KerbStoneWidthM : inner;
        float skirt = PaveSkirtM + (kerb ? 0f : 0.6f);
        for (int i = i0; i < i1; i++)
        {
            var sf0 = route.SideFlat(i) * s;
            var sf1 = route.SideFlat(i + 1) * s;
            var sfm = (sf0 + sf1).normalized;
            if (kerb)
            {
                Quad(At(i, inner, 0f), At(i, kerbOuter, 0f), At(i + 1, kerbOuter, 0f), At(i + 1, inner, 0f), Vector3.up, 0.86f);
                // Kerb face down past the (crowned, banked) road surface.
                Quad(At(i, inner, 0f), At(i + 1, inner, 0f), At(i + 1, inner, -PaveTopM - 0.3f), At(i, inner, -PaveTopM - 0.3f), -sfm, br * 0.9f);
            }
            else
            {
                Quad(At(i, inner, 0f), At(i + 1, inner, 0f), At(i + 1, inner, -skirt), At(i, inner, -skirt), -sfm, br * 0.8f);
            }
            // Paving, split into 1.2 m-ish courses across so it reads as slabs, not one sheet.
            int courses = Mathf.Max(1, Mathf.RoundToInt((outer - kerbOuter) / 1.2f));
            for (int k = 0; k < courses; k++)
            {
                float o0 = Mathf.Lerp(kerbOuter, outer, k / (float)courses), o1 = Mathf.Lerp(kerbOuter, outer, (k + 1) / (float)courses);
                float b = topBr * (((i + k) & 1) == 0 ? 1f : 0.93f);
                Quad(At(i, o0, 0f), At(i, o1, 0f), At(i + 1, o1, 0f), At(i + 1, o0, 0f), Vector3.up, b);
            }
            Quad(At(i, outer, 0f), At(i + 1, outer, 0f), At(i + 1, outer, -skirt), At(i, outer, -skirt), sfm, br * 0.8f);
        }
        // End caps.
        foreach (var (i, dir) in new[] { (i0, -1f), (i1, 1f) })
        {
            var t = route.Tangent[i]; t.y = 0f; t = t.normalized * dir;
            Quad(At(i, inner, 0f), At(i, outer, 0f), At(i, outer, -skirt), At(i, inner, -skirt), t, br * 0.8f);
        }
    }

    // =================================================================== mesh assets

    /// <summary>Writes a simple (non-skinned) mesh, overwriting an existing asset IN PLACE so its
    /// GUID survives (the 15:02 COORDINATION rule: never DeleteAsset+CreateAsset a shared asset).</summary>
    private static Mesh SaveMeshInPlace(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        if (existing == mesh) return existing;
        string keep = existing.name;
        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.SetVertices(mesh.vertices);
        existing.SetNormals(mesh.normals);
        var uv = new List<Vector2>(); mesh.GetUVs(0, uv); existing.SetUVs(0, uv);
        existing.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++)
            existing.SetTriangles(mesh.GetTriangles(s), s, false);
        existing.RecalculateBounds();
        existing.name = keep;
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        return existing;
    }

    private static string San(string n)
    {
        var chars = n.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
        return new string(chars);
    }

    // =================================================================== F1 people LOD

    /// <summary>A vertex-clustered copy of a static figure bake: same submeshes (so the clone's
    /// look materials apply unchanged), a fraction of the triangles. Cached per source mesh.</summary>
    private static Mesh ClusterProxy(Mesh src, Dictionary<Mesh, Mesh> cache)
    {
        if (cache.TryGetValue(src, out var hit)) return hit;
        cache[src] = null;
        if (!src.isReadable) { Debug.LogWarning($"[fuji] F1 proxy: {src.name} is not readable - no LOD2."); return null; }
        var v = src.vertices;
        var uv = src.uv;
        bool hasUv = uv != null && uv.Length == v.Length;
        var map = new int[v.Length];
        var dict = new Dictionary<Vector3Int, int>();
        var sums = new List<Vector3>(); var counts = new List<int>(); var nuv = new List<Vector2>();
        for (int i = 0; i < v.Length; i++)
        {
            var key = new Vector3Int(Mathf.FloorToInt(v[i].x / PeopleProxyCellM), Mathf.FloorToInt(v[i].y / PeopleProxyCellM),
                                     Mathf.FloorToInt(v[i].z / PeopleProxyCellM));
            if (!dict.TryGetValue(key, out int idx))
            {
                idx = sums.Count; dict[key] = idx;
                sums.Add(Vector3.zero); counts.Add(0); nuv.Add(hasUv ? uv[i] : Vector2.zero);
            }
            sums[idx] += v[i]; counts[idx]++;
            map[i] = idx;
        }
        var nv = new List<Vector3>(sums.Count);
        for (int i = 0; i < sums.Count; i++) nv.Add(sums[i] / counts[i]);
        var mesh = new Mesh { name = $"FujiPeople_Proxy_{San(src.name)}" };
        mesh.indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(nv);
        mesh.SetUVs(0, nuv);
        mesh.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            var t = src.GetTriangles(s);
            var outT = new List<int>(t.Length / 3);
            var seen = new HashSet<(int, int, int)>();
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                int a = map[t[k]], b = map[t[k + 1]], c = map[t[k + 2]];
                if (a == b || b == c || a == c) continue;
                int lo = Mathf.Min(a, Mathf.Min(b, c)), hi = Mathf.Max(a, Mathf.Max(b, c)), mid = a + b + c - lo - hi;
                if (!seen.Add((lo, mid, hi))) continue;
                outT.Add(a); outT.Add(b); outT.Add(c);
            }
            mesh.SetTriangles(outT, s, false);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh = SaveMeshInPlace(mesh, $"{MeshDir}/{mesh.name}.asset");
        cache[src] = mesh;
        return mesh;
    }

    private static long Tris(Renderer r)
    {
        Mesh m = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        if (m == null) return 0;
        long t = 0;
        for (int s = 0; s < m.subMeshCount; s++) t += (long)m.GetIndexCount(s) / 3;
        return t;
    }

    private static float LodHeight(float size, float metres) => Mathf.Clamp(size / (metres * LodFovK), 0.0005f, 0.98f);

    /// <summary>Moves each figure under a wrapper whose LODGroup implements the Fuji tiers.</summary>
    private static string WrapPeopleLods(Transform life, List<GameObject> people)
    {
        var cache = new Dictionary<Mesh, Mesh>();
        int wrapped = 0, proxies = 0, noLow = 0;
        long hiT = 0, loT = 0, pxT = 0;
        string sample = "";
        foreach (var fig in people)
        {
            if (fig == null) continue;
            var high = fig.transform.Find("LOD0 High Skinned");
            var low = fig.transform.Find("LOD1 Low 3D");
            var hi = high != null ? high.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            var lo = low != null ? low.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            var px = new List<Renderer>();
            foreach (var r in lo)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var pm = ClusterProxy(mf.sharedMesh, cache);
                if (pm == null) continue;
                var g = new GameObject("LOD2 Proxy", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(r.transform, false);
                g.GetComponent<MeshFilter>().sharedMesh = pm;
                var mr = g.GetComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = r.receiveShadows;
                mr.lightProbeUsage = r.lightProbeUsage;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                px.Add(mr);
                proxies++;
            }
            if (lo.Length == 0) noLow++;

            var old = fig.GetComponent<LODGroup>();
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var w = new GameObject($"Fuji Person LOD {wrapped}").transform;
            w.SetParent(life, false);
            w.position = fig.transform.position;
            w.rotation = Quaternion.identity;
            fig.transform.SetParent(w, true);
            var grp = w.gameObject.AddComponent<LODGroup>();
            grp.fadeMode = LODFadeMode.None;
            grp.animateCrossFading = false;
            // Provisional LODs first so RecalculateBounds sees every renderer, then the real heights.
            var all = hi.Concat(lo).Concat(px).ToArray();
            grp.SetLODs(new[] { new LOD(0.5f, all) });
            grp.RecalculateBounds();
            float size = Mathf.Max(grp.size, 0.5f);
            var lods = new List<LOD>();
            if (hi.Length > 0) lods.Add(new LOD(LodHeight(size, PeopleSkinnedM), hi));
            if (lo.Length > 0) lods.Add(new LOD(LodHeight(size, px.Count > 0 ? PeopleStaticM : PeopleCullM), lo));
            if (px.Count > 0) lods.Add(new LOD(LodHeight(size, PeopleCullM), px.ToArray()));
            if (lods.Count == 1) { var l = lods[0]; l.screenRelativeTransitionHeight = LodHeight(size, PeopleCullM); lods[0] = l; }
            if (lods.Count > 0) grp.SetLODs(lods.ToArray());
            grp.RecalculateBounds();
            foreach (var r in hi) hiT += Tris(r);
            foreach (var r in lo) loT += Tris(r);
            foreach (var r in px) pxT += Tris(r);
            if (wrapped == 0)
                sample = $"first '{fig.name}': size {size:0.00} m, LOD0 {hi.Length} r/{hi.Sum(Tris)} tris @h{(lods.Count > 0 ? lods[0].screenRelativeTransitionHeight : 0f):0.0000}, " +
                         $"LOD1 {lo.Length} r/{lo.Sum(Tris)} tris, LOD2 {px.Count} r/{px.Sum(Tris)} tris";
            wrapped++;
        }
        return $"{wrapped} figures wrapped ({noLow} without LOD1), {proxies} proxy renderers, {cache.Count(k => k.Value != null)} proxy meshes; " +
               $"tris LOD0 {hiT}, LOD1 {loT}, LOD2 {pxT}; tiers skinned<{PeopleSkinnedM:0} m (shadows), static<{PeopleStaticM:0} m, " +
               $"proxy<{PeopleCullM:0} m, culled beyond. {sample}";
    }

    // =================================================================== F4 hats

    /// <summary>Hat crown radius as a share of the head half-width (PROVISIONAL look tuning).</summary>
    private const float HatFitK = 0.92f;
    private static bool _bonesLogged;
    private static int _hatDiag;
    private static readonly Dictionary<string, int> _hatFail = new Dictionary<string, int>();

    private static SkinnedMeshRenderer LiveSkin(GameObject fig)
    {
        var high = fig.transform.Find("LOD0 High Skinned");
        if (high == null) return null;
        SkinnedMeshRenderer any = null;
        foreach (var s in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (s.name == "Mesh_0") return s;
            if (any == null && s.bones != null && s.bones.Length > 0) any = s;
        }
        return any;
    }

    private static void LogBoneNames(GameObject fig)
    {
        if (_bonesLogged) return;
        _bonesLogged = true;
        var smr = LiveSkin(fig);
        if (smr == null) { Debug.Log($"[fuji] F4 bones: '{fig.name}' has no live skin under 'LOD0 High Skinned'."); return; }
        var names = smr.bones.Select(b => b == null ? "<null>" : b.name).ToArray();
        int head = Array.FindIndex(smr.bones, b => b != null && b.name == "Head");
        var hb = head >= 0 ? smr.bones[head] : null;
        string kids = hb == null ? "-" : string.Join(",", hb.Cast<Transform>().Select(t => t.name));
        Debug.Log($"[fuji] F4 bones of '{fig.name}' (skin '{smr.name}', mesh '{smr.sharedMesh?.name}', readable {smr.sharedMesh?.isReadable}): " +
                  $"{names.Length} bones, Head index {head}, Head children [{kids}]: {string.Join(" ", names)}");
    }

    private static void HatFail(string why) => _hatFail[why] = _hatFail.TryGetValue(why, out int n) ? n + 1 : 1;

    /// <summary>Puts a hat on the figure's Head bone, sized from the head-weighted skin + hair cap.</summary>
    private static bool AddHat(GameObject fig, Mesh hat)
    {
        var smr = LiveSkin(fig);
        if (smr == null || smr.sharedMesh == null) { HatFail("no live skin"); return false; }
        var bones = smr.bones;
        int head = Array.FindIndex(bones, b => b != null && b.name == "Head");
        if (head < 0) head = Array.FindIndex(bones, b => b != null && b.name.EndsWith("Head", StringComparison.OrdinalIgnoreCase));
        if (head < 0) { HatFail("no Head bone"); return false; }
        var hb = bones[head];

        Bounds b = default; bool any = false;
        void Add(Vector3 p) { if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
        var src = smr.sharedMesh;
        if (src.isReadable)
        {
            var bw = src.boneWeights;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            if (bw.Length == verts.Length)
                for (int i = 0; i < verts.Length; i++)
                    if (bw[i].boneIndex0 == head && bw[i].weight0 >= 0.5f) Add(mtx.MultiplyPoint3x4(verts[i]));
            UnityEngine.Object.DestroyImmediate(baked);
        }
        var cap = hb.Find("Hair Cap");
        if (cap != null && cap.TryGetComponent<Renderer>(out var capR)) { Add(capR.bounds.min); Add(capR.bounds.max); }
        if (!any) { HatFail("no head verts/cap"); return false; }
        float rad = Mathf.Min(b.extents.x, b.extents.z) * HatFitK;   // world AABB of a yawed head: min axis ~ skull width
        if (_hatDiag++ < 3)
            Debug.Log($"[fuji] F4 hat '{fig.name}': head bounds {b.size.x:0.000}x{b.size.y:0.000}x{b.size.z:0.000} m, radius {rad:0.000}, cap {(cap != null)}.");
        if (rad < 0.04f || rad > 0.5f) { HatFail($"radius {rad:0.00} out of range"); return false; }

        var go = new GameObject("Fuji Hat", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(hb, true);
        go.transform.position = new Vector3(b.center.x, b.max.y - rad * 0.7f, b.center.z);
        go.transform.rotation = Quaternion.Euler(0f, fig.transform.eulerAngles.y, 0f);
        go.transform.localScale = Vector3.one;
        var ls = go.transform.lossyScale;
        go.transform.localScale = new Vector3(rad / Mathf.Max(ls.x, 1e-4f), rad / Mathf.Max(ls.y, 1e-4f), rad / Mathf.Max(ls.z, 1e-4f));
        go.GetComponent<MeshFilter>().sharedMesh = hat;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _pilgrimMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Under 'LOD0 High Skinned' (via the Head bone), so WrapPeopleLods puts it in LOD0.
        return true;
    }

    private static string HatFailures() => _hatFail.Count == 0 ? "none" : string.Join(", ", _hatFail.Select(kv => $"{kv.Key} x{kv.Value}"));

    // =================================================================== F5 rifugio riders

    private static readonly Dictionary<string, Material> _rifugioMats = new Dictionary<string, Material>();

    /// <summary>Fuji-owned CelLit copy of a donor's raw glTF LOD0 material (overwritten in place).</summary>
    private static Material RifugioMat(Material src)
    {
        if (_rifugioMats.TryGetValue(src.name, out var hit) && hit != null) return hit;
        string path = $"{MaterialDir}/FujiRifugio_{San(src.name)}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(src); AssetDatabase.CreateAsset(mat, path); }
        else { mat.shader = src.shader; mat.CopyPropertiesFromMaterial(src); }
        mat.name = $"FujiRifugio_{San(src.name)}";
        NpcCelLitConversion.ConvertInPlace(mat);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        _rifugioMats[src.name] = mat;
        return mat;
    }

    /// <summary>Removes the rider's body (skinned meshes and anything attached to the skeleton),
    /// keeping the bike. Returns (removed, kept) renderer counts; does nothing if no bike would remain.</summary>
    private static (int removed, int kept) StripRiderBody(GameObject go)
    {
        var bones = new HashSet<Transform>();
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.rootBone != null) bones.Add(smr.rootBone);
            foreach (var b in smr.bones) if (b != null) bones.Add(b);
        }
        var body = new List<Renderer>(); var keep = new List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            bool isBody = r is SkinnedMeshRenderer;
            for (var t = r.transform; !isBody && t != null && t != go.transform; t = t.parent)
                if (bones.Contains(t)) isBody = true;
            (isBody ? body : keep).Add(r);
        }
        if (keep.Count < 4) return (0, keep.Count);
        foreach (var r in body) UnityEngine.Object.DestroyImmediate(r);
        return (body.Count, keep.Count);
    }

    private static int BuildRifugioRiders(Transform life, List<MinatoCrowdActor> pool, List<GameObject> people, System.Random rnd)
    {
        var roster = GameObject.Find("Fuji NPCs");
        if (roster == null || roster.transform.childCount == 0 || _riderSpots.Count == 0)
        { Debug.LogWarning("[fuji] F5: no 'Fuji NPCs' roster - rifugio has no parked riders."); return 0; }
        var rails = new PMesh();
        var leanDir = -_rifugioSf;
        int n = 0, standers = 0;
        foreach (var (p, r) in _riderSpots)
        {
            var src = roster.transform.GetChild(n % roster.transform.childCount).gameObject;
            var bike = UnityEngine.Object.Instantiate(src, life);
            bike.name = $"Fuji Rifugio Bike {n}";
            foreach (var mb in bike.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            foreach (var an in bike.GetComponentsInChildren<Animator>(true)) an.enabled = false;
            var (removed, kept) = StripRiderBody(bike);
            if (n == 0)
                Debug.Log($"[fuji] F5 bike from '{src.name}': {removed} body renderers removed, {kept} bike renderers kept; children [" +
                          string.Join(",", bike.transform.Cast<Transform>().Select(t => t.name)) + "]");
            if (removed == 0) { UnityEngine.Object.DestroyImmediate(bike); n++; continue; }

            // Lean about the tyre contact line (the root sits on the course line) toward the rail.
            bike.transform.SetPositionAndRotation(p, Quaternion.AngleAxis(RifugioBikeLeanDeg, Vector3.Cross(Vector3.up, leanDir)) * r);
            var mfs = bike.GetComponentsInChildren<MeshFilter>(false).Where(f => f.sharedMesh != null && f.sharedMesh.isReadable &&
                                                                             f.GetComponent<Renderer>() != null).ToList();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var f in mfs)
            {
                var mtx = f.transform.localToWorldMatrix;
                foreach (var v in f.sharedMesh.vertices) { float y = mtx.MultiplyPoint3x4(v).y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            }
            if (minY == float.MaxValue)
                foreach (var rr in bike.GetComponentsInChildren<Renderer>(false)) { minY = Mathf.Min(minY, rr.bounds.min.y); maxY = Mathf.Max(maxY, rr.bounds.max.y); }
            bike.transform.position += Vector3.up * (_rifugioTop - minY);
            maxY += _rifugioTop - minY; minY = _rifugioTop;
            // Rail touches the bike's outermost point in its upper third (bars / saddle).
            float ext = float.MinValue, extY = minY + (maxY - minY) * 0.85f;
            float cut = minY + (maxY - minY) * 0.6f;
            foreach (var f in mfs)
            {
                var mtx = f.transform.localToWorldMatrix;
                foreach (var v in f.sharedMesh.vertices)
                {
                    var w = mtx.MultiplyPoint3x4(v);
                    if (w.y < cut) continue;
                    float d = Vector3.Dot(w - p, leanDir);
                    if (d > ext) { ext = d; extY = w.y; }
                }
            }
            if (ext == float.MinValue) ext = (maxY - minY) * Mathf.Sin(RifugioBikeLeanDeg * Mathf.Deg2Rad) + 0.25f;
            const float Bar = 0.035f;
            var railC = new Vector3(p.x, 0f, p.z) + leanDir * (ext + Bar);
            railC.y = extY;
            rails.OBox(railC, _rifugioFwd * 0.95f, Vector3.up * Bar, leanDir * Bar, PTimber, 0.5f, 0.6f);
            for (int e = -1; e <= 1; e += 2)
            {
                var post = railC + _rifugioFwd * (e * 0.88f);
                float h = post.y + Bar - _rifugioTop;
                rails.OBox(new Vector3(post.x, _rifugioTop + h * 0.5f - 0.1f, post.z), _rifugioFwd * 0.05f, Vector3.up * (h * 0.5f + 0.1f), leanDir * 0.05f, PTimber, 0.42f);
            }

            // LOD: one level, culled beyond RifugioBikeCullM (118-part bikes).
            var wrap = new GameObject($"Fuji Rifugio Bike LOD {n}").transform;
            wrap.SetParent(life, false); wrap.position = bike.transform.position;
            bike.transform.SetParent(wrap, true);
            var rs = bike.GetComponentsInChildren<Renderer>(false);
            var g = wrap.gameObject.AddComponent<LODGroup>();
            g.SetLODs(new[] { new LOD(0.5f, rs) }); g.RecalculateBounds();
            g.SetLODs(new[] { new LOD(LodHeight(Mathf.Max(g.size, 0.5f), RifugioBikeCullM), rs) });
            g.fadeMode = LODFadeMode.None;

            // The rider, standing beside the bike on the deck side, in kit and helmet.
            if (pool.Count > 0)
            {
                var donor = pool[rnd.Next(pool.Count)];
                var fig = UnityEngine.Object.Instantiate(donor.gameObject, life);
                fig.name = $"Fuji Rifugio Rider {n}";
                fig.hideFlags = HideFlags.None;
                fig.SetActive(true);
                var bench = fig.transform.Find("Timber Waterfront Bench");
                if (bench != null) UnityEngine.Object.DestroyImmediate(bench.gameObject);
                var high = fig.transform.Find("LOD0 High Skinned");
                if (high != null)
                    foreach (var rr in high.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = rr.sharedMaterials;
                        for (int k = 0; k < mats.Length; k++) if (mats[k] != null) mats[k] = RifugioMat(mats[k]);
                        rr.sharedMaterials = mats;
                    }
                var along = (float)(rnd.NextDouble() - 0.5) * 0.5f;
                var pos = new Vector3(p.x, _rifugioTop, p.z) - leanDir * RifugioStandOffsetM + _rifugioFwd * along;
                float yaw = (float)(rnd.NextDouble() < 0.5 ? -1 : 1) * Mathf.Lerp(20f, 55f, (float)rnd.NextDouble());
                var rot = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.LookRotation(leanDir, Vector3.up);
                fig.transform.SetPositionAndRotation(pos, rot);
                fig.transform.localScale = Vector3.one;
                // Soles on the deck: lowest point of the live skin in its rest pose.
                float lowY = float.PositiveInfinity;
                if (high != null)
                    foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr.sharedMesh == null) continue;
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        foreach (var v in baked.vertices) lowY = Mathf.Min(lowY, mtx.MultiplyPoint3x4(v).y);
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                if (!float.IsInfinity(lowY)) fig.transform.position += Vector3.up * (_rifugioTop - lowY);
                var actor = fig.GetComponent<MinatoCrowdActor>();
                var rig = high != null ? high.Find("Rigged Character") : null;
                if (actor != null && rig != null)
                {
                    actor.Configure(MinatoCrowdActor.MotionKind.Idle, rig, fig.transform.position, fig.transform.position, 0f, (float)rnd.NextDouble());
                    actor.armDropDegrees = 32f;
                }
                people.Add(fig);
                standers++;
            }
            n++;
        }
        if (rails.T.Count > 0)
        {
            var mesh = Finish("Fuji_RifugioRails", rails.V.ToArray(), rails.UV.ToArray(), rails.T);
            AddMesh(life, "Fuji Rifugio Hitching Rails", mesh, _pilgrimMat, collider: false);
        }
        Debug.Log($"[fuji] F5 rifugio: {n} roster bikes leaned {RifugioBikeLeanDeg:0} deg on hitching rails, {standers} riders standing beside them.");
        return standers;
    }
}
