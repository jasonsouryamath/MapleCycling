using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Minato Coast redesign workstream: SKYLINE. See MINATO_VISUAL_DESIGN.md at the project root
/// and design_assets/concepts/MinatoCoast_GlassTideDistrict_Target_v01.png (primary target).
/// Owned by one workstream only; called from MinatoCoastEnvironment.Apply().
///
/// Builds, on the LANDWARD side of the route (the rider's right):
///   * the Glass Tide District waterfront edge - podium / white mid-rise frontage with cafe
///     terraces, planters, pines and a brushed-metal ring sculpture;
///   * an ELEVATED MONORAIL (guideway on columns, a shuttling 3-car train) running parallel to
///     the road above the plaza, curving inland into terminal blocks at both ends;
///   * the city behind it from 4-6 recognisable building FAMILIES (tools/blender/
///     build_minato_skyline.py): slim setback glass towers, curved glass towers, stepped
///     terrace towers, white-concrete mid-rises and podiums, with skybridges between towers;
///   * a height-graded skyline mass (peaking over the district core) that holds the backdrop of
///     zones 1-3, and a smaller far-shore skyline cluster that reads ahead of the rider across the
///     water from the causeway and the bridge.
///
/// Everything is DATA-DRIVEN from the route-metre constants below so the lead can re-map the
/// zones without rewriting placement. Shared meshes/materials only; every building carries an
/// authored LOD0/1/2 ladder with its own screen-height thresholds (see SkyLod).
/// </summary>
public static partial class MinatoCoastEnvironment
{
    /// <summary>Flip to true once this workstream's replacement is verified in gameplay captures;
    /// Apply() then skips the legacy builders it replaces.</summary>
    private const bool SkylineReplacesLegacy = true;

    // =============================================================== route-metre ranges
    // ALL PROVISIONAL. Zone map (UPDATE 2): Glass Tide District 1000-1800.
    /// <summary>Waterfront frontage + plaza dressing (podiums, cafes, sculpture, pines).</summary>
    private const float SkyDistrictStartM = 1000f;
    private const float SkyDistrictEndM = 1800f;
    /// <summary>Guideway runs parallel over the district and curves inland beyond both ends.</summary>
    private const float SkyGuideCurveM = 130f;
    /// <summary>Backdrop skyline extent (city behind zones 1-3).</summary>
    private const float SkyBackdropStartM = 150f;
    private const float SkyBackdropEndM = 2600f;
    /// <summary>Peak of the height grading (district core), route metres / lateral metres.</summary>
    private const float SkyCoreM = 1420f;
    private const float SkyCoreLateralM = 250f;
    /// <summary>Far-shore skyline cluster: anchor route metre and landward offset.</summary>
    private const float SkyFarAnchorM = 10900f;
    private const float SkyFarLateralM = 760f;
    private const float SkyFarRadiusM = 430f;
    private const bool SkyFarClusterEnabled = true;

    // =============================================================== lateral layout (metres)
    /// <summary>Front face of the frontage buildings. Outside the 18.5-30 m market band.</summary>
    private const float SkyFrontageFaceM = 40f;
    /// <summary>Guideway centreline over the plaza, between the market band and the frontage.</summary>
    private const float SkyGuideLateralM = 31f;
    private const float SkyGuideInlandM = 118f;
    /// <summary>Beam-top height above the plaza.</summary>
    private const float SkyGuideDeckM = 11.5f;
    private const float SkyGuideSpanM = 24f;             // == GUIDE_LEN in the Blender builder
    private const float SkyPierNominalM = 10f;            // pier cap-top height in the asset
    private const float SkyBeamDepthM = 1.9f;
    /// <summary>Clear-of-road rule for anything this workstream places (legacy CorridorClearM).</summary>
    private const float SkyCorridorClearM = 18f;

    private sealed class SkyFamily
    {
        public string name;
        public GameObject src;
        public float hx, hz, h;      // footprint half-extents (local X along road, Z toward road), height
        public bool round;
        public int tier;             // 0 podium, 1 mid-rise, 2 tower, 3 tall tower
    }

    private sealed class SkyLot
    {
        public SkyFamily fam;
        public Vector3 pos;          // base centre (world)
        public Quaternion rot;
        public Vector3 scale;
        public float d, lateral;
        public float HalfX => fam.hx * scale.x;
        public float HalfZ => fam.hz * scale.z;
        public float Height => fam.h * scale.y;
        public float Radius => Mathf.Max(HalfX, HalfZ);
    }

    private static void RedesignSkyline(MinatoRoute route, Transform[] chapters)
    {
        // Only the new district is built while the legacy builders are still live - otherwise the
        // two cities would interpenetrate. The flag flip is the single switch.
        if (!SkylineReplacesLegacy) return;

        var chapter = chapters[0];
        var existing = chapter.Find("Glass Tide District");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var root = new GameObject("Glass Tide District").transform;
        root.SetParent(chapter, false);

        var fams = SkyFamilies();
        if (fams == null) return;
        var mats = SkyMaterials();

        var aprons = new List<MeshCollider>();
        var terrainGround = new List<MeshCollider>();
        var regionRoot = chapter.parent != null ? chapter.parent : chapter;
        foreach (var mc in regionRoot.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.name.StartsWith("Connected Port")) aprons.Add(mc);
            else if (mc.name.StartsWith("Terrain_Chunk")) terrainGround.Add(mc);
        }
        Physics.SyncTransforms();
        float Surface(Vector3 q)
        {
            if (GroundByRenderedSurface(aprons, terrainGround, q, out float y, out _)) return y;
            return Mathf.Max(SeaLevelY + 0.65f, GroundAt(route, q.x, q.z) + 0.38f);
        }

        var lots = new List<SkyLot>();
        var clearDiscs = new List<Vector3>();   // (x, radius, z) for clearing legacy props
        var guide = SkyGuidewayPath(route, Surface);

        // 1. Frontage + plaza dressing, 2. backdrop rows, 3. far cluster, 4. transit, 5. bridges.
        var frontRoot = new GameObject("Waterfront Frontage").transform; frontRoot.SetParent(root, false);
        var cityRoot = new GameObject("Skyline Towers").transform; cityRoot.SetParent(root, false);
        var plazaRoot = new GameObject("Plaza Dressing").transform; plazaRoot.SetParent(root, false);
        var transitRoot = new GameObject("Elevated Transit").transform; transitRoot.SetParent(root, false);

        int frontage = SkyBuildFrontage(route, fams, mats, frontRoot, plazaRoot, lots, clearDiscs,
                                        Surface);
        int towers = SkyBuildBackdrop(route, fams, mats, cityRoot, lots, guide);
        int far = SkyFarClusterEnabled ? SkyBuildFarCluster(route, fams, mats, cityRoot, lots) : 0;
        int piers = SkyBuildTransit(route, mats, transitRoot, guide, fams, lots, clearDiscs, Surface);
        int bridges = SkyBuildSkybridges(mats, cityRoot, lots);
        int lamps = SkyLegacyLampParity(route, root);

        foreach (var lot in lots)
            clearDiscs.Add(new Vector3(lot.pos.x, lot.Radius + 1.5f, lot.pos.z));
        int cleared = SkyClearUnder(chapter, clearDiscs);

        // Triangle budget report per family (LOD0), so perf regressions are visible in the log.
        var perFam = new Dictionary<string, int>();
        foreach (var lot in lots) perFam[lot.fam.name] = perFam.TryGetValue(lot.fam.name, out var c) ? c + 1 : 1;
        var sb = new System.Text.StringBuilder();
        foreach (var f in fams.Values)
        {
            int n = perFam.TryGetValue(f.name, out var k) ? k : 0;
            sb.Append($"{f.name} x{n} ({SkyTris(f.src, "_LOD0")}/{SkyTris(f.src, "_LOD1")}/{SkyTris(f.src, "_LOD2")} tris); ");
        }
        Debug.Log($"[minato-skyline] Glass Tide District: {frontage} frontage blocks, {towers} backdrop " +
                  $"buildings, {far} far-shore towers, {piers} guideway piers, {bridges} skybridges, " +
                  $"{lamps} sea-side lamps (legacy parity); cleared {cleared} legacy props under new " +
                  $"footprints. Families: {sb}");
    }

    // =================================================================== assets + materials

    private static Dictionary<string, SkyFamily> SkyFamilies()
    {
        var d = new Dictionary<string, SkyFamily>();
        void Add(string n, float hx, float hz, float h, bool round, int tier)
        {
            var src = Model("Minato_Skyline_" + n);
            if (src == null) return;
            d[n] = new SkyFamily { name = n, src = src, hx = hx, hz = hz, h = h, round = round, tier = tier };
        }
        Add("TowerSlimA", 16.0f, 14.0f, 119f, false, 3);
        Add("TowerSlimB", 12.8f, 12.8f, 172f, false, 3);
        Add("TowerRoundA", 19.8f, 19.8f, 124f, true, 3);
        Add("TowerRoundB", 21.0f, 12.0f, 102f, false, 2);
        Add("TowerStepped", 19.0f, 15.0f, 63f, false, 2);
        Add("MidriseA", 21.3f, 11.8f, 35f, false, 1);
        Add("MidriseB", 15.2f, 13.2f, 46f, false, 1);
        Add("PodiumA", 18.2f, 10.0f, 11f, false, 0);
        Add("PodiumB", 14.2f, 9.0f, 14f, false, 0);
        if (d.Count < 9)
        {
            Debug.LogError("[minato-skyline] building families missing - run " +
                           "tools/blender/build_minato_skyline.py (found " + d.Count + "/9)");
            return null;
        }
        return d;
    }

    private static Dictionary<string, Material> _skyMats;

    private static Dictionary<string, Material> SkyMaterials()
    {
        const string SkyTexDir = "Assets/Environment/MinatoCoast/Skyline";
        foreach (var f in new[] { "Minato_Skyline_Curtain.png", "Minato_Skyline_Ribbon.png",
                                  "Minato_Skyline_Shopfront.png" })
        {
            // Facades are seen at grazing angles down a boulevard: anisotropic filtering keeps the
            // mullion grid from smearing into a blur on the near towers.
            if (AssetImporter.GetAtPath($"{SkyTexDir}/{f}") is TextureImporter ti && ti.anisoLevel < 8)
            {
                ti.anisoLevel = 8;
                ti.SaveAndReimport();
            }
        }
        var curtain = Tex(SkyTexDir, "Minato_Skyline_Curtain.png");
        var ribbon = Tex(SkyTexDir, "Minato_Skyline_Ribbon.png");
        var shop = Tex(SkyTexDir, "Minato_Skyline_Shopfront.png");

        // GLASS. The cel shader has no reflections, so "reflective glass" is built from three
        // cheap cues: (1) the curtain texture's per-panel value jitter and per-floor sky gradient,
        // (2) a world-HEIGHT tint that lifts the upper floors towards the sky colour, the way a
        // real tower reflects more sky the higher it climbs, and (3) a light-independent fresnel
        // edge (_EdgeRim*) that brightens faces seen at grazing angles. Spec is kept moderate so
        // the low sun draws a glint band instead of a white-out.
        Material Glass(string n, Color c, Color shade)
        {
            var m = CelMaterial(n, c, gloss: 0.55f, spec: 0.42f, rim: 0.30f, texture: curtain, shade: shade);
            m.SetColor("_RimColor", new Color(0.80f, 0.90f, 1.00f, 1f));
            m.SetFloat("_ShadowAmbient", 0.50f);
            m.SetFloat("_ShadeStrength", 0.55f);
            m.SetColor("_EdgeRimColor", new Color(0.72f, 0.86f, 1.00f, 1f));
            m.SetFloat("_EdgeRimStrength", 0.30f);
            m.SetFloat("_EdgeRimPower", 2.4f);
            m.SetColor("_HeightTint", new Color(1.30f, 1.40f, 1.45f, 1f));
            m.SetVector("_HeightRange", new Vector4(10f, 170f, 0.75f, 0f));
            m.SetFloat("_WeatherAmount", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }
        Material Solid(string n, Color c, float gloss, float spec, float rim, Texture tex = null)
        {
            var m = CelMaterial(n, c, gloss: gloss, spec: spec, rim: rim, texture: tex, shade: GroundShade);
            m.SetFloat("_WeatherAmount", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        _skyMats = new Dictionary<string, Material>
        {
            ["sky_glass_blue"] = Glass("Minato_Skyline_GlassBlue", new Color(0.40f, 0.66f, 0.96f),
                                       new Color(0.30f, 0.44f, 0.66f)),
            ["sky_glass_teal"] = Glass("Minato_Skyline_GlassTeal", new Color(0.36f, 0.76f, 0.80f),
                                       new Color(0.26f, 0.46f, 0.54f)),
            ["sky_glass_deep"] = Glass("Minato_Skyline_GlassDeep", new Color(0.26f, 0.44f, 0.62f),
                                       new Color(0.22f, 0.32f, 0.48f)),
            ["sky_frame"] = Solid("Minato_Skyline_Frame", new Color(0.88f, 0.90f, 0.92f), 0.30f, 0.20f, 0.25f),
            ["sky_concrete"] = Solid("Minato_Skyline_WhiteConcrete", new Color(0.93f, 0.92f, 0.89f), 0.14f, 0.08f, 0.25f),
            ["sky_metal"] = Solid("Minato_Skyline_BrushedMetal", new Color(0.72f, 0.75f, 0.79f), 0.50f, 0.45f, 0.40f),
            ["sky_steel"] = Solid("Minato_Skyline_PolishedSteel", new Color(0.84f, 0.87f, 0.90f), 0.75f, 0.85f, 0.55f),
            ["sky_roof"] = Solid("Minato_Skyline_Roof", new Color(0.58f, 0.60f, 0.62f), 0.12f, 0.06f, 0.20f),
            ["sky_coral"] = Solid("Minato_Skyline_Coral", new Color(0.95f, 0.46f, 0.34f), 0.22f, 0.14f, 0.30f),
            ["sky_ribbon"] = Solid("Minato_Skyline_Ribbon", Color.white, 0.30f, 0.22f, 0.25f, ribbon),
            ["sky_shop"] = Solid("Minato_Skyline_Shopfront", Color.white, 0.30f, 0.20f, 0.20f, shop),
            ["sky_train"] = Solid("Minato_Skyline_TrainWhite", new Color(0.96f, 0.96f, 0.96f), 0.45f, 0.35f, 0.35f),
            ["sky_livery"] = Solid("Minato_Skyline_TrainBlue", new Color(0.12f, 0.36f, 0.80f), 0.30f, 0.20f, 0.30f),
            ["sky_dark"] = Solid("Minato_Skyline_Dark", new Color(0.12f, 0.14f, 0.16f), 0.10f, 0.05f, 0.10f),
            ["sky_garden"] = MedianFoliageMaterial(),
        };
        // The ribbon/shop textures carry their own colour; keep their shaded side neutral.
        return _skyMats;
    }

    private static void SkyRetint(GameObject go)
    {
        if (_skyMats == null) SkyMaterials();   // StartPrecinct can run before the skyline pass
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = mr.sharedMaterials;
            var outM = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                Material pick = _skyMats["sky_concrete"];
                // Longest tokens first: "sky_glass_blue" must not be caught by a shorter token.
                foreach (var kv in _skyMats)
                    if (n.Contains(kv.Key)) { pick = kv.Value; break; }
                outM[i] = pick;
            }
            mr.sharedMaterials = outM;
        }
    }

    /// <summary>
    /// Buildings are always BIG on screen, so the generic Inst() ladder (tuned for 6 m trees:
    /// 0.060/0.022/0.008) would keep LOD0 on every tower out to several kilometres. Authored
    /// building LODs switch on their own screen-height thresholds instead.
    /// </summary>
    private static void SkyLod(GameObject go, float lod0, float lod1, float lod2)
    {
        var g = go.GetComponent<LODGroup>();
        if (g == null) return;
        var lods = g.GetLODs();
        float[] cut = { lod0, lod1, lod2, 0.0008f };
        for (int i = 0; i < lods.Length; i++)
            lods[i].screenRelativeTransitionHeight = i == lods.Length - 1 ? 0.0008f : cut[Mathf.Min(i, 3)];
        g.SetLODs(lods);
        g.RecalculateBounds();
    }

    private static int SkyTris(GameObject src, string suffix)
    {
        foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
            if (mf.name.EndsWith(suffix) && mf.sharedMesh != null)
                return (int)(mf.sharedMesh.GetIndexCount(0) / 3 +
                             (mf.sharedMesh.subMeshCount > 1 ? SkySubTris(mf.sharedMesh) : 0));
        return 0;
    }

    private static int SkySubTris(Mesh m)
    {
        int t = 0;
        for (int s = 1; s < m.subMeshCount; s++) t += (int)(m.GetIndexCount(s) / 3);
        return t;
    }

    // =================================================================== placement helpers

    /// <summary>Route frame at a metre: centreline point, forward (flat), landward unit vector.</summary>
    private static void SkyFrame(MinatoRoute route, float d, out int i, out Vector3 p, out Vector3 fwd,
                                 out Vector3 city)
    {
        d = Mathf.Clamp(d, 0f, route.Length - 1f);
        i = route.IndexAt(d);
        p = route.Position[i];
        int j = route.IndexAt(Mathf.Min(d + 12f, route.Length - 1f));
        int k = route.IndexAt(Mathf.Max(d - 12f, 0f));
        fwd = Vector3.ProjectOnPlane(route.Position[j] - route.Position[k], Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        city = route.SideFlat(i) * -SeaSideSign(route, i);
    }

    private static bool SkyOverlaps(List<SkyLot> lots, Vector3 c, float r, float gap)
    {
        foreach (var l in lots)
        {
            float dx = l.pos.x - c.x, dz = l.pos.z - c.z;
            float rr = l.Radius + r + gap;
            if (dx * dx + dz * dz < rr * rr) return true;
        }
        return false;
    }

    /// <summary>Base height for a footprint: the LOWEST plaza/terrain point under it, so no
    /// corner floats; returns false if any corner is in the sea or the lot is too steep.</summary>
    private static bool SkyBase(MinatoRoute route, Vector3 c, Quaternion rot, float hx, float hz,
                                System.Func<Vector3, float> surface, out float baseY, float maxStep = 7f)
    {
        baseY = 0f;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int k = 0; k < 5; k++)
        {
            var o = k == 4 ? Vector3.zero : new Vector3((k & 1) == 0 ? -hx : hx, 0f, (k & 2) == 0 ? -hz : hz);
            var q = c + rot * o;
            if (GroundAt(route, q.x, q.z) < SeaLevelY + 1.4f) return false;
            float y = surface(q);
            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
        }
        if (hi - lo > maxStep) return false;
        baseY = lo - 0.35f;
        return true;
    }

    private static SkyLot SkyPlace(SkyFamily fam, Transform parent, Vector3 pos, Quaternion rot,
                                   Vector3 scale, float d, float lateral, List<SkyLot> lots)
    {
        var go = Inst(fam.src, parent, Vector3.zero, Quaternion.identity, null);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        SkyRetint(go);
        if (fam.tier >= 2) SkyLod(go, 0.30f, 0.07f, 0.012f);
        else SkyLod(go, 0.16f, 0.045f, 0.010f);
        go.isStatic = true;
        var lot = new SkyLot { fam = fam, pos = pos, rot = rot, scale = scale, d = d, lateral = lateral };
        lots.Add(lot);
        return lot;
    }

    private static float SkyCoreness(float d, float lateral)
    {
        float a = (d - SkyCoreM) / 700f;
        float b = (lateral - SkyCoreLateralM) / 240f;
        return Mathf.Exp(-(a * a) - (b * b));
    }

    // =================================================================== 1. frontage

    private static int SkyBuildFrontage(MinatoRoute route, Dictionary<string, SkyFamily> fams,
                                        Dictionary<string, Material> mats, Transform parent,
                                        Transform plaza, List<SkyLot> lots, List<Vector3> clear,
                                        System.Func<Vector3, float> surface)
    {
        var rng = new System.Random(610927);
        var cycle = new[] { "PodiumA", "MidriseA", "PodiumB", "MidriseB", "PodiumA", "PodiumB", "MidriseA" };
        var planter = Model("Minato_Skyline_Planter");
        var sculpture = Model("Minato_Skyline_RingSculpture");
        var cafe = new[] { Model("Minato_City_CafeSet"), Model("Minato_City_CafeSetB") };
        var pine = Glb(ShiosaiGlb, "Shiosai_Pine");
        var pineB = Glb(ShiosaiGlb, "Shiosai_Pine_B");
        var needle = FoliageMaterial("Minato_Needle", new Color(0.78f, 0.88f, 0.76f),
                                     Tex(ShiosaiTex, "Shiosai_Needle_Albedo.png"), 0.22f);
        var bark = CelMaterial("Minato_Bark", Color.white, gloss: 0.10f, spec: 0.05f, rim: 0.40f,
                               texture: Tex(ShiosaiTex, "Shiosai_Bark_Albedo.png"));

        int placed = 0, openings = 0, planters = 0, cafes = 0, pines = 0, sculptures = 0;
        float d = SkyDistrictStartM + 6f;
        int ci = 0;
        while (d < SkyDistrictEndM - 10f)
        {
            // Every 2-3 buildings the frontage opens into a small plaza: sculpture / pines / cafes
            // in the gap, so the street wall reads as a walkable waterfront, not a barrier.
            bool opening = ci > 0 && (ci % 3 == 2 || rng.NextDouble() < 0.15);
            if (opening)
            {
                float w = 20f + (float)rng.NextDouble() * 8f;
                float mid = d + w * 0.5f;
                SkyFrame(route, mid, out _, out var p, out var fwd, out var city);
                if (sculpture != null && openings % 2 == 0)
                {
                    var q = p + city * 24.5f;
                    float y = surface(q);
                    var s = Inst(sculpture, plaza, Vector3.zero, Quaternion.identity, null);
                    s.transform.SetPositionAndRotation(new Vector3(q.x, y - 0.05f, q.z),
                        Quaternion.LookRotation(-city, Vector3.up) * Quaternion.Euler(0f, 25f, 0f));
                    s.transform.localScale = Vector3.one * 1.1f;
                    SkyRetint(s);
                    s.isStatic = true;
                    clear.Add(new Vector3(q.x, 4.2f, q.z));
                    sculptures++;
                }
                for (int k = 0; k < 2; k++)
                {
                    var src = k == 0 ? pine : pineB;
                    if (src == null) continue;
                    var q = p + city * (36f + k * 6f) + fwd * ((k == 0 ? -1f : 1f) * w * 0.28f);
                    var t = Inst(src, plaza, Vector3.zero, Quaternion.Euler(0f, rng.Next(360), 0f), needle, bark);
                    t.transform.position = new Vector3(q.x, surface(q) - 0.1f, q.z);
                    t.transform.localScale = Vector3.one * (1.0f + (float)rng.NextDouble() * 0.3f);
                    t.isStatic = true;
                    clear.Add(new Vector3(q.x, 2.2f, q.z));
                    pines++;
                }
                d += w;
                openings++;
                ci++;
                continue;
            }

            var fam = fams[cycle[ci % cycle.Length]];
            ci++;
            float half = fam.hx;
            float centreD = d + half;
            if (centreD + half > SkyDistrictEndM) break;
            SkyFrame(route, centreD, out _, out var pc, out var fw, out var cdir);
            float lat = SkyFrontageFaceM + fam.hz;
            var c = pc + cdir * lat;
            // Local +Z faces the road; local X then runs along the route.
            var rot = Quaternion.LookRotation(-cdir, Vector3.up);
            if (SkyBase(route, c, rot, fam.hx, fam.hz, surface, out float by))
            {
                SkyPlace(fam, parent, new Vector3(c.x, by, c.z), rot, Vector3.one, centreD, lat, lots);
                placed++;

                // Planter line and cafe tables in front of the shopfronts.
                for (float o = -half + 3f; o < half - 2f; o += 8.5f)
                {
                    var q = pc + cdir * (SkyFrontageFaceM - 2.6f) + fw * o;
                    if (planter != null)
                    {
                        var pl = Inst(planter, plaza, Vector3.zero, Quaternion.identity, null);
                        pl.transform.SetPositionAndRotation(new Vector3(q.x, surface(q) - 0.02f, q.z), rot);
                        SkyRetint(pl);
                        pl.isStatic = true;
                        clear.Add(new Vector3(q.x, 2.3f, q.z));
                        planters++;
                    }
                    if (fam.tier == 0 && cafe[0] != null && rng.NextDouble() < 0.8)
                    {
                        var cq = pc + cdir * (SkyFrontageFaceM - 6.0f) + fw * (o + 4.2f);
                        var cs = Inst(cafe[rng.Next(cafe.Length)], plaza, Vector3.zero,
                                      Quaternion.Euler(0f, rng.Next(360), 0f), null);
                        cs.transform.position = new Vector3(cq.x, surface(cq) - 0.03f, cq.z);
                        RetintMarket(cs);
                        cs.isStatic = true;
                        clear.Add(new Vector3(cq.x, 2.3f, cq.z));
                        cafes++;
                    }
                }
            }
            d += half * 2f + 5f + (float)rng.NextDouble() * 5f;
        }

        // A pine row along the plaza edge (reference: waterfront pines framing the promenade).
        for (float pd = SkyDistrictStartM + 18f; pd < SkyDistrictEndM - 10f; pd += 30f + (float)rng.NextDouble() * 8f)
        {
            if (pine == null) break;
            SkyFrame(route, pd, out _, out var p, out _, out var city);
            var q = p + city * (20.5f + (float)rng.NextDouble() * 2f);
            var t = Inst(rng.NextDouble() < 0.5 || pineB == null ? pine : pineB, plaza, Vector3.zero,
                         Quaternion.Euler(0f, rng.Next(360), 0f), needle, bark);
            t.transform.position = new Vector3(q.x, surface(q) - 0.1f, q.z);
            t.transform.localScale = Vector3.one * (0.95f + (float)rng.NextDouble() * 0.3f);
            t.isStatic = true;
            clear.Add(new Vector3(q.x, 2.4f, q.z));
            pines++;
        }

        Debug.Log($"[minato-skyline] frontage {placed} blocks, {openings} plaza openings, " +
                  $"{sculptures} ring sculptures, {planters} planters, {cafes} cafe sets, {pines} pines");
        return placed;
    }

    // =================================================================== 2. backdrop rows

    private static int SkyBuildBackdrop(MinatoRoute route, Dictionary<string, SkyFamily> fams,
                                        Dictionary<string, Material> mats, Transform parent,
                                        List<SkyLot> lots, List<Vector3> guide)
    {
        var rng = new System.Random(48221);
        // (lateral centre, start m, end m, fill probability, tier)
        var rows = new (float lat, float a, float b, float fill, int tier)[]
        {
            (86f,  SkyDistrictStartM, SkyDistrictEndM, 0.95f, 1),
            (140f, SkyDistrictStartM - 60f, SkyDistrictEndM + 60f, 0.92f, 2),
            // Zones 1/Port Gate: the harbor workstream owns the near landward band (warehouses,
            // logistics yards), so the city only starts ~170 m back there.
            (170f, SkyBackdropStartM + 150f, SkyDistrictStartM - 60f, 0.45f, 1),
            (125f, SkyDistrictEndM + 60f, SkyBackdropEndM - 200f, 0.45f, 1),
            (200f, SkyBackdropStartM, SkyBackdropEndM, 0.88f, 2),
            (265f, SkyBackdropStartM, SkyBackdropEndM, 0.90f, 3),
            (335f, SkyBackdropStartM + 50f, SkyBackdropEndM - 100f, 0.85f, 3),
            (410f, SkyBackdropStartM + 150f, SkyBackdropEndM - 200f, 0.80f, 3),
            (490f, SkyBackdropStartM + 350f, SkyBackdropEndM - 450f, 0.60f, 3),
        };
        string[] tier1 = { "MidriseA", "MidriseB", "TowerStepped", "PodiumB", "MidriseB" };
        string[] tier2 = { "TowerStepped", "TowerRoundB", "TowerSlimA", "MidriseB", "TowerRoundA" };
        string[] tier3Core = { "TowerSlimB", "TowerRoundA", "TowerSlimA", "TowerSlimB", "TowerRoundB" };
        string[] tier3 = { "TowerSlimA", "TowerRoundB", "TowerRoundA", "TowerStepped", "TowerSlimB", "MidriseB" };

        int placed = 0;
        string last = "";
        foreach (var row in rows)
        {
            float d = row.a + (float)rng.NextDouble() * 20f;
            while (d < row.b)
            {
                float lat = row.lat + ((float)rng.NextDouble() - 0.5f) * 22f;
                float core = SkyCoreness(d, lat);
                string[] pool = row.tier == 1 ? tier1 : row.tier == 2 ? tier2
                              : (core > 0.45f ? tier3Core : tier3);
                string pick = pool[rng.Next(pool.Length)];
                if (pick == last) pick = pool[rng.Next(pool.Length)];
                var fam = fams[pick];

                // Height grading: the skyline peaks over the district core and falls away to both
                // ends, so the city reads as ONE coherent mass rather than evenly sprinkled towers.
                float sy = row.tier >= 3 ? Mathf.Lerp(0.80f, 1.20f, core) : Mathf.Lerp(0.85f, 1.1f, core);
                sy *= 0.92f + (float)rng.NextDouble() * 0.16f;
                float sxz = 0.92f + (float)rng.NextDouble() * 0.16f;
                var scale = new Vector3(sxz, Mathf.Clamp(sy, 0.72f, 1.25f), sxz);
                float half = fam.hx * sxz;
                float centreD = d + half;
                d += half * 2f + 9f + (float)rng.NextDouble() * 14f;
                if (rng.NextDouble() > row.fill) continue;

                SkyFrame(route, centreD, out _, out var p, out _, out var city);
                var c = p + city * lat;
                if (SkyOverlaps(lots, c, fam.hx * sxz > fam.hz * sxz ? fam.hx * sxz : fam.hz * sxz, 8f)) continue;
                if (SkyNearGuide(guide, c, Mathf.Max(fam.hx, fam.hz) * sxz + 7f)) continue;
                // Never let the backdrop creep into the ride corridor on a bend.
                if (NearestLand(route, c.x, c.z, out _) < SkyCorridorClearM + Mathf.Max(fam.hx, fam.hz) * sxz) continue;
                var rot = Quaternion.LookRotation(-city, Vector3.up) *
                          Quaternion.Euler(0f, fam.round ? rng.Next(360) : ((float)rng.NextDouble() - 0.5f) * 14f, 0f);
                if (!SkyBase(route, c, rot, fam.hx * sxz, fam.hz * sxz, q =>
                        Mathf.Max(SeaLevelY + 0.65f, GroundAt(route, q.x, q.z) + 0.38f), out float by, 9f)) continue;
                SkyPlace(fam, parent, new Vector3(c.x, by, c.z), rot, scale, centreD, lat, lots);
                last = pick;
                placed++;
            }
        }
        return placed;
    }

    // =================================================================== 3. far-shore cluster

    /// <summary>
    /// A compact second skyline on the far shore beside the bridge landfall. From the sea-level
    /// causeway and the bridge the rider faces AWAY from the main city (the route heads ENE), so
    /// without this the "skyline across the water" in the causeway/bridge targets would never be
    /// in frame. Placed landward of the landfall, well clear of the route.
    /// </summary>
    private static int SkyBuildFarCluster(MinatoRoute route, Dictionary<string, SkyFamily> fams,
                                          Dictionary<string, Material> mats, Transform parent,
                                          List<SkyLot> lots)
    {
        var far = new GameObject("Far Shore Skyline").transform; far.SetParent(parent, false);
        SkyFrame(route, SkyFarAnchorM, out _, out var p, out var fwd, out var city);
        var centre = p + city * SkyFarLateralM;
        var rng = new System.Random(77031);
        string[] pool = { "TowerSlimB", "TowerSlimA", "TowerRoundA", "TowerRoundB", "TowerSlimA", "TowerStepped", "MidriseB" };
        int placed = 0;
        for (int attempt = 0; attempt < 140 && placed < 26; attempt++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt((float)rng.NextDouble()) * SkyFarRadiusM;
            var c = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            var fam = fams[pool[rng.Next(pool.Length)]];
            float core = 1f - r / SkyFarRadiusM;
            float sy = Mathf.Lerp(0.75f, 1.2f, core) * (0.9f + (float)rng.NextDouble() * 0.2f);
            var scale = new Vector3(1f, Mathf.Clamp(sy, 0.7f, 1.25f), 1f);
            if (SkyOverlaps(lots, c, Mathf.Max(fam.hx, fam.hz), 10f)) continue;
            if (NearestLand(route, c.x, c.z, out _) < 240f) continue;
            var rot = Quaternion.LookRotation(-city, Vector3.up) * Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
            if (!SkyBase(route, c, rot, fam.hx, fam.hz, q =>
                    Mathf.Max(SeaLevelY + 0.65f, GroundAt(route, q.x, q.z) + 0.2f), out float by, 12f)) continue;
            SkyPlace(fam, far, new Vector3(c.x, by, c.z), rot, scale, SkyFarAnchorM, SkyFarLateralM, lots);
            placed++;
        }
        Debug.Log($"[minato-skyline] far-shore cluster {placed} towers around {centre} " +
                  $"(route {SkyFarAnchorM:N0} m, {SkyFarLateralM:N0} m landward)");
        return placed;
    }

    // =================================================================== 4. elevated transit

    private static float SkyGuideLateral(float d)
    {
        float a = Mathf.InverseLerp(SkyDistrictStartM, SkyDistrictStartM - SkyGuideCurveM, d);
        float b = Mathf.InverseLerp(SkyDistrictEndM, SkyDistrictEndM + SkyGuideCurveM, d);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Max(a, b));
        return Mathf.Lerp(SkyGuideLateralM, SkyGuideInlandM, t);
    }

    /// <summary>Beam-top polyline of the guideway (world), one point per span.</summary>
    private static List<Vector3> SkyGuidewayPath(MinatoRoute route, System.Func<Vector3, float> surface)
    {
        var pts = new List<Vector3>();
        var raw = new List<float>();
        for (float d = SkyDistrictStartM - SkyGuideCurveM; d <= SkyDistrictEndM + SkyGuideCurveM + 0.1f;
             d += SkyGuideSpanM * 0.92f)
        {
            SkyFrame(route, d, out _, out var p, out _, out var city);
            var q = p + city * SkyGuideLateral(d);
            pts.Add(q);
            raw.Add(surface(q));
        }
        // Smooth the deck profile: a guideway does not follow every plaza undulation.
        for (int k = 0; k < pts.Count; k++)
        {
            float sum = 0f; int n = 0;
            for (int o = -3; o <= 3; o++)
            {
                int m = Mathf.Clamp(k + o, 0, pts.Count - 1);
                sum += raw[m]; n++;
            }
            var q = pts[k];
            q.y = sum / n + SkyGuideDeckM;
            pts[k] = q;
        }
        return pts;
    }

    private static bool SkyNearGuide(List<Vector3> guide, Vector3 c, float r)
    {
        for (int k = 0; k + 1 < guide.Count; k++)
        {
            Vector2 a = new Vector2(guide[k].x, guide[k].z), b = new Vector2(guide[k + 1].x, guide[k + 1].z);
            Vector2 p = new Vector2(c.x, c.z);
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            if ((a + ab * t - p).sqrMagnitude < r * r) return true;
        }
        return false;
    }

    private static int SkyBuildTransit(MinatoRoute route, Dictionary<string, Material> mats,
                                       Transform parent, List<Vector3> guide,
                                       Dictionary<string, SkyFamily> fams, List<SkyLot> lots,
                                       List<Vector3> clear, System.Func<Vector3, float> surface)
    {
        var beam = Model("Minato_Skyline_GuidewayBeam");
        var pier = Model("Minato_Skyline_GuidewayPier");
        var head = Model("Minato_Skyline_MonorailHead");
        var car = Model("Minato_Skyline_MonorailCar");
        if (beam == null || pier == null || guide.Count < 3) return 0;
        var beams = new GameObject("Guideway").transform; beams.SetParent(parent, false);

        int piers = 0;
        for (int k = 0; k + 1 < guide.Count; k++)
        {
            var a = guide[k];
            var b = guide[k + 1];
            var dir = b - a;
            var go = Inst(beam, beams, Vector3.zero, Quaternion.identity, null);
            go.transform.SetPositionAndRotation(a, Quaternion.LookRotation(dir.normalized, Vector3.up));
            // +0.25 m overlap so consecutive spans never show a hairline gap on the bends.
            go.transform.localScale = new Vector3(1f, 1f, (dir.magnitude + 0.25f) / SkyGuideSpanM);
            SkyRetint(go);
            go.isStatic = true;

            // Terminal ends sit inside the terminal blocks; no pier needed there.
            if (k == 0) continue;
            float g = surface(a);
            var pr = Inst(pier, beams, Vector3.zero, Quaternion.identity, null);
            pr.transform.SetPositionAndRotation(new Vector3(a.x, g - 0.2f, a.z),
                Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, Vector3.up).normalized, Vector3.up));
            pr.transform.localScale = new Vector3(1f, (a.y - SkyBeamDepthM - g + 0.2f) / SkyPierNominalM, 1f);
            SkyRetint(pr);
            pr.isStatic = true;
            clear.Add(new Vector3(a.x, 2.6f, a.z));
            piers++;
        }

        // Terminal blocks swallow both guideway ends (the line "continues" into the city).
        foreach (int end in new[] { 0, guide.Count - 1 })
        {
            var fam = fams["MidriseA"];
            var a = guide[end];
            var inward = end == 0 ? guide[0] - guide[1] : guide[end] - guide[end - 1];
            inward.y = 0f;
            inward.Normalize();
            var c = a + inward * (fam.hx - 6f);
            // Local X of the block runs along the guideway so the beam enters its end wall.
            var rot = Quaternion.LookRotation(Vector3.Cross(inward, Vector3.up), Vector3.up);
            if (SkyBase(route, c, rot, fam.hx, fam.hz, surface, out float by, 9f))
            {
                c.y = by;
                SkyPlace(fam, parent, c, rot, Vector3.one, 0f, 0f, lots);
            }
        }

        // The train: lead car + middle car + lead car reversed, shuttling on the beam top.
        if (head != null && car != null)
        {
            var trainGo = new GameObject("Monorail Train");
            trainGo.transform.SetParent(parent, false);
            var cars = new Transform[3];
            var srcs = new[] { head, car, head };
            for (int c = 0; c < 3; c++)
            {
                var cg = Inst(srcs[c], trainGo.transform, Vector3.zero, Quaternion.identity, null);
                SkyRetint(cg);
                cg.isStatic = false;
                cars[c] = cg.transform;
            }
            var train = trainGo.AddComponent<MinatoTransitTrain>();
            train.path = guide.ToArray();
            train.cars = cars;
            train.carOffsets = new[] { 0f, 13.9f, 27.8f };
            train.carYaw = new[] { 0f, 0f, 180f };
            train.speed = 11f;
            train.dwellSeconds = 6f;
            // Start with the consist over the district entrance, heading down-route, so the first
            // gameplay frames of the zone show the train above the plaza.
            float s = 0f, best = float.MaxValue, acc = 0f;
            SkyFrame(route, SkyDistrictStartM + 140f, out _, out var target, out _, out _);
            for (int k = 1; k < guide.Count; k++)
            {
                acc += Vector3.Distance(guide[k - 1], guide[k]);
                float dd = (new Vector2(guide[k].x - target.x, guide[k].z - target.z)).sqrMagnitude;
                if (dd < best) { best = dd; s = acc; }
            }
            train.startDistance = s;
            train.SetHead(s);
        }
        return piers;
    }

    // =================================================================== 5. skybridges

    private static float SkySurfaceDistance(SkyLot l, Vector3 dirWorld)
    {
        if (l.fam.round) return l.Radius * 0.95f;
        var local = Quaternion.Inverse(l.rot) * dirWorld;
        float tx = Mathf.Abs(local.x) > 1e-4f ? l.HalfX * 0.86f / Mathf.Abs(local.x) : float.MaxValue;
        float tz = Mathf.Abs(local.z) > 1e-4f ? l.HalfZ * 0.86f / Mathf.Abs(local.z) : float.MaxValue;
        return Mathf.Min(tx, tz);
    }

    private static int SkyBuildSkybridges(Dictionary<string, Material> mats, Transform parent,
                                          List<SkyLot> lots)
    {
        var src = Model("Minato_Skyline_Skybridge");
        if (src == null) return 0;
        var root = new GameObject("Skybridges").transform; root.SetParent(parent, false);
        var cands = new List<(SkyLot a, SkyLot b, float gap, float score)>();
        for (int i = 0; i < lots.Count; i++)
            for (int j = i + 1; j < lots.Count; j++)
            {
                var a = lots[i]; var b = lots[j];
                if (a.fam.tier < 2 || b.fam.tier < 2) continue;
                if (a.lateral > 300f || b.lateral > 300f || a.lateral < 60f || b.lateral < 60f) continue;
                var dv = b.pos - a.pos; dv.y = 0f;
                float dist = dv.magnitude;
                if (dist < 1f) continue;
                var dir = dv / dist;
                float gap = dist - SkySurfaceDistance(a, dir) - SkySurfaceDistance(b, -dir);
                if (gap < 7f || gap > 34f) continue;
                float mid = (a.d + b.d) * 0.5f;
                float score = gap + Mathf.Abs(mid - (SkyDistrictStartM + SkyDistrictEndM) * 0.5f) * 0.05f
                              + (a.lateral + b.lateral) * 0.03f;
                cands.Add((a, b, gap, score));
            }
        cands.Sort((x, y) => x.score.CompareTo(y.score));
        var used = new HashSet<SkyLot>();
        int placed = 0;
        foreach (var cnd in cands)
        {
            if (placed >= 6) break;
            if (used.Contains(cnd.a) || used.Contains(cnd.b)) continue;
            var dv = cnd.b.pos - cnd.a.pos; dv.y = 0f;
            var dir = dv.normalized;
            float minH = Mathf.Min(cnd.a.Height, cnd.b.Height);
            float baseY = Mathf.Max(cnd.a.pos.y, cnd.b.pos.y);
            float ta = SkySurfaceDistance(cnd.a, dir);
            float len = dv.magnitude - ta - SkySurfaceDistance(cnd.b, -dir);
            foreach (float frac in new[] { 0.36f, 0.58f })
            {
                if (minH * frac < 20f) continue;
                var start = cnd.a.pos + dir * (ta - 0.8f);
                start.y = baseY + minH * frac;
                var go = Inst(src, root, Vector3.zero, Quaternion.identity, null);
                go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir, Vector3.up));
                go.transform.localScale = new Vector3(1f, 1f, (len + 1.6f) / 10f);
                SkyRetint(go);
                go.isStatic = true;
                if (frac > 0.5f && placed % 2 == 1) break;
            }
            used.Add(cnd.a); used.Add(cnd.b);
            placed++;
        }
        return placed;
    }

    // =================================================================== legacy parity

    /// <summary>
    /// BuildPortCity (skipped once this workstream replaces it) also planted the SEA-SIDE lamp
    /// row that the boulevard banners hang from. Those lamps are streetscape, not skyline, so the
    /// row is recreated here - same pitch, same offset, same banner mount - and the boulevard
    /// keeps its lamp + banner rhythm on both sides. (Lead: see the report; this block can move
    /// back into MinatoCoastEnvironment unconditionally.)
    /// </summary>
    private static int SkyLegacyLampParity(MinatoRoute route, Transform parent)
    {
        var lamp = Model("Minato_Bridge_Lamp");
        if (lamp == null) return 0;
        var bannerA = Model("Minato_City_BannerFlag");
        var bannerB = Model("Minato_City_BannerFlagB");
        var root = new GameObject("Sea-side Lamps").transform; root.SetParent(parent, false);
        var steel = SteelMaterial();
        int n = 0;
        for (float d = 40f; d < 2250f; d += 48f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + 16f, route.Length - 1f));
            var fwd = route.Position[j] - p;
            if (fwd.sqrMagnitude < 1e-4f) continue;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            var pos = p + side * -(RoadHalfWidth + ShoulderW + 0.30f);
            var l = Inst(lamp, root, Vector3.zero, rot, steel);
            l.transform.SetPositionAndRotation(pos, rot);
            l.isStatic = true;
            if (bannerA != null && d >= BoulevardStartM - 40f && d <= BoulevardEndM)
            {
                var b = Inst(n % 3 == 2 && bannerB != null ? bannerB : bannerA, root, Vector3.zero,
                             Quaternion.identity, null);
                b.transform.SetPositionAndRotation(pos, rot);   // lateral < 0 -> no 180 spin
                RetintBoulevard(b);
                b.isStatic = true;
            }
            n++;
        }
        return n;
    }

    /// <summary>
    /// The market / crowd passes ran before this one and filled the city-side plaza out to
    /// 140 m. Anything of theirs that now sits inside a building footprint, a guideway pier or a
    /// plaza piece is removed (exact-name groups only, never a Contains match).
    /// </summary>
    private static int SkyClearUnder(Transform chapter, List<Vector3> discs)
    {
        int removed = 0;
        foreach (var groupName in new[] { "Market District", "City Life" })
        {
            var g = FirstNamed(chapter, groupName);
            if (g == null) continue;
            var victims = new List<GameObject>();
            foreach (Transform sub in g)
                foreach (Transform item in sub)
                {
                    var q = item.position;
                    foreach (var c in discs)
                    {
                        float dx = q.x - c.x, dz = q.z - c.z;
                        if (dx * dx + dz * dz < c.y * c.y) { victims.Add(item.gameObject); break; }
                    }
                }
            foreach (var v in victims) { Object.DestroyImmediate(v); removed++; }
        }
        return removed;
    }
}
