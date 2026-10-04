using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes the eyelid surface data <see cref="RiderBlink"/> needs, once per distinct face mesh in
/// the scene (the player and every NPC rider), into Assets/Resources/Race/eyelids.json.
///
/// The faces are PAINTED (the eyes live in the kit atlas), so the eyes are found from the
/// texture: on the triangles skinned to the Head bone, skin-toned texels define the face (its
/// facing direction and extent), and the painted eyes are the two largest DARK blobs inside it
/// that mirror each other across the face. For each eye a 9 x 8 grid is ray-cast onto the head
/// surface over the eye (plus margins, so the lid covers the painted upper lash line), in Head
/// bone space. The skin texel just above the eye and the darkest lash texel are recorded so the
/// lid can reuse the rider's own material.
///
/// Read-only on the scene (never saves). Debug images of each detection land in
/// reference/good_graphics/race/eyelid_*.png. Batch: run_steps "EyelidBake.Run|log|1".
/// </summary>
public static class EyelidBake
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string OutPath = "Assets/Resources/Race/eyelids.json";
    const int Cols = 9, Rows = 8;

    [MenuItem("MapleRide/Race/Bake Eyelids", priority = 40)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Every rider sharing a face mesh paints it with their OWN kit atlas, and a dark hair or
        // kit colour can fool the detector on one rider but not on the next. So each mesh is
        // analysed on up to 30 riders (the player's original atlas first) and the eye position
        // most of them agree on wins. The eye
        // geometry and the UV layout belong to the mesh, so the winner serves every rider.
        var byKey = new Dictionary<string, List<(SkinnedMeshRenderer smr, string rider)>>();
        int riders = 0;
        var rigs = new List<KuroBikeRig>(Object.FindObjectsByType<KuroBikeRig>(FindObjectsInactive.Include));
        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        var playerRig = boot != null && boot.rider != null ? boot.rider.GetComponentInChildren<KuroBikeRig>(true) : null;
        if (playerRig != null) { rigs.Remove(playerRig); rigs.Insert(0, playerRig); }
        foreach (var rig in rigs)
        {
            riders++;
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string key = RiderBlink.KeyFor(smr);
                if (string.IsNullOrEmpty(key)) continue;
                if (!byKey.TryGetValue(key, out var l)) byKey[key] = l = new List<(SkinnedMeshRenderer, string)>();
                if (l.Count < 30) l.Add((smr, rig.name));
            }
        }
        var bodies = new List<RiderBlink.BodyData>();
        foreach (var kv in byKey)
        {
            var found = new List<RiderBlink.BodyData>();
            foreach (var (smr, rider) in kv.Value)
            {
                var d = BakeOne(smr, kv.Key, rider);
                if (d != null) found.Add(d);
            }
            if (found.Count == 0) continue;
            RiderBlink.BodyData best = null;
            int bestVotes = 0;
            foreach (var cand in found)
            {
                int votes = 0;
                foreach (var other in found) if (SameEyes(cand, other)) votes++;
                // ties go to the larger lid, which is sure to cover the whole painted eye
                if (votes > bestVotes || (votes == bestVotes && best != null && LidArea(cand) > LidArea(best)))
                { bestVotes = votes; best = cand; }
            }
            Debug.Log($"[eyelid-bake] {kv.Key}: {found.Count}/{kv.Value.Count} riders detected eyes; {bestVotes} agree on the chosen pair");
            bodies.Add(best);
        }
        var file = new RiderBlink.DataFile { bodies = bodies.ToArray() };
        Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
        File.WriteAllText(OutPath, JsonUtility.ToJson(file));
        AssetDatabase.ImportAsset(OutPath);
        Debug.Log($"[eyelid-bake] RESULT {bodies.Count} face mesh(es) baked from {byKey.Count} skinned meshes on {riders} riders -> {OutPath}");
    }

    static float LidArea(RiderBlink.BodyData d)
    {
        float a = 0f;
        foreach (var e in d.eyes)
            a += (e.points[e.cols - 1] - e.points[0]).magnitude * (e.points[(e.rows - 1) * e.cols] - e.points[0]).magnitude;
        return a;
    }

    static Vector3 EyeCentre(RiderBlink.EyeData e)
    {
        var c = Vector3.zero;
        foreach (var p in e.points) c += p;
        return c / Mathf.Max(1, e.points.Length);
    }

    /// <summary>Two detections agree when both eyes land within 15 mm of each other (either order).</summary>
    static bool SameEyes(RiderBlink.BodyData a, RiderBlink.BodyData b)
    {
        if (a.eyes.Length != 2 || b.eyes.Length != 2) return false;
        Vector3 a0 = EyeCentre(a.eyes[0]), a1 = EyeCentre(a.eyes[1]), b0 = EyeCentre(b.eyes[0]), b1 = EyeCentre(b.eyes[1]);
        const float tol = 0.015f;
        return ((a0 - b0).magnitude < tol && (a1 - b1).magnitude < tol) || ((a0 - b1).magnitude < tol && (a1 - b0).magnitude < tol);
    }

    struct Tri
    {
        public int a, b, c, sub;
        public Vector3 centroid, normal;
        public float area;
        public Vector2 uv;
        public Color col;
        public bool skin, dark;
    }

    static RiderBlink.BodyData BakeOne(SkinnedMeshRenderer smr, string key, string riderName)
    {
        var mesh = smr.sharedMesh;
        var bones = smr.bones;
        int hi = -1;
        for (int i = 0; bones != null && i < bones.Length; i++)
            if (bones[i] != null && bones[i].name == "Head") { hi = i; break; }
        if (hi < 0) return null;
        var head = bones[hi];

        var weights = mesh.boneWeights;
        if (weights == null || weights.Length != mesh.vertexCount) return null;
        var onHead = new bool[mesh.vertexCount];
        var nearHead = new bool[mesh.vertexCount];
        int headVerts = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            var w = weights[i];
            float s = (w.boneIndex0 == hi ? w.weight0 : 0f) + (w.boneIndex1 == hi ? w.weight1 : 0f) +
                      (w.boneIndex2 == hi ? w.weight2 : 0f) + (w.boneIndex3 == hi ? w.weight3 : 0f);
            if (s > 0.6f) { onHead[i] = true; headVerts++; }
            if (s > 0.02f) nearHead[i] = true;
        }
        if (headVerts < 50) return null;

        // Pose the mesh and bring it into Head-bone space. BakeMesh's space convention with
        // useScale is checked empirically: whichever mapping puts the head vertices nearest the
        // Head bone is the right one.
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var bv = baked.vertices;
        var bn = baked.normals;
        var t = smr.transform;
        // METRIC head frame: the Head bone's position and rotation at scale 1. The bone's own local
        // space carries the armature's import scale (1/100 on the Kuro body), which would turn
        // every millimetre below into a metre. RiderBlink follows the bone in this same frame.
        var headFrame = Matrix4x4.TRS(head.position, head.rotation, Vector3.one).inverse;
        var m1 = headFrame * Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        var m2 = headFrame * t.localToWorldMatrix;
        float d1 = 0f, d2 = 0f;
        int n = 0;
        for (int i = 0; i < bv.Length; i += 7)
            if (onHead[i]) { d1 += m1.MultiplyPoint3x4(bv[i]).magnitude; d2 += m2.MultiplyPoint3x4(bv[i]).magnitude; n++; }
        var toHead = d1 <= d2 ? m1 : m2;
        var pos = new Vector3[bv.Length];
        var nor = new Vector3[bv.Length];
        for (int i = 0; i < bv.Length; i++)
        {
            pos[i] = toHead.MultiplyPoint3x4(bv[i]);
            nor[i] = bn.Length == bv.Length ? toHead.MultiplyVector(bn[i]).normalized : Vector3.zero;
        }
        Object.DestroyImmediate(baked);
        var uvs = mesh.uv;
        if (uvs == null || uvs.Length != bv.Length) return null;

        // head triangles + their painted colour; `surface` also keeps the blended-weight ones
        // around the head (eyelids are cast onto the real surface, holes and all)
        var tris = new List<Tri>();
        var surface = new List<Tri>();
        var mats = smr.sharedMaterials;
        var texCache = new Dictionary<int, Texture2D>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var idx = mesh.GetTriangles(s);
            Texture2D tex = null;
            for (int k = 0; k < idx.Length; k += 3)
            {
                int a = idx[k], b = idx[k + 1], c = idx[k + 2];
                if (nearHead[a] || nearHead[b] || nearHead[c])
                {
                    var sc = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                    var st = new Tri { a = a, b = b, c = c, sub = s, normal = sc.normalized };
                    var svn = nor[a] + nor[b] + nor[c];
                    if (svn.sqrMagnitude > 1e-8f && Vector3.Dot(svn, st.normal) < 0f) st.normal = -st.normal;
                    surface.Add(st);
                }
                if (!onHead[a] || !onHead[b] || !onHead[c]) continue;
                if (tex == null && !texCache.TryGetValue(s, out tex))
                {
                    tex = Readable(s < mats.Length ? mats[s] : null);
                    texCache[s] = tex;
                }
                if (tex == null) continue;
                var cr = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                var tr = new Tri
                {
                    a = a, b = b, c = c, sub = s,
                    centroid = (pos[a] + pos[b] + pos[c]) / 3f,
                    normal = cr.normalized,
                    area = cr.magnitude * 0.5f,
                    uv = (uvs[a] + uvs[b] + uvs[c]) / 3f,
                };
                // winding may be either way; orient the normal with the vertex normals
                var vn = nor[a] + nor[b] + nor[c];
                if (vn.sqrMagnitude > 1e-8f && Vector3.Dot(vn, tr.normal) < 0f) tr.normal = -tr.normal;
                Color col = tex.GetPixelBilinear(tr.uv.x, tr.uv.y);
                foreach (var v in new[] { uvs[a], uvs[b], uvs[c] })
                    col += tex.GetPixelBilinear(Mathf.Lerp(tr.uv.x, v.x, 0.5f), Mathf.Lerp(tr.uv.y, v.y, 0.5f));
                tr.col = col / 4f;
                float lum = 0.299f * tr.col.r + 0.587f * tr.col.g + 0.114f * tr.col.b;
                tr.skin = tr.col.r > 0.55f && tr.col.r >= tr.col.g && tr.col.g >= tr.col.b * 0.95f &&
                          tr.col.r - tr.col.b > 0.1f && tr.col.r - tr.col.b < 0.55f && lum > 0.45f;
                tr.dark = lum < 0.2f;
                tris.Add(tr);
            }
        }
        string label = $"{riderName}/{smr.name} [{key}]";
        int skinCount = 0;
        Vector3 F = Vector3.zero;
        foreach (var tr in tris) if (tr.skin) { F += tr.normal * tr.area; skinCount++; }
        if (skinCount < 20 || F.sqrMagnitude < 1e-10f)
        {
            Debug.Log($"[eyelid-bake] skip {label}: {skinCount} skin triangles on the head (not a face mesh)");
            return null;
        }
        F.Normalize();
        // face centre and extent from front-facing skin
        Vector3 C = Vector3.zero;
        float wsum = 0f;
        foreach (var tr in tris)
            if (tr.skin && Vector3.Dot(tr.normal, F) > 0.4f) { C += tr.centroid * tr.area; wsum += tr.area; }
        C /= Mathf.Max(1e-9f, wsum);
        Vector3 U = head.InverseTransformDirection(Vector3.up);
        U = (U - F * Vector3.Dot(U, F)).normalized;
        Vector3 R = Vector3.Cross(U, F).normalized;
        var xs = new List<float>();
        var ys = new List<float>();
        foreach (var tr in tris)
            if (tr.skin && Vector3.Dot(tr.normal, F) > 0.4f)
            {
                xs.Add(Vector3.Dot(tr.centroid - C, R));
                ys.Add(Vector3.Dot(tr.centroid - C, U));
            }
        xs.Sort(); ys.Sort();
        float fx0 = xs[(int)(xs.Count * 0.02f)], fx1 = xs[(int)(xs.Count * 0.98f)];
        float fy0 = ys[(int)(ys.Count * 0.02f)], fy1 = ys[(int)(ys.Count * 0.98f)];

        // dark blobs inside the face, joined through shared (welded) vertices
        var dark = new List<int>();
        for (int i = 0; i < tris.Count; i++)
        {
            var tr = tris[i];
            if (!tr.dark || Vector3.Dot(tr.normal, F) < 0.2f) continue;
            float x = Vector3.Dot(tr.centroid - C, R), y = Vector3.Dot(tr.centroid - C, U);
            if (x < fx0 || x > fx1 || y < fy0 || y > fy1) continue;
            dark.Add(i);
        }
        var parent = new int[dark.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        var byVert = new Dictionary<Vector3Int, int>();
        for (int k = 0; k < dark.Count; k++)
        {
            var tr = tris[dark[k]];
            foreach (int vi in new[] { tr.a, tr.b, tr.c })
            {
                var q = Vector3Int.RoundToInt(pos[vi] * 5000f);   // 0.2 mm weld
                if (byVert.TryGetValue(q, out int other)) parent[Find(k)] = Find(other);
                else byVert[q] = k;
            }
        }
        var blobs = new Dictionary<int, List<int>>();
        for (int k = 0; k < dark.Count; k++)
        {
            int r = Find(k);
            if (!blobs.TryGetValue(r, out var l)) blobs[r] = l = new List<int>();
            l.Add(dark[k]);
        }
        // pick the mirrored pair with the largest smaller-area
        var list = new List<(List<int> t, float area, Vector2 c)>();
        foreach (var l in blobs.Values)
        {
            float a = 0f; Vector2 c = Vector2.zero;
            foreach (int i in l)
            {
                a += tris[i].area;
                c += new Vector2(Vector3.Dot(tris[i].centroid - C, R), Vector3.Dot(tris[i].centroid - C, U)) * tris[i].area;
            }
            if (a > 0f) list.Add((l, a, c / a));
        }
        int bi = -1, bj = -1;
        float best = 0f;
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var A = list[i]; var B = list[j];
                if (A.c.x * B.c.x >= 0f) continue;
                float sep = Mathf.Abs(A.c.x - B.c.x);
                if (Mathf.Abs(A.c.x + B.c.x) > 0.3f * sep || Mathf.Abs(A.c.y - B.c.y) > 0.25f * sep) continue;
                float score = Mathf.Min(A.area, B.area);
                if (score > best) { best = score; bi = i; bj = j; }
            }
        Debug.Log($"[eyelid-bake] {label}: {tris.Count} head tris, {skinCount} skin, {dark.Count} dark in face " +
                  $"({fx0 * 1000f:0}..{fx1 * 1000f:0} x {fy0 * 1000f:0}..{fy1 * 1000f:0} mm), {list.Count} blobs; " +
                  (bi < 0 ? "no pair" : $"pair at ({list[bi].c.x * 1000f:0.0},{list[bi].c.y * 1000f:0.0}) and ({list[bj].c.x * 1000f:0.0},{list[bj].c.y * 1000f:0.0}) mm"));
        if (bi < 0)
        {
            Debug.LogWarning($"[eyelid-bake] {label}: no mirrored eye pair among {list.Count} dark blobs");
            return null;
        }

        var data = new RiderBlink.BodyData { key = key, headBone = "Head" };
        var eyes = new List<RiderBlink.EyeData>();
        var debug = new List<(List<int> t, Rect r)>();
        float lashLum = 9f;
        foreach (var eye in new[] { list[bi], list[bj] })
        {
            float x0 = 9f, x1 = -9f, y0 = 9f, y1 = -9f;
            foreach (int i in eye.t)
                foreach (int vi in new[] { tris[i].a, tris[i].b, tris[i].c })
                {
                    float x = Vector3.Dot(pos[vi] - C, R), y = Vector3.Dot(pos[vi] - C, U);
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
            float w = x1 - x0, h = y1 - y0;
            x0 -= 0.16f * w; x1 += 0.16f * w;
            float top = y1 + 0.2f * h, bottom = y0 - 0.12f * h;
            var e = new RiderBlink.EyeData { cols = Cols, rows = Rows, points = new Vector3[Cols * Rows], normals = new Vector3[Cols * Rows] };
            bool ok = true;
            for (int r = 0; r < Rows && ok; r++)
                for (int c = 0; c < Cols; c++)
                {
                    float x = Mathf.Lerp(x0, x1, c / (Cols - 1f));
                    float y = Mathf.Lerp(top, bottom, r / (Rows - 1f));
                    var o = C + R * x + U * y + F * 0.2f;
                    if (!Cast(surface, pos, o, -F, F, out var hit, out var hn) &&
                        !Nearest(surface, pos, C, R, U, F, x, y, out hit, out hn))
                    {
                        Debug.LogWarning($"[eyelid-bake] {label}: grid point r{r} c{c} found no surface (x {x * 1000f:0.0} y {y * 1000f:0.0} mm)");
                        ok = false; break;
                    }
                    e.points[r * Cols + c] = hit + hn * 0.0012f;
                    e.normals[r * Cols + c] = hn;
                }
            if (!ok) { Debug.LogWarning($"[eyelid-bake] {label}: eyelid grid ray missed the head"); return null; }
            eyes.Add(e);
            data.submesh = tris[eye.t[0]].sub;
            debug.Add((eye.t, new Rect(x0, bottom, x1 - x0, top - bottom)));
            var texE = texCache.TryGetValue(tris[eye.t[0]].sub, out var te) ? te : null;
            // lash: the blackest, least saturated, most uniform texel of the eye (not the iris)
            foreach (int i in eye.t)
            {
                var cc = tris[i].col;
                float l = 0.299f * cc.r + 0.587f * cc.g + 0.114f * cc.b;
                float sat = Mathf.Max(cc.r, Mathf.Max(cc.g, cc.b)) - Mathf.Min(cc.r, Mathf.Min(cc.g, cc.b));
                float score = l + sat * 0.8f + Uniformity(texE, tris[i].uv) * 2f;
                if (score < lashLum) { lashLum = score; data.lashUV = tris[i].uv; }
            }
            // skin: from a flat patch of skin above the eye, closest to the typical skin colour
            var cands = new List<Tri>();
            foreach (var tr in tris)
            {
                if (!tr.skin || Vector3.Dot(tr.normal, F) < 0.4f) continue;
                float px = Vector3.Dot(tr.centroid - C, R), py = Vector3.Dot(tr.centroid - C, U);
                if (py > top - 0.1f * h && py < top + 1.5f * h && Mathf.Abs(px - (x0 + x1) * 0.5f) < (x1 - x0) * 0.6f) cands.Add(tr);
            }
            if (cands.Count == 0) foreach (var tr in tris) if (tr.skin && Vector3.Dot(tr.normal, F) > 0.4f) cands.Add(tr);
            var med = Median(cands);
            float bestS = float.MaxValue;
            foreach (var tr in cands)
            {
                float score = Dist(tr.col, med) + Uniformity(texCache.TryGetValue(tr.sub, out var ts) ? ts : null, tr.uv) * 3f;
                if (score < bestS) { bestS = score; data.skinUV = tr.uv; }
            }
            float ew = x1 - x0, eh = top - bottom;
            // anime eyes are clearly wider than tall (the lid grid adds margins: ~1.3-2.4)
            // ...and each eye is well under half the face wide (a big chibi eye is ~1/3)
            if (ew > 2.6f * eh || ew < 1.15f * eh || ew > 0.4f * (fx1 - fx0))
            {
                Debug.LogWarning($"[eyelid-bake] {label}: implausible eye {ew * 1000f:0} x {eh * 1000f:0} mm on a {(fx1 - fx0) * 1000f:0} mm face; rejected");
                return null;
            }
            Debug.Log($"[eyelid-bake] {label}: eye at x {(x0 + x1) * 500f:0.0} mm, y {(top + bottom) * 500f:0.0} mm, " +
                      $"{(x1 - x0) * 1000f:0.0} x {(top - bottom) * 1000f:0.0} mm, {eye.t.Count} dark tris");
        }
        data.eyes = eyes.ToArray();
        WriteDebug(texCache.TryGetValue(data.submesh, out var dt) ? dt : null, tris, debug, data, key + "_" + riderName);
        foreach (var tx in texCache.Values) if (tx != null) Object.DestroyImmediate(tx);
        return data;
    }

    /// <summary>Closest front-facing triangle hit along the ray, own Moller-Trumbore (no physics).</summary>
    static bool Cast(List<Tri> tris, Vector3[] pos, Vector3 o, Vector3 dir, Vector3 F, out Vector3 hit, out Vector3 n)
    {
        hit = n = Vector3.zero;
        float bestT = float.MaxValue;
        foreach (var tr in tris)
        {
            if (Vector3.Dot(tr.normal, F) < 0.05f) continue;
            Vector3 v0 = pos[tr.a], v1 = pos[tr.b], v2 = pos[tr.c];
            Vector3 e1 = v1 - v0, e2 = v2 - v0, p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) continue;
            float inv = 1f / det;
            Vector3 s = o - v0;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) continue;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f) continue;
            float t = Vector3.Dot(e2, q) * inv;
            if (t > 0f && t < bestT) { bestT = t; hit = o + dir * t; n = tr.normal; }
        }
        return bestT < float.MaxValue;
    }

    /// <summary>Colour spread in a 5 x 5 texel patch around uv (0 = flat), so a UV-pinned lid
    /// never samples an edge that mipmapping would smear.</summary>
    static float Uniformity(Texture2D tex, Vector2 uv)
    {
        if (tex == null) return 0f;
        float du = 3f / tex.width, dv = 3f / tex.height;
        Color mean = Color.clear;
        var cs = new Color[25];
        int k = 0;
        for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++) { cs[k] = tex.GetPixelBilinear(uv.x + x * du, uv.y + y * dv); mean += cs[k++]; }
        mean /= 25f;
        float v = 0f;
        foreach (var c in cs) v += Dist(c, mean);
        return v / 25f;
    }

    static float Dist(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    static Color Median(List<Tri> l)
    {
        if (l.Count == 0) return new Color(0.9f, 0.72f, 0.62f);
        var r = new List<float>(); var g = new List<float>(); var b = new List<float>();
        foreach (var t in l) { r.Add(t.col.r); g.Add(t.col.g); b.Add(t.col.b); }
        r.Sort(); g.Sort(); b.Sort();
        int m = l.Count / 2;
        return new Color(r[m], g[m], b[m]);
    }

    /// <summary>Fallback for grid points past the face's silhouette: the front-facing surface
    /// triangle whose centroid projects nearest, with the point dropped onto its plane.</summary>
    static bool Nearest(List<Tri> tris, Vector3[] pos, Vector3 C, Vector3 R, Vector3 U, Vector3 F,
                        float x, float y, out Vector3 hit, out Vector3 n)
    {
        hit = n = Vector3.zero;
        float best = float.MaxValue;
        var want = new Vector2(x, y);
        foreach (var tr in tris)
        {
            if (Vector3.Dot(tr.normal, F) < 0.2f) continue;
            var ctr = (pos[tr.a] + pos[tr.b] + pos[tr.c]) / 3f;
            var p = new Vector2(Vector3.Dot(ctr - C, R), Vector3.Dot(ctr - C, U));
            float d = (p - want).sqrMagnitude;
            if (d < best)
            {
                best = d;
                var onPlane = C + R * x + U * y;
                float depth = Vector3.Dot(ctr - onPlane, tr.normal) / Mathf.Max(0.2f, Vector3.Dot(F, tr.normal));
                hit = onPlane + F * depth;
                n = tr.normal;
            }
        }
        return best < 0.02f * 0.02f;   // within 2 cm of real surface
    }

    /// <summary>The material's albedo as a CPU-readable texture: the source PNG when there is one
    /// (exact texels), else a GPU blit.</summary>
    static Texture2D Readable(Material m)
    {
        if (m == null) return null;
        Texture src = null;
        foreach (var p in new[] { "_MainTex", "_BaseColorMap", "_BaseMap" })
            if (m.HasProperty(p) && m.GetTexture(p) != null) { src = m.GetTexture(p); break; }
        if (src == null) return null;
        string path = AssetDatabase.GetAssetPath(src);
        if (!string.IsNullOrEmpty(path) && (path.EndsWith(".png") || path.EndsWith(".jpg")) && File.Exists(path))
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (t.LoadImage(File.ReadAllBytes(path))) return t;
        }
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    static void WriteDebug(Texture2D tex, List<Tri> tris, List<(List<int> t, Rect r)> eyes,
                           RiderBlink.BodyData d, string key)
    {
        if (tex == null) return;
        const int S = 1024;
        var img = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                px[y * S + x] = tex.GetPixelBilinear((x + 0.5f) / S, (y + 0.5f) / S) * 0.7f;
        void Dot(Vector2 uv, Color c, int r)
        {
            int cx = (int)(uv.x * S), cy = (int)(uv.y * S);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    int X = cx + x, Y = cy + y;
                    if (X >= 0 && X < S && Y >= 0 && Y < S) px[Y * S + X] = c;
                }
        }
        foreach (var tr in tris) if (tr.skin) Dot(tr.uv, new Color(0.2f, 0.9f, 0.3f), 0);
        foreach (var e in eyes) foreach (int i in e.t) Dot(tris[i].uv, Color.red, 1);
        Dot(d.skinUV, Color.cyan, 5);
        Dot(d.lashUV, Color.magenta, 5);
        img.SetPixels(px);
        img.Apply();
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/race"));
        Directory.CreateDirectory(dir);
        string safe = key.Replace('#', '_').Replace('/', '_').Replace(' ', '_');
        File.WriteAllBytes(Path.Combine(dir, "eyelid_" + safe + ".png"), img.EncodeToPNG());
        Object.DestroyImmediate(img);
    }
}
