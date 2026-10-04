using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CityRoute = MapleCityEnvironment.CityRoute;

/// <summary>
/// MAPLE CITY SKYLINE - board task C7 (Copilot, 2026-09-25; phase 2a by claude-city).
///
/// The first pass placed Minato's extruded glass slabs, so the downtown read as one slab
/// repeated. This pass places a purpose-built kit of distinctive towers
/// (tools/blender/build_maple_city_towers.py -> Assets/Environment/MapleCity/Skyline/Meshes/
/// MapleTower_*.glb): stepped setbacks, a chamfered taper with a glass crown, twin towers with a
/// skybridge, an art-deco stone tower with a gold ziggurat crown, a cylinder, a rounded slab
/// with a sloped roof, a podium tower with a lobby canopy and a stone mid-rise. Minato's small
/// mid-rises are mixed in only for LOW fringe lots.
///
/// Layout:
///  * Lots sit on a STREET GRID aligned with the loop's long west avenue; every tower yaw is
///    snapped to that grid (0/90/180/270).
///  * The tallest towers cluster in a DOWNTOWN CORE inside the loop, pulled toward the Ginkgo
///    Boulevard checkpoint (arc 0.74) and well away from Maple Row; heights fall off with
///    distance from the core, and the ring outside the loop is lower still - a believable
///    skyline silhouette seen from anywhere on the lap.
///  * Hard exclusions: footprint edge >= MinClearOfRouteM from the centreline everywhere,
///    >= RowClearM from Maple Row (route metres 430-720), >= OldTownClearM from the Old Town
///    stretch, and no two footprints overlap.
///
/// Materials are MapleRide/HDRP/CelLit (copied from Minato's skyline glass so every global
/// matches), shared per tint and GPU-instanced. Curtain-wall glass carries its mullion grid,
/// spandrels and a random lit-window pattern in the texture (baked per tint so lit windows stay
/// warm), high gloss + sky-tinted rim for reflectance, and a height tint that brightens toward
/// the sky.
///
/// Idempotent: the "Maple City Skyline" group is matched by exact name and rebuilt from scratch.
/// Run standalone: run_steps.ps1 "MapleCitySkyline.ApplyToScene|log|1"
/// Wide views:     run_steps.ps1 "MapleCitySkyline.CaptureSkyline|log|1"
///                 -> reference/copilot/maple_skyline/*.png
/// </summary>
public static class MapleCitySkyline
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string KitDir = "Assets/Environment/MapleCity/Skyline/Meshes";
    private const string TexDir = "Assets/Environment/MapleCity/Skyline/Textures";
    private const string MatDir = "Assets/Environment/MapleCity/Skyline/Materials";
    private const string MinatoModelDir = "Assets/Environment/MinatoCoast/Models";
    private const string MinatoMatDir = "Assets/Environment/MinatoCoast/Materials";
    public const string GroupName = "Maple City Skyline";

    // ---- PROVISIONAL tuning (illustrative - not final design numbers) ----------------------
    private const int Seed = 20260925;
    private const float BasinY = 30f;              // MapleCityEnvironment.BasinY
    private const float CorridorHalfWidthM = 68f;  // MapleCityEnvironment.CorridorHalfWidthM
    /// <summary>Footprint edge to route centreline; the street wall reaches ~42 m.</summary>
    private const float MinClearOfRouteM = 56f;
    private const float RowFromM = 430f, RowToM = 720f;  // Maple Row (copilot boutiques)
    private const float RowClearM = 150f;                // nothing within 150 m laterally
    private const float OldTownFromFrac = 0.262f, OldTownToFrac = 0.530f;
    private const float OldTownClearM = 110f;            // one block of low roofs first
    private const float OldTownHeightCapM = 150f;        // towers seen close over its roofs
    private const float OldTownCapRadiusM = 200f;
    /// <summary>
    /// VISTA TERMINATION: a lot that sits in the forward sight-cone of the road (VistaNearM..
    /// VistaFarM ahead, within VistaConeTan of the tangent) is where the rider actually SEES
    /// the skyline - down the avenue, framed by the street wall. Those lots are always built
    /// and always tall. PROVISIONAL numbers.
    /// </summary>
    private const float VistaNearM = 220f, VistaFarM = 1300f, VistaConeTan = 0.08f;
    private const float VistaMinH = 150f, VistaMaxH = 250f;
    private const float CoreFrac = 0.74f;                // Ginkgo Boulevard checkpoint
    private const float CorePull = 0.34f;                // loop centroid -> boulevard
    private const float CoreRadiusM = 760f;
    private const float HeightMaxM = 260f, HeightMinM = 60f;
    private const float LotPitchM = 94f, LotJitterM = 13f, LotGapM = 8f;
    private const float OuterBandM = 470f;               // towers outside the loop
    private const float FringeHeightM = 88f;             // below this Minato mid-rises may mix in
    private const float MinatoFringeShare = 0.22f;

    private sealed class Family
    {
        public string Name;
        public GameObject Src;
        public float R;          // footprint radius (m, unscaled)
        public float BaseH;      // authored top (m)
        public float MinS, MaxS; // allowed vertical scale
        public bool Minato;
        public int Tier;         // Minato LOD thresholds
        public bool AllStone;    // no curtain wall (art deco / stone mid-rise)
        public int Used;
    }

    private sealed class Palette
    {
        public Material[] Glass;     // Blue, Green, Bronze, Silver, Deep
        public Material[] Stone;     // Limestone, Granite
        public Material FrameLight, FrameDark, Accent, Roof;
    }

    private sealed class Lot { public Vector2 Pos; public float R; }

    private static readonly string[] GlassTints = { "Blue", "Green", "Bronze", "Silver", "Deep" };
    private static readonly float[] GlassWeights = { 0.28f, 0.16f, 0.18f, 0.22f, 0.16f };

    // =========================================================================== entry points

    [MenuItem("MapleRide/Environment/Build Maple City Skyline", priority = 26)]
    public static void ApplyToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = FindCityRoot();
        if (root == null)
        {
            Debug.LogError($"[maple-skyline] no '{MapleCityEnvironment.RootName}' in the scene - run Build Maple City first.");
            return;
        }
        Build(root.transform, CityRoute.Load());
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[maple-skyline] saved '{scene.path}'.");
    }

    public static void Build(Transform cityRoot, CityRoute route)
    {
        if (cityRoot == null || route == null || route.Count < 2) return;
        for (int k = cityRoot.childCount - 1; k >= 0; k--)
            if (cityRoot.GetChild(k).name == GroupName)
                UnityEngine.Object.DestroyImmediate(cityRoot.GetChild(k).gameObject);

        var kit = KitFamilies();
        if (kit.Count < 6)
        {
            Debug.LogError($"[maple-skyline] only {kit.Count} MapleTower kit meshes in {KitDir} - run " +
                           "blender -b -P tools/blender/build_maple_city_towers.py. Skipped.");
            return;
        }
        var minato = MinatoFringeFamilies();
        var pal = Palette_();
        var minatoMats = MinatoMaterials();
        if (pal == null) { Debug.LogError("[maple-skyline] could not build materials - skipped."); return; }

        var group = new GameObject(GroupName).transform;
        group.SetParent(cityRoot, false);

        // ---- plan-view helpers
        int n = route.Count;
        var poly = new Vector2[n];
        var centroid = Vector2.zero;
        for (int i = 0; i < n; i++) { poly[i] = XZ(route.Position[i]); centroid += poly[i]; }
        centroid /= n;
        var core = Vector2.Lerp(centroid, XZ(route.Position[route.IndexAt(CoreFrac * route.Length)]), CorePull);
        var rowPts = new List<Vector2>();
        var oldPts = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            if (route.Distance[i] >= RowFromM && route.Distance[i] <= RowToM) rowPts.Add(poly[i]);
            float f = route.Frac(i);
            if (f >= OldTownFromFrac && f <= OldTownToFrac) oldPts.Add(poly[i]);
        }

        // Street grid aligned with the long west avenue (Maple Row's straight).
        var t = route.Tangent[route.IndexAt((RowFromM + RowToM) * 0.5f)];
        float gridYaw = Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg;
        var ax = new Vector2(Mathf.Cos(-gridYaw * Mathf.Deg2Rad), Mathf.Sin(-gridYaw * Mathf.Deg2Rad));
        var az = new Vector2(-ax.y, ax.x);
        float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
        foreach (var p in poly)
        {
            float u = Vector2.Dot(p - centroid, ax), v = Vector2.Dot(p - centroid, az);
            uMin = Mathf.Min(uMin, u); uMax = Mathf.Max(uMax, u);
            vMin = Mathf.Min(vMin, v); vMax = Mathf.Max(vMax, v);
        }
        uMin -= OuterBandM; uMax += OuterBandM; vMin -= OuterBandM; vMax += OuterBandM;

        var rng = new System.Random(Seed);
        float R01() => (float)rng.NextDouble();
        var lots = new List<Lot>();
        var famCount = new Dictionary<string, int>();
        var tintCount = new Dictionary<string, int>();
        int placed = 0, rejRoute = 0, rejRow = 0, rejOld = 0, rejOverlap = 0, tris = 0;
        float hLo = float.MaxValue, hHi = 0f, minRowMargin = float.MaxValue, minRouteMargin = float.MaxValue;
        int inside = 0, outside = 0, vistaN = 0;

        for (float u = uMin; u <= uMax; u += LotPitchM)
        for (float v = vMin; v <= vMax; v += LotPitchM)
        {
            var c = centroid + ax * (u + (R01() - 0.5f) * 2f * LotJitterM)
                             + az * (v + (R01() - 0.5f) * 2f * LotJitterM);
            float rollOcc = R01(), rollH = R01(), rollBoost = R01(), rollFam = R01(), rollS = R01();
            int yawK = rng.Next(4);

            float dRoute = Nearest(poly, c, out int ni);
            bool isInside = PointInPolygon(poly, c);
            bool vista = InVista(route, c);
            if (!isInside && dRoute > OuterBandM && !vista) continue;

            // ---- height field: downtown core inside the loop, lower ring outside.
            float tc = Vector2.Distance(c, core) / CoreRadiusM;
            float H, pOcc;
            if (isInside)
            {
                // steep near the core (a peak, not a plateau), long tail toward the loop edge
                float fall = Mathf.Pow(Mathf.Clamp01(tc), 0.7f);
                H = Mathf.Lerp(HeightMaxM, HeightMinM * 1.4f, fall);
                pOcc = Mathf.Lerp(0.97f, 0.66f, Mathf.Clamp01(tc));
                if (rollBoost < 0.12f) H *= 1.22f;                        // the odd landmark
            }
            else
            {
                float tb = Mathf.Clamp01((dRoute - MinClearOfRouteM) / (OuterBandM - MinClearOfRouteM));
                H = Mathf.Lerp(170f, HeightMinM * 1.1f, Mathf.Pow(tb, 0.8f));
                pOcc = Mathf.Lerp(0.74f, 0.30f, tb);
            }
            H *= 0.74f + 0.40f * rollH;
            if (vista)
            {
                // vista lots are tall, but still fall off with core distance so the skyline keeps a peak
                float vmax = Mathf.Lerp(VistaMaxH, VistaMinH, Mathf.Clamp01(tc - 0.3f));
                H = Mathf.Max(H, Mathf.Lerp(VistaMinH * 0.8f, vmax, rollBoost));
                pOcc = Mathf.Max(pOcc, isInside ? 0.98f : 0.62f);
            }
            float dOld = oldPts.Count > 0 ? Nearest(oldPts, c) : float.MaxValue;
            if (dOld < OldTownCapRadiusM) H = Mathf.Min(H, OldTownHeightCapM);
            H = Mathf.Clamp(H, HeightMinM, HeightMaxM);
            if (rollOcc > pOcc) continue;

            // ---- family for this height
            Family fam = null;
            if (H < FringeHeightM && minato.Count > 0 && rollFam < MinatoFringeShare)
                fam = Pick(minato, H, rng);
            if (fam == null) fam = Pick(kit, H, rng);
            if (fam == null) continue;
            float sxz = 0.93f + 0.14f * rollS;
            float sy = Mathf.Clamp(H / fam.BaseH, fam.MinS, fam.MaxS);
            float r = fam.R * sxz;

            // ---- exclusions
            if (dRoute < MinClearOfRouteM + r) { rejRoute++; continue; }
            float dRow = rowPts.Count > 0 ? Nearest(rowPts, c) : float.MaxValue;
            if (dRow < RowClearM + r) { rejRow++; continue; }
            if (dOld < OldTownClearM + r) { rejOld++; continue; }
            if (Overlaps(lots, c, r, LotGapM)) { rejOverlap++; continue; }

            // ---- place
            fam.Used++;
            var rot = Quaternion.Euler(0f, gridYaw + 90f * yawK, 0f);
            float baseY = FootY(route, ni, c, r) - 1.0f;
            var pos = new Vector3(c.x, baseY, c.y);
            var scale = new Vector3(sxz, sy, sxz);
            GameObject go;
            string tint;
            if (fam.Minato)
            {
                go = PlaceMinato(fam, group, pos, rot, scale, minatoMats);
                tint = "Minato";
            }
            else
            {
                go = PlaceKit(fam, group, pos, rot, scale, pal, rng, out tint);
            }
            go.name = $"Tower_{placed:000}_{fam.Name}";
            lots.Add(new Lot { Pos = c, R = r });
            placed++;
            if (isInside) inside++; else outside++;
            if (vista) vistaN++;
            famCount[fam.Name] = famCount.TryGetValue(fam.Name, out int fc) ? fc + 1 : 1;
            tintCount[tint] = tintCount.TryGetValue(tint, out int tcn) ? tcn + 1 : 1;
            float top = fam.BaseH * sy;
            hLo = Mathf.Min(hLo, top); hHi = Mathf.Max(hHi, top);
            minRowMargin = Mathf.Min(minRowMargin, dRow - r);
            minRouteMargin = Mathf.Min(minRouteMargin, dRoute - r);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && !mf.name.Contains("_LOD1") && !mf.name.Contains("_LOD2") && !mf.name.Contains("_LOD3"))
                    tris += TriCount(mf.sharedMesh);
        }

        var fams = new List<string>();
        foreach (var kv in famCount) fams.Add($"{kv.Key}={kv.Value}");
        var tints = new List<string>();
        foreach (var kv in tintCount) tints.Add($"{kv.Key}={kv.Value}");
        Debug.Log($"[maple-skyline] placed {placed} towers ({inside} inside the loop, {outside} outside, {vistaN} vista terminators); " +
                  $"heights {hLo:0}-{hHi:0} m; ~{tris:N0} LOD0 tris; grid yaw {gridYaw:0.0} deg; core {core}.");
        Debug.Log($"[maple-skyline] families: {string.Join(", ", fams)}");
        Debug.Log($"[maple-skyline] tints: {string.Join(", ", tints)}");
        Debug.Log($"[maple-skyline] clearances: min footprint-edge to route {minRouteMargin:0.0} m " +
                  $"(need {MinClearOfRouteM}), to Maple Row {minRowMargin:0.0} m (need {RowClearM}); " +
                  $"rejected route {rejRoute}, row {rejRow}, old town {rejOld}, overlap {rejOverlap}.");
    }

    /// <summary>Wide skyline views for review (edit mode, Maple City region forced then restored).</summary>
    [MenuItem("MapleRide/Diagnostics/Capture Maple City Skyline", priority = 34)]
    public static void CaptureSkyline()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/copilot/maple_skyline"));
        Directory.CreateDirectory(dir);
        var shot = typeof(MapleCityDiagnostics).GetMethod("Shot", BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(string), typeof(string), typeof(Vector3), typeof(Vector3), typeof(float), typeof(bool) }, null);
        if (shot == null) { Debug.LogError("[maple-skyline] MapleCityDiagnostics.Shot not found."); return; }

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[maple-skyline] no RegionDirector in scene."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.MapleCity;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        try
        {
            var route = CityRoute.Load();
            var centroid = Vector3.zero;
            for (int i = 0; i < route.Count; i++) centroid += route.Position[i];
            centroid /= route.Count;
            var coreP = Vector3.Lerp(centroid, route.Position[route.IndexAt(CoreFrac * route.Length)], CorePull);
            var core = new Vector3(coreP.x, BasinY + 110f, coreP.z);
            void Shot(string name, Vector3 eye, Vector3 look, float fov) =>
                shot.Invoke(null, new object[] { dir, name, eye, look, fov, true });

            foreach (float d in new[] { 200f, 900f, 1500f, 2500f, 3400f, 4300f })
            {
                int i = route.IndexAt(d);
                var p = route.Position[i];
                // elevated view from the road across to the core
                Shot($"sky_{d:0000}m", p + Vector3.up * 55f - route.Tangent[i] * 30f, core, 60f);
            }
            // aerial oblique over the whole downtown from outside the loop (south-west)
            {
                var off = new Vector3(-900f, 520f, -1100f);
                Shot("sky_aerial_sw", coreP + off, core, 55f);
                Shot("sky_aerial_ne", coreP + new Vector3(1100f, 420f, 1200f), core, 55f);
            }
            // basin-level telephoto of the silhouette
            Shot("sky_silhouette_e", coreP + new Vector3(1800f, 60f, 200f), core + Vector3.up * 20f, 38f);
        }
        finally
        {
            regions.currentRegionId = previous;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
            Debug.Log($"[maple-skyline] capture done; restored region '{regions.currentRegionId}'.");
        }
    }

    // ================================================================================ placement

    private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

    private static float Nearest(Vector2[] pts, Vector2 c, out int idx)
    {
        float best = float.MaxValue; idx = 0;
        for (int k = 0; k < pts.Length; k++)
        {
            float d = (pts[k] - c).sqrMagnitude;
            if (d < best) { best = d; idx = k; }
        }
        return Mathf.Sqrt(best);
    }

    private static float Nearest(List<Vector2> pts, Vector2 c)
    {
        float best = float.MaxValue;
        foreach (var p in pts) best = Mathf.Min(best, (p - c).sqrMagnitude);
        return Mathf.Sqrt(best);
    }

    /// <summary>True if the lot terminates a street vista somewhere on the lap (see VistaNearM).</summary>
    private static bool InVista(CityRoute route, Vector2 c)
    {
        for (int i = 0; i < route.Count; i += 3)
        {
            var p = route.Position[i];
            var t = new Vector2(route.Tangent[i].x, route.Tangent[i].z);
            if (t.sqrMagnitude < 1e-6f) continue;
            t.Normalize();
            var d = c - new Vector2(p.x, p.z);
            float along = Vector2.Dot(d, t);
            if (along < VistaNearM || along > VistaFarM) continue;
            float lat = Mathf.Abs(d.x * t.y - d.y * t.x);
            if (lat < along * VistaConeTan) return true;
        }
        return false;
    }

    private static bool PointInPolygon(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y + 1e-6f) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    private static bool Overlaps(List<Lot> lots, Vector2 c, float r, float gap)
    {
        foreach (var l in lots)
        {
            float rr = l.R + r + gap;
            if ((l.Pos - c).sqrMagnitude < rr * rr) return true;
        }
        return false;
    }

    /// <summary>Family whose vertical-scale window reaches H; least-used of two random fits.</summary>
    private static Family Pick(List<Family> fams, float h, System.Random rng)
    {
        var fit = new List<Family>();
        foreach (var f in fams)
            if (h >= f.BaseH * f.MinS && h <= f.BaseH * f.MaxS) fit.Add(f);
        if (fit.Count == 0) return null;
        var a = fit[rng.Next(fit.Count)];
        var b = fit[rng.Next(fit.Count)];
        return a.Used <= b.Used ? a : b;
    }

    private static readonly MethodInfo GroundFn = typeof(MapleCityEnvironment).GetMethod(
        "GroundHeight", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>Lowest ground under a footprint: city terrace inside the corridor, basin beyond.</summary>
    private static float FootY(CityRoute route, int i, Vector2 c, float r)
    {
        var s = route.SideFlat(i);
        var p = route.Position[i];
        float lat = (c.x - p.x) * s.x + (c.y - p.z) * s.z;
        float lo = BasinY;
        foreach (float o in new[] { lat - r, lat, lat + r })
        {
            if (Mathf.Abs(o) >= CorridorHalfWidthM || GroundFn == null) continue;
            lo = Mathf.Min(lo, (float)GroundFn.Invoke(null, new object[] { route, i, o }));
        }
        return lo;
    }

    private static int TriCount(Mesh m)
    {
        int t = 0;
        for (int s = 0; s < m.subMeshCount; s++) t += (int)m.GetIndexCount(s) / 3;
        return t;
    }

    // ================================================================================ kit

    private static List<Family> KitFamilies()
    {
        var list = new List<Family>();
        void Add(string n, float baseH, float r, float minS, float maxS, bool stone = false)
        {
            string path = $"{KitDir}/MapleTower_{n}.glb";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            if (src == null) { Debug.LogWarning($"[maple-skyline] missing {path}"); return; }
            list.Add(new Family { Name = n, Src = src, BaseH = baseH, R = r, MinS = minS, MaxS = maxS, AllStone = stone });
        }
        // (authored top incl. spire/mast, footprint radius, vertical scale window)
        Add("SetbackA", 216f, 31f, 0.66f, 1.20f);
        Add("SetbackB", 262f, 31f, 0.64f, 1.00f);
        Add("Tapered", 252f, 32f, 0.64f, 1.03f);
        Add("Twin", 180f, 43f, 0.80f, 1.22f);
        Add("ArtDeco", 200f, 25f, 0.80f, 1.25f, stone: true);
        Add("Cylinder", 235f, 22f, 0.66f, 1.10f);
        Add("Rounded", 143f, 31f, 0.78f, 1.12f);
        Add("Podium", 124f, 44f, 0.75f, 1.12f);
        Add("Midrise", 63f, 31f, 0.95f, 1.50f);
        return list;
    }

    private static List<Family> MinatoFringeFamilies()
    {
        var list = new List<Family>();
        void Add(string n, float hx, float hz, float h, int tier)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{MinatoModelDir}/Minato_Skyline_{n}.fbx");
            if (src == null) return;
            list.Add(new Family { Name = "Minato" + n, Src = src, BaseH = h, R = Mathf.Sqrt(hx * hx + hz * hz),
                                  MinS = 0.9f, MaxS = 1.4f, Minato = true, Tier = tier });
        }
        Add("MidriseA", 21.3f, 11.8f, 35f, 1);
        Add("MidriseB", 15.2f, 13.2f, 46f, 1);
        Add("TowerStepped", 19.0f, 15.0f, 63f, 2);
        return list;
    }

    private static GameObject PlaceKit(Family fam, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale,
                                       Palette pal, System.Random rng, out string tint)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(fam.Src, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;

        // tint: weighted glass pick; frame follows the glass; warm limestone favoured for stone.
        float roll = (float)rng.NextDouble(), acc = 0f;
        int g = 0;
        for (; g < GlassWeights.Length - 1; g++) { acc += GlassWeights[g]; if (roll < acc) break; }
        var stone = pal.Stone[rng.NextDouble() < (fam.AllStone ? 0.70 : 0.55) ? 0 : 1];
        var frame = (GlassTints[g] == "Bronze" || GlassTints[g] == "Deep" || rng.NextDouble() < 0.25)
            ? pal.FrameDark : pal.FrameLight;
        tint = fam.AllStone ? (stone == pal.Stone[0] ? "Limestone" : "Granite") : GlassTints[g];

        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = mr.sharedMaterials;
            var outM = new Material[src.Length];
            for (int k = 0; k < src.Length; k++)
            {
                string n = src[k] != null ? src[k].name.ToLowerInvariant() : "";
                outM[k] = n.Contains("mct_glass") ? pal.Glass[g]
                        : n.Contains("mct_stone") ? stone
                        : n.Contains("mct_frame") ? frame
                        : n.Contains("mct_accent") ? pal.Accent
                        : pal.Roof;
            }
            mr.sharedMaterials = outM;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
        return go;
    }

    // ================================================================================ materials

    private static Texture2D Tex(string name)
    {
        string path = $"{TexDir}/{name}.png";
        if (AssetImporter.GetAtPath(path) == null)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            bool dirty = false;
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
            if (ti.anisoLevel != 8) { ti.anisoLevel = 8; dirty = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; dirty = true; }
            if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; dirty = true; }
            if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
            if (dirty) ti.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) Debug.LogWarning($"[maple-skyline] missing texture {path} - run build_maple_city_towers.py");
        return tex;
    }

    private static Material Mat(string name, Material template, Texture2D tex, Color col)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(template); AssetDatabase.CreateAsset(m, path); }
        else { m.shader = template.shader; m.CopyPropertiesFromMaterial(template); }
        m.name = name;
        m.SetTexture("_MainTex", tex);
        m.SetTextureScale("_MainTex", Vector2.one);
        m.SetTextureOffset("_MainTex", Vector2.zero);
        m.SetColor("_Color", col);
        void F(string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        F("_WeatherAmount", 0f); F("_DappleStrength", 0f); F("_TintVariation", 0f);
        F("_EdgeRimStrength", 0f); F("_Cull", 2f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Palette Palette_()
    {
        Directory.CreateDirectory(MatDir);
        var template = AssetDatabase.LoadAssetAtPath<Material>($"{MinatoMatDir}/Minato_Skyline_GlassBlue.mat");
        if (template == null) return null;
        var pal = new Palette { Glass = new Material[GlassTints.Length], Stone = new Material[2] };

        for (int g = 0; g < GlassTints.Length; g++)
        {
            var m = Mat($"MapleTower_Glass_{GlassTints[g]}", template, Tex($"MCT_Glass_{GlassTints[g]}"), Color.white);
            // PROVISIONAL glass look: glossy sun glint, sky-tinted fresnel rim, soft cel ramp,
            // brightening toward the sky with height (reflected sky is brighter than street).
            m.SetFloat("_Gloss", 0.86f); m.SetFloat("_SpecStrength", 0.95f);
            m.SetColor("_SpecTint", new Color(1f, 0.95f, 0.86f));
            m.SetColor("_RimColor", new Color(0.72f, 0.86f, 1f)); m.SetFloat("_RimStrength", 0.55f);
            m.SetFloat("_RimPower", 2.6f);
            m.SetColor("_ShadeColor", new Color(0.46f, 0.56f, 0.76f)); m.SetFloat("_ShadeStrength", 0.78f);
            m.SetFloat("_RampSmooth", 0.22f);
            m.SetColor("_HeightTint", new Color(1.10f, 1.15f, 1.22f));
            m.SetVector("_HeightRange", new Vector4(BasinY + 10f, BasinY + 280f, 0.35f, 0f));
            pal.Glass[g] = m;
        }
        string[] stones = { "Limestone", "Granite" };
        for (int s = 0; s < 2; s++)
        {
            var m = Mat($"MapleTower_Stone_{stones[s]}", template, Tex($"MCT_Stone_{stones[s]}"), Color.white);
            m.SetFloat("_Gloss", 0.30f); m.SetFloat("_SpecStrength", 0.18f);
            m.SetColor("_RimColor", new Color(1f, 0.86f, 0.68f)); m.SetFloat("_RimStrength", 0.25f);
            m.SetColor("_ShadeColor", new Color(0.52f, 0.52f, 0.66f)); m.SetFloat("_ShadeStrength", 0.72f);
            m.SetColor("_HeightTint", new Color(1.08f, 1.06f, 1.04f));
            m.SetVector("_HeightRange", new Vector4(BasinY + 10f, BasinY + 260f, 0.25f, 0f));
            pal.Stone[s] = m;
        }
        Material Flat(string n, Color c, float gloss, float spec)
        {
            var m = Mat(n, template, null, c);
            m.SetFloat("_Gloss", gloss); m.SetFloat("_SpecStrength", spec);
            m.SetColor("_RimColor", new Color(0.9f, 0.92f, 1f));
            m.SetFloat("_RimStrength", 0.3f);
            m.SetVector("_HeightRange", new Vector4(0f, 1f, 0f, 0f));
            return m;
        }
        pal.FrameLight = Flat("MapleTower_Frame_Silver", new Color(0.78f, 0.80f, 0.83f), 0.7f, 0.6f);
        pal.FrameDark = Flat("MapleTower_Frame_Dark", new Color(0.22f, 0.21f, 0.21f), 0.6f, 0.5f);
        pal.Accent = Flat("MapleTower_Accent_Gold", new Color(0.88f, 0.68f, 0.36f), 0.75f, 0.9f);
        pal.Roof = Flat("MapleTower_Roof", new Color(0.36f, 0.37f, 0.39f), 0.2f, 0.05f);
        return pal;
    }

    // ================================================================================ Minato fringe

    /// <summary>
    /// Material slot token -> material for the Minato fringe mid-rises. Minato's skyline
    /// materials are reused as-is; blue/deep glass swap to the warm copies saved under
    /// Assets/Environment/MapleCity/Skyline/Materials (Minato's assets are never modified).
    /// </summary>
    private static List<(string token, Material mat)> MinatoMaterials()
    {
        Directory.CreateDirectory(MatDir);
        Material Minato(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MinatoMatDir}/Minato_Skyline_{n}.mat");
        Material Variant(string srcName, string name, Color col, Color heightTint)
        {
            var src = Minato(srcName);
            if (src == null) return null;
            string path = $"{MatDir}/MapleSkyline_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(src);
            m.name = $"MapleSkyline_{name}";
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_HeightTint")) m.SetColor("_HeightTint", heightTint);
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(1f, 0.82f, 0.60f, 1f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
        var list = new List<(string, Material)>
        {
            ("sky_glass_blue", Variant("GlassBlue", "GlassBronze", new Color(0.70f, 0.56f, 0.42f), new Color(1.25f, 1.20f, 1.25f))),
            ("sky_glass_deep", Variant("GlassDeep", "GlassAmberDeep", new Color(0.46f, 0.36f, 0.30f), new Color(1.30f, 1.22f, 1.20f))),
            ("sky_glass_teal", Minato("GlassTeal")),
            ("sky_frame", Minato("Frame")),
            ("sky_concrete", Minato("WhiteConcrete")),
            ("sky_metal", Minato("BrushedMetal")),
            ("sky_steel", Minato("PolishedSteel")),
            ("sky_roof", Minato("Roof")),
            ("sky_coral", Minato("Coral")),
            ("sky_ribbon", Minato("Ribbon")),
            ("sky_shop", Minato("Shopfront")),
            ("sky_dark", Minato("Dark")),
        };
        list.RemoveAll(e => e.Item2 == null);
        return list;
    }

    private static GameObject PlaceMinato(Family fam, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale,
                                          List<(string token, Material mat)> mats)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(fam.Src, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;

        Material fallback = null;
        foreach (var e in mats) if (e.token == "sky_concrete") fallback = e.mat;
        var lods = new List<Renderer>[4];
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = mr.sharedMaterials;
            var outM = new Material[src.Length];
            for (int k = 0; k < src.Length; k++)
            {
                string n = src[k] != null ? src[k].name.ToLowerInvariant() : "";
                Material pick = fallback != null ? fallback : src[k];
                foreach (var e in mats)
                    if (n.Contains(e.token)) { pick = e.mat; break; }
                outM[k] = pick;
            }
            mr.sharedMaterials = outM;
            for (int l = 0; l < 4; l++)
                if (mr.name.EndsWith($"_LOD{l}")) (lods[l] ??= new List<Renderer>()).Add(mr);
        }
        float[] cuts = fam.Tier >= 2 ? new[] { 0.30f, 0.07f, 0.012f } : new[] { 0.16f, 0.045f, 0.010f };
        var list = new List<LOD>();
        for (int l = 0; l < 4; l++)
            if (lods[l] != null) list.Add(new LOD(l < 3 ? cuts[l] : 0.0008f, lods[l].ToArray()));
        if (list.Count > 1)
        {
            var group = go.GetComponent<LODGroup>();
            if (group == null) group = go.AddComponent<LODGroup>();
            list[list.Count - 1] = new LOD(0.0008f, list[list.Count - 1].renderers);
            group.SetLODs(list.ToArray());
            group.RecalculateBounds();
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
        return go;
    }

    private static GameObject FindCityRoot()
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == MapleCityEnvironment.RootName) return go;
        return null;
    }
}
