using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shunta Metro look: generated scenery. Neon-sign buildings lining the road, zone-tinted edge strips and
/// wet-road light streaks, the zone-4 tunnel shell, zone-8 railway viaduct, torii gates, sakura trees, ground and sea,
/// and the landmark props (Tokyo Tower, Mt Fuji, Rainbow Bridge, Odaiba ferris wheel) placed from the JSON landmarks.
/// Everything is merged into a few meshes per material (<see cref="ShuntaLookKit.MeshBag"/>).
/// </summary>
public static class ShuntaLookScenery
{
    public const float GroundY = -5.5f;

    /// <summary>A material whose emissive intensity follows the zone's time-of-day "neon" multiplier.</summary>
    public sealed class NeonMat { public Material mat; public Color rgb; public float rel; }

    public sealed class ZoneMats
    {
        public Material building, strip, streak, signA, signB, signC, signHot;
    }

    public sealed class Result
    {
        public List<GameObject> objects = new List<GameObject>();
        public List<NeonMat> neon = new List<NeonMat>();
        public int buildings, signs, strips, streaks, props;
        public Bounds bounds;
    }

    struct Rule { public float spacing, latMin, latMax, hMin, hMax, signProb; public bool none; }

    static Rule RuleFor(int zone)
    {
        switch (zone)
        {
            case 1: return new Rule { spacing = 26, latMin = 9, latMax = 13, hMin = 10, hMax = 75, signProb = 0.95f };
            case 2: return new Rule { spacing = 22, latMin = 6.5f, latMax = 8.5f, hMin = 2, hMax = 28, signProb = 0.85f };
            case 3: return new Rule { spacing = 40, latMin = 12, latMax = 20, hMin = -4, hMax = 32, signProb = 0.6f };
            case 4: return new Rule { none = true };
            case 5: return new Rule { spacing = 60, latMin = 20, latMax = 38, hMin = 10, hMax = 70, signProb = 0.55f };
            case 6: return new Rule { spacing = 50, latMin = 16, latMax = 30, hMin = -5, hMax = 40, signProb = 0.4f };
            case 7: return new Rule { spacing = 24, latMin = 7, latMax = 10, hMin = 4, hMax = 34, signProb = 1f };
            case 8: return new Rule { spacing = 28, latMin = 9, latMax = 14, hMin = 4, hMax = 26, signProb = 0.8f };
            case 9: return new Rule { spacing = 46, latMin = 22, latMax = 44, hMin = 0, hMax = 70, signProb = 0.5f };
            case 11: return new Rule { spacing = 90, latMin = 30, latMax = 70, hMin = 2, hMax = 16, signProb = 0.5f };
            default: return new Rule { none = true };
        }
    }

    public static ZoneMats[] BuildZoneMaterials(ShuntaCourseData c, List<NeonMat> neon)
    {
        var win = ShuntaLookKit.MakeWindowTexture(11);
        var res = new ZoneMats[c.zones.Length + 1];
        NeonMat Reg(Material m, Color rgb, float rel) { ShuntaLookKit.SetEmissive(m, rgb, rel); var n = new NeonMat { mat = m, rgb = rgb, rel = rel }; neon.Add(n); return n; }
        foreach (var z in c.zones)
        {
            var col = z.Color32; Color.RGBToHSV(col, out float h, out float s, out float v);
            var comp = Color.HSVToRGB((h + 0.5f) % 1f, Mathf.Clamp01(s), 1f);
            var side = Color.HSVToRGB((h + 0.12f) % 1f, 0.7f, 1f);
            var zm = new ZoneMats();
            zm.building = ShuntaLookKit.Lit("Bld" + z.index, new Color(0.05f, 0.055f, 0.075f), 0.6f);
            var wtint = Color.Lerp(new Color(1f, 0.85f, 0.6f), col, 0.55f);
            ShuntaLookKit.SetEmissiveMap(zm.building, win, wtint, 0.7f);
            neon.Add(new NeonMat { mat = zm.building, rgb = wtint, rel = 0.7f });
            zm.strip = ShuntaLookKit.Lit("Strip" + z.index, new Color(0.02f, 0.02f, 0.02f), 0.5f); Reg(zm.strip, col, 1.5f);
            zm.streak = ShuntaLookKit.Lit("Streak" + z.index, new Color(0.02f, 0.02f, 0.02f), 0.9f); Reg(zm.streak, col, 0.9f);
            zm.signA = ShuntaLookKit.Lit("SignA" + z.index, new Color(0.03f, 0.03f, 0.03f), 0.5f); Reg(zm.signA, col, 4f);
            zm.signB = ShuntaLookKit.Lit("SignB" + z.index, new Color(0.03f, 0.03f, 0.03f), 0.5f); Reg(zm.signB, comp, 3.5f);
            zm.signC = ShuntaLookKit.Lit("SignC" + z.index, new Color(0.03f, 0.03f, 0.03f), 0.5f); Reg(zm.signC, side, 3.5f);
            zm.signHot = ShuntaLookKit.Lit("SignHot" + z.index, new Color(0.03f, 0.03f, 0.03f), 0.5f); Reg(zm.signHot, new Color(1f, 0.93f, 0.8f), 3f);
            res[z.index] = zm;
        }
        return res;
    }

    static Material Plain(List<NeonMat> neon, string name, Color albedo, float smooth, Color emit, float rel, bool follow = true)
    {
        var m = ShuntaLookKit.Lit(name, albedo, smooth);
        if (rel > 0f) { ShuntaLookKit.SetEmissive(m, emit, rel); if (follow) neon.Add(new NeonMat { mat = m, rgb = emit, rel = rel }); }
        return m;
    }

    static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    public static Result Build(ShuntaRouteBuilder rb, Transform parent)
    {
        var res = new Result();
        var c = rb.Course; if (c == null || rb.Positions.Length < 2) return res;
        var rng = new System.Random(20261002);
        var neon = res.neon;
        var zmats = BuildZoneMaterials(c, neon);
        var bag = new ShuntaLookKit.MeshBag();
        float half = rb.roadWidth * 0.5f;
        float endKm = rb.Km[rb.Km.Length - 1];

        var b = new Bounds(rb.Positions[0], Vector3.zero);
        foreach (var p in rb.Positions) b.Encapsulate(p);
        res.bounds = b;

        // sparse horizontal copy of the route for overlap tests
        var pts = new List<Vector2>(); var pkm = new List<float>();
        for (int i = 0; i < rb.Positions.Length; i += 2) { pts.Add(new Vector2(rb.Positions[i].x, rb.Positions[i].z)); pkm.Add(rb.Km[i]); }
        bool NearOtherRoad(Vector2 xz, float km, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < pts.Count; i++)
                if (Mathf.Abs(pkm[i] - km) > 0.35f && (pts[i] - xz).sqrMagnitude < r2) return true;
            return false;
        }

        // ------------------------------------------------ road edge strips + tunnel + viaduct, per sample segment
        for (int i = 0; i < rb.Positions.Length - 1; i++)
        {
            var a = rb.Positions[i]; var d = rb.Positions[i + 1];
            var dir = d - a; float len = dir.magnitude; if (len < 0.1f) continue;
            var dirH = new Vector3(dir.x, 0f, dir.z); if (dirH.sqrMagnitude < 1e-6f) continue; dirH.Normalize();
            var right = new Vector3(dirH.z, 0f, -dirH.x);
            int zi = rb.ZoneIndex[i]; var zm = zmats[Mathf.Clamp(zi, 1, zmats.Length - 1)];
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var mid = (a + d) * 0.5f;
            bool tunnel = zi == 4;
            if (!tunnel || true)
            {
                bag.Box(zm.strip, mid + right * (half - 0.22f) + Vector3.up * 0.035f, rot, new Vector3(0.16f, 0.03f, len + 0.05f));
                bag.Box(zm.strip, mid - right * (half - 0.22f) + Vector3.up * 0.035f, rot, new Vector3(0.16f, 0.03f, len + 0.05f));
                res.strips += 2;
            }
            if (tunnel)
            {
                var concrete = TunnelMat(neon);
                float wallH = 6.2f, off = half + 1.0f;
                bag.Box(concrete, mid + right * off + Vector3.up * (wallH * 0.5f), rot, new Vector3(0.8f, wallH, len + 0.05f), 6f, 6f);
                bag.Box(concrete, mid - right * off + Vector3.up * (wallH * 0.5f), rot, new Vector3(0.8f, wallH, len + 0.05f), 6f, 6f);
                bag.Box(concrete, mid + Vector3.up * (wallH + 0.4f), rot, new Vector3(off * 2f + 1.6f, 0.8f, len + 0.05f), 6f, 6f);
                // ceiling light bar and wall wash strips (cyan zone tint)
                bag.Box(zm.signA, mid + Vector3.up * (wallH - 0.05f), rot, new Vector3(0.5f, 0.1f, len + 0.05f));
                bag.Box(zm.strip, mid + right * (off - 0.45f) + Vector3.up * 1.0f, rot, new Vector3(0.1f, 0.14f, len + 0.05f));
                bag.Box(zm.strip, mid - right * (off - 0.45f) + Vector3.up * 1.0f, rot, new Vector3(0.1f, 0.14f, len + 0.05f));
                res.props++;
            }
        }

        // ------------------------------------------------ light streaks and signs/buildings along the road
        var sakura = Plain(neon, "Sakura", new Color(0.55f, 0.28f, 0.38f), 0.3f, new Color(1f, 0.55f, 0.75f), 0.55f);
        var trunk = Plain(neon, "Trunk", new Color(0.08f, 0.05f, 0.05f), 0.2f, Color.black, 0f);
        var concreteMat = TunnelMat(neon);
        var torii = Plain(neon, "Torii", new Color(0.12f, 0.02f, 0.02f), 0.5f, new Color(1f, 0.16f, 0.08f), 1.6f);
        var railLamp = Plain(neon, "RailLamp", new Color(0.02f, 0.02f, 0.02f), 0.5f, new Color(0.5f, 1f, 0.8f), 3f);

        for (float km = 0.05f; km < endKm - 0.05f; km += 0.012f)
        {
            // keep a deterministic walk by distance; spacing handled per zone below
            var z = c.ZoneAtKm(km); if (z == null) continue;
            var rule = RuleFor(z.index);
            float stepKm = (rule.none ? 40f : rule.spacing) / 1000f;
            // quantise: only act on km grid points of this zone's spacing
            float cell = Mathf.Floor(km / stepKm);
            float cellKm = cell * stepKm + (stepKm * 0.5f);
            if (Mathf.Abs(km - cellKm) > 0.006f) { /* not on grid */ continue; }

            var zm = zmats[z.index];
            var pos = rb.PositionAtKm(km); var tan = rb.TangentAtKm(km);
            var tanH = new Vector3(tan.x, 0f, tan.z); if (tanH.sqrMagnitude < 1e-6f) continue; tanH.Normalize();
            var rightV = new Vector3(tanH.z, 0f, -tanH.x);
            var rotH = Quaternion.LookRotation(tanH, Vector3.up);

            // road light streak (reflection of the neon on a wet surface)
            if (rng.NextDouble() < (z.surface == "wet_asphalt" ? 0.9 : 0.35))
            {
                float lat = Rand(rng, -half + 1.2f, half - 1.2f), slen = Rand(rng, 6f, 18f), sw = Rand(rng, 0.25f, 0.9f);
                var sp = rb.PositionAtKm(km) + rightV * lat;
                var q = Quaternion.LookRotation(rb.TangentAtKm(km), Vector3.up);
                bag.Box(zm.streak, sp + Vector3.up * 0.045f, q, new Vector3(sw, 0.02f, slen));
                res.streaks++;
            }

            if (rule.none) { SideProps(z, km, pos, rightV, rotH, bag, torii, railLamp, concreteMat, sakura, trunk, rb, rng, res); continue; }

            // Skyline and street buildings are now supplied by the streamed Neo Tokyo architectural kit.
            SideProps(z, km, pos, rightV, rotH, bag, torii, railLamp, concreteMat, sakura, trunk, rb, rng, res);
        }

        // ------------------------------------------------ ground, sea
        var ground = Plain(neon, "Ground", new Color(0.03f, 0.03f, 0.04f), 0.15f, Color.black, 0f);
        var sea = Plain(neon, "Sea", new Color(0.01f, 0.03f, 0.07f), 0.97f, new Color(0.05f, 0.2f, 0.45f), 0.12f);
        bag.Box(ground, new Vector3(b.center.x, GroundY - 1f, b.center.z), Quaternion.identity, new Vector3(b.size.x + 14000f, 2f, b.size.z + 14000f), 40f, 40f);
        var sb = new Bounds(); bool any = false;
        for (int i = 0; i < rb.Positions.Length; i++)
            if (rb.Km[i] >= 21.0f) { var p = rb.Positions[i]; if (!any) { sb = new Bounds(p, Vector3.zero); any = true; } else sb.Encapsulate(p); }
        if (any) bag.Box(sea, new Vector3(sb.center.x, GroundY + 1.2f, sb.center.z), Quaternion.identity, new Vector3(sb.size.x + 3600f, 0.3f, sb.size.z + 3600f), 100f, 100f);

        // ------------------------------------------------ landmarks (from JSON, with fixed stylised placements)
        foreach (var lm in c.landmarks)
        {
            string n = lm.name.ToLowerInvariant();
            if (n.Contains("tokyo tower")) { TokyoTower(rb, lm.km, bag, neon, half); res.props++; }
            else if (n.Contains("fuji")) { Fuji(rb, lm.km, bag, neon, b); res.props++; }
            else if (n.Contains("rainbow")) { RainbowBridge(rb, lm.km, bag, neon, half, c); res.props++; }
            else if (n.Contains("ferris")) { FerrisWheel(rb, lm.km, bag, neon); res.props++; }
        }

        res.objects.AddRange(bag.Flush(parent, "Look"));
        return res;
    }

    static Material tunnelMat;
    static Material TunnelMat(List<NeonMat> neon)
    {
        if (tunnelMat == null) tunnelMat = ShuntaLookKit.Lit("TunnelConcrete", new Color(0.12f, 0.12f, 0.13f), 0.55f);
        return tunnelMat;
    }
    public static void ResetCaches() { tunnelMat = null; }

    // ------------------------------------------------------------------ zone-specific roadside props
    static void SideProps(ShuntaZone z, float km, Vector3 pos, Vector3 rightV, Quaternion rotH, ShuntaLookKit.MeshBag bag,
        Material torii, Material railLamp, Material concrete, Material sakura, Material trunk, ShuntaRouteBuilder rb, System.Random rng, Result res)
    {
        float half = rb.roadWidth * 0.5f;
        if (z.index == 7 && rng.NextDouble() < 0.18)
        {   // torii gate over the road
            float w = half + 1.4f;
            bag.Box(torii, pos + rightV * w + Vector3.up * 3.6f, rotH, new Vector3(0.7f, 7.2f, 0.7f));
            bag.Box(torii, pos - rightV * w + Vector3.up * 3.6f, rotH, new Vector3(0.7f, 7.2f, 0.7f));
            bag.Box(torii, pos + Vector3.up * 7.4f, rotH, new Vector3(w * 2f + 3f, 0.55f, 0.8f));
            bag.Box(torii, pos + Vector3.up * 6.2f, rotH, new Vector3(w * 2f, 0.4f, 0.5f));
        }
        if (z.index == 8)
        {   // railway viaduct columns and deck over the road, lit underneath
            bag.Box(concrete, pos + rightV * (half + 2.2f) + Vector3.up * 5f, rotH, new Vector3(1.4f, 10f, 1.4f), 4f, 4f);
            bag.Box(concrete, pos - rightV * (half + 2.2f) + Vector3.up * 5f, rotH, new Vector3(1.4f, 10f, 1.4f), 4f, 4f);
            bag.Box(concrete, pos + Vector3.up * 10.4f, rotH, new Vector3((half + 2.2f) * 2f + 3f, 1.0f, 26f), 6f, 6f);
            bag.Box(railLamp, pos + Vector3.up * 9.85f, rotH, new Vector3(1.2f, 0.12f, 20f));
            res.props++;
        }
        if (z.index >= 11 && rng.NextDouble() < (z.index == 12 ? 0.9 : 0.35))
        {   // stylised sakura trees both sides (finish zone is a pink pre-dawn avenue)
            foreach (int s in new[] { -1, 1 })
            {
                var tp = pos + rightV * (s * (half + Rand(rng, 3f, 6f)));
                float th = Rand(rng, 4f, 7f);
                bag.Box(trunk, tp + Vector3.up * th * 0.5f, rotH, new Vector3(0.4f, th, 0.4f));
                for (int k = 0; k < 3; k++)
                    bag.Box(sakura, tp + Vector3.up * (th + 0.8f + k * 0.3f) + rotH * new Vector3(Rand(rng, -1f, 1f), 0, Rand(rng, -1f, 1f)),
                        Quaternion.Euler(Rand(rng, 0, 40), Rand(rng, 0, 90), Rand(rng, 0, 40)) * rotH, Vector3.one * Rand(rng, 2.8f, 4.4f));
                res.props++;
            }
        }
    }

    // ------------------------------------------------------------------ landmarks

    static void TokyoTower(ShuntaRouteBuilder rb, float km, ShuntaLookKit.MeshBag bag, List<NeonMat> neon, float half)
    {
        var pos = rb.PositionAtKm(km); var tan = rb.TangentAtKm(km); tan.y = 0; tan.Normalize();
        var right = new Vector3(tan.z, 0, -tan.x);
        var baseP = pos + right * 380f; baseP.y = ShuntaLookScenery.GroundY;
        var red = Plain(neon, "TowerRed", new Color(0.3f, 0.04f, 0.02f), 0.4f, new Color(1f, 0.28f, 0.08f), 1.5f);
        var white = Plain(neon, "TowerWhite", new Color(0.4f, 0.4f, 0.4f), 0.4f, new Color(1f, 0.88f, 0.75f), 1.1f);
        var light = Plain(neon, "TowerLight", Color.black, 0.2f, new Color(1f, 0.85f, 0.6f), 4f);
        // tapered four-legged lattice: sections (height, half-width)
        float[] hs = { 0f, 40f, 90f, 150f, 200f, 250f, 300f };
        float[] ws = { 44f, 33f, 22f, 14f, 8f, 4.5f, 2f };
        for (int s = 0; s < hs.Length - 1; s++)
        {
            var mat = (s % 2 == 0) ? red : white;
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    var a = baseP + new Vector3(cx * ws[s], hs[s], cz * ws[s]);
                    var b = baseP + new Vector3(cx * ws[s + 1], hs[s + 1], cz * ws[s + 1]);
                    bag.Beam(mat, a, b, Mathf.Lerp(3.2f, 1f, s / 6f));
                }
            // rings and cross braces at the upper end of the section
            for (int e = 0; e < 4; e++)
            {
                var ang = e * 90f * Mathf.Deg2Rad; var ang2 = (e + 1) * 90f * Mathf.Deg2Rad;
                Vector3 c0 = new Vector3(Mathf.Sign(Mathf.Cos(ang + 0.7854f)), 0, Mathf.Sign(Mathf.Sin(ang + 0.7854f)));
                Vector3 c1 = new Vector3(Mathf.Sign(Mathf.Cos(ang2 + 0.7854f)), 0, Mathf.Sign(Mathf.Sin(ang2 + 0.7854f)));
                var p0 = baseP + new Vector3(c0.x * ws[s + 1], hs[s + 1], c0.z * ws[s + 1]);
                var p1 = baseP + new Vector3(c1.x * ws[s + 1], hs[s + 1], c1.z * ws[s + 1]);
                bag.Beam(mat, p0, p1, 1.1f);
                var q0 = baseP + new Vector3(c0.x * ws[s], hs[s], c0.z * ws[s]);
                bag.Beam(white, q0, p1, 0.7f);
            }
        }
        bag.Box(red, baseP + Vector3.up * 150f, Quaternion.identity, new Vector3(34f, 12f, 34f));       // main deck
        bag.Box(light, baseP + Vector3.up * 150f, Quaternion.identity, new Vector3(34.4f, 1.6f, 34.4f));
        bag.Box(white, baseP + Vector3.up * 250f, Quaternion.identity, new Vector3(14f, 8f, 14f));      // special deck
        bag.Box(light, baseP + Vector3.up * 250f, Quaternion.identity, new Vector3(14.4f, 1.1f, 14.4f));
        bag.Beam(red, baseP + Vector3.up * 300f, baseP + Vector3.up * 333f, 1.4f, 1.4f);                 // antenna
        bag.Box(light, baseP + Vector3.up * 334f, Quaternion.identity, new Vector3(1.4f, 2f, 1.4f));
    }

    static void Fuji(ShuntaRouteBuilder rb, float km, ShuntaLookKit.MeshBag bag, List<NeonMat> neon, Bounds route)
    {
        // a silhouette far to the west-south-west; stylised (about 1/2 real height, far larger than it looks)
        var origin = new Vector3(route.center.x - 9500f, GroundY, route.center.z - 3500f);
        var body = Plain(neon, "FujiBody", new Color(0.03f, 0.03f, 0.08f), 0.2f, new Color(0.12f, 0.08f, 0.3f), 0.35f);
        var snow = Plain(neon, "FujiSnow", new Color(0.35f, 0.35f, 0.45f), 0.2f, new Color(0.7f, 0.65f, 0.95f), 0.5f);
        const int N = 14; float R = 5200f, H = 2000f, snowAt = 0.74f;
        var rs = new List<float>(); var hsL = new List<float>();
        for (int i = 0; i <= N; i++) { float t = i / (float)N; rs.Add(R * Mathf.Pow(1f - t, 1.9f)); hsL.Add(H * t); }
        var rb1 = new List<float>(); var hb1 = new List<float>(); var rs1 = new List<float>(); var hs1 = new List<float>();
        for (int i = 0; i <= N; i++) { if (hsL[i] / H <= snowAt) { rb1.Add(rs[i]); hb1.Add(hsL[i]); } }
        // overlap one ring so the cap meets the body
        for (int i = 0; i <= N; i++) { if (hsL[i] / H >= snowAt - H * 0.0f) { rs1.Add(rs[i]); hs1.Add(hsL[i]); } }
        if (rb1.Count > 0) { int j = rb1.Count; if (j < rs.Count) { rb1.Add(rs[j]); hb1.Add(hsL[j]); } }
        bag.Lathe(body, origin, rb1.ToArray(), hb1.ToArray(), 40);
        if (rs1.Count > 1) bag.Lathe(snow, origin, rs1.ToArray(), hs1.ToArray(), 40);
    }

    static void RainbowBridge(ShuntaRouteBuilder rb, float km, ShuntaLookKit.MeshBag bag, List<NeonMat> neon, float half, ShuntaCourseData c)
    {
        var steel = Plain(neon, "BridgeSteel", new Color(0.6f, 0.6f, 0.65f), 0.5f, new Color(1f, 0.55f, 0.85f), 0.55f);
        var cable = Plain(neon, "BridgeCable", new Color(0.7f, 0.7f, 0.7f), 0.5f, new Color(1f, 0.85f, 0.95f), 1.3f);
        var lamp = Plain(neon, "BridgeLamp", Color.black, 0.3f, new Color(1f, 0.4f, 0.8f), 4f);
        float k0 = 21.0f, k1 = 24.0f, t0 = 21.8f, t1 = 23.25f;
        float lat = half + 4.5f, towerTop = 85f;
        Vector3 Pt(float k, float l, float up)
        {
            var p = rb.PositionAtKm(k); var tg = rb.TangentAtKm(k); tg.y = 0; if (tg.sqrMagnitude < 1e-6f) tg = Vector3.forward; tg.Normalize();
            var r = new Vector3(tg.z, 0, -tg.x);
            return p + r * l + Vector3.up * up;
        }
        foreach (float tk in new[] { t0, t1 })
            foreach (int s in new[] { -1, 1 })
            {
                var bot = Pt(tk, s * lat, -(rb.PositionAtKm(tk).y - GroundY));
                var top = Pt(tk, s * lat, towerTop);
                bag.Beam(steel, bot, top, 3.2f, 3.2f);
                bag.Box(lamp, top + Vector3.up * 1f, Quaternion.identity, new Vector3(2f, 2f, 2f));
            }
        foreach (float tk in new[] { t0, t1 })
            foreach (float hh in new[] { 30f, 55f, towerTop - 3f })
                bag.Beam(steel, Pt(tk, -lat, hh), Pt(tk, lat, hh), 2.4f, 2.4f);
        // main cables with catenary sag, hangers to the deck
        float step = 0.02f;
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 prev = Vector3.zero; bool hasPrev = false;
            for (float k = k0; k <= k1 + 0.0001f; k += step)
            {
                float h;
                if (k < t0) h = Mathf.Lerp(3f, towerTop, Mathf.SmoothStep(0, 1, (k - k0) / (t0 - k0)));
                else if (k <= t1) { float tt = (k - t0) / (t1 - t0); h = Mathf.Lerp(towerTop, towerTop, tt) - 4f * 55f * tt * (1f - tt) * 1.0f; }
                else h = Mathf.Lerp(towerTop, 3f, Mathf.SmoothStep(0, 1, (k - t1) / (k1 - t1)));
                var p = Pt(k, s * lat, h);
                if (hasPrev) bag.Beam(cable, prev, p, 0.7f);
                prev = p; hasPrev = true;
                if (Mathf.Abs((k / 0.06f) - Mathf.Round(k / 0.06f)) < 0.2f)
                    bag.Beam(cable, Pt(k, s * lat, 0f), p, 0.18f);
            }
        }
        // deck edge girders
        for (float k = k0; k <= k1; k += 0.03f)
            foreach (int s in new[] { -1, 1 })
                bag.Beam(steel, Pt(k, s * (half + 0.4f), -1.2f), Pt(k + 0.03f, s * (half + 0.4f), -1.2f), 1.6f, 2.4f);
    }

    static void FerrisWheel(ShuntaRouteBuilder rb, float km, ShuntaLookKit.MeshBag bag, List<NeonMat> neon)
    {
        var pos = rb.PositionAtKm(km); var tan = rb.TangentAtKm(km); tan.y = 0; tan.Normalize();
        var right = new Vector3(tan.z, 0, -tan.x);
        var centre = pos + right * 170f; centre.y = GroundY + 62f;
        var axis = tan;   // wheel plane contains up and right; spin axis along the road tangent
        var rim = Plain(neon, "WheelRim", new Color(0.4f, 0.4f, 0.45f), 0.5f, new Color(0.5f, 0.9f, 1f), 1.8f);
        var frame = Plain(neon, "WheelFrame", new Color(0.35f, 0.35f, 0.4f), 0.5f, new Color(0.6f, 0.6f, 0.9f), 0.5f);
        var gondolas = new Material[6];
        Color[] cols = { new Color(1f, 0.2f, 0.25f), new Color(1f, 0.6f, 0.15f), new Color(1f, 0.95f, 0.3f), new Color(0.2f, 1f, 0.5f), new Color(0.25f, 0.6f, 1f), new Color(0.8f, 0.3f, 1f) };
        for (int i = 0; i < 6; i++) gondolas[i] = Plain(neon, "Gondola" + i, new Color(0.05f, 0.05f, 0.05f), 0.5f, cols[i], 4f);
        float R = 52f; int seg = 40;
        Vector3 Ring(float a, float r) => centre + Vector3.up * (Mathf.Cos(a) * r) + right * (Mathf.Sin(a) * r);
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            bag.Beam(rim, Ring(a0, R) + axis * 1.5f, Ring(a1, R) + axis * 1.5f, 1.0f);
            bag.Beam(rim, Ring(a0, R) - axis * 1.5f, Ring(a1, R) - axis * 1.5f, 1.0f);
            bag.Beam(rim, Ring(a0, R * 0.55f), Ring(a1, R * 0.55f), 0.6f);
            if (i % 2 == 0) bag.Beam(frame, centre, Ring(a0, R), 0.5f);
            if (i % 2 == 1) bag.Beam(frame, Ring(a0, R * 0.55f), Ring(a0 + 2f * Mathf.PI / seg * 1.0f, R), 0.3f);
            if (i % 2 == 0)
            {
                var g = Ring(a0, R + 2.5f) + Vector3.down * 0f;
                bag.Box(gondolas[(i / 2) % 6], g, Quaternion.LookRotation(axis, Vector3.up), new Vector3(2.4f, 3f, 3f));
            }
        }
        // A-frame supports and hub
        foreach (int s in new[] { -1, 1 })
        {
            bag.Beam(frame, centre + axis * (s * 3f), new Vector3(centre.x, GroundY, centre.z) + axis * (s * 3f) + right * 36f, 2.2f);
            bag.Beam(frame, centre + axis * (s * 3f), new Vector3(centre.x, GroundY, centre.z) + axis * (s * 3f) - right * 36f, 2.2f);
        }
        bag.Box(rim, centre, Quaternion.LookRotation(axis, Vector3.up), new Vector3(5f, 5f, 8f));
    }
}

