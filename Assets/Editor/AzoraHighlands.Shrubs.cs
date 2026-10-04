using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2026-09-26 (claude-azora2): the shared Minato low-shrub GLBs are camera-facing CARDS, and in
/// Azora's purple heather / yellow gorse materials they read as flat coloured quads from the
/// road. These are procedural low-poly shrub clumps instead: 3-5 jittered icosahedron lumps
/// (smooth normals so the cel ramp rounds them), optionally crowned with small flower lumps.
/// Two mesh parts per variant, "leaf" and "bloom", so the dressing role can pick a green for
/// the foliage and an alpine tint (heather, alpenrose, yellow, white) or nothing for the flowers.
/// The meshes are only CombineMeshes sources; the merged tile meshes are what get saved.
/// </summary>
public static partial class AzoraHighlandsEnvironment
{
    private static GlbSource[] _procShrubs;
    private static Material _shrubGreen2, _shrubDark, _alpenrose, _alpineYellow, _alpineWhite;

    private static readonly Vector3[] IcoV =
    {
        new Vector3(-1, 1.618f, 0), new Vector3(1, 1.618f, 0), new Vector3(-1, -1.618f, 0), new Vector3(1, -1.618f, 0),
        new Vector3(0, -1, 1.618f), new Vector3(0, 1, 1.618f), new Vector3(0, -1, -1.618f), new Vector3(0, 1, -1.618f),
        new Vector3(1.618f, 0, -1), new Vector3(1.618f, 0, 1), new Vector3(-1.618f, 0, -1), new Vector3(-1.618f, 0, 1),
    };

    private static readonly int[] IcoT =
    {
        0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
    };

    private static void Lump(List<Vector3> v, List<int> t, Vector3 c, Vector3 r, float seed)
    {
        int b = v.Count;
        for (int k = 0; k < IcoV.Length; k++)
        {
            var n = IcoV[k].normalized;
            float j = 1f + (H01(seed + k * 1.7f, 3.1f) - 0.5f) * 0.32f;
            // flatten the underside so the clump sits on the ground
            var p = new Vector3(n.x * r.x, Mathf.Max(n.y, -0.35f) * r.y, n.z * r.z) * j;
            v.Add(c + p);
        }
        // Unity's face normal is Cross(v1-v0, v2-v0); make it point outwards (flip if inwards).
        var f0 = Vector3.Cross(IcoV[IcoT[1]] - IcoV[IcoT[0]], IcoV[IcoT[2]] - IcoV[IcoT[0]]);
        bool flip = Vector3.Dot(f0, IcoV[IcoT[0]] + IcoV[IcoT[1]] + IcoV[IcoT[2]]) < 0f;
        for (int k = 0; k < IcoT.Length; k += 3)
        {
            if (flip) { t.Add(b + IcoT[k]); t.Add(b + IcoT[k + 2]); t.Add(b + IcoT[k + 1]); }
            else { t.Add(b + IcoT[k]); t.Add(b + IcoT[k + 1]); t.Add(b + IcoT[k + 2]); }
        }
    }

    private static Mesh ShrubMesh(string name, List<Vector3> v, List<int> t)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v);
        var uv = new Vector2[v.Count];
        for (int k = 0; k < uv.Length; k++) uv[k] = new Vector2(v[k].x + v[k].z, v[k].y);
        m.uv = uv;
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Six shrub variants (unit height ~1 before PlaceOnGround scales them).</summary>
    private static GlbSource[] ProcShrubs()
    {
        if (_procShrubs != null && _procShrubs.Length > 0 && _procShrubs[0].parts.Count > 0 && _procShrubs[0].parts[0].mesh != null)
            return _procShrubs;
        _shrubGreen2 = CelMaterial("Azora_Dress_ShrubLight", new Color(0.36f, 0.50f, 0.20f), gloss: 0.05f, spec: 0.03f, rim: 0.18f);
        _shrubDark = CelMaterial("Azora_Dress_ShrubDark", new Color(0.17f, 0.32f, 0.20f), gloss: 0.05f, spec: 0.03f, rim: 0.14f);
        _alpenrose = CelMaterial("Azora_Dress_Alpenrose", new Color(0.78f, 0.30f, 0.42f), gloss: 0.08f, spec: 0.04f, rim: 0.22f);
        _alpineYellow = CelMaterial("Azora_Dress_AlpineYellow", new Color(0.90f, 0.76f, 0.22f), gloss: 0.08f, spec: 0.04f, rim: 0.22f);
        _alpineWhite = CelMaterial("Azora_Dress_AlpineWhite", new Color(0.93f, 0.92f, 0.88f), gloss: 0.08f, spec: 0.04f, rim: 0.2f, shade: GroundShade);

        var list = new List<GlbSource>();
        for (int variant = 0; variant < 6; variant++)
        {
            var lv = new List<Vector3>(); var lt = new List<int>();
            var bv = new List<Vector3>(); var bt = new List<int>();
            int lumps = 3 + variant % 3;
            float spread = variant >= 4 ? 0.75f : 0.45f; // 4,5 = low wide cushions
            float tall = variant >= 4 ? 0.32f : 0.5f;
            for (int k = 0; k < lumps; k++)
            {
                float a = k * 2.399f + variant;
                float rr = k == 0 ? 0f : spread * Mathf.Sqrt(H01(variant, k + 0.3f));
                var c = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                float s = Mathf.Lerp(0.34f, 0.5f, H01(variant + 2f, k)) * (k == 0 ? 1.15f : 1f);
                var r = new Vector3(s * Mathf.Lerp(0.9f, 1.2f, H01(k, variant + 9f)), s * tall / 0.5f * 0.9f, s);
                c.y = r.y * 0.55f;
                Lump(lv, lt, c, r, variant * 13f + k * 3f);
                // 1-2 flower heads on top of each lump
                int heads = 1 + (k == 0 ? 1 : 0);
                for (int h = 0; h < heads; h++)
                {
                    float ha = H01(variant + h, k + 6.6f) * 6.283f;
                    var hc = c + new Vector3(Mathf.Cos(ha) * r.x * 0.45f, r.y * 0.82f, Mathf.Sin(ha) * r.z * 0.45f);
                    float hs = r.x * 0.28f;
                    Lump(bv, bt, hc, new Vector3(hs, hs * 0.55f, hs), variant * 31f + k * 7f + h);
                }
            }
            var src = new GlbSource { name = $"AzoraShrub{variant}" };
            var leaf = ShrubMesh($"AzoraShrubLeaf{variant}", lv, lt);
            var bloom = ShrubMesh($"AzoraShrubBloom{variant}", bv, bt);
            src.parts.Add(new GlbPart { mesh = leaf, sub = 0, rel = Matrix4x4.identity, mat = "leaf" });
            src.parts.Add(new GlbPart { mesh = bloom, sub = 0, rel = Matrix4x4.identity, mat = "bloom" });
            src.bounds = leaf.bounds; src.bounds.Encapsulate(bloom.bounds);
            src.ok = true;
            list.Add(src);
        }
        _procShrubs = list.ToArray();
        return _procShrubs;
    }

    // ------------------------------------------------------------------ WP-G: snowpack burial

    private static int _snowCulled, _snowSunk;

    /// <summary>
    /// Modelled snowpack depth (metres) at a ground point. The shader's snow is a uniform white
    /// coat (WeatherSnowCover has no altitude term here), so without this every low shrub pokes a
    /// green lump through what should be a metre of snow on the plateau. Depth grows with
    /// altitude (valley 0.2 m -> 1.5 m above ~1850 m), is scoured off steep ground (wind strips
    /// slopes, drifts fill flats), and wanders with a ~60 m drift noise so the edge is organic.
    /// </summary>
    private static float SnowDepthM(Vector3 f)
    {
        float alt = Mathf.Lerp(0.20f, 1.50f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1500f, 1920f, f.y)));
        const float e = 3f;
        float gx = (GH(f.x + e, f.z) - GH(f.x - e, f.z)) / (2f * e);
        float gz = (GH(f.x, f.z + e) - GH(f.x, f.z - e)) / (2f * e);
        float slope = Mathf.Sqrt(gx * gx + gz * gz);            // rise/run
        float scour = Mathf.Lerp(1f, 0.25f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, slope)));
        float drift = Mathf.Lerp(0.70f, 1.30f, Mathf.PerlinNoise(f.x * 0.017f + 31.7f, f.z * 0.017f + 12.9f));
        return alt * scour * drift;
    }

    /// <summary>
    /// Snowpack test for a low plant of height <paramref name="h"/> at <paramref name="f"/>.
    /// Returns false (cull) when the pack covers ~80% of it; otherwise returns the extra sink
    /// that seats it IN the pack (so only its crown shows) rather than perched on the ground.
    /// A <paramref name="keep"/> hash below <paramref name="keepChance"/> lets a sparse few poke through anyway.
    /// </summary>
    private static bool AboveSnow(Vector3 f, float h, out float sink, float keep = 1f, float keepChance = 0f)
    {
        float depth = SnowDepthM(f);
        sink = 0f;
        // keepChance lets a sparse few poke through where the pack is not much taller than the plant;
        // on the deepest snowfield (depth > 1.6 h) nothing survives.
        bool spared = keep < keepChance && depth < h * 1.6f;
        if (depth >= h * 0.8f && !spared) { _snowCulled++; return false; }
        if (depth > 0.12f) { sink = Mathf.Min(depth, h * 0.8f) * 0.7f; _snowSunk++; }
        return true;
    }

    /// <summary>Role for a procedural shrub: pick = 0..1 hash; lush = 0..1 (1 low valley).
    /// Mostly natural greens; ~35% carry alpine tints (heather, alpenrose, yellow, white).</summary>
    private static System.Func<string, Material> ShrubRole(float pick, float pick2)
    {
        var leaf = pick < 0.4f ? _shrubGreen : pick < 0.7f ? _shrubGreen2 : _shrubDark;
        Material bloom = null;
        if (pick2 < 0.14f) bloom = _heather;
        else if (pick2 < 0.24f) bloom = _alpenrose;
        else if (pick2 < 0.31f) bloom = _alpineYellow;
        else if (pick2 < 0.36f) bloom = _alpineWhite;
        return m => m == "bloom" ? bloom : leaf;
    }
}
