using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Minato Coast redesign workstream: RED SUSPENSION BRIDGE (title-screen landfall).
/// Called from MinatoCoastEnvironment.Apply() after RedesignShores. Owns ONLY the root
/// "Red Suspension Bridge" under chapter 2, rebuilt by exact name each run (idempotent).
///
/// The final stretch of the sea crossing, after the white cable-stayed signature span, becomes
/// the vermilion suspension bridge from MapleRideTitleSeaBridge.png that carries the road onto
/// the far shore: a sea anchorage, two portal towers on concrete caissons, parabolic main
/// cables, vertical hangers, a red stiffening truss under the existing white girder, and a
/// shore anchorage in the landfall. The dressing around it (boats, sea stacks, a headland
/// lighthouse, pines, spectators on the footways and at the landfall) makes the stretch lively.
///
/// Kit: tools/blender/build_minato_redbridge.py (Minato_RedBridge_Tower/Caisson/Anchorage).
/// All stations/counts below are PROVISIONAL tuning (design handoff: illustrative).
/// </summary>
public static partial class MinatoCoastEnvironment
{
    // ---- stations (provisional). White pylon 2 (9670) fans forward to 9952 m; stay clear.
    private const float RbAnchorSeaM = 9990f;    // sea anchorage span face (body runs back to ~9952)
    private const float RbTower1M = 10045f;
    private static float RbTower2M = 10272f;    // re-solved each build: last deep-water station before the landfall cliff
    private const float RbAnchorShoreM = 10334f; // shore anchorage span face (body runs forward)

    // ---- mirror of build_minato_redbridge.py
    private const float RbCableX = 8.4f;
    private const float RbCableTopY = 76.2f;
    private const float RbAnchorCableY = 4.6f, RbAnchorCableZ = 5.0f;
    private const float RbTrussX = 7.4f;
    private const float RbTrussTopY = -1.05f;
    private const float RbTrussBotY = -7.6f;

    // ---- tuning (provisional)
    private const float RbMidSagY = 3.4f;        // cable centre above deck at mid-span
    private const float RbSideSag = 3.0f;        // side-span sag below the chord
    private const float RbCableR = 0.48f;
    private const float RbHangerPitchM = 11.4f;
    private const float RbPanelM = 7.6f;
    private const int RbSpectatorSeed = 7219;

    private static void RedesignRedBridge(MinatoRoute route, Transform[] chapters)
    {
        const string RootName = "Red Suspension Bridge";
        for (int i = chapters[1].childCount - 1; i >= 0; i--)
            if (chapters[1].GetChild(i).name == RootName)
                Object.DestroyImmediate(chapters[1].GetChild(i).gameObject);
        var root = new GameObject(RootName).transform;
        root.SetParent(chapters[1], false);

        var tower = Model("Minato_RedBridge_Tower");
        var caisson = Model("Minato_RedBridge_Caisson");
        var anchor = Model("Minato_RedBridge_Anchorage");
        if (tower == null || caisson == null || anchor == null)
        {
            Debug.LogError("[minato-redbridge] kit missing - run tools/blender/build_minato_redbridge.py");
            return;
        }

        var red = RbRedMat();
        var dark = RbDarkMat();
        var concrete = RbConcreteMat();
        var wet = RbWetConcreteMat();
        var beacon = CelMaterial("Minato_RedBridge_Beacon", new Color(1.0f, 0.22f, 0.16f),
                                 gloss: 0.7f, spec: 0.5f, rim: 0.9f);
        var map = new[] { ("red_steel", red), ("steel_dark", dark), ("beacon", beacon),
                          ("concrete_wet", wet), ("concrete", concrete) };

        // Tower 2 stands in the sea at the foot of the landfall cliff (title art): walk back from
        // the landfall to the first station whose whole caisson footprint is deep water.
        RbTower2M = 10272f;
        var probe = new System.Text.StringBuilder();
        for (float d = 10300f; d > RbTower1M + 120f; d -= 2f)
        {
            CsFrame(route, d, out var pp, out var ff, out var ss);
            bool deep = true;
            foreach (var o in new[] { -17f, 0f, 17f })
                foreach (var a in new[] { -13f, 13f })
                {
                    var q = pp + ss * o + ff * a;
                    if (GroundAt(route, q.x, q.z) > SeaLevelY - 4f) deep = false;
                }
            if ((int)d % 20 == 0) probe.Append($" {d:F0}:{GroundAt(route, pp.x, pp.z):F0}");
            if (deep) { RbTower2M = d; break; }
        }

        // Remove white viaduct piers standing inside the new suspended main span / caissons.
        int prunedPiers = 0;
        var piers = chapters[1].Find("Bridge/Viaduct Piers");
        if (piers != null)
            for (int i = piers.childCount - 1; i >= 0; i--)
            {
                var c = piers.GetChild(i);
                if (!c.name.StartsWith("Pier_")) continue;
                if (!float.TryParse(c.name.Substring(5), out float pd)) continue;
                if (pd > RbTower1M - 20f && pd < RbTower2M + 20f)
                {
                    Object.DestroyImmediate(c.gameObject);
                    prunedPiers++;
                }
            }

        // ---- towers on caissons
        var structure = new GameObject("Structure").transform;
        structure.SetParent(root, false);
        var diag = new System.Text.StringBuilder();
        foreach (float d in new[] { RbTower1M, RbTower2M })
        {
            CsFrame(route, d, out var p, out var fwd, out _);
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            var t = Inst(tower, structure, Vector3.zero, Quaternion.identity, null);
            RetintBySlot(t, map, red);
            t.name = $"Tower_{d:F0}";
            t.transform.SetPositionAndRotation(p, rot);
            float g = GroundAt(route, p.x, p.z);
            float baseY = Mathf.Max(SeaLevelY, g);
            var c = Inst(caisson, structure, Vector3.zero, Quaternion.identity, null);
            RetintBySlot(c, map, concrete);
            c.name = $"Caisson_{d:F0}";
            c.transform.SetPositionAndRotation(new Vector3(p.x, baseY, p.z), rot);
            diag.Append($" tower@{d:F0} deckY={p.y:F1} ground={g:F1};");
        }

        // ---- anchorages (sea: +Z along the route; shore: +Z back toward the span)
        foreach (var (d, back) in new[] { (RbAnchorSeaM, false), (RbAnchorShoreM, true) })
        {
            CsFrame(route, d, out var p, out var fwd, out _);
            var a = Inst(anchor, structure, Vector3.zero, Quaternion.identity, null);
            RetintBySlot(a, map, concrete);
            a.name = back ? "Anchorage_Shore" : "Anchorage_Sea";
            a.transform.SetPositionAndRotation(p, Quaternion.LookRotation(back ? -fwd : fwd, Vector3.up));
            diag.Append($" anchor@{d:F0} deckY={p.y:F1} ground={GroundAt(route, p.x, p.z):F1};");
        }
        foreach (var tr in structure.GetComponentsInChildren<Transform>(true)) tr.gameObject.isStatic = true;

        int cableTris = RbBuildCables(route, root, red, dark, out int hangers);
        int trussTris = RbBuildTruss(route, root, red);
        int life = RbBuildLife(route, root, out string lifeInfo);

        Debug.Log($"[minato-redbridge] red suspension span {RbTower1M:F0}-{RbTower2M:F0} m " +
                  $"(anchorages {RbAnchorSeaM:F0}/{RbAnchorShoreM:F0}), cables+{hangers} hangers " +
                  $"{cableTris:N0} LOD0 tris, truss {trussTris:N0} tris, pruned {prunedPiers} white piers;" +
                  $"{diag} ground probe:{probe} life: {lifeInfo}");
    }

    // ============================================================== materials

    private static Material RbRedMat()
    {
        // International-orange / vermilion paint (title art), salt-weathered low on the tower.
        var m = CelMaterial("Minato_RedBridge_RedPaint", new Color(0.80f, 0.19f, 0.12f),
                            gloss: 0.34f, spec: 0.22f, rim: 0.45f,
                            shade: new Color(0.55f, 0.30f, 0.40f));
        m.SetFloat("_WeatherAmount", 0.25f);
        m.SetFloat("_GrimeAmount", 0.12f);
        m.SetFloat("_WearAmount", 0.10f);
        m.SetFloat("_TintVariation", 0.05f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material RbDarkMat()
    {
        var m = CelMaterial("Minato_RedBridge_DarkSteel", new Color(0.20f, 0.19f, 0.20f),
                            gloss: 0.4f, spec: 0.3f, rim: 0.3f);
        return m;
    }

    private static Material RbConcreteMat()
    {
        var m = CelMaterial("Minato_RedBridge_Concrete", new Color(0.62f, 0.60f, 0.56f),
                            gloss: 0.12f, spec: 0.08f, rim: 0.35f,
                            shade: GroundShade);
        m.SetTexture("_MainTex", null); // asset may still hold the earlier granite albedo (CelMaterial never clears it)
        m.SetFloat("_WeatherAmount", 0.15f);
        m.SetFloat("_GrimeAmount", 0.06f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material RbWetConcreteMat()
    {
        var m = CelMaterial("Minato_RedBridge_ConcreteWet", new Color(0.46f, 0.48f, 0.45f),
                            gloss: 0.5f, spec: 0.3f, rim: 0.3f, shade: GroundShade);
        return m;
    }

    // ============================================================== cables + hangers

    /// <summary>Cable centre height (world y) at route distance d, and whether d is on a span.</summary>
    private static bool RbCableY(MinatoRoute r, float d, out float y)
    {
        CsFrame(r, RbTower1M, out var t1, out _, out _);
        CsFrame(r, RbTower2M, out var t2, out _, out _);
        float y1 = t1.y + RbCableTopY, y2 = t2.y + RbCableTopY;
        y = 0f;
        float a0 = RbAnchorSeaM - RbAnchorCableZ, a1 = RbAnchorShoreM + RbAnchorCableZ;
        if (d < a0 || d > a1) return false;
        if (d <= RbTower1M)
        {
            CsFrame(r, a0, out var pa, out _, out _);
            float u = (d - a0) / (RbTower1M - a0);
            y = Mathf.Lerp(pa.y + RbAnchorCableY, y1, u) - 4f * RbSideSag * u * (1f - u);
            return true;
        }
        if (d >= RbTower2M)
        {
            CsFrame(r, a1, out var pb, out _, out _);
            float u = (d - RbTower2M) / (a1 - RbTower2M);
            y = Mathf.Lerp(y2, pb.y + RbAnchorCableY, u) - 4f * RbSideSag * u * (1f - u);
            return true;
        }
        // main span: quadratic through both saddles and the mid-span low point
        float dm = (RbTower1M + RbTower2M) * 0.5f;
        CsFrame(r, dm, out var pm, out _, out _);
        float ym = pm.y + RbMidSagY;
        float L0 = (d - dm) * (d - RbTower2M) / ((RbTower1M - dm) * (RbTower1M - RbTower2M));
        float L1 = (d - RbTower1M) * (d - RbTower2M) / ((dm - RbTower1M) * (dm - RbTower2M));
        float L2 = (d - RbTower1M) * (d - dm) / ((RbTower2M - RbTower1M) * (RbTower2M - dm));
        y = y1 * L0 + ym * L1 + y2 * L2;
        return true;
    }

    private static void RbSweep(CsMesh m, int sub, List<Vector3> pts, List<Vector3> sides, float r, int n)
    {
        int s = m.V.Count;
        for (int k = 0; k < pts.Count; k++)
        {
            var t = (pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized;
            var up = Vector3.Cross(t, sides[k]).normalized;
            if (up.y < 0f) up = -up;
            var sd = Vector3.Cross(up, t).normalized;
            for (int j = 0; j < n; j++)
            {
                float ang = (j + 0.5f) * Mathf.PI * 2f / n;
                var dir = sd * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                m.Add(pts[k] + dir * r, dir, new Vector2(j / (float)n, k * 0.5f));
            }
        }
        for (int k = 0; k < pts.Count - 1; k++)
            for (int j = 0; j < n; j++)
            {
                int j1 = (j + 1) % n;
                int a = s + k * n + j, b = s + k * n + j1;
                m.Quad(sub, a, b, b + n, a + n);
            }
    }

    private static int RbBuildCables(MinatoRoute route, Transform root, Material red, Material dark,
                                     out int hangerCount)
    {
        var parent = new GameObject("Cables").transform;
        parent.SetParent(root, false);
        float a0 = RbAnchorSeaM - RbAnchorCableZ, a1 = RbAnchorShoreM + RbAnchorCableZ;

        // Main cables: sample densely (2.5 m) with an exact vertex at each saddle.
        var ds = new List<float>();
        for (float d = a0; d < a1; d += 2.5f) ds.Add(d);
        ds.Add(a1);
        ds.Add(RbTower1M); ds.Add(RbTower2M);
        ds.Sort();

        int[] rings = { 12, 7, 4 };
        var levels = new GameObject[3];
        int lod0 = 0;
        for (int l = 0; l < 3; l++)
        {
            var m = new CsMesh(1);
            for (int s = -1; s <= 1; s += 2)
            {
                var pts = new List<Vector3>(); var sides = new List<Vector3>();
                foreach (float d in ds)
                {
                    CsFrame(route, d, out var p, out _, out var side);
                    RbCableY(route, d, out float y);
                    var q = p + side * (s * RbCableX); q.y = y;
                    pts.Add(q); sides.Add(side);
                }
                RbSweep(m, 0, pts, sides, RbCableR * (l == 2 ? 1.4f : 1f), rings[l]);
            }
            if (l == 0) lod0 += m.Tris;
            levels[l] = CsRenderer(parent, $"MainCables_LOD{l}", m.Build($"Minato_RedBridge_MainCables_LOD{l}"),
                                   new[] { red }, l < 2);
        }
        var grp = new GameObject("MainCables");
        grp.transform.SetParent(parent, false);
        foreach (var lv in levels) lv.transform.SetParent(grp.transform, true);
        CsLods(grp, levels, new[] { 1200f, 4000f, 14000f });

        // Hangers + cable bands + floor-beam brackets.
        hangerCount = 0;
        var h0 = new CsMesh(2); var h1 = new CsMesh(1);
        for (float d = a0 + 6f; d < a1 - 4f; d += RbHangerPitchM)
        {
            if (Mathf.Abs(d - RbTower1M) < 6f || Mathf.Abs(d - RbTower2M) < 6f) continue;
            RbCableY(route, d, out float y);
            CsFrame(route, d, out var p, out var fwd, out var side);
            if (y - p.y < 2.2f) continue;
            for (int s = -1; s <= 1; s += 2)
            {
                var top = p + side * (s * RbCableX); top.y = y - RbCableR * 0.6f;
                var bot = p + side * (s * RbCableX) + Vector3.up * -2.6f;
                CsTube(h0, 0, bot, top, 0.11f, 6);
                CsTube(h1, 0, bot, top, 0.16f, 3);
                // cable band clamp (dark steel)
                var band = top + Vector3.up * (RbCableR * 0.6f);
                CsBox(h0, 1, band, side, Vector3.up, fwd, RbCableR + 0.14f, RbCableR + 0.14f, 0.45f, true);
                hangerCount++;
            }
        }
        var hg = new GameObject("Hangers");
        hg.transform.SetParent(parent, false);
        var hl0 = CsRenderer(hg.transform, "Hangers_LOD0", h0.Build("Minato_RedBridge_Hangers_LOD0"), new[] { red, dark }, false);
        var hl1 = CsRenderer(hg.transform, "Hangers_LOD1", h1.Build("Minato_RedBridge_Hangers_LOD1"), new[] { red }, false);
        CsLods(hg, new[] { hl0, hl1 }, new[] { 650f, 2600f });
        return lod0 + h0.Tris;
    }

    // ============================================================== stiffening truss

    private static void RbBeam(CsMesh m, int sub, Vector3 a, Vector3 b, Vector3 sideRef, float hw, float hh)
    {
        var az = b - a;
        float len = az.magnitude;
        if (len < 1e-3f) return;
        az /= len;
        var ax = (sideRef - az * Vector3.Dot(sideRef, az));
        if (ax.sqrMagnitude < 1e-4f) ax = Vector3.Cross(az, Vector3.up);
        ax.Normalize();
        var ay = Vector3.Cross(az, ax).normalized;
        CsBox(m, sub, (a + b) * 0.5f, ax, ay, az, hw, hh, len * 0.5f, true);
    }

    private static int RbBuildTruss(MinatoRoute route, Transform root, Material red)
    {
        var parent = new GameObject("Stiffening Truss").transform;
        parent.SetParent(root, false);
        float d0 = RbAnchorSeaM + 0.5f, d1 = Mathf.Min(RbAnchorShoreM - 0.5f, CsDeckToM);
        int panels = Mathf.Max(1, Mathf.RoundToInt((d1 - d0) / RbPanelM));
        float step = (d1 - d0) / panels;

        Vector3 Node(float d, float x, float y, out Vector3 side)
        {
            CsFrame(route, d, out var p, out _, out side);
            return p + side * x + Vector3.up * y;
        }

        var m0 = new CsMesh(1); var m1 = new CsMesh(1);
        for (int k = 0; k < panels; k++)
        {
            float da = d0 + k * step, db = da + step;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * RbTrussX;
                var ta = Node(da, x, RbTrussTopY, out var sa); var tb = Node(db, x, RbTrussTopY, out _);
                var ba = Node(da, x, RbTrussBotY, out _); var bb = Node(db, x, RbTrussBotY, out _);
                RbBeam(m0, 0, ta, tb, sa, 0.36f, 0.42f);
                RbBeam(m0, 0, ba, bb, sa, 0.36f, 0.42f);
                RbBeam(m0, 0, ta, ba, sa, 0.26f, 0.26f);
                if ((k & 1) == 0) RbBeam(m0, 0, ta, bb, sa, 0.2f, 0.22f);
                else RbBeam(m0, 0, ba, tb, sa, 0.2f, 0.22f);
                RbBeam(m1, 0, ta, tb, sa, 0.4f, 0.45f);
                RbBeam(m1, 0, ba, bb, sa, 0.4f, 0.45f);
            }
            // floor beam under the soffit (extends to the hanger plane) + bottom lateral bracing
            var fl = Node(da, -(RbCableX + 0.25f), -3.3f, out var sd);
            var fr = Node(da, RbCableX + 0.25f, -3.3f, out _);
            RbBeam(m0, 0, fl, fr, Vector3.Cross(Vector3.up, sd), 0.28f, 0.42f);
            RbBeam(m0, 0, Node(da, -RbTrussX, RbTrussBotY, out _), Node(db, RbTrussX, RbTrussBotY, out _),
                   Vector3.up, 0.12f, 0.12f);
            RbBeam(m0, 0, Node(da, RbTrussX, RbTrussBotY, out _), Node(db, -RbTrussX, RbTrussBotY, out _),
                   Vector3.up, 0.12f, 0.12f);
        }
        var l0 = CsRenderer(parent, "Truss_LOD0", m0.Build("Minato_RedBridge_Truss_LOD0"), new[] { red }, true);
        var l1 = CsRenderer(parent, "Truss_LOD1", m1.Build("Minato_RedBridge_Truss_LOD1"), new[] { red }, false);
        CsLods(parent.gameObject, new[] { l0, l1 }, new[] { 900f, 5000f });
        return m0.Tris;
    }

    // ============================================================== liveliness

    private static int RbBuildLife(MinatoRoute route, Transform root, out string info)
    {
        var life = new GameObject("Landfall Life").transform;
        life.SetParent(root, false);
        var rng = new System.Random(RbSpectatorSeed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // -- boats and sea stacks around the span (clear of the deck, on real water)
        var mats = HarborMaterials();
        string[] boats = { "Minato_Sea_FishingBoat", "Minato_Sea_Sailboat", "Minato_Sea_FishingBoat",
                           "Minato_Marina_YachtA", "Minato_Marina_YachtB" };
        int nBoats = 0, nStacks = 0;
        for (int k = 0; k < 40 && nBoats < 12; k++)
        {
            float d = R(9760f, 10360f);
            float off = R(110f, 520f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
            CsFrame(route, d, out var p, out var fwd, out var side);
            var q = p + side * off;
            if (GroundAt(route, q.x, q.z) > SeaLevelY - 4f) continue;
            var heading = Quaternion.LookRotation(Quaternion.Euler(0f, R(-40f, 40f), 0f) * (rng.NextDouble() < 0.5 ? fwd : -fwd));
            var go = HarborInst(boats[rng.Next(boats.Length)], life, new Vector3(q.x, SeaLevelY + 0.3f, q.z),
                                heading, mats, R(1.6f, 2.2f));
            if (go != null) nBoats++;
        }
        var rock = CelMaterial("Minato_RedBridge_StackRock", new Color(0.72f, 0.70f, 0.64f), gloss: 0.08f,
                               spec: 0.05f, rim: 0.30f,
                               texture: Tex(TakaTex, "Taka_Granite_Albedo.png"), shade: GroundShade);
        var stacks = new[] { Glb(ShiosaiGlb, "Shiosai_SeaStack_A"), Glb(ShiosaiGlb, "Shiosai_SeaStack_B"),
                             Glb(ShiosaiGlb, "Shiosai_SeaStack_C") };
        const int RbMaxStacks = 0; // Shiosai stack GLBs read as dark spires here; disabled (provisional)
        for (int k = 0; k < 60 && nStacks < RbMaxStacks; k++)
        {
            float d = R(10080f, 10480f);
            float off = R(180f, 380f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
            CsFrame(route, d, out var p, out _, out var side);
            var q = p + side * off;
            bool deepFoot = true;
            for (int a = 0; a < 6; a++)
            {
                var o = Quaternion.Euler(0f, a * 60f, 0f) * new Vector3(28f, 0f, 0f);
                if (GroundAt(route, q.x + o.x, q.z + o.z) > SeaLevelY - 8f) deepFoot = false;
            }
            if (!deepFoot || GroundAt(route, q.x, q.z) > SeaLevelY - 8f) continue;
            var src = stacks[rng.Next(stacks.Length)];
            if (src == null) continue;
            var go = Inst(src, life, Vector3.zero, Quaternion.Euler(0f, R(0f, 360f), 0f), null);
            RetintBySlot(go, new[] { ("foliage", MedianFoliageMaterial()), ("rock", rock) }, rock);
            go.transform.localScale = Vector3.one * R(0.9f, 1.5f);
            go.transform.position = new Vector3(q.x, SeaLevelY, q.z);
            SeatInWater(go, SeaLevelY - 2.5f);
            go.isStatic = true;
            nStacks++;
        }

        // -- headland lighthouse: first land point at a sea edge beside the landfall
        int lights = 0;
        var lh = Model("Minato_Shore_HeadlandLighthouse");
        for (float d = 10300f; d < 10700f && lights == 0 && lh != null; d += 20f)
            for (int s = -1; s <= 1 && lights == 0; s += 2)
                for (float off = 90f; off < 260f; off += 15f)
                {
                    CsFrame(route, d, out var p, out var fwd, out var side);
                    var q = p + side * (s * off);
                    float g = GroundAt(route, q.x, q.z);
                    var sea = q + side * (s * 45f);
                    if (g < 3f || g > 45f || GroundAt(route, sea.x, sea.z) > SeaLevelY - 1f) continue;
                    float y = ShoreSurfaceY(route, q.x, q.z, out _);
                    var go = ShorePlace(lh, life, new Vector3(q.x, y - 0.3f, q.z),
                                        Quaternion.LookRotation(side * s, Vector3.up), 1f);
                    if (go != null) lights++;
                    break;
                }

        // -- black pines on the landfall slopes either side of the shore anchorage
        var pineA = Glb(ShiosaiGlb, "Shiosai_Pine"); var pineB = Glb(ShiosaiGlb, "Shiosai_Pine_B");
        int pines = 0;
        for (int k = 0; k < 90 && pines < 26; k++)
        {
            float d = R(10300f, 10520f);
            float off = R(20f, 110f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
            CsFrame(route, d, out var p, out _, out var side);
            var q = p + side * off;
            if (GroundAt(route, q.x, q.z) < 1.5f) continue;
            float y = ShoreSurfaceY(route, q.x, q.z, out var n);
            if (n.y < 0.75f) continue;
            if (Mathf.Abs(off) < 26f && d > RbAnchorShoreM - 5f && d < RbAnchorShoreM + 45f) continue; // anchorage
            if (ShorePine(rng.NextDouble() < 0.5 ? pineA : pineB, life, new Vector3(q.x, y - 0.12f, q.z),
                          R(0f, 360f), R(1.0f, 1.45f)) != null) pines++;
        }

        // -- people: spectators + walkers on the bridge footways, a viewpoint crowd at landfall
        int people = 0, riders = 0;
        MinatoCrowdPopulation.Prepare();
        if (MinatoCrowdPopulation.Ready)
        {
            var ped = MinatoCrowdPopulation.PedestrianKeys;
            var cyc = MinatoCrowdPopulation.CyclistKeys;
            for (float d = RbTower1M - 30f; d < RbAnchorShoreM - 10f; d += 14f)
                for (int s = -1; s <= 1; s += 2)
                {
                    if (rng.NextDouble() > 0.55) continue;
                    CsFrame(route, d + R(-3f, 3f), out var p, out var fwd, out var side);
                    string key = ped[rng.Next(ped.Length)];
                    bool walk = key.Contains("_Walk_");
                    float x = s * (walk ? 5.1f : 5.7f);
                    var q = p + side * x;
                    var face = walk ? (rng.NextDouble() < 0.5 ? fwd : -fwd)
                                    : (side * s + fwd * R(-0.4f, 0.4f)).normalized; // look out to sea
                    var go = MinatoCrowdPopulation.Spawn(key, life, new Vector3(q.x, p.y + CsFootwayY, q.z),
                        Quaternion.LookRotation(face, Vector3.up), R(0.95f, 1.06f), null,
                        walk ? face : default, walk ? R(8f, 14f) : 0f, walk ? R(0.6f, 0.95f) : 0.78f, false);
                    if (go != null) people++;
                }
            // a few leisure riders on the footway
            if (cyc.Length > 0)
                for (float d = RbTower1M + 20f; d < RbTower2M; d += 60f)
                {
                    int s = rng.NextDouble() < 0.5 ? -1 : 1;
                    CsFrame(route, d, out var p, out var fwd, out var side);
                    var q = p + side * (s * 5.25f);
                    var go = MinatoCrowdPopulation.Spawn(cyc[rng.Next(cyc.Length)], life,
                        new Vector3(q.x, p.y + CsFootwayY, q.z),
                        Quaternion.LookRotation(s > 0 ? fwd : -fwd, Vector3.up), 1f);
                    if (go != null) riders++;
                }
            // landfall viewpoint crowd on the verge beyond the shore anchorage
            for (int k = 0; k < 60 && people < 70; k++)
            {
                float d = R(10380f, 10560f);
                float off = R(8f, 22f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                CsFrame(route, d, out var p, out var fwd, out var side);
                var q = p + side * off;
                float y = ShoreSurfaceY(route, q.x, q.z, out var n);
                if (y < SeaLevelY + 0.8f || n.y < 0.85f || Mathf.Abs(y - p.y) > 6f) continue;
                string key = ped[rng.Next(ped.Length)];
                bool walk = key.Contains("_Walk_");
                var face = walk ? fwd : (-fwd + side * R(-0.6f, 0.6f)).normalized; // toward the bridge
                var go = MinatoCrowdPopulation.Spawn(key, life, new Vector3(q.x, y, q.z),
                    Quaternion.LookRotation(face, Vector3.up), R(0.95f, 1.06f), null,
                    walk ? face : default, walk ? R(6f, 12f) : 0f, walk ? R(0.6f, 0.95f) : 0.78f, false);
                if (go != null) people++;
            }
        }
        info = $"{nBoats} boats, {nStacks} sea stacks, {lights} lighthouse, {pines} pines, " +
               $"{people} pedestrians, {riders} footway riders";
        return nBoats + nStacks + lights + pines + people + riders;
    }
}
