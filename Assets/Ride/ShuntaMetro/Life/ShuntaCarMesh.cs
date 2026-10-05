using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural road-car meshes (sedan, hatchback, van) for Shunta traffic. One mesh per variant, lofted from cross-section rings so the
/// body has a tapered nose, shoulders, a raked windscreen and tumblehome instead of stacked boxes.
/// Front = +Z. Origin on the ground. Submeshes: 0 body, 1 glass, 2 tyres, 3 headlights, 4 taillights.
/// </summary>
public static class ShuntaCarMesh
{
    public const int Body = 0, Glass = 1, Tyre = 2, Head = 3, Tail = 4;
    public enum Kind { Sedan, Hatch, Van }

    static readonly Dictionary<Kind, Mesh> Cache = new Dictionary<Kind, Mesh>();

    // station: z (m, + front), half width, bottom y, top y
    struct St { public float z, hw, y0, y1; public St(float z, float hw, float y0, float y1) { this.z = z; this.hw = hw; this.y0 = y0; this.y1 = y1; } }

    public static Mesh Get(Kind k)
    {
        if (Cache.TryGetValue(k, out var m) && m != null) return m;
        m = Build(k); Cache[k] = m; return m;
    }

    class Sub { public List<int> t = new List<int>(); }

    static Mesh Build(Kind kind)
    {
        var v = new List<Vector3>(); var subs = new[] { new Sub(), new Sub(), new Sub(), new Sub(), new Sub() };
        const float hw = 0.93f, clr = 0.17f;
        St[] body, cabin;
        switch (kind)
        {
            case Kind.Van:
                body = new[] { new St(2.25f, hw * 0.80f, clr + 0.18f, 0.62f), new St(2.12f, hw * 0.94f, clr + 0.05f, 0.80f), new St(1.55f, hw, clr, 0.98f), new St(0.0f, hw, clr, 1.02f), new St(-1.9f, hw, clr, 1.04f), new St(-2.22f, hw * 0.94f, clr + 0.08f, 0.98f), new St(-2.28f, hw * 0.86f, clr + 0.25f, 0.82f) };
                cabin = new[] { new St(1.50f, hw * 0.90f, 0.98f, 0.98f), new St(0.95f, hw * 0.86f, 0.98f, 1.78f), new St(-1.95f, hw * 0.86f, 0.98f, 1.82f), new St(-2.18f, hw * 0.82f, 0.98f, 1.52f) };
                break;
            case Kind.Hatch:
                body = new[] { new St(2.00f, hw * 0.78f, clr + 0.16f, 0.58f), new St(1.88f, hw * 0.93f, clr + 0.05f, 0.68f), new St(1.20f, hw, clr, 0.80f), new St(-0.2f, hw, clr, 0.86f), new St(-1.70f, hw, clr, 0.92f), new St(-1.95f, hw * 0.92f, clr + 0.10f, 0.88f), new St(-2.02f, hw * 0.84f, clr + 0.22f, 0.74f) };
                cabin = new[] { new St(0.72f, hw * 0.88f, 0.84f, 0.84f), new St(-0.02f, hw * 0.80f, 0.84f, 1.38f), new St(-1.20f, hw * 0.78f, 0.88f, 1.42f), new St(-1.88f, hw * 0.76f, 0.90f, 1.00f) };
                break;
            default:
                body = new[] { new St(2.20f, hw * 0.78f, clr + 0.16f, 0.56f), new St(2.08f, hw * 0.93f, clr + 0.05f, 0.66f), new St(1.40f, hw, clr, 0.78f), new St(-0.3f, hw, clr, 0.82f), new St(-1.75f, hw, clr, 0.88f), new St(-2.10f, hw * 0.93f, clr + 0.08f, 0.88f), new St(-2.20f, hw * 0.84f, clr + 0.22f, 0.76f) };
                cabin = new[] { new St(0.70f, hw * 0.88f, 0.80f, 0.80f), new St(0.05f, hw * 0.78f, 0.80f, 1.36f), new St(-0.95f, hw * 0.76f, 0.82f, 1.36f), new St(-1.60f, hw * 0.80f, 0.86f, 0.90f) };
                break;
        }

        Loft(v, subs[Body], body, 0.30f, true);
        // glass greenhouse sits just proud of a thin roof skin
        Loft(v, subs[Glass], cabin, 0.22f, true);
        var roof = new[] { cabin[1], cabin[2] };
        for (int i = 0; i < roof.Length; i++) { roof[i].y0 = roof[i].y1 - 0.05f; roof[i].hw *= 1.02f; roof[i].y1 += 0.025f; }
        roof[0].z -= 0.02f; roof[1].z += 0.02f;
        Loft(v, subs[Body], roof, 0.22f, true);

        float wheelZ = kind == Kind.Van ? 1.38f : 1.32f, wheelR = kind == Kind.Van ? 0.36f : 0.33f;
        float rearZ = kind == Kind.Van ? -1.32f : kind == Kind.Hatch ? -1.12f : -1.30f;
        foreach (float wz in new[] { wheelZ, rearZ })
            foreach (float sx in new[] { -1f, 1f })
                Cylinder(v, subs[Tyre], new Vector3(sx * (hw - 0.07f), wheelR, wz), wheelR, 0.24f, 14);

        float fz = body[0].z, bz = body[body.Length - 1].z;
        float lampY = (kind == Kind.Van ? 0.58f : 0.56f);
        foreach (float sx in new[] { -1f, 1f })
        {
            Quad(v, subs[Head], new Vector3(sx * hw * 0.62f, lampY, fz - 0.05f), new Vector3(0.20f, 0.07f, 0.06f), 1f);
            Quad(v, subs[Tail], new Vector3(sx * hw * 0.64f, lampY + 0.04f, bz + 0.03f), new Vector3(0.22f, 0.065f, 0.05f), -1f);
        }

        var mesh = new Mesh { name = "ShuntaCar_" + kind, hideFlags = HideFlags.DontSave, subMeshCount = 5 };
        mesh.SetVertices(v);
        for (int i = 0; i < 5; i++) mesh.SetTriangles(subs[i].t, i);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // Rounded-rectangle ring (10 points, counter-clockwise seen from the front) swept through stations.
    const int Ring = 10;
    static void RingPts(St s, float round, Vector3[] o, float z)
    {
        float H = s.y1 - s.y0, hw = s.hw, r = round;
        // bottom-left, bottom-right, lower flank R, shoulder R, top edge R, top edge L, shoulder L, lower flank L
        o[0] = new Vector3(-hw * 0.88f, s.y0, z); o[1] = new Vector3(hw * 0.88f, s.y0, z);
        o[2] = new Vector3(hw, s.y0 + H * 0.22f, z); o[3] = new Vector3(hw, s.y0 + H * (0.70f - r * 0.2f), z);
        o[4] = new Vector3(hw * (1f - r * 0.35f), s.y0 + H * 0.93f, z); o[5] = new Vector3(hw * 0.55f, s.y1, z);
        o[6] = new Vector3(-hw * 0.55f, s.y1, z); o[7] = new Vector3(-hw * (1f - r * 0.35f), s.y0 + H * 0.93f, z);
        o[8] = new Vector3(-hw, s.y0 + H * (0.70f - r * 0.2f), z); o[9] = new Vector3(-hw, s.y0 + H * 0.22f, z);
    }

    static void Loft(List<Vector3> v, Sub sub, St[] st, float round, bool caps)
    {
        // order stations rear -> front so winding faces outward
        var pts = new Vector3[Ring];
        int first = v.Count;
        var ordered = new List<St>(st); ordered.Sort((a, b) => a.z.CompareTo(b.z));
        for (int i = 0; i < ordered.Count; i++) { RingPts(ordered[i], round, pts, ordered[i].z); v.AddRange(pts); }
        for (int i = 0; i < ordered.Count - 1; i++)
            for (int j = 0; j < Ring; j++)
            {
                int a = first + i * Ring + j, b = first + i * Ring + (j + 1) % Ring, c = a + Ring, d = b + Ring;
                sub.t.AddRange(new[] { a, b, c, b, d, c });
            }
        if (!caps) return;
        for (int end = 0; end < 2; end++)
        {
            int ringStart = first + (end == 0 ? 0 : (ordered.Count - 1) * Ring);
            var s = ordered[end == 0 ? 0 : ordered.Count - 1];
            int cidx = v.Count; v.Add(new Vector3(0f, (s.y0 + s.y1) * 0.5f, s.z));
            for (int j = 0; j < Ring; j++)
            {
                int a = ringStart + j, b = ringStart + (j + 1) % Ring;
                if (end == 0) sub.t.AddRange(new[] { cidx, b, a }); else sub.t.AddRange(new[] { cidx, a, b });
            }
        }
    }

    static void Cylinder(List<Vector3> v, Sub sub, Vector3 c, float r, float width, int seg)
    {
        int first = v.Count;
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f; float y = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
            v.Add(c + new Vector3(-width * 0.5f, y, z)); v.Add(c + new Vector3(width * 0.5f, y, z));
        }
        int hubL = v.Count; v.Add(c + new Vector3(-width * 0.5f, 0, 0)); int hubR = v.Count; v.Add(c + new Vector3(width * 0.5f, 0, 0));
        for (int i = 0; i < seg; i++)
        {
            int n = (i + 1) % seg; int a = first + i * 2, b = first + n * 2;
            sub.t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            sub.t.AddRange(new[] { hubL, a, b });
            sub.t.AddRange(new[] { hubR, b + 1, a + 1 });
        }
    }

    // flat lamp lens facing +Z (dir 1) or -Z (dir -1)
    static void Quad(List<Vector3> v, Sub sub, Vector3 c, Vector3 half, float dir)
    {
        int i = v.Count;
        v.Add(c + new Vector3(-half.x, -half.y, 0)); v.Add(c + new Vector3(half.x, -half.y, 0));
        v.Add(c + new Vector3(half.x, half.y, 0)); v.Add(c + new Vector3(-half.x, half.y, 0));
        if (dir > 0) sub.t.AddRange(new[] { i, i + 1, i + 3, i + 1, i + 2, i + 3 });
        else sub.t.AddRange(new[] { i, i + 3, i + 1, i + 1, i + 3, i + 2 });
    }
}
