using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Minato Coast redesign workstream: HARBOR. See MINATO_VISUAL_DESIGN.md at the project root and
/// the zone targets MinatoCoast_HarborAwakens_Target_v01 / MinatoCoast_PortGateTransition_Target_v01.
///
/// Builds (all under "Chapter 1 - Port City Departure/Harbor Redesign"):
///   * a working container terminal on the SEA side of zones Harbor Awakens + Port Gate: a conforming
///     yard apron, a flat pier deck along the real waterline with a quay face, coral ship-to-shore
///     gantry cranes on the pier (booms out over the water, so they silhouette against the low sun),
///     multi-livery container stacks + RTG gantries + floodlight masts on the yard, bollards and
///     hazard-striped kerbs on the quay edge, a laden container ship and a tug at the berth;
///   * a rubble breakwater with tetrapod armour and a red harbour light at its head, plus
///     red/green/yellow buoys marking the channel;
///   * Port Gate: an elevated freight-rail viaduct crossing over the road with a moving blue
///     freight train, container walls and chevron barriers beside the carriageway, and one crane
///     standing close to the road;
///   * moving sea traffic along the whole route (ferries, container ships, tugs, sailboats, a
///     fishing boat) driven by the runtime <see cref="MinatoSeaTraffic"/> component.
///
/// Placement is DATA-DRIVEN from the route-metre constants below (the zone order may be re-mapped)
/// and from the real ground: the quay line is found per station by searching GroundAt for the
/// waterline, so the terminal follows the lead's shoreline if it moves.
/// </summary>
public static partial class MinatoCoastEnvironment
{
    /// <summary>
    /// KEEP THIS false. REVIEWED AND RESOLVED 2026-09-25 (see COPILOT_HANDOFF.md "item 2").
    ///
    /// The name is misleading: this workstream is a PARTIAL replacement paired with surgical
    /// pruning, so `false` is the finished, correct configuration - not an unfinished state.
    /// RedesignHarbor runs unconditionally and PruneLegacyHarbor already deletes exactly the
    /// legacy pieces this file replaces (old cranes, crate stacks, bollards, berthed ships, the
    /// warehouses inside the new terminal, and every legacy vessel on the crossing).
    ///
    /// Setting it to true makes Apply() skip BuildPortInfrastructure AND BuildOceanTraffic
    /// wholesale, which deletes four things this file does NOT rebuild:
    ///   1. the Connected Port Seaward Apron A/B (100 m -> 2295 m) and Landward Apron and their
    ///      MeshColliders - BuildCityLife raycasts those to ground every crowd figure, so the
    ///      pedestrians would float or sink. This is a FUNCTIONAL regression, not a visual one.
    ///      (This file's own yard apron only covers TerminalStartM..TerminalEndM on the sea side.)
    ///   2. the ferris wheel - a landmark, clearly visible in play_minato_contact_00900m.png.
    ///   3. EVERY islet and sea stack across the crossing (2300 m -> 10300 m), including the
    ///      sculpted Minato_Sea_IsletA/B the user asked for after calling the old ones eyesores.
    ///   4. the legacy warehouses outside the terminal footprint that PruneLegacyHarbor
    ///      deliberately keeps as city backdrop.
    /// If this workstream ever does own the aprons, the ferris wheel and the islets, revisit.
    /// </summary>
    private const bool HarborReplacesLegacy = false;

    // ------------------------------------------------------------------ zone ranges (route metres)
    // Keep ALL placement ranges here: the lead may re-map the zone order along the route.
    private const float HarborZoneStartM = 0f;       // "Harbor Awakens"
    private const float HarborZoneEndM = 600f;
    private const float PortGateStartM = 600f;       // "Port Gate Transition"
    private const float PortGateEndM = 1000f;
    /// <summary>The container terminal runs along the sea side over this range.</summary>
    private const float TerminalStartM = 0f;
    private const float TerminalEndM = 1060f;
    /// <summary>Crane stations (route metres). Densest over Harbor Awakens so the cluster
    /// silhouettes against the sun ahead-left; every third one parks its boom raised.</summary>
    private static readonly float[] CraneStationsM = { 60f, 125f, 190f, 255f, 330f, 420f, 505f, 880f, 960f, 1040f };
    /// <summary>The Port Gate crane standing right beside the road (target: crane legs close to the road).</summary>
    private const float GateCraneM = 700f;
    private const float GateCraneLandLegOffsetM = 13.5f;
    /// <summary>Freight-rail viaduct crossing over the road (Port Gate).</summary>
    private const float RailCrossingM = 800f;
    private const float RailDeckAboveRoadM = 10.5f;
    private const float RailLeftReachM = 330f;        // towards the quay
    private const float RailRightReachM = 75f;        // stops short of the skyline towers (>=110 m)
    /// <summary>Moored container ship (bow towards +route) centred on this station.</summary>
    private const float BerthShipM = 270f;
    /// <summary>Breakwater root station; it runs out from the pier end towards open water.</summary>
    private const float BreakwaterRootM = 1060f;
    private const int BreakwaterSegments = 7;

    // ------------------------------------------------------------------ terminal geometry
    private const float YardNearM = 40f;              // yard apron starts this far off the road
    private const float PierDeckY = 2.4f;             // pier deck above sea level
    private const float PierWidthM = 44f;             // pier deck seaward of the quay line
    private const float CraneSeawardOfQuayM = 18f;    // crane gantry centre, so both leg lines sit on the pier
    private const float BerthOffsetFromQuayM = 64f;   // ship centreline from the quay line
    private const float SeaLaneMinClearM = 110f;      // moving ships vs any route station (road, causeway, bridge)

    private static readonly List<(Vector3 c, float r)> _harborKeepOut = new List<(Vector3, float)>();

    private static void RedesignHarbor(MinatoRoute route, Transform[] chapters)
    {
        var root = new GameObject("Harbor Redesign").transform;
        root.SetParent(chapters[0], false);
        _harborKeepOut.Clear();

        if (!HarborReplacesLegacy) PruneLegacyHarbor(route, chapters);
        var mats = HarborMaterials();

        var quay = HarborQuayLine(route);
        BuildHarborTerminal(route, root, mats, quay);
        BuildHarborBreakwater(route, root, mats, quay);
        BuildPortGate(route, root, mats);
        var traffic = new GameObject("Harbor Sea Traffic").transform;
        traffic.SetParent(root, false);
        BuildHarborSeaTraffic(route, traffic, mats, quay);
        BuildStartPrecinct(route, chapters[0]);
        int batches = FlushInstanced(root);
        Debug.Log($"[harbor] redesign built: {root.GetComponentsInChildren<Renderer>(true).Length} renderers, " +
                  $"{batches} instanced batches");
    }

    // =================================================================== materials

    private sealed class HarborMats
    {
        public (string token, Material mat)[] map;
        public Material fallback, hazard, dark, concrete, rock, chevron, buoyRed, buoyGreen, buoyYellow;
    }

    private static Material Weathered(Material m, float weather, float grime, float salt)
    {
        m.SetFloat("_WeatherAmount", weather);
        m.SetFloat("_GrimeAmount", grime);
        m.SetFloat("_WearAmount", 0.20f);
        m.SetColor("_WearColor", new Color(1f, 0.95f, 0.88f));
        m.SetColor("_MossColor", new Color(0.66f, 0.66f, 0.62f));      // salt bloom, not moss
        m.SetFloat("_MossAmount", salt);
        m.SetFloat("_MossBase", SeaLevelY);
        m.SetFloat("_MossHeight", 10f);
        m.SetFloat("_DetailScale", 9f);
        m.SetFloat("_DetailAmount", 0.25f);
        m.SetFloat("_TintVariation", 0.10f);
        m.SetFloat("_TintVarScale", 0.012f);
        UnityEditor.EditorUtility.SetDirty(m);
        return m;
    }

    private static HarborMats HarborMaterials()
    {
        var ctex = Tex(MinatoTex, "Minato_Harbor_Container_Albedo.png");
        var htex = Tex(MinatoTex, "Minato_Harbor_Hazard_Albedo.png");
        Material Cont(string n, Color c) =>
            Weathered(CelMaterial("Minato_Harbor_Container_" + n, c, 0.16f, 0.10f, 0.30f, ctex), 0.25f, 0.30f, 0.10f);

        // Coral paint: brighter and warmer than the legacy bridge vermilion so the cranes glow
        // against the low sun like the targets; weathered (salt low down, grime at joints).
        var coral = Weathered(CelMaterial("Minato_Harbor_CraneCoral", new Color(0.95f, 0.44f, 0.27f),
                                          0.30f, 0.22f, 0.55f), 0.45f, 0.35f, 0.25f);
        var white = Weathered(CelMaterial("Minato_Harbor_White", new Color(0.92f, 0.93f, 0.92f),
                                          0.26f, 0.18f, 0.45f), 0.30f, 0.30f, 0.15f);
        var dark = CelMaterial("Minato_Harbor_Dark", new Color(0.17f, 0.18f, 0.20f), 0.25f, 0.15f, 0.30f);
        var yellow = CelMaterial("Minato_Harbor_Yellow", new Color(0.97f, 0.74f, 0.14f), 0.25f, 0.18f, 0.45f);
        var glass = CelMaterial("Minato_Harbor_Glass", new Color(0.30f, 0.44f, 0.55f), 0.80f, 0.60f, 0.60f);
        var navy = Weathered(CelMaterial("Minato_Harbor_HullNavy", new Color(0.11f, 0.17f, 0.34f),
                                         0.35f, 0.25f, 0.45f), 0.40f, 0.35f, 0.30f);
        var red = Weathered(CelMaterial("Minato_Harbor_HullRed", new Color(0.58f, 0.15f, 0.11f),
                                        0.25f, 0.15f, 0.35f), 0.40f, 0.40f, 0.20f);
        var deck = CelMaterial("Minato_Harbor_Deck", new Color(0.38f, 0.41f, 0.40f), 0.15f, 0.08f, 0.25f,
                               shade: GroundShade);
        var concrete = ConcreteMaterial();
        var rock = CelMaterial("Minato_Rock", new Color(0.62f, 0.60f, 0.57f), gloss: 0.10f,
                               spec: 0.06f, rim: 0.35f,
                               texture: Tex(ShiosaiTex, "Shiosai_Rock_Albedo.png"), shade: GroundShade);
        var hazard = CelMaterial("Minato_Harbor_Hazard", Color.white, 0.18f, 0.10f, 0.35f, htex, GroundShade);
        var chevron = CelMaterial("Minato_Harbor_Chevron", Color.white, 0.20f, 0.12f, 0.35f,
                                  Tex(MinatoTex, "Minato_Harbor_Chevron_Albedo.png"), GroundShade);
        var sign = CelMaterial("Minato_Harbor_SignFace", Color.white, 0.30f, 0.20f, 0.40f,
                               Tex(MinatoTex, "Minato_Harbor_Sign_Albedo.png"));
        var lamp = CelMaterial("Minato_Harbor_Lamp", new Color(1f, 0.96f, 0.82f), 0.60f, 0.50f, 0.80f);
        var steelBlue = Weathered(CelMaterial("Minato_Harbor_SteelBlue", new Color(0.36f, 0.44f, 0.52f),
                                              0.28f, 0.20f, 0.40f), 0.45f, 0.40f, 0.10f);
        var railM = CelMaterial("Minato_Harbor_Rail", new Color(0.33f, 0.30f, 0.28f), 0.20f, 0.12f, 0.25f);
        var loco = CelMaterial("Minato_Harbor_Loco", new Color(0.14f, 0.34f, 0.80f), 0.40f, 0.30f, 0.55f);
        var buoyRed = CelMaterial("Minato_Harbor_BuoyRed", new Color(0.90f, 0.16f, 0.12f), 0.35f, 0.25f, 0.55f);
        var buoyGreen = CelMaterial("Minato_Harbor_BuoyGreen", new Color(0.10f, 0.62f, 0.30f), 0.35f, 0.25f, 0.55f);
        var buoyYellow = CelMaterial("Minato_Harbor_BuoyYellow", new Color(0.98f, 0.78f, 0.12f), 0.35f, 0.25f, 0.55f);

        var map = new (string, Material)[]
        {
            ("harbor_c_navy",  Cont("Navy",  new Color(0.20f, 0.30f, 0.60f))),
            ("harbor_c_cyan",  Cont("Cyan",  new Color(0.22f, 0.68f, 0.78f))),
            ("harbor_c_coral", Cont("Coral", new Color(0.94f, 0.45f, 0.30f))),
            ("harbor_c_white", Cont("White", new Color(0.94f, 0.94f, 0.92f))),
            ("harbor_c_grey",  Cont("Grey",  new Color(0.60f, 0.62f, 0.64f))),
            ("harbor_c_rust",  Cont("Rust",  new Color(0.66f, 0.26f, 0.19f))),
            ("harbor_coral", coral), ("harbor_white", white), ("harbor_dark", dark),
            ("harbor_yellow", yellow), ("harbor_glass", glass), ("harbor_navy", navy),
            ("harbor_red", red), ("harbor_deck", deck), ("harbor_concrete", concrete),
            ("harbor_rock", rock), ("harbor_hazard", hazard), ("harbor_chevron", chevron),
            ("harbor_buoy", buoyRed), ("harbor_lamp", lamp), ("harbor_steelblue", steelBlue),
            ("harbor_rail", railM), ("harbor_loco", loco), ("harbor_sign", sign),
            // legacy Minato_Sea_* slots (sailboat / fishing boat are reused as traffic)
            ("hull_white", white), ("hull_blue", navy), ("steel_grey", steelBlue),
            ("steel_vermilion", coral), ("rust", red),
        };
        return new HarborMats
        {
            map = map, fallback = white, hazard = hazard, dark = dark, concrete = concrete, rock = rock,
            chevron = chevron, buoyRed = buoyRed, buoyGreen = buoyGreen, buoyYellow = buoyYellow,
        };
    }

    /// <summary>Instantiate a harbor family (LODGroup via Inst) and re-skin it by slot token.</summary>
    private static GameObject HarborInst(string model, Transform parent, Vector3 pos, Quaternion rot,
                                         HarborMats mats, float scale = 1f, bool isStatic = true,
                                         (string, Material)[] extra = null)
    {
        var src = Model(model);
        if (src == null) return null;
        var go = Inst(src, parent, Vector3.zero, rot, null);
        var map = mats.map;
        if (extra != null)
        {
            var list = new List<(string, Material)>(extra);
            list.AddRange(mats.map);
            map = list.ToArray();
        }
        RetintBySlot(go, map, mats.fallback);
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = Vector3.one * scale;
        go.isStatic = isStatic;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = isStatic;
        return go;
    }

    // =================================================================== legacy pruning

    /// <summary>
    /// With HarborReplacesLegacy = false the legacy BuildPortInfrastructure / BuildOceanTraffic
    /// still run (they also own the port apron colliders the crowd/boulevard passes ground on, the
    /// ferris wheel and the islands). Remove only the pieces this workstream replaces: the old
    /// cranes, crate stacks, bollards and berthed ships, the warehouses standing inside the new
    /// terminal, and every legacy vessel on the crossing.
    /// </summary>
    private static void PruneLegacyHarbor(MinatoRoute route, Transform[] chapters)
    {
        int removed = 0;
        var port = chapters[0].Find("Port Infrastructure");
        if (port != null)
        {
            var kill = new List<GameObject>();
            foreach (Transform c in port)
            {
                string n = c.name;
                if (n.StartsWith("Minato_Port_ContainerCrane") || n.StartsWith("Minato_Port_CrateStack") ||
                    n.StartsWith("Minato_Port_Bollard") || n.StartsWith("Minato_Sea_"))
                { kill.Add(c.gameObject); continue; }
                if (n.StartsWith("Minato_Port_Warehouse"))
                {
                    var p = c.position;
                    NearestLand(route, p.x, p.z, out int idx);
                    if (idx < 0) continue;
                    float d = route.Distance[idx];
                    var left = -route.SideFlat(idx);
                    float lat = Vector3.Dot(p - route.Position[idx], left);
                    if (lat > 0f && d >= TerminalStartM - 40f && d <= TerminalEndM + 60f) kill.Add(c.gameObject);
                }
            }
            foreach (var g in kill) UnityEngine.Object.DestroyImmediate(g);
            removed += kill.Count;
        }
        var sea = chapters[2].Find("Sea Traffic and Islands");
        if (sea != null)
        {
            var kill = new List<GameObject>();
            foreach (Transform c in sea)
                if (c.name.StartsWith("Minato_Sea_CargoShip") || c.name.StartsWith("Minato_Sea_Ferry") ||
                    c.name.StartsWith("Minato_Sea_Sailboat") || c.name.StartsWith("Minato_Sea_FishingBoat"))
                    kill.Add(c.gameObject);
            foreach (var g in kill) UnityEngine.Object.DestroyImmediate(g);
            removed += kill.Count;
        }
        Debug.Log($"[harbor] pruned {removed} legacy port/sea pieces replaced by the harbor redesign");
    }

    // =================================================================== terminal

    /// <summary>
    /// Per-station quay line: distance to the LEFT of the road (the sea side through the port -
    /// measured from the water mask, which SeaSideSign gets wrong for the first ~300 m) where the
    /// ground first drops under 1.5 m. Sampled every 12 m and median-smoothed so the pier edge
    /// is a clean line rather than following every noise wiggle.
    /// </summary>
    private sealed class QuayLine
    {
        public float[] d, off;
        public float At(float m)
        {
            if (m <= d[0]) return off[0];
            for (int i = 0; i < d.Length - 1; i++)
                if (m <= d[i + 1]) return Mathf.Lerp(off[i], off[i + 1], (m - d[i]) / (d[i + 1] - d[i]));
            return off[off.Length - 1];
        }
    }

    private static Vector3 HarborLeft(MinatoRoute route, int i) => -route.SideFlat(i);

    private static Vector3 HarborFwd(MinatoRoute route, float d)
    {
        int i = route.IndexAt(Mathf.Max(0f, d - 12f));
        int j = route.IndexAt(Mathf.Min(route.Length - 1f, d + 12f));
        var f = route.Position[j] - route.Position[i]; f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    /// <summary>Route point at metre d, extrapolated linearly before 0 / after the end.</summary>
    private static Vector3 HarborRoutePoint(MinatoRoute route, float d)
    {
        if (d < 0f) return route.Position[0] - HarborFwd(route, 0f) * (-d);
        if (d > route.Length) return route.Position[route.Count - 1] + HarborFwd(route, route.Length) * (d - route.Length);
        return route.Position[route.IndexAt(d)];
    }

    private static QuayLine HarborQuayLine(MinatoRoute route)
    {
        var ds = new List<float>();
        var raw = new List<float>();
        for (float d = TerminalStartM - 60f; d <= BreakwaterRootM + 80f; d += 12f)
        {
            float dd = Mathf.Max(0f, d);
            int i = route.IndexAt(dd);
            var p = HarborRoutePoint(route, d);
            var left = HarborLeft(route, i);
            float q = 420f;
            for (float o = 60f; o < 900f; o += 4f)
            {
                var s = p + left * o;
                if (GroundAt(route, s.x, s.z) < SeaLevelY + 1.5f) { q = o; break; }
            }
            ds.Add(d); raw.Add(q);
        }
        var sm = new float[raw.Count];
        for (int k = 0; k < raw.Count; k++)
        {
            var w = new List<float>();
            for (int j = Mathf.Max(0, k - 4); j <= Mathf.Min(raw.Count - 1, k + 4); j++) w.Add(raw[j]);
            w.Sort();
            sm[k] = w[w.Count / 2];
        }
        // light low-pass after the median
        var outv = new float[sm.Length];
        for (int k = 0; k < sm.Length; k++)
        {
            float s = 0f; int n = 0;
            for (int j = Mathf.Max(0, k - 3); j <= Mathf.Min(sm.Length - 1, k + 3); j++) { s += sm[j]; n++; }
            outv[k] = s / n;
        }
        var ql = new QuayLine { d = ds.ToArray(), off = outv };
        Debug.Log($"[harbor] quay line {ql.At(0f):0}/{ql.At(300f):0}/{ql.At(600f):0}/{ql.At(900f):0} m " +
                  "off the road at 0/300/600/900 m");
        return ql;
    }

    /// <summary>Yard surface height at a point: conforming pavement, never below the pier deck.</summary>
    private static float YardY(MinatoRoute route, Vector3 q) =>
        Mathf.Max(SeaLevelY + PierDeckY, GroundAt(route, q.x, q.z) + 0.40f);

    private static void BuildHarborTerminal(MinatoRoute route, Transform root, HarborMats mats, QuayLine quay)
    {
        var parent = new GameObject("Container Terminal").transform;
        parent.SetParent(root, false);
        var apronMat = CelMaterial("Minato_Harbor_YardPavement", new Color(0.66f, 0.67f, 0.65f),
                                   gloss: 0.10f, spec: 0.06f, rim: 0.20f,
                                   texture: Tex(MapleTex, "MapleCity_Pavement_Albedo.png"), shade: GroundShade);

        // ---- yard apron (conforming) + flat pier deck + quay face, one mesh per 240 m chunk
        const float Step = 12f;
        int chunk = 0;
        for (float c0 = TerminalStartM; c0 < TerminalEndM; c0 += 240f, chunk++)
        {
            float c1 = Mathf.Min(TerminalEndM, c0 + 240f);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var pv = new List<Vector3>(); var puv = new List<Vector2>(); var ptri = new List<int>();
            int rows = 0; int lanes = -1;
            for (float d = c0; d <= c1 + 0.01f; d += Step, rows++)
            {
                int i = route.IndexAt(d);
                var p = route.Position[i];
                var left = HarborLeft(route, i);
                float q = quay.At(d);
                // yard lanes from YardNearM to the quay line
                int n = 14;
                lanes = n;
                for (int k = 0; k <= n; k++)
                {
                    float o = Mathf.Lerp(YardNearM, q, k / (float)n);
                    var s = p + left * o;
                    s.y = YardY(route, s);
                    v.Add(s); uv.Add(new Vector2(o * 0.08f, d * 0.08f));
                }
                // pier deck: quay line -> quay + PierWidth, flat; then quay face down to -2.5
                var a = p + left * (q - 2f); a.y = SeaLevelY + PierDeckY;
                var b = p + left * (q + PierWidthM); b.y = SeaLevelY + PierDeckY;
                var c = b; c.y = SeaLevelY - 2.5f;
                pv.Add(a); pv.Add(b); pv.Add(c);
                puv.Add(new Vector2(0f, d * 0.08f)); puv.Add(new Vector2(PierWidthM * 0.08f, d * 0.08f));
                puv.Add(new Vector2(PierWidthM * 0.08f + 0.4f, d * 0.08f));
            }
            int stride = lanes + 1;
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < lanes; k++)
                {
                    int i0 = r * stride + k;
                    // Same winding as PortApronMesh on the sea side (route-forward x left points up).
                    var t0 = new[] { i0, i0 + stride, i0 + stride + 1 };
                    var t1 = new[] { i0, i0 + stride + 1, i0 + 1 };
                    foreach (var t in new[] { t0, t1 })
                    {
                        var nrm = Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]);
                        if (nrm.y < 0f) { (t[1], t[2]) = (t[2], t[1]); nrm = -nrm; }
                        if (nrm.sqrMagnitude < 1e-8f || nrm.normalized.y < 0.55f) continue;   // fold/steep debris
                        tri.AddRange(t);
                    }
                }
            for (int r = 0; r < rows - 1; r++)
            {
                int i0 = r * 3, i1 = (r + 1) * 3;
                AddQuadUp(ptri, pv, i0, i1, i1 + 1, i0 + 1, Vector3.up);
                // quay face: outward = away from the road
                var outward = pv[i0 + 1] - pv[i0]; outward.y = 0f;
                AddQuadUp(ptri, pv, i0 + 1, i1 + 1, i1 + 2, i0 + 2, outward.normalized);
            }
            var yard = new Mesh { name = $"Minato_Harbor_Yard_{chunk}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            yard.SetVertices(v); yard.SetUVs(0, uv); yard.SetTriangles(tri, 0);
            yard.RecalculateNormals(); yard.RecalculateBounds();
            AddMesh(parent, $"Harbor Yard Apron {chunk}", yard, apronMat, true).isStatic = true;
            var pier = new Mesh { name = $"Minato_Harbor_Pier_{chunk}" };
            pier.SetVertices(pv); pier.SetUVs(0, puv); pier.SetTriangles(ptri, 0);
            pier.RecalculateNormals(); pier.RecalculateBounds();
            AddMesh(parent, $"Harbor Pier Deck {chunk}", pier, mats.concrete, true).isStatic = true;
        }

        // ---- quay-edge kerb (hazard stripes) + bollards, GPU-instanced
        var kerb = Model("Minato_Harbor_HazardKerb");
        var bollard = Model("Minato_Harbor_Bollard");
        int kerbs = 0, bollards = 0;
        for (float d = TerminalStartM; d < TerminalEndM; d += 3f)
        {
            int i = route.IndexAt(d);
            var left = HarborLeft(route, i);
            var fwd = HarborFwd(route, d);
            var e = route.Position[i] + left * (quay.At(d) + PierWidthM - 0.45f);
            e.y = SeaLevelY + PierDeckY;
            QueueInstanced(kerb, e, Quaternion.LookRotation(fwd), Vector3.one, mats.hazard, null, 900 + (int)(d / 240f));
            kerbs++;
            if (((int)(d / 3f)) % 8 == 0)
            {
                var bp = e - left * 1.6f;
                QueueInstanced(bollard, bp, Quaternion.LookRotation(fwd), Vector3.one, mats.dark, null, 900 + (int)(d / 240f));
                bollards++;
            }
        }

        // ---- STS cranes on the pier, booms out over the water (+Z = seaward)
        int cranes = 0;
        for (int k = 0; k < CraneStationsM.Length; k++)
        {
            float d = CraneStationsM[k];
            if (d > TerminalEndM - 20f) continue;
            int i = route.IndexAt(d);
            var left = HarborLeft(route, i);
            var pos = route.Position[i] + left * (quay.At(d) + CraneSeawardOfQuayM);
            pos.y = SeaLevelY + PierDeckY;
            bool raised = k % 3 == 2;
            var go = HarborInst(raised ? "Minato_Harbor_STSCraneRaised" : "Minato_Harbor_STSCrane", parent, pos,
                                Quaternion.LookRotation(left, Vector3.up), mats);
            if (go != null) { go.name = $"Harbor STS Crane {d:0}"; cranes++; }
            _harborKeepOut.Add((pos + left * 40f, 70f));
        }

        // ---- moored container ship + berth keep-out
        {
            int i = route.IndexAt(BerthShipM);
            var left = HarborLeft(route, i);
            var fwd = HarborFwd(route, BerthShipM);
            var pos = route.Position[i] + left * (quay.At(BerthShipM) + BerthOffsetFromQuayM);
            pos.y = SeaLevelY;
            var ship = HarborInst("Minato_Harbor_ContainerShip", parent, pos, Quaternion.LookRotation(fwd), mats);
            if (ship != null) ship.name = "Harbor Berth Container Ship";
            _harborKeepOut.Add((pos, 150f));
        }

        // ---- yard: container blocks, RTG gantries, floodlight masts
        var blocks = new[] { "Minato_Harbor_ContainerBlockA", "Minato_Harbor_ContainerBlockB", "Minato_Harbor_ContainerBlockC" };
        var rng = new System.Random(606);
        int nBlocks = 0, rtgs = 0, masts = 0;
        for (float d = TerminalStartM + 20f; d < TerminalEndM - 20f; d += 42f)
        {
            // Keep the rail viaduct corridor clear (the deck is lower than a 5-high stack on the hump).
            if (Mathf.Abs(d - RailCrossingM) < 30f) continue;
            int i = route.IndexAt(d);
            var left = HarborLeft(route, i);
            var fwd = HarborFwd(route, d);
            float q = quay.At(d);
            for (float o = 95f; o < q - 30f; o += 48f)
            {
                if (rng.NextDouble() < 0.28) continue;
                var c = route.Position[i] + left * (o + (float)rng.NextDouble() * 10f) + fwd * (float)(rng.NextDouble() * 10f - 5f);
                // Reject steep ground: the stack footprint must sit on a near-flat pad.
                float y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var dx in new[] { -14f, 14f })
                    foreach (var dz in new[] { -24f, 24f })
                    {
                        var s = c + left * dx + fwd * dz;
                        float y = YardY(route, s);
                        y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                    }
                if (y1 - y0 > 3.2f) continue;
                c.y = y0 - 0.1f;
                var rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, rng.Next(2) * 180f, 0f);
                var go = HarborInst(blocks[rng.Next(blocks.Length)], parent, c, rot, mats);
                if (go == null) continue;
                nBlocks++;
                if (rng.NextDouble() < 0.30)
                {
                    HarborInst("Minato_Harbor_RTG", parent, c + fwd * ((float)rng.NextDouble() * 16f - 8f), rot, mats);
                    rtgs++;
                }
            }
            if (((int)(d / 42f)) % 4 == 1)
            {
                var m = route.Position[i] + left * Mathf.Lerp(80f, q - 20f, (float)rng.NextDouble());
                m.y = YardY(route, m);
                HarborInst("Minato_Harbor_LightMast", parent, m, Quaternion.LookRotation(fwd), mats);
                masts++;
            }
        }

        // ---- marine signs facing the approaching rider on the sea-side verge
        foreach (float d in new[] { 90f, 360f, 640f })
        {
            int i = route.IndexAt(d);
            var left = HarborLeft(route, i);
            var s = route.Position[i] + left * 9.5f;
            s.y = GroundAt(route, s.x, s.z) - 0.1f;
            HarborInst("Minato_Harbor_Sign", parent, s, Quaternion.LookRotation(HarborFwd(route, d)), mats);
        }

        // ---- the target's yellow/black seawall kerb, just outside the sea-side guardrail
        for (float d = HarborZoneStartM + 10f; d < PortGateEndM; d += 3f)
        {
            int i = route.IndexAt(d);
            var s = route.Position[i] + HarborLeft(route, i) * 6.3f;
            s.y = GroundAt(route, s.x, s.z) - 0.12f;
            QueueInstanced(kerb, s, Quaternion.LookRotation(HarborFwd(route, d)), Vector3.one, mats.hazard, null, 950 + (int)(d / 240f));
            kerbs++;
        }

        Debug.Log($"[harbor] terminal: {cranes} STS cranes, {nBlocks} container blocks, {rtgs} RTGs, " +
                  $"{masts} light masts, {kerbs} hazard kerbs, {bollards} bollards, 1 berthed ship");
    }

    private static void AddQuadUp(List<int> tri, List<Vector3> v, int a, int b, int c, int d, Vector3 want)
    {
        var n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (Vector3.Dot(n, want) >= 0f) tri.AddRange(new[] { a, b, c, a, c, d });
        else tri.AddRange(new[] { a, c, b, a, d, c });
    }

    // =================================================================== breakwater + buoys

    private static void BuildHarborBreakwater(MinatoRoute route, Transform root, HarborMats mats, QuayLine quay)
    {
        var parent = new GameObject("Breakwater").transform;
        parent.SetParent(root, false);
        int i = route.IndexAt(BreakwaterRootM);
        var left = HarborLeft(route, i);
        var fwd = HarborFwd(route, BreakwaterRootM);
        // Runs out from the pier end, angled back towards the harbour so from the road (ahead-left)
        // it reads as the arm enclosing the basin with the red light at its head.
        var dir = (left * 0.82f - fwd * 0.57f).normalized;
        var start = route.Position[i] + left * (quay.At(BreakwaterRootM) + PierWidthM - 6f);
        start.y = SeaLevelY;
        var rot = Quaternion.LookRotation(dir);
        for (int k = 0; k < BreakwaterSegments; k++)
        {
            var c = start + dir * (20f + k * 40f);
            HarborInst("Minato_Harbor_Breakwater", parent, c, rot, mats);
            _harborKeepOut.Add((c, 45f));
        }
        var head = start + dir * (20f + BreakwaterSegments * 40f - 6f);
        HarborInst("Minato_Harbor_BreakwaterHead", parent, head, rot, mats);
        HarborInst("Minato_Harbor_Lighthouse", parent, head + Vector3.up * 2.8f, rot, mats);
        _harborKeepOut.Add((head, 60f));

        // Channel buoys: red to port / green to starboard of the fairway into the basin, plus
        // yellow special marks along the quay approach. They bob (MinatoSeaTraffic, speed 0).
        var traffic = new List<MinatoSeaTraffic.Mover>();
        int nb = 0;
        // Fairway off the breakwater head, running back along the terminal: red (port hand,
        // quay side) and green (starboard) pairs 100 m apart.
        for (int k = 0; k < 4; k++)
        {
            foreach (var (s, kind) in new[] { (-1f, "Can"), (1f, "Cone") })
            {
                var bp = head + left * (60f + s * 50f) - fwd * (90f + k * 120f);
                bp.y = SeaLevelY;
                if (GroundAt(route, bp.x, bp.z) > SeaLevelY - 3f) continue;
                bool blocked = false;
                foreach (var (c, r) in _harborKeepOut)
                    if ((new Vector2(c.x - bp.x, c.z - bp.z)).sqrMagnitude < (r * 0.8f) * (r * 0.8f)) blocked = true;
                if (blocked) continue;
                var mat = kind == "Can" ? mats.buoyRed : mats.buoyGreen;
                var go = HarborInst($"Minato_Harbor_Buoy{kind}", parent, bp, Quaternion.identity, mats, 1f, false,
                                    new (string, Material)[] { ("harbor_buoy", mat) });
                if (go == null) continue;
                traffic.Add(new MinatoSeaTraffic.Mover { target = go.transform, a = bp, b = bp, speed = 0f, heave = 0.18f, tilt = 3f });
                nb++;
            }
        }
        for (float d = 150f; d < 700f; d += 180f)
        {
            int j = route.IndexAt(d);
            var bp = route.Position[j] + HarborLeft(route, j) * (quay.At(d) + 150f);
            bp.y = SeaLevelY;
            if (GroundAt(route, bp.x, bp.z) > SeaLevelY - 3f) continue;
            var go = HarborInst("Minato_Harbor_BuoyCross", parent, bp, Quaternion.identity, mats, 1f, false,
                                new (string, Material)[] { ("harbor_buoy", mats.buoyYellow) });
            if (go == null) continue;
            traffic.Add(new MinatoSeaTraffic.Mover { target = go.transform, a = bp, b = bp, speed = 0f, heave = 0.18f, tilt = 3f });
            nb++;
        }
        var drv = parent.gameObject.AddComponent<MinatoSeaTraffic>();
        drv.movers = traffic.ToArray();
        Debug.Log($"[harbor] breakwater {BreakwaterSegments} x 40 m + head + light, {nb} buoys");
    }

    // =================================================================== Port Gate

    private static void BuildPortGate(MinatoRoute route, Transform root, HarborMats mats)
    {
        var parent = new GameObject("Port Gate").transform;
        parent.SetParent(root, false);

        // ---- crane standing right beside the road, boom out to sea
        {
            int i = route.IndexAt(GateCraneM);
            var left = HarborLeft(route, i);
            var pos = route.Position[i] + left * (GateCraneLandLegOffsetM + 15.25f);
            pos.y = GroundAt(route, pos.x, pos.z) - 0.2f;
            var go = HarborInst("Minato_Harbor_STSCrane", parent, pos, Quaternion.LookRotation(left), mats);
            if (go != null) go.name = "Port Gate Crane";
        }

        // ---- freight rail viaduct across the road + train
        var movers = new List<MinatoSeaTraffic.Mover>();
        {
            int i = route.IndexAt(RailCrossingM);
            var p = route.Position[i];
            var left = HarborLeft(route, i);
            float deckY = p.y + RailDeckAboveRoadM;
            var rot = Quaternion.LookRotation(left);
            int spans = 0;
            // Spans centred on the road and every 30 m either side, so the bents (at span ends)
            // stand at +/-15 m, +/-45 m... never inside the corridor.
            for (float o = -Mathf.Floor(RailRightReachM / 30f) * 30f; o <= RailLeftReachM; o += 30f)
            {
                var c = p + left * o; c.y = deckY;
                HarborInst("Minato_Harbor_RailSpan", parent, c, rot, mats);
                spans++;
                foreach (float e in o + 30f > RailLeftReachM ? new[] { -15f, 15f } : new[] { -15f })
                {
                    var b = p + left * (o + e);
                    float gy = GroundAt(route, b.x, b.z) - 0.4f;
                    float h = Mathf.Max(0.5f, deckY - 3.0f - gy);
                    b.y = gy;
                    var bent = HarborInst("Minato_Harbor_RailBent", parent, b, rot, mats);
                    if (bent != null) bent.transform.localScale = new Vector3(1f, h, 1f);
                }
            }
            var a = p - left * (RailRightReachM - 140f);          // train front range
            var bEnd = p + left * (RailLeftReachM - 8f);
            a.y = bEnd.y = deckY + 0.46f;
            var train = HarborInst("Minato_Harbor_FreightTrain", parent, a, rot, mats, 1f, false);
            if (train != null)
                movers.Add(new MinatoSeaTraffic.Mover
                {
                    target = train.transform, a = a, b = bEnd, speed = 7f, phase = 0.18f,
                    heave = 0f, tilt = 0f, keepHeading = true,
                });
            Debug.Log($"[harbor] port gate rail viaduct: {spans} spans, deck {RailDeckAboveRoadM} m over the road");
        }

        // ---- container walls + chevron barriers on the landward side, hazard posts
        var post = Model("Minato_Harbor_HazardPost");
        var rng = new System.Random(61);
        int walls = 0, barriers = 0;
        for (float d = PortGateStartM + 20f; d < PortGateEndM - 20f; d += 62f)
        {
            if (Mathf.Abs(d - RailCrossingM) < 22f) continue;
            int i = route.IndexAt(d);
            var right = route.SideFlat(i);
            var fwd = HarborFwd(route, d);
            var c = route.Position[i] + right * (11.5f + (float)rng.NextDouble() * 2f) + fwd * 31f;
            c.y = GroundAt(route, c.x, c.z) - 0.15f;
            HarborInst(rng.NextDouble() < 0.5 ? "Minato_Harbor_ContainerWallA" : "Minato_Harbor_ContainerWallB",
                       parent, c, Quaternion.LookRotation(fwd), mats);
            // a second, taller row behind for the canyon feel
            var c2 = c + right * 3.2f; c2.y = GroundAt(route, c2.x, c2.z) - 0.15f;
            HarborInst("Minato_Harbor_ContainerWallB", parent, c2, Quaternion.LookRotation(-fwd), mats);
            walls += 2;
        }
        // Chevron barriers are real instances (two material slots: concrete + chevron board);
        // 6 m pitch with the 3 m unit doubled up keeps the object count ~130.
        for (float d = PortGateStartM; d < PortGateEndM; d += 3f)
        {
            if (Mathf.Abs(d - RailCrossingM - 15f) < 2.5f) continue;     // bent at +15 m is on the other side
            int i = route.IndexAt(d);
            var fwd = HarborFwd(route, d);
            var s = route.Position[i] + route.SideFlat(i) * 7.0f;
            s.y = GroundAt(route, s.x, s.z) - 0.1f;
            var g = HarborInst("Minato_Harbor_ChevronBarrier", parent, s, Quaternion.LookRotation(fwd), mats);
            if (g != null) barriers++;
        }
        // Hazard posts on the sea-side verge (the target's yellow/black kerb posts).
        for (float d = PortGateStartM; d < PortGateEndM; d += 24f)
        {
            int i = route.IndexAt(d);
            var s = route.Position[i] + HarborLeft(route, i) * 6.4f;
            s.y = GroundAt(route, s.x, s.z) - 0.1f;
            QueueInstanced(post, s, Quaternion.LookRotation(HarborFwd(route, d)), Vector3.one, mats.hazard, null, 981);
        }
        var drv = parent.gameObject.AddComponent<MinatoSeaTraffic>();
        drv.movers = movers.ToArray();
        Debug.Log($"[harbor] port gate: {walls} container walls, {barriers} chevron barriers, 1 roadside crane");
    }

    // =================================================================== sea traffic

    private struct HarborLane
    {
        public float d0, d1, side, off, speed;
        public string model;
        public float scale;
        public HarborLane(float d0, float d1, float side, float off, string model, float speed, float scale = 1f)
        { this.d0 = d0; this.d1 = d1; this.side = side; this.off = off; this.model = model; this.speed = speed; this.scale = scale; }
    }

    private static void BuildHarborSeaTraffic(MinatoRoute route, Transform parent, HarborMats mats, QuayLine quay)
    {
        // side +1 = left of the direction of travel (the harbour side in the port), -1 = right.
        // Lanes run parallel to the local route so they never cross the causeway/bridge; each is
        // validated below and pushed further out if it strays too close or onto land.
        float q = quay.At(300f);
        var lanes = new[]
        {
            new HarborLane(-500f, 1300f, 1f, q + 400f, "Minato_Harbor_Ferry", 5.5f),
            new HarborLane(-300f, 1600f, 1f, q + 620f, "Minato_Harbor_ContainerShip", 3.2f),
            new HarborLane(120f, 760f, 1f, q + 230f, "Minato_Harbor_Tug", 2.4f),
            new HarborLane(900f, 2200f, 1f, 560f, "Minato_Sea_Sailboat", 2.2f, 1.2f),
            new HarborLane(2500f, 4300f, 1f, 240f, "Minato_Sea_Sailboat", 2.0f, 1.2f),
            new HarborLane(2700f, 3900f, -1f, 170f, "Minato_Sea_Sailboat", 2.3f, 1.2f),
            new HarborLane(3200f, 6900f, -1f, 420f, "Minato_Harbor_ContainerShip", 4.2f),
            new HarborLane(3800f, 7000f, 1f, 650f, "Minato_Harbor_Ferry", 5.0f),
            new HarborLane(4600f, 6600f, -1f, 160f, "Minato_Harbor_Tug", 2.8f),
            new HarborLane(5200f, 7300f, 1f, 190f, "Minato_Sea_FishingBoat", 2.0f, 1.3f),
            new HarborLane(7700f, 10000f, 1f, 520f, "Minato_Harbor_ContainerShip", 3.6f),
            new HarborLane(8000f, 9900f, -1f, 330f, "Minato_Sea_Sailboat", 2.0f, 1.2f),
            new HarborLane(7900f, 9700f, -1f, 900f, "Minato_Harbor_Ferry", 5.0f),
        };

        // All stations (land AND over water) - ships must clear the causeway and bridge too.
        var stations = new List<Vector3>();
        for (int i = 0; i < route.Count; i += 4) stations.Add(route.Position[i]);
        bool Clear(Vector3 s)
        {
            if (GroundAt(route, s.x, s.z) > SeaLevelY - 4f) return false;
            float c2 = SeaLaneMinClearM * SeaLaneMinClearM;
            foreach (var p in stations)
            {
                float dx = p.x - s.x, dz = p.z - s.z;
                if (dx * dx + dz * dz < c2) return false;
            }
            foreach (var (c, r) in _harborKeepOut)
            {
                float dx = c.x - s.x, dz = c.z - s.z;
                if (dx * dx + dz * dz < r * r) return false;
            }
            return true;
        }

        var movers = new List<MinatoSeaTraffic.Mover>();
        var rng = new System.Random(4242);
        int placed = 0, skipped = 0;
        foreach (var L in lanes)
        {
            bool ok = false;
            Vector3 a = default, b = default;
            for (int attempt = 0; attempt < 6 && !ok; attempt++)
            {
                float off = L.off + attempt * 70f;
                a = LanePoint(route, L.d0, L.side, off);
                b = LanePoint(route, L.d1, L.side, off);
                ok = true;
                for (int k = 0; k <= 24 && ok; k++) ok = Clear(Vector3.Lerp(a, b, k / 24f));
                if (!ok)
                {
                    // try trimming the lane ends before pushing it further out
                    var a2 = Vector3.Lerp(a, b, 0.15f); var b2 = Vector3.Lerp(a, b, 0.85f);
                    bool ok2 = true;
                    for (int k = 0; k <= 24 && ok2; k++) ok2 = Clear(Vector3.Lerp(a2, b2, k / 24f));
                    if (ok2) { a = a2; b = b2; ok = true; }
                }
            }
            if (!ok) { skipped++; Debug.LogWarning($"[harbor] sea lane {L.d0:0}-{L.d1:0} ({L.model}) rejected: no clear water"); continue; }
            a.y = b.y = SeaLevelY;
            float phase = (float)rng.NextDouble();
            var start = Vector3.Lerp(a, b, phase < 0.5f ? phase * 2f : (1f - phase) * 2f);
            var go = HarborInst(L.model, parent, start, Quaternion.LookRotation(b - a), mats, L.scale, false);
            if (go == null) continue;
            movers.Add(new MinatoSeaTraffic.Mover
            {
                target = go.transform, a = a, b = b, speed = L.speed, phase = phase,
                heave = L.model.Contains("ContainerShip") ? 0.05f : 0.14f,
                tilt = L.model.Contains("ContainerShip") ? 0.25f : 1.2f,
                turnRate = L.model.Contains("ContainerShip") ? 5f : 14f,
            });
            placed++;
        }
        var drv = parent.gameObject.AddComponent<MinatoSeaTraffic>();
        drv.movers = movers.ToArray();
        Debug.Log($"[harbor] sea traffic: {placed} moving vessels on validated lanes ({skipped} lanes rejected), " +
                  $">= {SeaLaneMinClearM:0} m from any route station");
    }

    private static Vector3 LanePoint(MinatoRoute route, float d, float side, float off)
    {
        float dd = Mathf.Clamp(d, 0f, route.Length);
        int i = route.IndexAt(dd);
        return HarborRoutePoint(route, d) + (-route.SideFlat(i)) * (side * off);
    }

    // =================================================================== probe

    /// <summary>Read-only geography probe for planning the harbor layout: logs where the sea
    /// actually starts beside the port road and writes a coarse water mask. Touches no scene.</summary>
    public static void HarborProbe()
    {
        var route = MinatoRoute.Load();
        BuildLandform(route);
        var sb = new StringBuilder();
        for (float d = 0f; d <= 2600f; d += 50f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var left = HarborLeft(route, i);
            float quayOff = -1f;
            var row = new StringBuilder();
            for (float off = 10f; off <= 900f; off += 10f)
            {
                var q = p + left * off;
                float g = GroundAt(route, q.x, q.z);
                if (quayOff < 0f && g < SeaLevelY + 1.0f) quayOff = off;
                if (off % 50f == 0f) row.Append($"{g:0.0} ");
            }
            sb.AppendLine($"d={d:0} p=({p.x:0.0},{p.y:0.0},{p.z:0.0}) leftQuay={quayOff} | {row}");
        }
        File.WriteAllText("harbor_probe.txt", sb.ToString());
        Debug.Log("[harbor-probe] wrote harbor_probe.txt");
    }
}
