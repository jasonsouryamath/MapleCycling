using System.Collections.Generic;
using UnityEngine;

// Skyline hero landmarks: Tokyo Tower, Mt. Fuji, Odaiba ferris wheel, finish arch, sakura avenue + embankment.
public sealed partial class ShuntaLandmarkSet
{
    // ------------------------------------------------------------------ Tokyo Tower

    Vector3 PickTowerSite()
    {
        // 2026-10-04: the twisty 11.6 x 8.2 km course moved the old fixed search window ~1 km away from the road. Place the tower beside the
        // skyline expressway (landmark km 9.5): 240-420 m off the road on whichever side keeps >= 140 m clear of every other stretch.
        var P = route.Positions;
        int i0 = IdxAtKm(9.5f);
        var ptan = P[Mathf.Min(i0 + 1, P.Length - 1)] - P[Mathf.Max(i0 - 1, 0)]; ptan.y = 0f; ptan.Normalize();
        var side = new Vector3(ptan.z, 0f, -ptan.x);
        Vector3 best = P[i0] + side * 300f; float bestScore = -1f;
        for (int sg = -1; sg <= 1; sg += 2)
            for (float lat = 240f; lat <= 420f; lat += 30f)
                for (float along = -120f; along <= 120f; along += 60f)
                {
                    var c = P[i0] + side * (sg * lat) + ptan * along; float d = float.MaxValue;
                    for (int i = 0; i < P.Length; i += 2)
                    {
                        if (Mathf.Abs(i - i0) < 4) { }
                        float dx = P[i].x - c.x, dz = P[i].z - c.z; float dd = dx * dx + dz * dz; if (dd < d) d = dd;
                    }
                    float clear = Mathf.Sqrt(d);
                    if (clear < 140f) continue;
                    float score = -Mathf.Abs(clear - 260f);          // closest to the 260 m sweet spot wins
                    if (score > bestScore) { bestScore = score; best = c; }
                }
        rep.Append($"towerSite clear~{(260f + bestScore):F0}m; ");
        best.y = GroundY; TowerBase = best; return best;
    }

    void BuildTokyoTower()
    {
        var root = Child("Tokyo Tower"); var bag = new ShuntaLookKit.MeshBag();
        var orange = M("TTOrange", new Color(0.75f, 0.22f, 0.06f), 0.35f, new Color(1f, 0.36f, 0.07f), 1.35f);
        var white = M("TTWhite", new Color(0.8f, 0.8f, 0.82f), 0.35f, new Color(1f, 0.9f, 0.78f), 1.1f);
        var window = M("TTWindow", new Color(0.3f, 0.25f, 0.15f), 0.3f, new Color(1f, 0.78f, 0.4f), 2.8f);
        var baseM = M("TTBase", new Color(0.25f, 0.2f, 0.2f), 0.3f, new Color(1f, 0.55f, 0.25f), 0.45f);
        var beaconM = M("TTBeacon", new Color(0.4f, 0.1f, 0.1f), 0.3f, new Color(1f, 0.25f, 0.2f), 6f);
        var haloM = M("TTHalo", new Color(0.4f, 0.1f, 0.1f), 0.3f, new Color(1f, 0.3f, 0.25f), 2.2f);

        var b = PickTowerSite(); const float S = 0.85f;
        float W(float h) => h <= 250f ? 5.5f + 34.5f * Mathf.Pow(Mathf.Clamp01(1f - h / 250f), 1.3f) : Mathf.Lerp(5.5f, 1.6f, (h - 250f) / 50f);
        Vector3 C(float h, int sx, int sz) => b + new Vector3(sx * W(h) * S, h * S, sz * W(h) * S);
        float Th(float h) => Mathf.Lerp(2.8f, 1.0f, h / 300f) * S * 1.1f;

        var panels = new List<float>();
        for (int i = 0; i <= 10; i++) panels.Add(150f * i / 10f);
        for (int i = 1; i <= 6; i++) panels.Add(150f + 100f * i / 6f);
        for (int i = 1; i <= 4; i++) panels.Add(250f + 50f * i / 4f);
        for (int p = 0; p < panels.Count - 1; p++)
        {
            float h0 = panels[p], h1 = panels[p + 1];
            var mat = ((int)(h0 / 30f) % 2 == 0) ? orange : white;
            var mat2 = ReferenceEquals(mat, orange) ? white : orange;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2) bag.Beam(mat, C(h0, sx, sz), C(h1, sx, sz), Th(h0), Th(h1));
            for (int e = 0; e < 4; e++)
            {
                int ax = (e == 0 || e == 3) ? -1 : 1, az = e < 2 ? -1 : 1;           // corner A
                int bx = (e == 0 || e == 1) ? -1 : 1, bz = (e == 1 || e == 2) ? -1 : 1; // corner B (next around the ring)
                // faces: (-1,-1)->(-1,1)->(1,1)->(1,-1)
                int[] fx = { -1, -1, 1, 1 }, fz = { -1, 1, 1, -1 };
                ax = fx[e]; az = fz[e]; bx = fx[(e + 1) % 4]; bz = fz[(e + 1) % 4];
                bag.Beam(mat, C(h1, ax, az), C(h1, bx, bz), 0.9f * S * 1.1f);
                bag.Beam(mat2, C(h0, ax, az), C(h1, bx, bz), 0.55f * S * 1.1f);
                bag.Beam(mat2, C(h0, bx, bz), C(h1, ax, az), 0.55f * S * 1.1f);
            }
        }
        // decks
        float w150 = W(150f) * S;
        bag.Box(orange, b + Vector3.up * 146f * S, Quaternion.identity, new Vector3(w150 * 2f + 6f, 9f * S, w150 * 2f + 6f));
        bag.Box(window, b + Vector3.up * 145f * S, Quaternion.identity, new Vector3(w150 * 2f + 6.6f, 3.4f * S, w150 * 2f + 6.6f));
        bag.Box(white, b + Vector3.up * 152.5f * S, Quaternion.identity, new Vector3(w150 * 2f + 6.4f, 2.4f * S, w150 * 2f + 6.4f));
        bag.Box(orange, b + Vector3.up * 155f * S, Quaternion.identity, new Vector3(w150 * 2f - 4f, 3f * S, w150 * 2f - 4f));
        float w250 = W(250f) * S;
        bag.Box(white, b + Vector3.up * 249f * S, Quaternion.identity, new Vector3(w250 * 2f + 5f, 7f * S, w250 * 2f + 5f));
        bag.Box(window, b + Vector3.up * 248f * S, Quaternion.identity, new Vector3(w250 * 2f + 5.6f, 2.6f * S, w250 * 2f + 5.6f));
        bag.Box(orange, b + Vector3.up * 254f * S, Quaternion.identity, new Vector3(w250 * 2f + 2f, 2.5f * S, w250 * 2f + 2f));
        // base building between the legs
        bag.Box(baseM, b + Vector3.up * 6f * S, Quaternion.identity, new Vector3(W(0f) * S * 1.3f, 12f * S, W(0f) * S * 1.3f));
        bag.Box(window, b + Vector3.up * 5f * S, Quaternion.identity, new Vector3(W(0f) * S * 1.34f, 2.4f * S, W(0f) * S * 1.34f));
        // antenna with banding and the beacon
        for (int s = 0; s < 6; s++)
        {
            float a0 = 300f + s * 6f, a1 = a0 + 6f;
            bag.Beam(s % 2 == 0 ? orange : white, b + Vector3.up * a0 * S, b + Vector3.up * a1 * S, Mathf.Lerp(1.5f, 0.8f, s / 5f) * S * 1.2f);
        }
        var tip = b + Vector3.up * 339f * S;
        bag.Box(beaconM, tip, Quaternion.identity, new Vector3(3.4f, 3.4f, 3.4f) * S * 1.4f);
        bag.Box(haloM, tip, Quaternion.identity, new Vector3(9f, 1f, 1f) * S * 1.4f);
        bag.Box(haloM, tip, Quaternion.identity, new Vector3(1f, 1f, 9f) * S * 1.4f);
        bag.Box(haloM, tip, Quaternion.identity, new Vector3(1f, 9f, 1f) * S * 1.4f);
        Flush(bag, root, "TokyoTower");
        motion.beacon = beaconM; motion.beaconRgb = new Color(1f, 0.25f, 0.2f); motion.beaconRel = 6f;
    }

    // ------------------------------------------------------------------ Mt. Fuji

    static void TriBoth(ShuntaLookKit.MeshBag bag, Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        bag.Tri(m, a, b, c, n, n, n); bag.Tri(m, a, c, b, -n, -n, -n);
    }

    void BuildFuji()
    {
        var root = Child("Mt Fuji"); var bag = new ShuntaLookKit.MeshBag();
        // evening gradient bands (cool indigo foot -> violet -> warm rose -> sunlit snow cap)
        var b0 = M("FujiFoot", new Color(0.10f, 0.10f, 0.28f), 0.15f, new Color(0.20f, 0.17f, 0.50f), 0.75f);
        var b1 = M("FujiLow", new Color(0.30f, 0.14f, 0.38f), 0.15f, new Color(0.50f, 0.22f, 0.58f), 0.85f);
        var b2 = M("FujiMid", new Color(0.55f, 0.24f, 0.36f), 0.15f, new Color(0.95f, 0.38f, 0.50f), 0.95f);
        var b3 = M("FujiHigh", new Color(0.62f, 0.34f, 0.36f), 0.15f, new Color(1.00f, 0.55f, 0.42f), 1.05f);
        var snow = M("FujiSnow", new Color(0.8f, 0.78f, 0.85f), 0.3f, new Color(1.0f, 0.88f, 0.92f), 1.5f);
        var gully = M("FujiGully", new Color(0.8f, 0.78f, 0.85f), 0.3f, new Color(1.0f, 0.86f, 0.9f), 1.3f);
        var hills = M("FujiHills", new Color(0.08f, 0.08f, 0.2f), 0.1f, new Color(0.14f, 0.12f, 0.36f), 0.7f);

        var origin = Bias + new Vector3(3800f, 0f, 8300f); origin.y = GroundY - 2f; FujiBase = origin;
        const float R = 2500f, H = 1500f, P = 1.75f; const int seg = 56;
        float Rt(float t) => R * Mathf.Pow(1f - t * 0.97f, P);
        void Band(Material m, float t0, float t1, int steps)
        {
            var rs = new float[steps + 1]; var hs = new float[steps + 1];
            for (int i = 0; i <= steps; i++) { float t = Mathf.Lerp(t0, t1, i / (float)steps); rs[i] = Rt(t); hs[i] = H * t; }
            bag.Lathe(m, origin, rs, hs, seg);
        }
        Band(b0, 0f, 0.20f, 5); Band(b1, 0.20f, 0.42f, 5); Band(b2, 0.42f, 0.62f, 5); Band(b3, 0.62f, 0.74f, 3);
        Band(snow, 0.74f, 1.0f, 8);
        // crater: dip the centre
        bag.Lathe(snow, origin, new[] { Rt(1f), 0f }, new[] { H, H * 0.93f }, seg);
        // snow gullies running down the slope below the cap
        var rng = new System.Random(77);
        for (int g = 0; g < 22; g++)
        {
            float a = (g / 22f + Rand(rng, -0.015f, 0.015f)) * Mathf.PI * 2f, da = Rand(rng, 0.018f, 0.03f);
            float tTop = 0.76f, tBot = Rand(rng, 0.46f, 0.66f);
            Vector3 Pt(float t, float aa) { float r = Rt(t) * 1.015f; return origin + new Vector3(Mathf.Cos(aa) * r, H * t, Mathf.Sin(aa) * r); }
            var n = new Vector3(Mathf.Cos(a), 0.55f, Mathf.Sin(a)).normalized;
            TriBoth(bag, gully, Pt(tTop, a - da), Pt(tTop, a + da), Pt(tBot, a), n);
        }
        // low foothills ringing the base so the cone does not float on the plain
        for (int k = 0; k < 9; k++)
        {
            float a = k / 9f * Mathf.PI * 2f + 0.3f; float dist = R * Rand(rng, 0.85f, 1.35f);
            var o = origin + new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist);
            float hr = Rand(rng, 700f, 1100f), hh = Rand(rng, 160f, 330f);
            var rs = new[] { hr, hr * 0.7f, hr * 0.35f, 0f }; var hs = new[] { 0f, hh * 0.55f, hh * 0.9f, hh };
            bag.Lathe(hills, o, rs, hs, 16);
        }
        Flush(bag, root, "Fuji");
    }

    // ------------------------------------------------------------------ Odaiba ferris wheel

    void BuildFerrisWheel()
    {
        var root = Child("Ferris Wheel");
        float km = 26.0f; int i = IdxAtKm(km);
        var pos = route.Positions[i]; var right = RightAt(i);
        var f = route.Positions[Mathf.Min(i + 1, route.Positions.Length - 1)] - route.Positions[Mathf.Max(i - 1, 0)]; f.y = 0f;
        var tan = f.normalized; // spin axis = road tangent
        const float R = 58f;
        var centre = pos + right * 190f; centre.y = GroundY + R + 14f; WheelCentre = centre;
        var rim = M("WheelRim", new Color(0.55f, 0.58f, 0.62f), 0.5f, new Color(0.4f, 0.9f, 1f), 1.8f);
        var spokeA = M("WheelSpokeA", new Color(0.55f, 0.5f, 0.6f), 0.4f, new Color(1f, 0.4f, 0.75f), 1.3f);
        var spokeB = M("WheelSpokeB", new Color(0.5f, 0.55f, 0.6f), 0.4f, new Color(0.4f, 0.8f, 1f), 1.3f);
        var frame = M("WheelFrame", new Color(0.55f, 0.55f, 0.6f), 0.5f, new Color(0.65f, 0.6f, 0.95f), 0.55f);
        var hub = M("WheelHub", new Color(0.5f, 0.5f, 0.55f), 0.5f, new Color(1f, 0.85f, 0.6f), 2.2f);
        var roofM = M("GondolaRoof", new Color(0.35f, 0.35f, 0.4f), 0.5f, new Color(0.7f, 0.7f, 0.9f), 0.3f);
        Color[] cols = { new Color(1f, 0.25f, 0.3f), new Color(1f, 0.62f, 0.2f), new Color(1f, 0.95f, 0.35f), new Color(0.3f, 1f, 0.55f), new Color(0.3f, 0.7f, 1f), new Color(0.85f, 0.4f, 1f) };
        var gMats = new Material[6];
        for (int k = 0; k < 6; k++) gMats[k] = M("Gondola" + k, new Color(cols[k].r * 0.5f, cols[k].g * 0.5f, cols[k].b * 0.5f), 0.4f, cols[k], 3.2f);

        var rot = Quaternion.LookRotation(tan, Vector3.up);
        // static supports in world space
        var sbag = new ShuntaLookKit.MeshBag();
        Vector3 Wp(float x, float y, float z) => centre + rot * new Vector3(x, y, z);
        foreach (int s in new[] { -1, 1 })
        {
            var top = Wp(0f, 0f, s * 4f); var footL = new Vector3(centre.x, GroundY, centre.z) + rot * new Vector3(-40f, 0f, s * 8f);
            var footR = new Vector3(centre.x, GroundY, centre.z) + rot * new Vector3(40f, 0f, s * 8f);
            sbag.Beam(frame, top, footL, 3f); sbag.Beam(frame, top, footR, 3f);
            sbag.Beam(frame, Vector3.Lerp(top, footL, 0.45f), Vector3.Lerp(top, footR, 0.45f), 1.4f);
        }
        sbag.Beam(frame, Wp(0f, 0f, -8f), Wp(0f, 0f, 8f), 5f);
        Flush(sbag, root, "WheelSupport");

        // spinning wheel (local space: x = right, y = up, z = road tangent = spin axis)
        var wgo = new GameObject("Wheel") { hideFlags = HideFlags.DontSave };
        wgo.transform.SetParent(root, false); wgo.transform.SetPositionAndRotation(centre, rot);
        var wb = new ShuntaLookKit.MeshBag();
        const int seg = 36;
        Vector3 Ring(float a, float r, float z) => new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, z);
        for (int k = 0; k < seg; k++)
        {
            float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
            foreach (float z in new[] { -2f, 2f })
            {
                wb.Beam(rim, Ring(a0, R, z), Ring(a1, R, z), 1.3f);
                wb.Beam(rim, Ring(a0, R * 0.6f, z), Ring(a1, R * 0.6f, z), 0.8f);
            }
            wb.Beam(frame, Ring(a0, R, -2f), Ring(a0, R, 2f), 0.8f);
            if (k % 2 == 0)
            {
                var sm = (k / 2) % 2 == 0 ? spokeA : spokeB;
                foreach (float z in new[] { -2f, 2f }) wb.Beam(sm, new Vector3(0, 0, z), Ring(a0, R, z), 0.55f);
            }
            else
                foreach (float z in new[] { -2f, 2f }) wb.Beam(frame, Ring(a0, R * 0.6f, z), Ring(a0 + Mathf.PI * 2f / seg, R, z), 0.3f);
        }
        wb.Box(hub, Vector3.zero, Quaternion.identity, new Vector3(8f, 8f, 9f));
        Flush(wb, wgo.transform, "WheelBody");

        // gondolas: pivot on the rim, cabin hangs below and the pivot is kept world-upright by the motion script
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh; DestroyNow(cube);
        int gn = 20; var gl = new List<Transform>();
        for (int k = 0; k < gn; k++)
        {
            float a = k * Mathf.PI * 2f / gn;
            var pv = new GameObject("Gondola " + k) { hideFlags = HideFlags.DontSave };
            pv.transform.SetParent(wgo.transform, false); pv.transform.localPosition = Ring(a, R + 1f, 0f);
            Cab(pv.transform, "Cabin", cubeMesh, gMats[k % 6], new Vector3(0f, -2.3f, 0f), new Vector3(3.2f, 3.0f, 3.6f));
            Cab(pv.transform, "Roof", cubeMesh, roofM, new Vector3(0f, -0.6f, 0f), new Vector3(3.6f, 0.35f, 4.0f));
            Cab(pv.transform, "Hanger", cubeMesh, frame, new Vector3(0f, 0.2f, 0f), new Vector3(0.2f, 1.6f, 0.2f));
            gl.Add(pv.transform);
        }
        motion.wheel = wgo.transform; motion.wheelBase = rot; motion.wheelDegPerSec = 3.5f;
        motion.gondolas = gl.ToArray(); motion.gondolaWorld = rot; motion.rim = rim; motion.rimRel = 1.8f;
    }

    void Cab(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Vector3 scale)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------------ finish arch with maple leaf

    static readonly float[] LeafAng = { 0, 12, 26, 38, 55, 66, 78, 100, 118, 135, 150, 165, 172, 180 };
    static readonly float[] LeafR = { 1.00f, 0.60f, 0.68f, 0.50f, 0.92f, 1.00f, 0.60f, 0.78f, 0.88f, 0.55f, 0.40f, 0.45f, 0.30f, 0.80f };

    List<Vector2> MapleLeaf(float size)
    {
        var o = new List<Vector2>();
        for (int i = 0; i < LeafAng.Length; i++) { float a = LeafAng[i] * Mathf.Deg2Rad; o.Add(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * LeafR[i] * size); }
        for (int i = LeafAng.Length - 2; i >= 1; i--) { float a = -LeafAng[i] * Mathf.Deg2Rad; o.Add(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * LeafR[i] * size); }
        return o;
    }

    void BuildFinishArch()
    {
        var root = Child("Finish Arch"); var bag = new ShuntaLookKit.MeshBag();
        int last = route.Positions.Length - 1;
        var pos = route.Positions[last]; var f = route.Positions[last] - route.Positions[last - 2]; f.y = 0f; f.Normalize();
        var rot = Quaternion.LookRotation(f, Vector3.up);
        Vector3 L(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
        var pillar = M("ArchPillar", new Color(0.85f, 0.75f, 0.8f), 0.4f, new Color(1f, 0.6f, 0.8f), 0.8f);
        var arc = M("ArchArc", new Color(0.9f, 0.8f, 0.85f), 0.4f, new Color(1f, 0.72f, 0.88f), 1.4f);
        var panel = M("ArchPanel", new Color(0.35f, 0.07f, 0.1f), 0.3f, new Color(0.9f, 0.1f, 0.2f), 1.0f);
        var leaf = M("ArchLeaf", new Color(0.8f, 0.1f, 0.12f), 0.3f, new Color(1f, 0.12f, 0.15f), 3.2f);
        var ring = M("ArchRing", new Color(0.9f, 0.9f, 0.9f), 0.3f, new Color(1f, 0.95f, 0.9f), 3.0f);
        float span = Half + 4.2f, hP = 10f, rise = 6.5f;
        foreach (int s in new[] { -1, 1 })
        {
            bag.Box(pillar, L(s * span, hP * 0.5f, 0f), rot, new Vector3(1.8f, hP, 1.8f));
            bag.Box(arc, L(s * span, 0.4f, 0f), rot, new Vector3(2.6f, 0.8f, 2.6f));
        }
        // arch: parabola from the pillar tops to the crown
        int n = 18; Vector3 prev = Vector3.zero;
        for (int k = 0; k <= n; k++)
        {
            float u = k / (float)n; float x = Mathf.Lerp(-span, span, u); float y = hP + rise * (1f - (2f * u - 1f) * (2f * u - 1f));
            var p = L(x, y, 0f);
            if (k > 0) { bag.Beam(arc, prev, p, 1.5f, 1.5f); }
            prev = p;
        }
        // banner panel + roundel + maple leaf, facing the approaching rider (front is -forward)
        bag.Box(panel, L(0f, hP + rise + 0.2f, 0.25f), rot, new Vector3(span * 2f - 2.5f, 3.0f, 0.5f));
        // roundel (disc) and leaf, both wound to face -forward
        Vector3 Q(float x, float y) => L(x, hP + rise + 5.6f + y, -0.4f);
        int rs = 28; float rr = 4.6f;
        for (int k = 0; k < rs; k++)
        {
            float a0 = k * Mathf.PI * 2f / rs, a1 = (k + 1) * Mathf.PI * 2f / rs;
            var nrm = rot * Vector3.back;
            TriBoth(bag, ring, Q(Mathf.Sin(a0) * rr, Mathf.Cos(a0) * rr), Q(Mathf.Sin(a1) * rr, Mathf.Cos(a1) * rr), Q(0, 0), nrm);
        }
        var ol = MapleLeaf(3.9f);
        for (int k = 0; k < ol.Count; k++)
        {
            var a = ol[k]; var b = ol[(k + 1) % ol.Count];
            TriBoth(bag, leaf, Q(a.x, a.y - 0.4f) + rot * new Vector3(0, 0, -0.25f), Q(b.x, b.y - 0.4f) + rot * new Vector3(0, 0, -0.25f), Q(0, -0.4f) + rot * new Vector3(0, 0, -0.25f), rot * Vector3.back);
        }
        // sakura-pink garlands along the banner
        for (int k = -6; k <= 6; k++) bag.Box(leaf, L(k * 1.3f, hP + rise - 2.1f, 0f), rot, new Vector3(0.5f, 0.5f, 0.5f));
        Flush(bag, root, "FinishArch");
    }

    // ------------------------------------------------------------------ sakura avenue and embankment (zones 11-12)

    void BuildSakura()
    {
        var root = Child("Sakura Avenue"); var bag = new ShuntaLookKit.MeshBag();
        var trunk = M("SakuraTrunk", new Color(0.24f, 0.14f, 0.12f), 0.2f, new Color(0.3f, 0.15f, 0.12f), 0.12f);
        var pinkA = M("SakuraPinkA", new Color(0.85f, 0.5f, 0.62f), 0.2f, new Color(1f, 0.6f, 0.78f), 0.55f);
        var pinkB = M("SakuraPinkB", new Color(0.9f, 0.62f, 0.7f), 0.2f, new Color(1f, 0.74f, 0.85f), 0.65f);
        var earth = M("Embankment", new Color(0.16f, 0.22f, 0.15f), 0.1f, new Color(0.1f, 0.2f, 0.12f), 0.22f);
        var P = route.Positions; var rng = new System.Random(2026);
        int i0 = IdxAtKm(24.0f), i1 = IdxAtKm(route.Km[route.Km.Length - 1] - 0.05f);
        // embankment under the elevated coast road so trees stand on solid ground at road level
        BuildEmbankment(root, i0, i1, earth);
        int trees = 0;
        for (int i = i0; i <= i1; i += 3)
        {
            var r = RightAt(i);
            foreach (int s in new[] { -1, 1 })
            {
                if (rng.NextDouble() < 0.12) continue;
                float lat = Half + Rand(rng, 6f, 9.5f);
                var o = P[i] + r * (s * lat) + Vector3.up * -0.1f;
                float th = Rand(rng, 5f, 7.5f), cr = Rand(rng, 3.4f, 4.6f);
                bag.Lathe(trunk, o, new[] { 0.55f, 0.38f, 0.26f }, new[] { 0f, th * 0.5f, th }, 7);
                for (int k = 0; k < 3; k++)
                {
                    float ang = k * 2.1f + Rand(rng, 0f, 1f);
                    bag.Beam(trunk, o + Vector3.up * th * 0.8f, o + Vector3.up * (th + 1.6f) + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 2.2f, 0.28f);
                }
                for (int k = 0; k < 5; k++)
                {
                    float ang = k * 1.256f + Rand(rng, 0f, 0.6f), d = k == 0 ? 0f : cr * 0.55f;
                    float br = cr * Rand(rng, 0.62f, 0.85f);
                    var c = o + new Vector3(Mathf.Cos(ang) * d, th + 1.6f + Rand(rng, -0.4f, 1.1f), Mathf.Sin(ang) * d);
                    bag.Lathe((k + i) % 2 == 0 ? pinkA : pinkB, c - Vector3.up * br * 0.7f,
                        new[] { 0f, br * 0.7f, br, br * 0.8f, br * 0.45f, 0f }, new[] { 0f, br * 0.25f, br * 0.7f, br * 1.1f, br * 1.35f, br * 1.4f }, 9);
                }
                trees++;
            }
        }
        rep.Append($"sakura trees {trees}; ");
        Flush(bag, root, "Sakura");
    }

    void BuildEmbankment(Transform root, int i0, int i1, Material mat)
    {
        var P = route.Positions; var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float shoulder = Half + 12f, slope = 0.9f, grassTop = -0.2f;
        for (int i = i0; i < i1; i++)
        {
            float y0 = P[i].y + grassTop, y1 = P[i + 1].y + grassTop;
            if (y0 - GroundY < 6f && y1 - GroundY < 6f) continue;
            var r0 = RightAt(i); var r1 = RightAt(i + 1);
            float d0 = Mathf.Max(0f, y0 - GroundY) * slope, d1 = Mathf.Max(0f, y1 - GroundY) * slope;
            Vector3 SL0 = new Vector3(P[i].x, y0, P[i].z) - r0 * shoulder, SR0 = new Vector3(P[i].x, y0, P[i].z) + r0 * shoulder;
            Vector3 SL1 = new Vector3(P[i + 1].x, y1, P[i + 1].z) - r1 * shoulder, SR1 = new Vector3(P[i + 1].x, y1, P[i + 1].z) + r1 * shoulder;
            Vector3 GL0 = new Vector3(SL0.x, GroundY, SL0.z) - r0 * d0, GR0 = new Vector3(SR0.x, GroundY, SR0.z) + r0 * d0;
            Vector3 GL1 = new Vector3(SL1.x, GroundY, SL1.z) - r1 * d1, GR1 = new Vector3(SR1.x, GroundY, SR1.z) + r1 * d1;
            Vector2 u = Vector2.zero;
            Quad(v, nr, uv, t, SL0, SR0, SR1, SL1, Vector3.up, u, u, u, u);
            Quad(v, nr, uv, t, GL0, SL0, SL1, GL1, -r0 + Vector3.up * 0.6f, u, u, u, u);
            Quad(v, nr, uv, t, SR0, GR0, GR1, SR1, r0 + Vector3.up * 0.6f, u, u, u, u);
        }
        if (v.Count == 0) return;
        var mesh = new Mesh { name = "LM_Embankment", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetNormals(nr); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
        MeshObject(root, "Embankment", mesh, mat);
    }
}
