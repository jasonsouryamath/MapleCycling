// Worker E (premier-resort brief sections 10 + 17 + the summit half of 2): the NAGISA BAY Skyline Terrace and the
// signature coastal bridge.
//   * OverlookShapeTerrain(): called from the end of BenchUnderRoad so the in-memory ground field (mesh, props and
//     colliders all read it) is carved on the bay side of the road at route d = OvFromM..OvToM. The road runs in a cut
//     there: before the terrace the ocean is hidden by the bank (and by a black-pine screen), at the terrace the bank is
//     lowered to road level and the whole bay opens up (checked with a heightfield ray test: ~0.8 of the bay visible).
//   * Stage 89 "Overlook": paved terrace + teak observation deck + glass rail, summit cafe kiosk, benches, bike racks,
//     lamps, interpretation lecterns (bay panorama), coin telescopes, direction post, NAGISA BAY photo board, flags, pines.
//     Everything stays >= 7.6 m from the centreline; nothing taller than 1.4 m on the bay side of the kiosk.
//   * Stage 89 "Bridge": two white cable-stayed A-frame pylons over the sea viaduct (route d 15.45 / 15.88 km) whose legs
//     stand outside the deck; the rider passes between them under a signed cross-beam 16 m up.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class NagisaBayEnvironment
{
    // ---- PROVISIONAL tunables ------------------------------------------------------------------------------
    private const float OvFromM = 8100f, OvToM = 8290f;
    private const float OvPaveO = 26f, OvDeckO = 35.5f, OvDeckFromM = 8150f, OvDeckToM = 8250f;
    private const float OvCarveInner = 4.4f, OvCarveOuter = 64f, OvFeatherM = 16f;
    private const float OvTerraceDrop = 0.2f;
    private const float BrPylonH = 52f, BrLegBaseO = 7.6f, BrLegTopO = 1.7f;
    private static readonly float[] BrPylonM = { 15450f, 15880f };

    /// <summary>+1/-1 so that (SideFlat * sign) points toward the bay (south, -z) at the overlook.</summary>
    private static float OvSign(NagisaRoute r)
    {
        var s = r.SideFlat(r.IndexAt((OvFromM + OvToM) * 0.5f));
        return s.z > 0f ? -1f : 1f;
    }

    // =================================================================================== terrain carve
    private static int OverlookShapeTerrain(NagisaGround g, NagisaRoute r)
    {
        if (r.Length < OvToM + 80f) return 0;
        int i0 = r.IndexAt(OvFromM - 60f), i1 = r.IndexAt(OvToM + 60f);
        float sgn = OvSign(r);
        var bb = new Bounds(r.Position[i0], Vector3.zero);
        for (int i = i0; i <= i1; i++) bb.Encapsulate(r.Position[i]);
        float pad = OvCarveOuter + OvFeatherM + 20f;
        int ix0 = Mathf.Max(0, Mathf.FloorToInt((bb.min.x - pad - g.X0) / g.Cell));
        int ix1 = Mathf.Min(g.Nx - 1, Mathf.CeilToInt((bb.max.x + pad - g.X0) / g.Cell));
        int iz0 = Mathf.Max(0, Mathf.FloorToInt((bb.min.z - pad - g.Z0) / g.Cell));
        int iz1 = Mathf.Min(g.Nz - 1, Mathf.CeilToInt((bb.max.z + pad - g.Z0) / g.Cell));
        int lowered = 0;
        for (int iz = iz0; iz <= iz1; iz++)
        for (int ix = ix0; ix <= ix1; ix++)
        {
            float x = g.X0 + ix * g.Cell, z = g.Z0 + iz * g.Cell;
            int best = -1; float bd = float.MaxValue;
            for (int j = i0; j <= i1; j++)
            {
                float dx = r.Position[j].x - x, dz = r.Position[j].z - z; float d2 = dx * dx + dz * dz;
                if (d2 < bd) { bd = d2; best = j; }
            }
            var pj = r.Position[best];
            var tj = r.Tangent[best]; tj.y = 0f; tj.Normalize();
            var sj = r.SideFlat(best) * sgn;
            float vx = x - pj.x, vz = z - pj.z;
            float o = vx * sj.x + vz * sj.z;
            float m = r.Distance[best] + (vx * tj.x + vz * tj.z);
            if (o < OvCarveInner || o > OvCarveOuter + OvFeatherM) continue;
            float wAlong = Mathf.Clamp01((m - (OvFromM - OvFeatherM)) / OvFeatherM) *
                           Mathf.Clamp01(((OvToM + OvFeatherM) - m) / OvFeatherM);
            float wOut = o <= OvCarveOuter ? 1f : Mathf.Clamp01(1f - (o - OvCarveOuter) / OvFeatherM);
            float w = wAlong * wOut; if (w <= 0f) continue;
            float target = pj.y - OvTerraceDrop;
            int k = iz * g.Nx + ix;
            if (g.H[k] > target) { g.H[k] = Mathf.Lerp(g.H[k], target, w); lowered++; }
        }
        return lowered;
    }

    // ====================================================================================== overlook stage
    [NagisaStage(99, "Overlook")]
    private static void BuildOverlookStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();
        if (_nb.Count == 0) { Debug.LogError("[ov] Run NagisaBayEnvironment.Apply first."); return; }
        var mats = GwMats();
        float sg = OvSign(_route);
        int cleared = GwClearZone(group, OvFromM - 40f, OvToM + 40f, 4f, 130f, sg);
        var blockers = GwBlockers(group, (7650f, 8350f, 90f));
        var bags = new Dictionary<string, GwBag>();
        var rng = new System.Random(8210);

        // ---- surfaces
        GwStrip(Gb(bags, "paving"), OvFromM + 5f, OvToM - 5f, 6.0f, OvPaveO, sg, -0.10f, 2.4f, 2.4f, Vector3.up);
        GwStrip(Gb(bags, "lane"), OvFromM + 5f, OvToM - 5f, 4.6f, 6.0f, sg, -0.08f, 1.4f, 20f, Vector3.up);
        GwStrip(Gb(bags, "teak"), OvDeckFromM, OvDeckToM, OvPaveO, OvDeckO, sg, -0.06f, 1.6f, 1.6f, Vector3.up);
        // deck outer skirt (stone) down into the carved ground
        {
            int a = _route.IndexAt(OvDeckFromM), b = _route.IndexAt(OvDeckToM);
            var sk = Gb(bags, "stone");
            int prevTop = -1;
            for (int i = a; i <= b; i++)
            {
                var p = _route.Position[i]; var s = _route.SideFlat(i) * sg;
                float x = p.x + s.x * OvDeckO, z = p.z + s.z * OvDeckO;
                float top = p.y - 0.06f, bot = Mathf.Min(_ground.Height(x, z), top - 0.6f) - 0.3f;
                int k = sk.v.Count;
                sk.v.Add(new Vector3(x, top, z)); sk.v.Add(new Vector3(x, bot, z));
                sk.uv.Add(new Vector2(_route.Distance[i] / 4f, 0f)); sk.uv.Add(new Vector2(_route.Distance[i] / 4f, (top - bot) / 4f));
                if (prevTop >= 0) { GwTri(sk, prevTop, prevTop + 1, k, s); GwTri(sk, prevTop + 1, k + 1, k, s); }
                prevTop = k;
            }
            GwFrame(OvDeckFromM, sg, out var t0, out var s0);
            GwFBox(sk, GwW(OvDeckFromM, (OvPaveO + OvDeckO) * 0.5f, -1.1f, sg), s0, t0, OvDeckO - OvPaveO, 2.2f, 0.3f);
            GwFBox(sk, GwW(OvDeckToM, (OvPaveO + OvDeckO) * 0.5f, -1.1f, sg), s0, t0, OvDeckO - OvPaveO, 2.2f, 0.3f);
        }

        // ---- glass rail: terrace edge, around the deck
        var rail = new List<List<Vector3>>
        {
            new List<Vector3> { GwW(OvFromM + 8f, OvPaveO, -0.06f, sg), GwW(OvDeckFromM, OvPaveO, -0.06f, sg) },
            new List<Vector3> { GwW(OvDeckFromM, OvPaveO, -0.06f, sg), GwW(OvDeckFromM, OvDeckO, -0.06f, sg),
                                GwW(OvDeckToM, OvDeckO, -0.06f, sg), GwW(OvDeckToM, OvPaveO, -0.06f, sg) },
            new List<Vector3> { GwW(OvDeckToM, OvPaveO, -0.06f, sg), GwW(OvToM - 8f, OvPaveO, -0.06f, sg) },
        };
        int posts = 0;
        foreach (var line in rail)
            for (int s = 0; s < line.Count - 1; s++)
            {
                var a = line[s]; var b = line[s + 1];
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / 2.5f));
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector3.Lerp(a, b, k / (float)n);
                    GwFBox(Gb(bags, "steel"), p + Vector3.up * 0.55f, Vector3.right, Vector3.forward, 0.08f, 1.1f, 0.08f); posts++;
                    if (k < n)
                    {
                        var q = Vector3.Lerp(a, b, (k + 1) / (float)n);
                        GwBeam(Gb(bags, "steel"), p + Vector3.up * 1.1f, q + Vector3.up * 1.1f, 0.07f, 0.05f);
                        var dir = (q - p).normalized; var nrm = Vector3.Cross(dir, Vector3.up);
                        GwSign(Gb(bags, "glass"), (p + q) * 0.5f + Vector3.up * 0.6f, nrm, Vector3.up, Vector3.Distance(p, q) - 0.1f, 0.95f);
                    }
                }
            }

        // ---- cafe kiosk (m 8118, lateral 13)
        {
            const float km = 8118f, ko = 13f;
            GwFrame(km, sg, out var t, out var s);
            var c = GwW(km, ko, -0.1f, sg);
            Vector3 W(float lat, float h, float al) => c + s * (lat - ko) + Vector3.up * h + t * al;
            GwFBox(Gb(bags, "white"), W(ko, 1.55f, 0f), s, t, 4.0f, 3.1f, 7.0f);
            GwFBox(Gb(bags, "teak"), W(ko, 3.25f, 0f), s, t, 5.4f, 0.25f, 8.4f);
            GwFBox(Gb(bags, "teal"), W(ko + 2.02f, 1.75f, 0f), s, t, 0.1f, 1.0f, 3.2f);        // serving window
            GwFBox(Gb(bags, "teak"), W(ko + 2.25f, 1.05f, 0f), s, t, 0.7f, 0.1f, 3.4f);         // counter shelf
            GwSign(Gb(bags, "kiosk"), W(ko - 2.03f, 2.55f, 0f), -s, Vector3.up, 6.0f, 1.5f, 0f, 0.75f, 1f, 1f);
            GwSign(Gb(bags, "kiosk"), W(ko, 2.5f, -3.53f), -t, Vector3.up, 3.6f, 0.9f, 0f, 0.75f, 1f, 1f);
            GwSign(Gb(bags, "kiosk"), W(ko + 2.03f, 1.6f, -2.3f), s, Vector3.up, 1.6f, 1.2f, 0f, 0f, 1f, 0.75f);
            GwSign(Gb(bags, "kiosk"), W(ko, 1.55f, -3.54f), -t, Vector3.up, 1.6f, 1.2f, 0f, 0f, 1f, 0.75f);
            GwSign(Gb(bags, "awning"), W(ko + 2.9f, 2.85f, 0f), (s * 0.55f + Vector3.up * 0.83f), Vector3.up, 6.4f, 1.6f);
            foreach (float sd in new[] { -1f, 1f })
                GwCyl(Gb(bags, "steel"), W(ko + 3.5f, 0f, sd * 3.0f), W(ko + 3.5f, 2.3f, sd * 3.0f), 0.04f, 0.04f, 6);
        }

        // ---- signage: skyline wall, direction post, photo board, lecterns, telescopes
        {
            GwFrame(8104f, sg, out var t, out var s);
            GwFBox(Gb(bags, "stone"), GwW(8104f, 9f, 0.45f, sg), s, t, 6.0f, 0.9f, 0.5f);
            GwSign(Gb(bags, "skyline"), GwW(8104f, 9f, 1.45f, sg) + t * -0.3f, -t, Vector3.up, 5.6f, 1.4f);
            GwFBox(Gb(bags, "teal"), GwW(8104f, 9f, 1.45f, sg) + t * -0.05f, s, t, 5.8f, 1.55f, 0.12f);
            GwFrame(8262f, sg, out t, out s);
            GwFBox(Gb(bags, "steel"), GwW(8262f, 8.5f, 1.6f, sg), s, t, 0.18f, 3.2f, 0.18f);
            GwSign(Gb(bags, "direction"), GwW(8262f, 8.5f, 2.85f, sg) + t * -0.13f, -t, Vector3.up, 2.0f, 1.0f);
            GwFBox(Gb(bags, "teal"), GwW(8262f, 8.5f, 2.85f, sg), s, t, 2.1f, 1.1f, 0.1f);
            // NAGISA BAY photo board at the west end of the terrace
            GwFrame(8284f, sg, out t, out s);
            foreach (float lat in new[] { 10.4f, 17.6f })
                GwCyl(Gb(bags, "steel"), GwW(8284f, lat, 0f, sg), GwW(8284f, lat, 3.0f, sg), 0.12f, 0.1f, 8);
            GwFBox(Gb(bags, "teal"), GwW(8284f, 14f, 3.05f, sg), s, t, 7.6f, 2.0f, 0.2f);
            GwSign(Gb(bags, "wordmark"), GwW(8284f, 14f, 3.05f, sg) + t * -0.11f, -t, Vector3.up, 7.4f, 1.85f);
            // interpretation lecterns on the deck rail (facing the viewer, who looks at the bay)
            foreach (float lm in new[] { 8172f, 8228f })
            {
                GwFrame(lm, sg, out t, out s);
                GwFBox(Gb(bags, "steel"), GwW(lm, 33.6f, 0.45f, sg), s, t, 0.22f, 0.9f, 0.22f);
                var n = (Vector3.up * 0.72f - s * 0.69f).normalized;
                GwSign(Gb(bags, "panorama"), GwW(lm, 33.6f, 1.0f, sg), n, Vector3.up, 1.9f, 0.71f);
                GwSign(Gb(bags, "steel"), GwW(lm, 33.6f, 0.98f, sg) - n * 0.03f, -n, Vector3.up, 1.96f, 0.76f);
            }
            // coin telescopes
            foreach (float lm in new[] { 8196f, 8208f })
            {
                GwFrame(lm, sg, out t, out s);
                var baseP = GwW(lm, 34.2f, -0.06f, sg);
                GwCyl(Gb(bags, "steel"), baseP, baseP + Vector3.up * 1.25f, 0.07f, 0.06f, 8);
                GwCyl(Gb(bags, "teal"), baseP + Vector3.up * 1.3f - s * 0.25f, baseP + Vector3.up * 1.42f + s * 0.45f, 0.1f, 0.14f, 10);
            }
        }

        // ---- flags (sway)
        var flagsT = new GameObject("Terrace Flags").transform; flagsT.SetParent(group, false);
        var fm = GwFlagMesh("Nagisa_GW_FlagCoralOv", 1.9f, 1.15f); GwSaveMesh(fm);
        var fm2 = GwFlagMesh("Nagisa_GW_FlagTealOv", 1.9f, 1.15f); GwSaveMesh(fm2);
        var flagList = new List<Transform>();
        int fi = 0;
        foreach (var (m, o) in new[] { (8152f, 36.4f), (8248f, 36.4f), (8104f, 20f), (8286f, 22f) })
        {
            GwFrame(m, sg, out var t, out var s);
            flagList.Add(GwFlag(flagsT, mats, bags, fi % 2 == 0 ? fm : fm2, fi % 2 == 0 ? "flagCoral" : "flagTeal",
                                GwW(m, o, -0.06f, sg), 6.5f, t));
            fi++;
        }
        AmbientMoverStaging.StageSway(group, "Terrace Flag Sway", flagList.ToArray(), Vector3.up, 16f, 0.65f,
                                      AmbientSway.SwayMode.Noise, default, 810);

        GwFlush(group, "Skyline Terrace", bags, mats);

        // ---- furniture + planting
        var furn = new GameObject("Terrace Furniture").transform; furn.SetParent(group, false);
        int benches = 0, racks = 0, lamps = 0;
        Vector3 Ground(float m, float o, float dy) { var w = GwW(m, o, dy, sg); return w; }
        for (float m = OvFromM + 24f; m < OvToM - 8f; m += 13f)
        {
            GwFrame(m, sg, out var t, out var s);
            var bp = Ground(m, 20.5f, -0.10f);
            if (GwFree(blockers, bp, 0.9f)) { if (PlaceWorld("Nagisa_Bench", furn, bp, GwYaw(s), 1f, 0.003f) != null) benches++; }
        }
        foreach (float m in new[] { 8160f, 8190f, 8220f, 8240f })
        {
            GwFrame(m, sg, out var t, out var s);
            if (PlaceWorld("Nagisa_Bench", furn, Ground(m, 33.0f, -0.06f), GwYaw(s), 1f, 0.003f) != null) benches++;
        }
        for (float m = 8140f; m < 8280f; m += 22f)
        {
            GwFrame(m, sg, out var t, out var s);
            if (PlaceWorld("Nagisa_S_BikeRack", furn, Ground(m, 8.4f, -0.10f), GwYaw(s), 1f, 0.003f) != null) racks++;
        }
        for (float m = OvFromM + 20f; m < OvToM - 8f; m += 20f)
        {
            GwFrame(m, sg, out var t, out var s);
            if (PlaceWorld("Nagisa_PromenadeLamp", furn, Ground(m, 24.6f, -0.10f), GwYaw(-s), 1f, 0.003f) != null) lamps++;
        }
        foreach (var (m, o) in new[] { (8112f, 19f), (8124f, 19.5f) })
            PlaceWorld("Nagisa_S_Parasol", furn, Ground(m, o, -0.10f), 0f, 1f, 0.003f);

        // ---- reveal screen + framing pines
        var pineT = new GameObject("Summit Pines").transform; pineT.SetParent(group, false);
        int pines = 0;
        void Pine(float m, float o, float sc)
        {
            var pp = GwW(m, o, 0f, sg);
            if (!CanPlace(pp.x, pp.z, 1.5f, out float y, 3f) || !GwFree(blockers, pp, 2.2f)) return;
            GwPine(pineT, mats, new Vector3(pp.x, y - 0.1f, pp.z), (float)rng.NextDouble() * 360f, sc, rng.Next(3)); pines++;
        }
        // before the terrace: bay-side screen hides the sea until the terrace opens it
        for (float m = 7700f; m < OvFromM + 6f; m += 5f + (float)rng.NextDouble() * 2f)
        { Pine(m, 9f + (float)rng.NextDouble() * 3f, 0.95f + (float)rng.NextDouble() * 0.4f); Pine(m + 2.2f, 15f + (float)rng.NextDouble() * 4f, 1.1f); }
        // uphill side of the road: framing
        for (float m = 7950f; m < OvToM + 40f; m += 11f)
            Pine(m, -(9.5f + (float)rng.NextDouble() * 8f), 0.9f + (float)rng.NextDouble() * 0.5f);
        Pine(OvFromM - 6f, 40f, 1.5f); Pine(OvToM + 8f, 36f, 1.4f); Pine(OvToM + 14f, 24f, 1.3f);

        Debug.Log($"[ov] cleared {cleared} foreign props; Skyline Terrace d {OvFromM:0}-{OvToM:0} m, bay side sg={sg:+0;-0}, benches {benches}, racks {racks}, lamps {lamps}, " +
                  $"rail posts {posts}, pines {pines}, blockers {blockers.Count}.");
    }

    // ====================================================================================== bridge stage
    [NagisaStage(99, "Bridge")]
    private static void BuildBridgeStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();
        if (_nb.Count == 0) { Debug.LogError("[br] Run NagisaBayEnvironment.Apply first."); return; }
        var mats = GwMats();
        var bags = new Dictionary<string, GwBag>();
        int cables = 0;
        foreach (float pm in BrPylonM)
        {
            if (pm > _route.Length - 120f) continue;
            int pi = _route.IndexAt(pm);
            var p = _route.Position[pi];
            var t = _route.Tangent[pi]; t.y = 0f; t.Normalize();
            var s = _route.SideFlat(pi);
            float deckY = p.y;
            Vector3 PW(float lat, float h, float al) => new Vector3(p.x, deckY, p.z) + s * lat + Vector3.up * h + t * al;
            foreach (float side in new[] { -1f, 1f })
            {
                var a = PW(side * BrLegBaseO, -1.7f, 0f);
                var b = PW(side * BrLegTopO, BrPylonH, 0f);
                GwBeam(Gb(bags, "white"), a, b, 2.6f, 2.0f);
                // pile cap + shaft down to the sea floor
                var f = PW(side * BrLegBaseO, 0f, 0f);
                float floor = Mathf.Min(_ground.Height(f.x, f.z), -1f) - 3f;
                float topY = deckY - 1.7f;
                if (topY - floor > 1f)
                    GwFBox(Gb(bags, "stone"), new Vector3(f.x, (topY + floor) * 0.5f, f.z), s, t, 3.6f, topY - floor, 3.6f);
            }
            // cross beams + cap
            foreach (float h in new[] { 16f, 40f })
            {
                float lat = BrLegBaseO - (BrLegBaseO - BrLegTopO) * (h + 1.7f) / (BrPylonH + 1.7f);
                GwFBox(Gb(bags, "white"), PW(0f, h, 0f), s, t, lat * 2f, 1.6f, 2.2f);
                GwFBox(Gb(bags, "coral"), PW(0f, h - 0.85f, 0f), s, t, lat * 2f - 0.4f, 0.12f, 2.0f);
            }
            GwFBox(Gb(bags, "coral"), PW(0f, BrPylonH + 0.6f, 0f), s, t, 4.6f, 2.2f, 2.8f);
            GwCyl(Gb(bags, "steel"), PW(0f, BrPylonH + 1.7f, 0f), PW(0f, BrPylonH + 6.5f, 0f), 0.22f, 0.05f, 8);
            // plaques (both faces of the lower cross-beam)
            GwSign(Gb(bags, "bridge"), PW(0f, 16f, -1.13f), -t, Vector3.up, 7.0f, 1.75f);
            GwSign(Gb(bags, "bridge"), PW(0f, 16f, 1.13f), t, Vector3.up, 7.0f, 1.75f);
            // stay cables: fan from the tower to both deck edges, ahead and behind
            for (int dir = -1; dir <= 1; dir += 2)
                for (int k = 1; k <= 8; k++)
                {
                    float mk = pm + dir * (14f + k * 13f);
                    if (mk < _route.BridgeStartM + 60f || mk > _route.Length - 20f) continue;
                    int ai = _route.IndexAt(mk);
                    var ap = _route.Position[ai]; var asd = _route.SideFlat(ai);
                    float topH = BrPylonH - 2.5f - (k - 1) * 1.7f;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var deckPt = ap + asd * (side * 5.05f) + Vector3.up * (RoadY(ai, 5.0f) - ap.y + 1.0f);
                        var towerPt = PW(side * BrLegTopO, topH, dir * 0.0f);
                        GwCyl(Gb(bags, "white"), deckPt, towerPt, 0.07f, 0.07f, 6); cables++;
                    }
                }
        }
        GwFlush(group, "Bridge Landmark", bags, mats);
        Debug.Log($"[br] Signature bridge: {BrPylonM.Length} A-frame pylons, {cables} stay cables.");
    }
}
