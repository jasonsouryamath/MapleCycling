using System.Collections.Generic;
using UnityEngine;

// Tuna port, part 7: fitted work clothes and hand props for the townsfolk. Each donor's live
// skin is BAKED in its rest pose and every vertex binned by its dominant bone, so boots, gloves,
// aprons and hats are sized to that body, not guessed. Gear is authored in world space on the
// first clone of a donor mesh, stored in the owning bone's local space, and shared by every
// later clone of that donor (bone-local geometry is pose- and scale-independent).
public static partial class ShiosaiCoastEnvironment
{
    private sealed class FigScan
    {
        public Transform fig;
        public Vector3 R, U, Fw;
        public float sole = float.PositiveInfinity, top = float.NegativeInfinity;
        public readonly Dictionary<string, Transform> bone = new Dictionary<string, Transform>();
        public readonly Dictionary<string, List<Vector3>> pts = new Dictionary<string, List<Vector3>>();
        public float H(Vector3 p) => Vector3.Dot(p - fig.position, U);
        public float X(Vector3 p) => Vector3.Dot(p - fig.position, R);
        public float Z(Vector3 p) => Vector3.Dot(p - fig.position, Fw);
        public float FigH => top - sole;
        public Transform B(string n) => bone.TryGetValue(n, out var t) ? t : null;
        public List<Vector3> Of(params string[] names)
        {
            var l = new List<Vector3>();
            foreach (var n in names) if (pts.TryGetValue(n, out var p)) l.AddRange(p);
            return l;
        }
        public List<Vector3> Prefix(string prefix)
        {
            var l = new List<Vector3>();
            foreach (var kv in pts) if (kv.Key.StartsWith(prefix, System.StringComparison.Ordinal)) l.AddRange(kv.Value);
            return l;
        }
    }

    private static int _scanNearest, _scanFailed;

    private static FigScan ScanFigure(GameObject go, SkinnedMeshRenderer smr)
    {
        var s = new FigScan { fig = go.transform, R = go.transform.right, U = go.transform.up, Fw = go.transform.forward };
        foreach (var b in smr.bones) if (b != null) s.bone[b.name] = b;
        var mesh = smr.sharedMesh;
        if (mesh == null) return null;
        var bones = smr.bones;
        // Non-readable donor meshes (no civilian look -> the raw GLB skin) have no CPU bone
        // weights: fall back to binning each baked vertex by the NEAREST bone segment.
        var bw = mesh.isReadable ? mesh.boneWeights : null;
        bool nearest = bw == null || bw.Length == 0;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        var vs = baked.vertices;
        var segA = new List<Vector3>(); var segB = new List<Vector3>(); var segN = new List<string>();
        if (nearest)
            foreach (var b in bones)
            {
                if (b == null) continue;
                Transform child = null;
                foreach (Transform c in b) if (s.bone.ContainsKey(c.name)) { child = c; break; }
                segA.Add(b.position);
                segB.Add(child != null ? child.position : b.position + (b.position - (b.parent != null ? b.parent.position : b.position)) * 0.5f);
                segN.Add(b.name);
            }
        for (int i = 0; i < vs.Length; i++)
        {
            var w = mtx.MultiplyPoint3x4(vs[i]);
            float h = s.H(w);
            s.sole = Mathf.Min(s.sole, h); s.top = Mathf.Max(s.top, h);
            string bn = null;
            if (!nearest)
            {
                if (i >= bw.Length) break;
                int bi = bw[i].boneIndex0;
                if (bw[i].weight0 < 0.5f || bi < 0 || bi >= bones.Length || bones[bi] == null) continue;
                bn = bones[bi].name;
            }
            else
            {
                float best = float.PositiveInfinity;
                for (int k = 0; k < segA.Count; k++)
                {
                    var ab = segB[k] - segA[k];
                    float t = Mathf.Clamp01(Vector3.Dot(w - segA[k], ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                    float dd = (w - (segA[k] + ab * t)).sqrMagnitude;
                    if (dd < best) { best = dd; bn = segN[k]; }
                }
                if (bn == null) continue;
            }
            if (!s.pts.TryGetValue(bn, out var l)) s.pts[bn] = l = new List<Vector3>();
            l.Add(w);
        }
        Object.DestroyImmediate(baked);
        if (nearest) _scanNearest++;
        // the civilian hair cap sits outside the skull: include it for hats and bands
        var look = go.GetComponent<MapleCityLook>();
        var cap = look != null ? look.hairCap : null;
        var mf = cap != null ? cap.GetComponent<MeshFilter>() : null;
        if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
        {
            var l = new List<Vector3>();
            foreach (var v in mf.sharedMesh.vertices)
            {
                var w = cap.transform.TransformPoint(v);
                l.Add(w); s.top = Mathf.Max(s.top, s.H(w));
            }
            s.pts["HairCap"] = l;
        }
        return float.IsInfinity(s.sole) ? null : s;
    }

    /// <summary>Centre (at height h) and half-extents along R / Fw of the points within +-band of h.</summary>
    private static bool Slice(FigScan s, List<Vector3> pts, float h, float band, out Vector3 c, out float rx, out float rz,
                              out float front)
    {
        float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = x0, z1 = x1;
        int n = 0;
        foreach (var p in pts)
        {
            if (Mathf.Abs(s.H(p) - h) > band) continue;
            float x = s.X(p), z = s.Z(p);
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
            n++;
        }
        if (n < 3) { c = default; rx = rz = front = 0f; return false; }
        c = s.fig.position + s.R * ((x0 + x1) * 0.5f) + s.Fw * ((z0 + z1) * 0.5f) + s.U * h;
        rx = (x1 - x0) * 0.5f; rz = (z1 - z0) * 0.5f; front = z1;
        return true;
    }

    /// <summary>Largest distance from the axis o + a*t for points whose t/len lies in [t0, t1].</summary>
    private static float RadialMax(List<Vector3> pts, Vector3 o, Vector3 a, float len, float t0, float t1)
    {
        float r = 0f;
        foreach (var p in pts)
        {
            var d = p - o;
            float t = Vector3.Dot(d, a) / Mathf.Max(len, 1e-4f);
            if (t < t0 || t > t1) continue;
            r = Mathf.Max(r, (d - a * Vector3.Dot(d, a)).magnitude);
        }
        return r;
    }

    /// <summary>Bounds of the points in the orthonormal frame (a, p, q) at o.</summary>
    private static bool AxisBounds(List<Vector3> pts, Vector3 o, Vector3 a, Vector3 p, Vector3 q, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        foreach (var w in pts)
        {
            var d = w - o;
            var l = new Vector3(Vector3.Dot(d, a), Vector3.Dot(d, p), Vector3.Dot(d, q));
            mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
        }
        return pts.Count >= 4;
    }

    // ------------------------------------------------------------------ gear meshes

    /// <summary>Per-slot geometry (slot = which of the wearer's materials it takes).</summary>
    private sealed class GearBins
    {
        public readonly SortedDictionary<int, PB> slots = new SortedDictionary<int, PB>();
        public PB this[int slot]
        {
            get { if (!slots.TryGetValue(slot, out var b)) slots[slot] = b = new PB(); return b; }
        }
    }

    private sealed class GearMeshRec { public Mesh mesh; public int[] slots; }
    private static readonly Dictionary<string, GearMeshRec> _gear = new Dictionary<string, GearMeshRec>();
    private static int _gearPieces;

    /// <summary>Wear one piece of gear on <paramref name="bone"/>. <paramref name="build"/> runs once per
    /// donor mesh (world space, on this clone); mats[slot] colours it for this person.</summary>
    private static void Gear(string meshKey, string gear, Transform bone, System.Action<GearBins> build,
                             Material[] mats, List<Renderer> added)
    {
        if (bone == null) return;
        string key = meshKey + "_" + gear;
        if (!_gear.TryGetValue(key, out var rec))
        {
            rec = new GearMeshRec();
            var gb = new GearBins();
            try { build(gb); }
            catch (System.Exception ex) { Debug.LogWarning($"[tunaport] gear {key}: {ex.Message}"); gb.slots.Clear(); }
            int nv = 0;
            foreach (var b in gb.slots.Values) nv += b.v.Count;
            if (nv > 0)
            {
                var v = new List<Vector3>(nv); var uv = new List<Vector2>(nv);
                var subs = new List<List<int>>(); var slotIds = new List<int>();
                foreach (var kv in gb.slots)
                {
                    if (kv.Value.t.Count == 0) continue;
                    int o = v.Count;
                    foreach (var p in kv.Value.v) v.Add(bone.InverseTransformPoint(p));
                    uv.AddRange(kv.Value.uv);
                    var t = new List<int>(kv.Value.t.Count);
                    foreach (int i in kv.Value.t) t.Add(i + o);
                    subs.Add(t); slotIds.Add(kv.Key);
                }
                var m = new Mesh { name = $"Shiosai_TP_Gear_{key}" };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v); m.SetUVs(0, uv);
                m.subMeshCount = subs.Count;
                for (int k = 0; k < subs.Count; k++) m.SetTriangles(subs[k], k);
                m.RecalculateNormals(); m.RecalculateBounds();
                rec.mesh = PersistMesh(m);
                rec.slots = slotIds.ToArray();
            }
            _gear[key] = rec;
        }
        if (rec.mesh == null) return;
        var g = new GameObject(gear, typeof(MeshFilter), typeof(MeshRenderer));
        g.transform.SetParent(bone, false);
        g.GetComponent<MeshFilter>().sharedMesh = rec.mesh;
        var mr = g.GetComponent<MeshRenderer>();
        var arr = new Material[rec.slots.Length];
        for (int k = 0; k < arr.Length; k++) arr[k] = mats[Mathf.Clamp(rec.slots[k], 0, mats.Length - 1)];
        mr.sharedMaterials = arr;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        added.Add(mr);
        _gearPieces++;
    }

    /// <summary>Low-poly smooth (partial) ellipsoid: ph0..ph1 in degrees, -90 = bottom pole.</summary>
    private static void GEll(PB b, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, int seg, int rings,
                             float ph0 = -90f, float ph1 = 90f)
    {
        PGrid(b, seg, rings,
              (k, j) =>
              {
                  float th = k * Mathf.PI * 2f / seg;
                  float ph = Mathf.Lerp(ph0, ph1, (float)j / rings) * Mathf.Deg2Rad;
                  return c + (ax * Mathf.Cos(th) + az * Mathf.Sin(th)) * Mathf.Cos(ph) + ay * Mathf.Sin(ph);
              },
              (k, j) => new Vector2((float)k / seg, (float)j / rings), (k, j) => c);
    }

    /// <summary>Vertical band on an ellipse (rx along R, rz along Fw) between heights h0 and h1 above c.</summary>
    private static void GBand(PB b, FigScan s, Vector3 c, float rx0, float rz0, float rx1, float rz1, float h0, float h1, int seg)
    {
        PGrid(b, seg, 1,
              (i, j) =>
              {
                  float th = i * Mathf.PI * 2f / seg;
                  float rx = j == 0 ? rx0 : rx1, rz = j == 0 ? rz0 : rz1;
                  return c + s.R * (Mathf.Sin(th) * rx) + s.Fw * (Mathf.Cos(th) * rz) + s.U * (j == 0 ? h0 : h1);
              },
              (i, j) => new Vector2((float)i / seg, j), (i, j) => c + s.U * ((h0 + h1) * 0.5f));
    }

    /// <summary>Tube (torus-like) around an ellipse at c, tube radius tr.</summary>
    private static void GRing(PB b, FigScan s, Vector3 c, float rx, float rz, float tr, int seg, int tube = 6)
    {
        Vector3 Ctr(int i) { float th = i * Mathf.PI * 2f / seg; return c + s.R * (Mathf.Sin(th) * rx) + s.Fw * (Mathf.Cos(th) * rz); }
        PGrid(b, seg, tube,
              (i, j) =>
              {
                  var cl = Ctr(i);
                  var outw = (cl - c).normalized;
                  float ph = j * Mathf.PI * 2f / tube;
                  return cl + (outw * Mathf.Cos(ph) + s.U * Mathf.Sin(ph)) * tr;
              },
              (i, j) => new Vector2((float)i / seg, (float)j / tube), (i, j) => Ctr(i));
    }

    /// <summary>Thin two-faced brim: inner edge on the (rx, rz) ellipse at c, from angle th0 to th1
    /// (degrees from Fw toward R), outer edge len(th) further out and droop lower.</summary>
    private static void GBrim(PB b, FigScan s, Vector3 c, float rx, float rz, float th0, float th1, int seg,
                              System.Func<float, float> len, float droop, float thick)
    {
        for (int i = 0; i < seg; i++)
        {
            float ta = Mathf.Lerp(th0, th1, (float)i / seg), tb = Mathf.Lerp(th0, th1, (float)(i + 1) / seg);
            Vector3 E(float t) { float r = t * Mathf.Deg2Rad; return c + s.Fw * (Mathf.Cos(r) * rz) + s.R * (Mathf.Sin(r) * rx); }
            Vector3 O(float t) { var e = E(t); var d = (e - c); d -= s.U * Vector3.Dot(d, s.U); return e + d.normalized * len(t) - s.U * droop; }
            var ea = E(ta); var eb = E(tb); var oa = O(ta); var ob = O(tb);
            PQ(b, ea, eb, ob, oa, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, s.U);
            var dn = -s.U * thick;
            PQ(b, ea + dn, oa + dn, ob + dn, eb + dn, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -s.U);
            PQ(b, oa, ob, ob + dn, oa + dn, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, (oa + ob) * 0.5f - c);
        }
    }
}
