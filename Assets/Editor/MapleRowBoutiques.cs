using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using CityRoute = MapleCityEnvironment.CityRoute;

/// <summary>
/// MAPLE ROW (task C6): a short luxury boutique street on the Maple City crit, route metres
/// 450-700 (the straight canal-district avenue). VISUALS ONLY - the shop logic (alerts, UI,
/// purchases) lives in Assets/Ride/Shop and is keyed to the same metres, so nothing here is a
/// gameplay object and nothing has a collider.
///
/// Six fictional flagships, three per side:
///   right (inside the loop)  ARDENT Cycle Club, CARBONFORGE Superbike Frames, APEX Instruments
///   left  (across the canal) HALCYON, ALPENTEK, AEROLITE
/// The left side of this stretch is the canal (a named landmark), so the left-hand boutiques
/// face the road from a raised far-bank terrace, each reached by its own red-carpet footbridge.
///
/// HOW IT FITS THE CITY PASS. <see cref="MapleCityEnvironment"/> bakes the whole skyline, its
/// trees, lanterns and vending machines into a handful of combined meshes. This pass first
/// CARVES those meshes: every connected piece (a building box, a window quad, a tree) whose
/// centre lies beside the road inside 450-700 m is dropped from the index buffer. Then it lays
/// the boutiques into the cleared frontage. Carving by centre is idempotent, so the pass is safe
/// to re-run on a scene, and MapleCityEnvironment.Apply() calls <see cref="Build"/> at the end
/// so a city rebuild always ends with the street in place.
///
/// Textures: python tools/blender/build_maple_row_textures.py (PIL) ->
/// Assets/Environment/MapleCity/MapleRow/Textures. Meshes and materials are written next to them.
/// </summary>
[InitializeOnLoad]
public static partial class MapleRowBoutiques
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string Dir = "Assets/Environment/MapleCity/MapleRow";
    private const string TexDir = Dir + "/Textures";
    private const string MatDir = Dir + "/Materials";
    private const string MeshDir = Dir + "/Meshes";
    public const string GroupName = "Maple Row Boutiques";

    /// <summary>Route metres of the street. MUST match MapleRowShop.streetStartM / streetEndM.</summary>
    public const float StreetStartM = 450f, StreetEndM = 700f;

    // Cross-section facts copied from MapleCityEnvironment (canal district, frac 0.09-0.14).
    private const float PavementWidthM = 5.0f;
    private const float KerbHeightM = 0.14f;
    private const float CanalDistrictSetbackM = 1.5f;
    private const float CanalGapM = 1.0f, CopingWidthM = 0.45f, CopingHeightM = 0.5f;
    /// <summary>
    /// The city authors a 14 m brimming canal here (MapleCityEnvironment.ChannelWidthM). At that
    /// width the left-hand flagships stood ~24 m off the kerb and read as a distant far bank, so
    /// inside Maple Row the canal is NARROWED to <see cref="ChannelWidthM"/>: the raised far-bank
    /// terrace is built out over the outer part of the city's channel (covering its water and far
    /// coping) and its canal-side face becomes the new far wall.
    /// </summary>
    private const float CityChannelWidthM = 14f, ChannelWidthM = 5.0f;

    /// <summary>Nothing of ours may come closer to the centreline than half + this (the kerb stone).</summary>
    private const float KerbClearM = 0.3f;
    /// <summary>Far-bank terrace width in front of the left-hand boutiques.</summary>
    private const float TerraceM = 2.0f;
    /// <summary>
    /// The far-bank terrace is raised to the coping top so the footbridges land on it (+3 cm so it
    /// sits clear of the city's own far coping top, which it now covers, instead of z-fighting it).
    /// </summary>
    private const float TerraceLiftM = CopingHeightM + 0.03f;

    private enum Canopy { Fabric, Steel }
    private enum Display { Jerseys, Frames, Screens, Helmets }

    private sealed class Brand
    {
        public string Id;
        public int Side;                 // +1 right of travel, -1 left (canal side)
        public float Width;              // street frontage, metres
        public Color Trim, Awning, Valance, UpperTint;
        public Canopy Canopy;
        public Display Display;
        public bool Mural;               // facade texture maps once across the face instead of tiling
        public bool UpperWindows;        // punch a row of lit windows through the brand storey
        public float FacadeTint = 1f;    // albedo scale for bright cladding, so a sunlit face does not wash out
        public float FacadeTileM = 2f, FacadeGloss = 0.2f, FacadeMetal;
        public Color LedColor;           // steel canopy edge strip (black = none)
        public Color[] Kits;             // display colours (pairs for jerseys: body, band)
    }

    private static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString(h.StartsWith("#") ? h : "#" + h, out var c);
        return c;
    }

    // Colours match ShopCatalog so the windows sell what the shop UI sells.
    private static readonly Brand[] Brands =
    {
        new Brand { Id = "ARDENT", Side = +1, Width = 34f, Trim = Hex("#2B2D33"), Awning = Hex("#26282D"),
            Valance = Hex("#E86A8E"), UpperTint = new Color(0.90f, 0.87f, 0.82f), Canopy = Canopy.Fabric,
            Display = Display.Jerseys, FacadeTileM = 2f, FacadeGloss = 0.22f, UpperWindows = true,
            Kits = new[] { Hex("#2B2D33"), Hex("#E86A8E"), Hex("#F2EFE8"), Hex("#1E2A44"),
                           Hex("#E86A8E"), Hex("#2B2D33"), Hex("#1B1C20"), Hex("#E86A8E") } },
        new Brand { Id = "CARBONFORGE", Side = +1, Width = 34f, Trim = Hex("#141518"), Awning = Hex("#17181B"),
            Valance = Hex("#FF7A1A"), UpperTint = new Color(0.80f, 0.80f, 0.82f), Canopy = Canopy.Fabric,
            Display = Display.Frames, FacadeTileM = 1f, FacadeGloss = 0.62f, FacadeMetal = 0.1f, UpperWindows = true,
            Kits = new[] { Hex("#202124"), Hex("#34373D"), Hex("#FF7A1A") } },
        new Brand { Id = "APEX", Side = +1, Width = 30f, Trim = Hex("#D5DBE1"), Awning = Hex("#1A2633"),
            Valance = Hex("#1A2633"), UpperTint = new Color(0.86f, 0.84f, 0.80f), Canopy = Canopy.Steel,
            Display = Display.Screens, FacadeTileM = 2f, FacadeGloss = 0.35f, FacadeTint = 0.86f, UpperWindows = true,
            LedColor = new Color(0.25f, 1.25f, 1.6f), Kits = new[] { Hex("#1A2633") } },
        new Brand { Id = "HALCYON", Side = -1, Width = 34f, Trim = Hex("#16334A"), Awning = Hex("#FF6F59"),
            Valance = Hex("#1FA3A0"), UpperTint = new Color(0.92f, 0.88f, 0.80f), Canopy = Canopy.Fabric,
            Display = Display.Jerseys, Mural = true, FacadeGloss = 0.18f, FacadeTint = 0.8f,
            Kits = new[] { Hex("#1FA3A0"), Hex("#FF6F59"), Hex("#FF8A3D"), Hex("#3A1C5C"),
                           Hex("#16334A"), Hex("#1FA3A0"), Hex("#FFD23F"), Hex("#16334A") } },
        new Brand { Id = "ALPENTEK", Side = -1, Width = 32f, Trim = Hex("#6C7176"), Awning = Hex("#AEB6BE"),
            Valance = Hex("#AEB6BE"), UpperTint = new Color(0.82f, 0.83f, 0.85f), Canopy = Canopy.Steel,
            Display = Display.Jerseys, FacadeTileM = 2f, FacadeGloss = 0.3f, FacadeMetal = 0.15f, FacadeTint = 0.5f,
            LedColor = new Color(1.3f, 0.05f, 0.12f),
            Kits = new[] { Hex("#0F1012"), Hex("#C8CDD3"), Hex("#E9EEF2"), Hex("#D4002A"),
                           Hex("#0F1012"), Hex("#D4002A") } },
        new Brand { Id = "AEROLITE", Side = -1, Width = 30f, Trim = Hex("#1B1C20"), Awning = Hex("#C9302A"),
            Valance = Hex("#F4F4F4"), UpperTint = new Color(0.88f, 0.82f, 0.74f), Canopy = Canopy.Fabric,
            Display = Display.Helmets, Mural = true, FacadeGloss = 0.3f,
            Kits = new[] { Hex("#101114"), Hex("#F4F4F4"), Hex("#FF3B30"), Hex("#2F80ED") } },
    };

    // =================================================================== entry points

    static MapleRowBoutiques() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    /// <summary>
    /// Standalone: rebuild ONLY Maple Row inside the saved city (no full city rebuild), then save.
    /// Run: pwsh -NoProfile -File tools/unity/run_steps.ps1 "MapleRowBoutiques.ApplyToScene|copilot_c6_build.log|1"
    /// </summary>
    [MenuItem("MapleRide/Environment/Build Maple Row Boutiques", priority = 24)]
    public static void ApplyToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject root = null;
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == MapleCityEnvironment.RootName) { root = go; break; }
        if (root == null)
        {
            Debug.LogError($"[maple-row] no '{MapleCityEnvironment.RootName}' in the scene - run Build Maple City first.");
            return;
        }

        Build(root.transform, CityRoute.Load());
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[maple-row] saved '{scene.path}'.");
    }

    /// <summary>
    /// Carves the city's combined dressing meshes inside the street and (re)builds the boutiques
    /// under <paramref name="cityRoot"/>. Idempotent. Called from MapleCityEnvironment.Apply().
    /// </summary>
    public static void Build(Transform cityRoot, CityRoute route)
    {
        if (cityRoot == null || route == null || route.Count < 2) { Debug.LogError("[maple-row] no city root / route."); return; }
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(MeshDir);
        Mats.Clear();
        PrepareTextures();

        // Converge, never accumulate: exact-name match on the group, wherever a stray copy sits.
        for (int k = cityRoot.childCount - 1; k >= 0; k--)
            if (cityRoot.GetChild(k).name == GroupName) UnityEngine.Object.DestroyImmediate(cityRoot.GetChild(k).gameObject);
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == GroupName) UnityEngine.Object.DestroyImmediate(go);
        foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshDir }))
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

        var st = new Street(route);
        var clear = Carve(cityRoot, st);

        var group = new GameObject(GroupName).transform;
        group.SetParent(cityRoot, false);

        BuildProps();
        _galleryId = 0;
        int stores = 0;
        foreach (int side in new[] { +1, -1 })
        {
            float a = Mathf.Max(StreetStartM, clear[side].x + 1.0f);
            float b = Mathf.Min(StreetEndM, clear[side].y - 1.0f);
            stores += LayoutSide(group, st, side, a, b);
        }
        BuildPaving(group, st);
        BuildStreetDressing(group, st);

        int tris = 0, renderers = 0;
        foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            renderers++;
            tris += mf.sharedMesh.triangles.Length / 3;
        }
        CheckCorridor(group, st);

        // Drop material assets no longer referenced by this build (renamed or retired roles).
        var live = new HashSet<string>();
        foreach (var m in Mats.Values) if (m != null) live.Add(AssetDatabase.GetAssetPath(m));
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!live.Contains(p)) AssetDatabase.DeleteAsset(p);
        }
        Debug.Log($"[maple-row] built {stores} storefronts on {StreetStartM:0}-{StreetEndM:0} m: " +
                  $"{renderers} renderers, {tris} triangles (instances counted), {Mats.Count} materials.");
    }

    // =================================================================== route frame

    private static readonly MethodInfo HalfWidthFn = typeof(MapleCityEnvironment).GetMethod(
        "CarriagewayHalfWidth", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo RoadYFn = typeof(MapleCityEnvironment).GetMethod(
        "RoadY", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>
    /// Station/offset helpers on the published centreline. The carriageway width and road height
    /// come from MapleCityEnvironment itself (by reflection) so the street can never drift off the
    /// kerb line the city actually built; the fallbacks are that file's values at this stretch.
    /// </summary>
    private sealed class Street
    {
        public readonly CityRoute R;
        private readonly int _lo, _hi;

        public Street(CityRoute r)
        {
            R = r;
            if (HalfWidthFn == null || RoadYFn == null)
                Debug.LogWarning("[maple-row] MapleCityEnvironment.CarriagewayHalfWidth/RoadY not found - using fallback cross-section.");
            _lo = Mathf.Max(1, r.IndexAt(StreetStartM - 140f));
            _hi = Mathf.Min(r.Count - 1, r.IndexAt(StreetEndM + 140f));
        }

        private int Index(float d) => Mathf.Clamp(R.IndexAt(d), 1, R.Count - 1);

        private float Lerp01(float d, out int i)
        {
            i = Index(d);
            float d0 = R.Distance[i - 1], d1 = R.Distance[i];
            return d1 > d0 ? Mathf.Clamp01((d - d0) / (d1 - d0)) : 0f;
        }

        public Vector3 Pos(float d) { float f = Lerp01(d, out int i); return Vector3.Lerp(R.Position[i - 1], R.Position[i], f); }
        public Vector3 Tan(float d) { var t = R.Tangent[Index(d)]; t.y = 0f; return t.normalized; }
        public Vector3 Side(float d) => R.SideFlat(Index(d));
        public float Half(float d) => HalfWidth(R.Frac(Index(d)));

        /// <summary>World Y of the pavement top (kerb height) at station d.</summary>
        public float PavTop(float d)
        {
            float f = Lerp01(d, out int i);
            float h = Half(d);
            return Mathf.Lerp(RoadY(R, i - 1, h), RoadY(R, i, h), f) + KerbHeightM;
        }

        /// <summary>Frontage floor level for a side: pavement top, or the raised far-bank terrace.</summary>
        public float FloorY(float d, int side) => PavTop(d) + (side < 0 ? TerraceLiftM : 0f);

        /// <summary>Lateral offset of the storefront line on a side.</summary>
        public float Front(float d, int side) => side > 0
            ? Half(d) + PavementWidthM + CanalDistrictSetbackM
            : FarBank(d) + TerraceM;

        /// <summary>Outer edge of the canal's far coping (left side).</summary>
        public float FarBank(float d) => Half(d) + PavementWidthM + CanalGapM + ChannelWidthM + CopingWidthM;
        public float NearCoping(float d) => Half(d) + PavementWidthM + CanalGapM;
        /// <summary>Outer edge of the CITY's (unnarrowed) far coping, which the terrace must cover.</summary>
        public float CityFarBank(float d) => NearCoping(d) + CityChannelWidthM + CopingWidthM;

        public Vector3 At(float d, float offset, float y)
        {
            var p = Pos(d) + Side(d) * offset;
            p.y = y;
            return p;
        }

        /// <summary>Rotation whose +Z points away from the road on <paramref name="side"/> and +X along the street.</summary>
        public Quaternion Outward(float d, int side) => Quaternion.LookRotation(Side(d) * side, Vector3.up);

        /// <summary>Projects a world point into (station, signed offset) near the street. False when it is elsewhere on the loop.</summary>
        public bool Project(Vector3 p, out float s, out float o)
        {
            s = o = 0f;
            float best = float.MaxValue; int bi = -1;
            for (int i = _lo; i <= _hi; i++)
            {
                var q = R.Position[i];
                float dx = p.x - q.x, dz = p.z - q.z, dd = dx * dx + dz * dz;
                if (dd < best) { best = dd; bi = i; }
            }
            if (bi < 0 || best > 95f * 95f) return false;
            var t = R.Tangent[bi]; t.y = 0f; t.Normalize();
            var rel = p - R.Position[bi]; rel.y = 0f;
            s = R.Distance[bi] + Vector3.Dot(rel, t);
            o = Vector3.Dot(rel, R.SideFlat(bi));
            return true;
        }
    }

    private static float HalfWidth(float frac) =>
        HalfWidthFn != null ? (float)HalfWidthFn.Invoke(null, new object[] { frac }) : 4.6f;

    private static float RoadY(CityRoute r, int i, float offset) =>
        RoadYFn != null ? (float)RoadYFn.Invoke(null, new object[] { r, i, offset }) : r.Position[i].y + 0.07f;

    // =================================================================== carving

    private static readonly HashSet<string> CarveTargets = new HashSet<string>
    {
        "City Facades", "City Roofs", "City Windows", "City Rooftop Signs",
        "City Tree Trunks", "City Street Props", "City Lantern Glow",
    };

    /// <summary>
    /// Drops every connected piece of the city's combined dressing meshes whose centre sits
    /// beside the road inside the street. Returns, per side, the station interval still free of
    /// the KEPT buildings that straddle the street ends (x = last intrusion before the start,
    /// y = first intrusion after the end).
    /// </summary>
    private static Dictionary<int, Vector2> Carve(Transform cityRoot, Street st)
    {
        var clear = new Dictionary<int, Vector2>
        {
            { +1, new Vector2(float.MinValue, float.MaxValue) },
            { -1, new Vector2(float.MinValue, float.MaxValue) },
        };
        int meshes = 0, pieces = 0, tris = 0;
        foreach (var mf in cityRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            string n = mf.gameObject.name;
            if (!CarveTargets.Contains(n) && !n.StartsWith("City Canopy ")) continue;
            var mesh = mf.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            bool building = n == "City Facades" || n == "City Roofs";
            var (p, t) = CarveMesh(mesh, st, building, clear);
            if (p == 0) continue;
            meshes++; pieces += p; tris += t;
            EditorUtility.SetDirty(mesh);
            Debug.Log($"[maple-row] carved '{n}': {p} pieces, {t} triangles.");
        }
        Debug.Log($"[maple-row] carve total: {pieces} pieces / {tris} triangles from {meshes} city meshes " +
                  $"(re-runs carve 0 - already clear). Kept-building intrusions: right {Fmt(clear[+1])}, left {Fmt(clear[-1])}.");
        return clear;
    }

    private static string Fmt(Vector2 v) =>
        $"[{(v.x == float.MinValue ? "-" : v.x.ToString("0.0"))}, {(v.y == float.MaxValue ? "-" : v.y.ToString("0.0"))}]";

    private static (int pieces, int tris) CarveMesh(Mesh mesh, Street st, bool building, Dictionary<int, Vector2> clear)
    {
        var v = mesh.vertices;
        var parent = new int[v.Length];
        var weld = new Dictionary<Vector3, int>(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            if (weld.TryGetValue(v[i], out int j)) parent[i] = j;
            else { weld[v[i]] = i; parent[i] = i; }
        }
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }

        var subTris = new int[mesh.subMeshCount][];
        for (int sm = 0; sm < mesh.subMeshCount; sm++)
        {
            var tr = subTris[sm] = mesh.GetTriangles(sm);
            for (int k = 0; k < tr.Length; k += 3)
            {
                int a = Find(tr[k]), b = Find(tr[k + 1]), c = Find(tr[k + 2]);
                if (a != b) parent[b] = a;
                if (a != c) parent[Find(c)] = a;
            }
        }

        var min = new Dictionary<int, Vector3>();
        var max = new Dictionary<int, Vector3>();
        for (int sm = 0; sm < subTris.Length; sm++)
            foreach (int idx in subTris[sm])
            {
                int r = Find(idx);
                if (min.TryGetValue(r, out var lo)) { min[r] = Vector3.Min(lo, v[idx]); max[r] = Vector3.Max(max[r], v[idx]); }
                else { min[r] = v[idx]; max[r] = v[idx]; }
            }

        var drop = new HashSet<int>();
        foreach (var kv in min)
        {
            var lo = kv.Value; var hi = max[kv.Key];
            if (!st.Project((lo + hi) * 0.5f, out float s, out float o)) continue;
            float half = st.Half(Mathf.Clamp(s, StreetStartM, StreetEndM));
            float ao = Mathf.Abs(o);
            if (ao < half + 0.25f || ao > 85f) continue;
            if (s >= StreetStartM && s <= StreetEndM) { drop.Add(kv.Key); continue; }
            if (!building || ao < half + PavementWidthM) continue;

            // A kept building straddling a street end: record how far it reaches into the street.
            float s0 = float.MaxValue, s1 = float.MinValue;
            foreach (var corner in new[] { lo, hi, new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, lo.y, lo.z) })
                if (st.Project(corner, out float sc, out _)) { s0 = Mathf.Min(s0, sc); s1 = Mathf.Max(s1, sc); }
            int side = o > 0f ? +1 : -1;
            var c = clear[side];
            if (s < StreetStartM && s1 > StreetStartM) c.x = Mathf.Max(c.x, s1);
            if (s > StreetEndM && s0 < StreetEndM) c.y = Mathf.Min(c.y, s0);
            clear[side] = c;
        }
        if (drop.Count == 0) return (0, 0);

        int removed = 0;
        for (int sm = 0; sm < subTris.Length; sm++)
        {
            var src = subTris[sm];
            var keep = new List<int>(src.Length);
            for (int k = 0; k < src.Length; k += 3)
            {
                if (drop.Contains(Find(src[k]))) { removed++; continue; }
                keep.Add(src[k]); keep.Add(src[k + 1]); keep.Add(src[k + 2]);
            }
            mesh.SetTriangles(keep, sm, false);
        }
        mesh.RecalculateBounds();
        return (drop.Count, removed);
    }

    // =================================================================== layout

    private struct Door { public float Station; public int Side; public string Brand; }
    private static readonly List<Door> Doors = new List<Door>();
    private static readonly List<Vector2> Gaps = new List<Vector2>();    // (from, to) gallery stretches, side in sign of x
    private const float PlazaM = 7f;

    /// <summary>End plaza, then gallery / flagship / gallery ... / flagship / gallery, then end plaza.</summary>
    private static int LayoutSide(Transform group, Street st, int side, float a, float b)
    {
        if (side > 0) { Doors.Clear(); Gaps.Clear(); }
        var list = new List<Brand>();
        foreach (var br in Brands) if (br.Side == side) list.Add(br);

        float lo = a + PlazaM, hi = b - PlazaM, sum = 0f;
        foreach (var br in list) sum += br.Width;
        float gap = (hi - lo - sum) / (list.Count + 1), k = 1f;
        if (gap < 4f) { k = Mathf.Max(0.5f, (hi - lo - 4f * (list.Count + 1)) / sum); gap = 4f; }

        var parent = new GameObject(side > 0 ? "Right Side (inside loop)" : "Left Side (far canal bank)").transform;
        parent.SetParent(group, false);
        float d = lo;
        Galleries(parent, st, side, d, d + gap); d += gap;
        foreach (var br in list)
        {
            float w = br.Width * k;
            BuildStore(parent, st, br, d + w * 0.5f, w);
            Doors.Add(new Door { Station = d + w * 0.5f, Side = side, Brand = br.Id });
            d += w;
            Galleries(parent, st, side, d, d + gap); d += gap;
        }
        Debug.Log($"[maple-row] {(side > 0 ? "right" : "left")} side: frontage {a:0.0}-{b:0.0} m, " +
                  $"{list.Count} flagships (x{k:0.00} width), gallery gaps {gap:0.0} m.");
        return list.Count;
    }

    private static GameObject Place(Transform parent, string name, Mesh mesh, Material[] mats,
                                    Vector3 pos, Quaternion rot, Vector3? scale = null)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        if (scale.HasValue) go.transform.localScale = scale.Value;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        return go;
    }

    private static GameObject PlaceLocal(Transform parent, string name, Mesh mesh, Material[] mats,
                                         Vector3 localPos, Quaternion localRot, Vector3? scale = null)
    {
        var go = Place(parent, name, mesh, mats, Vector3.zero, Quaternion.identity, scale);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        return go;
    }

    // =================================================================== flagship storefront

    private const float Vitrine = 1.4f, StoreDepth = 16f, Bury = -3f, PlinthTop = 0.55f, WinTop = 4.3f;
    private const float EndPier = 1.0f, Pier = 0.7f, DoorW = 3.0f, Mullion = 0.3f;

    /// <summary>
    /// One flagship, authored in its own frame: +X along the street, +Z away from the road, y 0 =
    /// frontage floor. Ground storey = deep display vitrines (1.4 m) either side of a recessed
    /// door, fascia with the lit brand sign, a projecting blade sign, awnings or a steel canopy;
    /// above it one storey of brand cladding, a cornice, and a set-back residential block so the
    /// skyline stays continuous with the district.
    /// </summary>
    private static void BuildStore(Transform parent, Street st, Brand br, float c, float W)
    {
        int side = br.Side;
        bool canal = side < 0;
        float signH = canal ? 2.7f : 1.5f;     // canal side read from across the water: 1.5x the lettering
        float Hg = WinTop + signH + 0.6f, H = Hg + 3.6f, Hc = H + 0.45f, Hu = Hc + 5.6f;
        float vw = (W - 2f * EndPier - 2f * Pier - DoorW) * 0.5f;
        int nSub = Mathf.Max(1, Mathf.CeilToInt(vw / 7.2f));
        float subW = (vw - (nSub - 1) * Mullion) / nSub;
        float carpetL = canal ? TerraceM - 0.05f : st.Front(c, side) - st.Half(c) - 0.6f;

        var go = new GameObject($"{br.Id} Flagship").transform;
        go.SetParent(parent, false);
        go.SetPositionAndRotation(st.At(c, side * st.Front(c, side), st.FloorY(c, side)), st.Outward(c, side));

        // 0 facade  1 trim  2 window card  3 sign  4 awning/canopy  5 valance  6 door
        // 7 upper block (city facade, vertex tint)  8 warm glow  9 roof  10 LED  11 carpet  12 rope
        var mb = new MB(13);
        float hw = W * 0.5f;

        mb.Box(1, new Vector3(-hw, Bury, Vitrine), new Vector3(hw, Hg, StoreDepth), 2f, Faces.Left | Faces.Right | Faces.Back);

        // Brand storey: textured front and sides.
        var fuv0 = br.Mural ? Vector2.zero : new Vector2(-hw / br.FacadeTileM, Hg / br.FacadeTileM);
        var fuv1 = br.Mural ? Vector2.one : new Vector2(hw / br.FacadeTileM, H / br.FacadeTileM);
        mb.Face(0, new Vector3(0f, (Hg + H) * 0.5f, 0f), Vector3.back, Vector3.up, hw, (H - Hg) * 0.5f, fuv0, fuv1);
        mb.Box(0, new Vector3(-hw, Hg, 0f), new Vector3(hw, H, StoreDepth), br.Mural ? 4f : br.FacadeTileM, Faces.Left | Faces.Right);
        mb.Box(1, new Vector3(-hw, Hg, 0f), new Vector3(hw, H, StoreDepth), 2f, Faces.Back | Faces.Top);
        mb.Box(1, new Vector3(-hw - 0.15f, H, -0.25f), new Vector3(hw + 0.15f, Hc, StoreDepth + 0.1f), 1f, Faces.All);

        // Fascia and the lit wordmark, set high on the fascia so a canopy never hides it from the saddle.
        mb.Box(1, new Vector3(-hw, WinTop, -0.12f), new Vector3(hw, Hg, Vitrine), 1f, Faces.All);
        float sw = Mathf.Min(signH * 4f, W * 0.55f), sh = sw * 0.25f, sy = WinTop + 0.45f + sh * 0.5f;
        mb.Box(1, new Vector3(-sw * 0.5f - 0.12f, sy - sh * 0.5f - 0.12f, -0.22f),
                  new Vector3(sw * 0.5f + 0.12f, sy + sh * 0.5f + 0.12f, -0.12f), 1f,
               Faces.Front | Faces.Left | Faces.Right | Faces.Top | Faces.Bottom);
        mb.Face(3, new Vector3(0f, sy, -0.235f), Vector3.back, Vector3.up, sw * 0.5f, sh * 0.5f,
                Vector2.zero, Vector2.one);

        // Lit first-floor windows through the brand storey (lounge / fitting floor), trimmed in the accent.
        if (br.UpperWindows)
        {
            float wy0 = Hg + 0.7f, wy1 = H - 0.6f;
            for (float wx = -hw + 2.2f; wx <= hw - 2.2f + 0.01f; wx += 3.4f)
            {
                mb.Face(8, new Vector3(wx, (wy0 + wy1) * 0.5f, -0.02f), Vector3.back, Vector3.up, 0.8f, (wy1 - wy0) * 0.5f, Vector2.zero, Vector2.one);
                mb.Box(5, new Vector3(wx - 0.92f, wy0 - 0.12f, -0.1f), new Vector3(wx + 0.92f, wy0, 0f), 1f, Faces.Front | Faces.Top | Faces.Bottom);
                mb.Box(5, new Vector3(wx - 0.92f, wy1, -0.1f), new Vector3(wx + 0.92f, wy1 + 0.12f, 0f), 1f, Faces.Front | Faces.Top | Faces.Bottom);
            }
        }

        // Ground storey, left to right in local X.
        var slots = new List<(float x0, float x1)>();
        float x = -hw;
        mb.Box(1, new Vector3(x, Bury, 0f), new Vector3(x + EndPier, WinTop, Vitrine), 1f, Faces.Front | Faces.Left | Faces.Right);
        x += EndPier;
        for (int half = 0; half < 2; half++)
        {
            float x0 = x, x1 = x + vw;
            mb.Box(1, new Vector3(x0, Bury, 0f), new Vector3(x1, PlinthTop, Vitrine), 1f, Faces.Front | Faces.Top);
            for (int k = 0; k < nSub; k++)
            {
                float sx0 = x0 + k * (subW + Mullion), sx1 = sx0 + subW;
                if (k > 0)
                    mb.Box(1, new Vector3(sx0 - Mullion, PlinthTop, 0.08f), new Vector3(sx0, WinTop, Vitrine), 1f,
                           Faces.Front | Faces.Left | Faces.Right);
                mb.Face(2, new Vector3((sx0 + sx1) * 0.5f, (PlinthTop + WinTop) * 0.5f, Vitrine - 0.01f),
                        Vector3.back, Vector3.up, subW * 0.5f, (WinTop - PlinthTop) * 0.5f, Vector2.zero, Vector2.one);
                slots.Add((sx0, sx1));
            }
            if (br.Canopy == Canopy.Fabric) Awning(mb, 4, 5, x0 - 0.1f, x1 + 0.1f, WinTop - 0.02f, 1.75f, 0.78f);
            x = x1;
            if (half == 0)
            {
                mb.Box(1, new Vector3(x, Bury, 0f), new Vector3(x + Pier, WinTop, Vitrine), 1f, Faces.Front | Faces.Left | Faces.Right);
                x += Pier;
                mb.Box(1, new Vector3(x, Bury, 0f), new Vector3(x + DoorW, 0.02f, Vitrine), 1f, Faces.Top | Faces.Front);
                mb.Face(6, new Vector3(x + DoorW * 0.5f, (0.02f + WinTop) * 0.5f, Vitrine - 0.01f), Vector3.back, Vector3.up,
                        DoorW * 0.5f, (WinTop - 0.02f) * 0.5f, Vector2.zero, Vector2.one);
                x += DoorW;
                mb.Box(1, new Vector3(x, Bury, 0f), new Vector3(x + Pier, WinTop, Vitrine), 1f, Faces.Front | Faces.Left | Faces.Right);
                x += Pier;
            }
        }
        mb.Box(1, new Vector3(x, Bury, 0f), new Vector3(x + EndPier, WinTop, Vitrine), 1f, Faces.Front | Faces.Left | Faces.Right);

        if (br.Canopy == Canopy.Steel)
        {
            float cy = WinTop - 0.3f;
            mb.Box(4, new Vector3(-hw + 0.6f, cy - 0.07f, -1.5f), new Vector3(hw - 0.6f, cy + 0.07f, -0.02f), 1f, Faces.All);
            if (br.LedColor.maxColorComponent > 0f)
                mb.Face(10, new Vector3(0f, cy, -1.505f), Vector3.back, Vector3.up, hw - 0.6f, 0.035f, Vector2.zero, Vector2.one);
            for (int k = -1; k <= 1; k += 2)     // tie rods up to the fascia
                mb.Box(4, new Vector3(k * (hw - 1.4f) - 0.03f, cy + 0.07f, -1.3f), new Vector3(k * (hw - 1.4f) + 0.03f, WinTop + 0.3f, -1.22f), 1f, Faces.All);
        }

        // Blade sign over the pavement, readable by a rider looking down the street.
        {
            float bx = hw - 0.55f, y0 = WinTop + 0.25f, y1 = y0 + 0.5f;
            mb.Box(1, new Vector3(bx - 0.03f, y0, -2.2f), new Vector3(bx + 0.03f, y1, -0.2f), 1f,
                   Faces.Front | Faces.Back | Faces.Top | Faces.Bottom);
            mb.Box(1, new Vector3(bx - 0.02f, y1, -2.25f), new Vector3(bx + 0.02f, y1 + 0.06f, -0.12f), 1f, Faces.All);
            mb.Face(3, new Vector3(bx + 0.035f, (y0 + y1) * 0.5f, -1.2f), Vector3.right, Vector3.up, 0.98f, 0.24f, Vector2.zero, Vector2.one);
            mb.Face(3, new Vector3(bx - 0.035f, (y0 + y1) * 0.5f, -1.2f), Vector3.left, Vector3.up, 0.98f, 0.24f, Vector2.zero, Vector2.one);
        }

        // Set-back residential block + roof + a few lit flats.
        float ux0 = -hw + 0.8f, ux1 = hw - 0.8f, uz0 = 4.5f, uz1 = StoreDepth - 0.5f;
        mb.Tint = br.UpperTint;
        mb.Box(7, new Vector3(ux0, Hc, uz0), new Vector3(ux1, Hu, uz1), 3f, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
        mb.Tint = Color.white;
        mb.Box(9, new Vector3(ux0 - 0.12f, Hu, uz0 - 0.12f), new Vector3(ux1 + 0.12f, Hu + 0.4f, uz1 + 0.12f), 2f, Faces.All);
        int seed = StableHash(br.Id);
        for (float wy = Hc + 1.0f; wy + 1.7f < Hu - 0.3f; wy += 3.0f)
            for (float wx = ux0 + 1.5f; wx < ux1 - 1.0f; wx += 3.0f)
                if (Hash01(seed, wx, wy) < 0.42f)
                    mb.Face(8, new Vector3(wx, wy + 0.8f, uz0 - 0.03f), Vector3.back, Vector3.up, 0.62f, 0.8f, Vector2.zero, Vector2.one);

        // Red carpet to the kerb (or to the terrace edge, where the footbridge carries it on).
        mb.Face(11, new Vector3(0f, 0.035f, -carpetL * 0.5f), Vector3.up, Vector3.forward, 1.0f, carpetL * 0.5f,
                new Vector2(0f, 0f), new Vector2(1f, carpetL / 2f));
        mb.Box(11, new Vector3(-1.0f, -0.05f, -carpetL), new Vector3(1.0f, 0.035f, 0f), 1f, Faces.Front | Faces.Left | Faces.Right);
        var posts = new List<float>();
        for (float z = -0.9f; z > -carpetL + 0.35f; z -= 1.6f) posts.Add(z);
        foreach (int s in new[] { -1, 1 })
        {
            for (int k = 0; k < posts.Count; k++)
            {
                PlaceLocal(go, "Stanchion", PropMesh["Stanchion"], M("Brass"), new Vector3(s * 1.3f, 0.035f, posts[k]), Quaternion.identity);
                if (k > 0)
                    mb.Box(12, new Vector3(s * 1.3f - 0.02f, 0.76f, posts[k]), new Vector3(s * 1.3f + 0.02f, 0.80f, posts[k - 1]), 1f, Faces.All);
            }
        }

        var mesh = mb.Build($"MapleRow_Store_{br.Id}");
        var r = go.gameObject.AddComponent<MeshRenderer>();
        go.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        r.sharedMaterials = new[]
        {
            FacadeMat(br), CelMat($"Trim_{br.Id}", br.Trim, 0.3f, 0.2f), UnlitTex($"Window_{br.Id}", 1.0f),
            UnlitTex($"Sign_{br.Id}", 1.05f), CelMat($"Awning_{br.Id}", br.Awning, 0.2f, 0.15f),
            CelMat($"Valance_{br.Id}", br.Valance, 0.2f, 0.15f), UnlitTex("Door", 1.0f), CityFacadeMat(),
            UnlitTex("Window_FLAT", 0.8f), CelMat("Roof", new Color(0.24f, 0.25f, 0.28f), 0.2f, 0.1f),
            UnlitColor($"Led_{br.Id}", br.LedColor.maxColorComponent > 0f ? br.LedColor : Color.black),
            CelTexMat("Carpet", "MapleRow_Carpet.png", new Color(0.95f, 0.95f, 0.95f)), CelMat("Rope", Hex("#7A0E1E"), 0.5f, 0.3f),
        };

        // Flanking topiary at the door, then the window displays.
        foreach (int s in new[] { -1, 1 })
            PlaceLocal(go, "Door Topiary", PropMesh["Topiary"], M("Planter", "Hedge"),
                       new Vector3(s * (DoorW * 0.5f + 0.75f), 0f, -0.55f), Quaternion.identity);
        for (int k = 0; k < slots.Count; k++) Displays(go, br, k, slots[k].x0, slots[k].x1);
    }

    private static int StableHash(string s)
    {
        int h = 17;
        foreach (char ch in s) h = unchecked(h * 31 + ch);
        return h & 0x7fff;
    }

    private static float Hash01(int seed, float a, float b)
    {
        unchecked
        {
            int h = seed * 73856093 ^ Mathf.RoundToInt(a * 10f) * 19349663 ^ Mathf.RoundToInt(b * 10f) * 83492791;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffff) / 65535f;
        }
    }

    /// <summary>Sloped fabric awning with a front valance, local frame, from the facade (z 0) out.</summary>
    private static void Awning(MB mb, int sub, int valanceSub, float x0, float x1, float yTop, float depth, float drop)
    {
        float hw = (x1 - x0) * 0.5f, cx = (x0 + x1) * 0.5f;
        var slope = new Vector3(0f, drop, depth).normalized;                 // up the awning, toward the wall
        var n = new Vector3(0f, depth, -drop).normalized;                    // top surface normal
        var c = new Vector3(cx, yTop - drop * 0.5f, -depth * 0.5f);
        float hl = new Vector2(depth, drop).magnitude * 0.5f;
        mb.Face(sub, c, n, slope, hw, hl, Vector2.zero, new Vector2(hw, hl));
        mb.Face(sub, c - n * 0.02f, -n, slope, hw, hl, Vector2.zero, new Vector2(hw, hl));
        var v = new Vector3(cx, yTop - drop - 0.14f, -depth);
        mb.Face(valanceSub, v, Vector3.back, Vector3.up, hw, 0.14f, Vector2.zero, Vector2.one);
        mb.Face(valanceSub, v + Vector3.forward * 0.01f, Vector3.forward, Vector3.up, hw, 0.14f, Vector2.zero, Vector2.one);
    }

    /// <summary>3D merchandise standing in front of the lit interior card of one vitrine bay.</summary>
    private static void Displays(Transform store, Brand br, int slot, float x0, float x1)
    {
        float w = x1 - x0, z = 0.72f;
        switch (br.Display)
        {
            case Display.Jerseys:
            {
                int n = Mathf.Max(1, Mathf.FloorToInt(w / 2.1f)), pairs = br.Kits.Length / 2;
                for (int j = 0; j < n; j++)
                {
                    int p = (slot * n + j) % pairs;
                    PlaceLocal(store, "Mannequin", PropMesh["Torso"],
                               new[] { KitMat(br.Kits[p * 2]), KitMat(br.Kits[p * 2 + 1]), M("Chrome")[0] },
                               new Vector3(x0 + (j + 0.5f) * w / n, PlinthTop, z), Quaternion.identity);
                }
                break;
            }
            case Display.Frames:
            {
                int n = Mathf.Max(1, Mathf.FloorToInt(w / 2.25f));
                var carbon = CelMat("Carbon", Hex("#1E2024"), 0.85f, 0.7f);
                for (int j = 0; j < n; j++)
                {
                    float cx = x0 + (j + 0.5f) * w / n;
                    float cy = PlinthTop + 0.72f;
                    // A complete superbike silhouette: two deep wheels behind a bright frame.
                    // From the saddle this reads as a frame boutique immediately, rather than a
                    // grid of loose wheelsets like the former WHEELWORKS display.
                    foreach (float dx in new[] { -0.43f, 0.43f })
                        PlaceLocal(store, "Display Wheel", PropMesh["Wheel"],
                                   new[] { KitMat(br.Kits[0]), carbon, KitMat(br.Kits[2]) },
                                   new Vector3(cx + dx, cy, z + 0.05f), Quaternion.identity);
                    PlaceLocal(store, "Superbike Frameset", PropMesh["Frame"],
                               new[] { KitMat(br.Kits[(slot + j) % 2 == 0 ? 2 : 1]), carbon },
                               new Vector3(cx, cy, z - 0.03f), Quaternion.identity);
                }
                break;
            }
            case Display.Screens:
            {
                int n = Mathf.Max(1, Mathf.FloorToInt(w / 1.8f));
                for (int j = 0; j < n; j++)
                    PlaceLocal(store, "Screen Stand", PropMesh["ScreenStand"], new[] { KitMat(br.Kits[0]), UnlitTex("Screen", 1.1f) },
                               new Vector3(x0 + (j + 0.5f) * w / n, PlinthTop, z), Quaternion.identity);
                break;
            }
            case Display.Helmets:
            {
                int n = Mathf.Max(1, Mathf.FloorToInt(w / 1.4f));
                for (int j = 0; j < n; j++)
                {
                    float h = 0.75f + 0.35f * Hash01(slot * 31 + j, x0, w);
                    var pos = new Vector3(x0 + (j + 0.5f) * w / n, PlinthTop, z);
                    PlaceLocal(store, "Plinth", PropMesh["Plinth"], M("PlinthWhite", "PlinthGlow"), pos, Quaternion.identity, new Vector3(1f, h, 1f));
                    PlaceLocal(store, "Helmet", PropMesh["Helmet"], new[] { KitMat(br.Kits[(slot * n + j) % br.Kits.Length]), M("VentDark")[0] },
                               pos + Vector3.up * (h + 0.005f), Quaternion.Euler(0f, 90f + 25f * (j % 2 == 0 ? 1 : -1), 0f));
                }
                break;
            }
        }
    }

    // =================================================================== gallery infill

    private static int _galleryId;
    private static readonly Color[] GalleryTints =
    {
        new Color(0.93f, 0.89f, 0.82f), new Color(0.86f, 0.80f, 0.71f), new Color(0.82f, 0.83f, 0.84f),
        new Color(0.95f, 0.93f, 0.89f), new Color(0.78f, 0.72f, 0.66f),
    };
    private static readonly Color[] GalleryAwnings = { Hex("#1F3B2D"), Hex("#1B2A44"), Hex("#5A1A24"), Hex("#2E2A26") };

    /// <summary>Unbranded limestone boutiques between the flagships, so the street reads continuous.</summary>
    private static void Galleries(Transform parent, Street st, int side, float d0, float d1)
    {
        const float Alley = 1.4f;
        float len = d1 - d0;
        if (len < 6f) return;
        int n = Mathf.Max(1, Mathf.RoundToInt(len / 15f));
        float w = (len - (n + 1) * Alley) / n;
        if (w < 5f) { n = 1; w = len - 2f * Alley; }
        if (w < 5f) return;
        for (int k = 0; k < n; k++)
            BuildGallery(parent, st, side, d0 + Alley + k * (w + Alley) + w * 0.5f, w, _galleryId++);
    }

    private static void BuildGallery(Transform parent, Street st, int side, float c, float w, int id)
    {
        var go = new GameObject($"Gallery {id:00}").transform;
        go.SetParent(parent, false);
        go.SetPositionAndRotation(st.At(c, side * (st.Front(c, side) + 0.3f), st.FloorY(c, side)), st.Outward(c, side));
        float H = Mathf.Lerp(side > 0 ? 11f : 10f, side > 0 ? 14.5f : 12.5f, Hash01(id, c, w));
        const float D = 14f;
        float hw = w * 0.5f;

        // 0 city facade  1 stone trim  2 gallery interior  3 awning  4 dark glass  5 roof  6 lit flats
        var mb = new MB(7);
        mb.Tint = GalleryTints[id % GalleryTints.Length];
        mb.Box(0, new Vector3(-hw, Bury, 0f), new Vector3(hw, H, D), 3f, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
        mb.Tint = Color.white;
        mb.Box(5, new Vector3(-hw - 0.1f, H, -0.18f), new Vector3(hw + 0.1f, H + 0.4f, D + 0.1f), 2f, Faces.All);
        mb.Box(1, new Vector3(-hw, 3.6f, -0.16f), new Vector3(hw, 4.2f, 0f), 1f, Faces.Front | Faces.Top | Faces.Bottom | Faces.Left | Faces.Right);

        const float DoorW2 = 1.8f;
        bool two = w > 9.5f;
        float doorX = two ? 0f : hw - 1.7f;
        float dl = doorX - DoorW2 * 0.5f, dr = doorX + DoorW2 * 0.5f;
        var wins = two
            ? new[] { (-hw + 0.8f, dl - 0.5f), (dr + 0.5f, hw - 0.8f) }
            : new[] { (-hw + 0.8f, dl - 0.5f) };

        // Plinth, split around the door (a full-width band hid the door's foot).
        mb.Box(1, new Vector3(-hw, Bury, -0.14f), new Vector3(dl - 0.25f, 0.5f, 0f), 1f, Faces.Front | Faces.Top | Faces.Left | Faces.Right);
        if (hw - (dr + 0.25f) > 0.05f)
            mb.Box(1, new Vector3(dr + 0.25f, Bury, -0.14f), new Vector3(hw, 0.5f, 0f), 1f, Faces.Front | Faces.Top | Faces.Left | Faces.Right);

        // Pilasters: full-height corner pilasters, plus ground-storey pilasters between bays.
        foreach (float px in new[] { -hw + 0.24f, hw - 0.24f })
            mb.Box(1, new Vector3(px - 0.24f, 0.5f, -0.2f), new Vector3(px + 0.24f, H - 0.75f, 0f), 1f, Faces.Front | Faces.Left | Faces.Right);
        // Cornice: a projecting band with a smaller bed moulding below it.
        mb.Box(1, new Vector3(-hw - 0.12f, H - 0.5f, -0.42f), new Vector3(hw + 0.12f, H, 0f), 1f, Faces.Front | Faces.Bottom | Faces.Left | Faces.Right);
        mb.Box(1, new Vector3(-hw - 0.04f, H - 0.75f, -0.2f), new Vector3(hw + 0.04f, H - 0.5f, 0f), 1f, Faces.Front | Faces.Bottom | Faces.Left | Faces.Right);

        int wi = 0;
        foreach (var (wx0, wx1) in wins)
        {
            if (wx1 - wx0 < 1f) continue;
            float wc = (wx0 + wx1) * 0.5f, ww = wx1 - wx0;
            mb.Face(2, new Vector3(wc, 2.05f, -0.03f), Vector3.back, Vector3.up, ww * 0.5f, 1.45f, Vector2.zero, Vector2.one);
            Awning(mb, 3, 3, wx0 - 0.1f, wx1 + 0.1f, 3.55f, 1.3f, 0.55f);
            // Window surround (jambs + a projecting sill) and bronze-dark mullions with a transom.
            mb.Box(1, new Vector3(wx0 - 0.16f, 0.5f, -0.12f), new Vector3(wx0, 3.6f, 0f), 1f, Faces.Front | Faces.Left | Faces.Right);
            mb.Box(1, new Vector3(wx1, 0.5f, -0.12f), new Vector3(wx1 + 0.16f, 3.6f, 0f), 1f, Faces.Front | Faces.Left | Faces.Right);
            mb.Box(1, new Vector3(wx0 - 0.2f, 0.5f, -0.24f), new Vector3(wx1 + 0.2f, 0.62f, 0f), 1f, Faces.Front | Faces.Top | Faces.Left | Faces.Right);
            int nm = Mathf.Max(1, Mathf.RoundToInt(ww / 1.3f));
            for (int k = 1; k < nm; k++)
            {
                float mx = wx0 + ww * k / nm;
                mb.Box(4, new Vector3(mx - 0.05f, 0.62f, -0.09f), new Vector3(mx + 0.05f, 3.5f, -0.03f), 1f, Faces.Front | Faces.Left | Faces.Right);
            }
            mb.Box(4, new Vector3(wx0, 2.78f, -0.09f), new Vector3(wx1, 2.86f, -0.03f), 1f, Faces.Front | Faces.Top | Faces.Bottom);
            // One small lit vitrine case standing proud of the first window.
            if (wi++ == 0 && ww > 2.6f)
            {
                float vx = wx0 + Mathf.Min(1.6f, ww * 0.3f);
                mb.Box(1, new Vector3(vx - 0.75f, 0.62f, -0.55f), new Vector3(vx + 0.75f, 0.95f, -0.03f), 1f, Faces.Front | Faces.Top | Faces.Left | Faces.Right);
                mb.Face(6, new Vector3(vx, 1.45f, -0.5f), Vector3.back, Vector3.up, 0.66f, 0.5f, Vector2.zero, Vector2.one);
                mb.Box(4, new Vector3(vx - 0.75f, 0.95f, -0.55f), new Vector3(vx + 0.75f, 2.0f, -0.03f), 1f, Faces.Left | Faces.Right | Faces.Top);
                mb.Box(1, new Vector3(vx - 0.78f, 2.0f, -0.58f), new Vector3(vx + 0.78f, 2.08f, -0.03f), 1f, Faces.Front | Faces.Top | Faces.Bottom | Faces.Left | Faces.Right);
            }
        }

        // Inset door: a proud stone architrave and step, the glass door set back inside it.
        mb.Face(4, new Vector3(doorX, 1.6f, -0.03f), Vector3.back, Vector3.up, DoorW2 * 0.5f, 1.6f, Vector2.zero, Vector2.one);
        mb.Box(1, new Vector3(dl - 0.25f, 0f, -0.26f), new Vector3(dl, 3.45f, 0f), 1f, Faces.Front | Faces.Left | Faces.Right);
        mb.Box(1, new Vector3(dr, 0f, -0.26f), new Vector3(dr + 0.25f, 3.45f, 0f), 1f, Faces.Front | Faces.Left | Faces.Right);
        mb.Box(1, new Vector3(dl - 0.35f, 3.2f, -0.34f), new Vector3(dr + 0.35f, 3.5f, 0f), 1f, Faces.Front | Faces.Top | Faces.Bottom | Faces.Left | Faces.Right);
        mb.Box(1, new Vector3(dl, Bury, -0.3f), new Vector3(dr, 0.1f, 0f), 1f, Faces.Front | Faces.Top);
        mb.Face(4, new Vector3(doorX, 1.6f, -0.05f), Vector3.back, Vector3.up, 0.03f, 1.45f, Vector2.zero, Vector2.one);
        for (float wy = 5.0f; wy + 1.6f < H - 0.5f; wy += 3.1f)
            for (float wx = -hw + 1.6f; wx < hw - 1.0f; wx += 3.0f)
                if (Hash01(id * 7 + 3, wx, wy) < 0.3f)
                    mb.Face(6, new Vector3(wx, wy + 0.8f, -0.03f), Vector3.back, Vector3.up, 0.62f, 0.8f, Vector2.zero, Vector2.one);

        go.gameObject.AddComponent<MeshFilter>().sharedMesh = mb.Build($"MapleRow_Gallery_{id:00}");
        go.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            CityFacadeMat(), CelMat("Stone", new Color(0.70f, 0.66f, 0.60f), 0.2f, 0.1f),
            UnlitTex("Window_GALLERY", 0.95f),
            CelMat($"GalleryAwning_{id % GalleryAwnings.Length}", GalleryAwnings[id % GalleryAwnings.Length], 0.2f, 0.12f),
            CelMat("DarkGlass", new Color(0.09f, 0.10f, 0.12f), 0.85f, 0.6f), CelMat("Roof", new Color(0.24f, 0.25f, 0.28f), 0.2f, 0.1f),
            UnlitTex("Window_FLAT", 0.8f),
        };
    }

    // =================================================================== paving + terrace

    /// <summary>Station list from d0 to d1 through every route sample in between (so slabs follow the road exactly).</summary>
    private static List<float> Stations(Street st, float d0, float d1)
    {
        var list = new List<float> { d0 };
        foreach (float d in st.R.Distance) if (d > d0 + 0.2f && d < d1 - 0.2f) list.Add(d);
        list.Add(d1);
        return list;
    }

    /// <summary>A slab strip between two signed offsets, top at floor + dyTop, sides down to floor + dyBottom.</summary>
    private static void Slab(MB mb, int topSub, int sideSub, Street st, float d0, float d1, float o0, float o1,
                             float dyTop, float dyBottom, bool innerFace, bool outerFace, bool caps)
    {
        var s = Stations(st, d0, d1);
        float sgn = Mathf.Sign(o1 - o0);
        for (int k = 0; k + 1 < s.Count; k++)
        {
            float da = s[k], db = s[k + 1];
            float ya = st.PavTop(da), yb = st.PavTop(db);
            Vector3 P(float d, float o, float y) => st.At(d, o, y);
            var a0 = P(da, o0, ya + dyTop); var a1 = P(da, o1, ya + dyTop);
            var b0 = P(db, o0, yb + dyTop); var b1 = P(db, o1, yb + dyTop);
            mb.Quad(topSub, a0, a1, b1, b0, new Vector2(o0 / 2.4f, da / 2.4f), new Vector2(o1 / 2.4f, da / 2.4f),
                    new Vector2(o1 / 2.4f, db / 2.4f), new Vector2(o0 / 2.4f, db / 2.4f), Vector3.up);
            if (innerFace)
                mb.Quad(sideSub, a0, b0, P(db, o0, yb + dyBottom), P(da, o0, ya + dyBottom),
                        new Vector2(da / 2f, dyTop / 2f), new Vector2(db / 2f, dyTop / 2f), new Vector2(db / 2f, dyBottom / 2f),
                        new Vector2(da / 2f, dyBottom / 2f), -st.Side(da) * sgn);
            if (outerFace)
                mb.Quad(sideSub, a1, b1, P(db, o1, yb + dyBottom), P(da, o1, ya + dyBottom),
                        new Vector2(da / 2f, dyTop / 2f), new Vector2(db / 2f, dyTop / 2f), new Vector2(db / 2f, dyBottom / 2f),
                        new Vector2(da / 2f, dyBottom / 2f), st.Side(da) * sgn);
        }
        if (!caps) return;
        foreach (var (d, dir) in new[] { (d0, -1f), (d1, 1f) })
        {
            float y = st.PavTop(d);
            mb.Quad(sideSub, st.At(d, o0, y + dyTop), st.At(d, o1, y + dyTop), st.At(d, o1, y + dyBottom), st.At(d, o0, y + dyBottom),
                    new Vector2(o0 / 2f, dyTop / 2f), new Vector2(o1 / 2f, dyTop / 2f), new Vector2(o1 / 2f, dyBottom / 2f),
                    new Vector2(o0 / 2f, dyBottom / 2f), st.Tan(d) * dir);
        }
    }

    private static void BuildPaving(Transform group, Street st)
    {
        float mid = (StreetStartM + StreetEndM) * 0.5f, half = st.Half(mid);
        float d0 = StreetStartM - 1f, d1 = StreetEndM + 1f;
        var mb = new MB(2);   // 0 limestone paving  1 dressed stone sides

        // Right: kerb stone to under the facades, over the pavement and the setback.
        Slab(mb, 0, 1, st, d0, d1, half + KerbClearM, st.Front(mid, +1) + 0.6f, 0.015f, -0.45f, true, true, true);
        // Left near pavement (road <-> canal), and the raised far-bank terrace in front of the boutiques.
        Slab(mb, 0, 1, st, d0, d1, -(half + KerbClearM), -(half + PavementWidthM), 0.015f, -0.45f, true, true, true);
        // The terrace runs back past the city's own (14 m) far coping so none of the covered
        // channel's water or stonework can show between the boutiques; its canal-side face, with a
        // raised coping lip, is the narrowed canal's far wall.
        float terraceOut = Mathf.Max(st.Front(mid, -1) + 0.6f, st.CityFarBank(mid) + 0.5f);
        Slab(mb, 0, 1, st, d0, d1, -st.FarBank(mid), -terraceOut, TerraceLiftM, -2.5f, true, true, true);
        Slab(mb, 1, 1, st, d0, d1, -(st.NearCoping(mid) + ChannelWidthM), -st.FarBank(mid), TerraceLiftM + 0.06f, -2.5f, true, true, true);

        Place(group, "Maple Row Paving", mb.Build("MapleRow_Paving"),
              new[] { CelTexMat("Paving", "MapleRow_Paving.png", new Color(0.70f, 0.69f, 0.66f)), CelMat("Stone", new Color(0.70f, 0.66f, 0.60f), 0.2f, 0.1f) },
              Vector3.zero, Quaternion.identity);
    }

    // =================================================================== street dressing

    private static bool NearDoor(float d, int side, float r)
    {
        foreach (var door in Doors) if (door.Side == side && Mathf.Abs(door.Station - d) < r) return true;
        return false;
    }

    private static void BuildStreetDressing(Transform group, Street st)
    {
        var dress = new GameObject("Street Dressing").transform;
        dress.SetParent(group, false);
        float mid = (StreetStartM + StreetEndM) * 0.5f, half = st.Half(mid);
        var lampMats = M("Iron", "Glow_Lamp", "Banner");
        int lamps = 0, planters = 0, benches = 0, racks = 0;

        // Boutique lamps with Maple Row banners, alternating with hedge planters, on the right kerb.
        for (float d = StreetStartM + 6f; d < StreetEndM - 3f; d += 15f)
        {
            if (!NearDoor(d, +1, 2.0f)) { Place(dress, "Lamp", PropMesh["Lamp"], lampMats, st.At(d, half + 1.0f, st.PavTop(d)), st.Outward(d, +1)); lamps++; }
            float p = d + 7.5f;
            if (p < StreetEndM - 3f && !NearDoor(p, +1, 2.2f))
            { Place(dress, "Planter", PropMesh["Planter"], M("Planter", "Hedge"), st.At(p, half + 0.9f, st.PavTop(p)), st.Outward(p, +1)); planters++; }
        }
        // Left near pavement: lamps on the kerb, benches facing the canal and the shops across it.
        for (float d = StreetStartM + 6f; d < StreetEndM - 3f; d += 15f)
        {
            if (!NearDoor(d, -1, 2.4f)) { Place(dress, "Lamp", PropMesh["Lamp"], lampMats, st.At(d, -(half + 1.0f), st.PavTop(d)), st.Outward(d, -1)); lamps++; }
            float b = d + 7.5f;
            if (b < StreetEndM - 3f && !NearDoor(b, -1, 3.5f))
            { Place(dress, "Canal Bench", PropMesh["Bench"], M("Wood", "Iron"), st.At(b, -(half + 4.1f), st.PavTop(b)), st.Outward(b, +1)); benches++; }
        }
        // Far-bank terrace lamps and planters.
        for (float d = StreetStartM + 9f; d < StreetEndM - 3f; d += 18f)
        {
            float fb = st.FarBank(d), y = st.FloorY(d, -1);
            if (!NearDoor(d, -1, 2.6f)) { Place(dress, "Terrace Lamp", PropMesh["Lamp"], lampMats, st.At(d, -(fb + 0.45f), y), st.Outward(d, -1)); lamps++; }
            float p = d + 9f;
            if (p < StreetEndM - 3f && !NearDoor(p, -1, 2.6f))
            { Place(dress, "Terrace Planter", PropMesh["Planter"], M("Planter", "Hedge"), st.At(p, -(fb + 0.75f), st.FloorY(p, -1)), st.Outward(p, -1)); planters++; }
        }
        // Bike racks (it is a cycling street): between the right-hand flagships and at both ends.
        var rackAt = new List<float> { StreetStartM + 10f, StreetEndM - 10f };
        for (int k = 0; k + 1 < Doors.Count; k++)
            if (Doors[k].Side > 0 && Doors[k + 1].Side > 0) rackAt.Add((Doors[k].Station + Doors[k + 1].Station) * 0.5f);
        foreach (float d in rackAt)
            for (int h = -1; h <= 1; h++)
            {
                float dh = d + h * 0.9f;
                Place(dress, "Bike Hoop", PropMesh["Hoop"], M("Iron"), st.At(dh, half + 1.6f, st.PavTop(dh)), st.Outward(dh, +1));
                racks++;
            }
        // Street-name totems at both ends of both pavements.
        foreach (float d in new[] { StreetStartM + 3f, StreetEndM - 3f })
            foreach (int s in new[] { +1, -1 })
                Place(dress, "Maple Row Totem", PropMesh["Totem"], M("TotemBody", "Totem"), st.At(d, s * (half + 1.3f), st.PavTop(d)), st.Outward(d, s));

        int bridges = 0;
        foreach (var door in Doors)
            if (door.Side < 0) { BuildBridge(dress, st, door.Station, bridges); bridges++; }

        Debug.Log($"[maple-row] dressing: {lamps} lamps, {planters} planters, {benches} benches, {racks} bike hoops, 4 totems, {bridges} footbridges.");
    }

    /// <summary>
    /// Arched red-carpet footbridge from the near pavement over the canal to a left-hand flagship's
    /// door. Starts 2.2 m outside the kerb (clear of the road) and lands on the raised terrace.
    /// </summary>
    private static void BuildBridge(Transform parent, Street st, float d, int id)
    {
        float half = st.Half(d), oA = half + 2.2f, oB = st.FarBank(d) + 0.35f;
        float y0 = st.PavTop(d);
        var t = st.Tan(d); var sd = -st.Side(d);        // sd points across the canal
        var basePt = st.Pos(d); basePt.y = y0;
        const int Seg = 14; const float HalfW = 1.4f, Deck = 0.3f, Wall = 0.18f, WallH = 0.95f;
        float H(float u) => 0.03f + TerraceLiftM * u + 1.15f * Mathf.Sin(Mathf.PI * u);
        Vector3 Q(float o, float x, float y) => basePt + sd * o + t * x + Vector3.up * y;

        var mb = new MB(2);   // 0 stone  1 carpet
        for (int k = 0; k < Seg; k++)
        {
            float u0 = k / (float)Seg, u1 = (k + 1) / (float)Seg;
            float oa = Mathf.Lerp(oA, oB, u0), ob = Mathf.Lerp(oA, oB, u1), ha = H(u0), hb = H(u1);
            var up = Vector3.up;
            // deck top (stone margins) + carpet runner
            mb.Quad(0, Q(oa, -HalfW, ha), Q(oa, HalfW, ha), Q(ob, HalfW, hb), Q(ob, -HalfW, hb),
                    new Vector2(0, oa / 2), new Vector2(1.4f, oa / 2), new Vector2(1.4f, ob / 2), new Vector2(0, ob / 2), up);
            mb.Quad(1, Q(oa, -1.0f, ha + 0.02f), Q(oa, 1.0f, ha + 0.02f), Q(ob, 1.0f, hb + 0.02f), Q(ob, -1.0f, hb + 0.02f),
                    new Vector2(0, oa / 2), new Vector2(1, oa / 2), new Vector2(1, ob / 2), new Vector2(0, ob / 2), up);
            // underside
            mb.Quad(0, Q(oa, -HalfW - Wall, ha - Deck), Q(oa, HalfW + Wall, ha - Deck), Q(ob, HalfW + Wall, hb - Deck), Q(ob, -HalfW - Wall, hb - Deck),
                    new Vector2(0, oa / 2), new Vector2(1.5f, oa / 2), new Vector2(1.5f, ob / 2), new Vector2(0, ob / 2), -up);
            // parapet walls: outer face, inner face, cap
            foreach (int s in new[] { -1, 1 })
            {
                float xi = s * HalfW, xo = s * (HalfW + Wall);
                mb.Quad(0, Q(oa, xo, ha - Deck), Q(ob, xo, hb - Deck), Q(ob, xo, hb + WallH), Q(oa, xo, ha + WallH),
                        new Vector2(oa / 2, 0), new Vector2(ob / 2, 0), new Vector2(ob / 2, 0.6f), new Vector2(oa / 2, 0.6f), t * s);
                mb.Quad(0, Q(oa, xi, ha), Q(ob, xi, hb), Q(ob, xi, hb + WallH), Q(oa, xi, ha + WallH),
                        new Vector2(oa / 2, 0), new Vector2(ob / 2, 0), new Vector2(ob / 2, 0.5f), new Vector2(oa / 2, 0.5f), -t * s);
                mb.Quad(0, Q(oa, xi, ha + WallH), Q(ob, xi, hb + WallH), Q(ob, xo, hb + WallH), Q(oa, xo, ha + WallH),
                        new Vector2(oa / 2, 0), new Vector2(ob / 2, 0), new Vector2(ob / 2, 0.1f), new Vector2(oa / 2, 0.1f), up);
            }
        }
        // Carpet across the near pavement from the kerb to (just under) the bridge foot.
        float oc = half + 0.6f, oe = oA + 0.4f;
        mb.Quad(1, Q(oc, -1.0f, 0.035f), Q(oc, 1.0f, 0.035f), Q(oe, 1.0f, 0.035f), Q(oe, -1.0f, 0.035f),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, (oe - oc) / 2), new Vector2(0, (oe - oc) / 2), Vector3.up);

        Place(parent, $"Footbridge {id + 1}", mb.Build($"MapleRow_Bridge_{id}"),
              new[] { CelMat("BridgeStone", new Color(0.66f, 0.61f, 0.55f), 0.2f, 0.1f), CelTexMat("Carpet", "MapleRow_Carpet.png", new Color(0.95f, 0.95f, 0.95f)) },
              Vector3.zero, Quaternion.identity);
    }

    /// <summary>Hard check: no vertex of ours below 4.6 m may sit on the carriageway or its kerb stone.</summary>
    private static void CheckCorridor(Transform group, Street st)
    {
        int bad = 0; string first = null;
        float minClear = float.MaxValue;
        foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var w = m.MultiplyPoint3x4(v);
                if (!st.Project(w, out float s, out float o)) continue;
                if (s < StreetStartM - 5f || s > StreetEndM + 5f) continue;
                if (w.y > st.PavTop(Mathf.Clamp(s, StreetStartM, StreetEndM)) + 4.6f) continue;
                float clearM = Mathf.Abs(o) - st.Half(Mathf.Clamp(s, StreetStartM, StreetEndM));
                minClear = Mathf.Min(minClear, clearM);
                if (clearM < KerbClearM - 0.01f) { bad++; first ??= mf.name; }
            }
        }
        if (bad > 0) Debug.LogError($"[maple-row] CORRIDOR VIOLATION: {bad} vertices inside the carriageway + kerb (first: {first}).");
        else Debug.Log($"[maple-row] corridor check OK: nearest geometry {minClear:0.00} m outside the carriageway edge " +
                       $"(road {2f * st.Half((StreetStartM + StreetEndM) * 0.5f):0.0} m wide, untouched).");
    }

    // =================================================================== mesh builder

    [Flags] private enum Faces { Front = 1, Back = 2, Left = 4, Right = 8, Top = 16, Bottom = 32, All = 63 }

    /// <summary>
    /// Minimal multi-submesh builder. Faces are authored as (centre, outward normal, up) so the
    /// winding is derived, never hand-ordered; free quads/triangles take a wanted normal and are
    /// flipped to match it. Either way nothing can come out inside-out.
    /// </summary>
    private sealed class MB
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector2> _u = new List<Vector2>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<int>[] _t;
        public Color Tint = Color.white;

        public MB(int subs)
        {
            _t = new List<int>[subs];
            for (int i = 0; i < subs; i++) _t[i] = new List<int>();
        }

        private int Add(Vector3 p, Vector2 uv) { _v.Add(p); _u.Add(uv); _c.Add(Tint); return _v.Count - 1; }

        public void Tri(int sub, int a, int b, int c, Vector3 want)
        {
            var n = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            if (Vector3.Dot(n, want) >= 0f) { _t[sub].Add(a); _t[sub].Add(b); _t[sub].Add(c); }
            else { _t[sub].Add(a); _t[sub].Add(c); _t[sub].Add(b); }
        }

        public void Face(int sub, Vector3 c, Vector3 n, Vector3 up, float hw, float hh, Vector2 uv0, Vector2 uv1)
        {
            var r = Vector3.Cross(n, up).normalized * hw;
            var u = up.normalized * hh;
            int b = Add(c - r - u, uv0);
            Add(c + r - u, new Vector2(uv1.x, uv0.y));
            Add(c + r + u, uv1);
            Add(c - r + u, new Vector2(uv0.x, uv1.y));
            _t[sub].Add(b); _t[sub].Add(b + 3); _t[sub].Add(b + 2);
            _t[sub].Add(b); _t[sub].Add(b + 2); _t[sub].Add(b + 1);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 want)
        {
            int i = Add(a, ua); Add(b, ub); Add(c, uc); Add(d, ud);
            Tri(sub, i, i + 1, i + 2, want);
            Tri(sub, i, i + 2, i + 3, want);
        }

        public void Box(int sub, Vector3 min, Vector3 max, float tile, Faces f)
        {
            var c = (min + max) * 0.5f; var s = max - min;
            float k = 1f / Mathf.Max(0.01f, tile);
            if ((f & Faces.Front) != 0) Face(sub, new Vector3(c.x, c.y, min.z), Vector3.back, Vector3.up, s.x * .5f, s.y * .5f, new Vector2(min.x, min.y) * k, new Vector2(max.x, max.y) * k);
            if ((f & Faces.Back) != 0) Face(sub, new Vector3(c.x, c.y, max.z), Vector3.forward, Vector3.up, s.x * .5f, s.y * .5f, new Vector2(-max.x, min.y) * k, new Vector2(-min.x, max.y) * k);
            if ((f & Faces.Left) != 0) Face(sub, new Vector3(min.x, c.y, c.z), Vector3.left, Vector3.up, s.z * .5f, s.y * .5f, new Vector2(-max.z, min.y) * k, new Vector2(-min.z, max.y) * k);
            if ((f & Faces.Right) != 0) Face(sub, new Vector3(max.x, c.y, c.z), Vector3.right, Vector3.up, s.z * .5f, s.y * .5f, new Vector2(min.z, min.y) * k, new Vector2(max.z, max.y) * k);
            if ((f & Faces.Top) != 0) Face(sub, new Vector3(c.x, max.y, c.z), Vector3.up, Vector3.forward, s.x * .5f, s.z * .5f, new Vector2(min.x, min.z) * k, new Vector2(max.x, max.z) * k);
            if ((f & Faces.Bottom) != 0) Face(sub, new Vector3(c.x, min.y, c.z), Vector3.down, Vector3.forward, s.x * .5f, s.z * .5f, new Vector2(-max.x, min.z) * k, new Vector2(-min.x, max.z) * k);
        }

        /// <summary>Surface of revolution about +Y through <paramref name="o"/>; r[i] at height y[i]; z squashed by zScale.</summary>
        public void Lathe(int sub, Vector3 o, float[] y, float[] r, float zScale, int seg, bool capBottom, bool capTop)
        {
            int rows = y.Length;
            var idx = new int[rows, seg + 1];
            for (int j = 0; j < rows; j++)
                for (int k = 0; k <= seg; k++)
                {
                    float a = k / (float)seg * Mathf.PI * 2f;
                    idx[j, k] = Add(o + new Vector3(Mathf.Cos(a) * r[j], y[j], Mathf.Sin(a) * r[j] * zScale), new Vector2(k / (float)seg, y[j]));
                }
            for (int j = 0; j + 1 < rows; j++)
                for (int k = 0; k < seg; k++)
                {
                    float a = (k + 0.5f) / seg * Mathf.PI * 2f;
                    var want = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Tri(sub, idx[j, k], idx[j + 1, k], idx[j + 1, k + 1], want);
                    Tri(sub, idx[j, k], idx[j + 1, k + 1], idx[j, k + 1], want);
                }
            if (capBottom) Cap(sub, o + Vector3.up * y[0], r[0], zScale, seg, Vector3.down);
            if (capTop) Cap(sub, o + Vector3.up * y[rows - 1], r[rows - 1], zScale, seg, Vector3.up);
        }

        private void Cap(int sub, Vector3 c, float r, float zScale, int seg, Vector3 want)
        {
            int ci = Add(c, new Vector2(0.5f, 0.5f));
            int first = -1, prev = -1;
            for (int k = 0; k <= seg; k++)
            {
                float a = k / (float)seg * Mathf.PI * 2f;
                int i = Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * zScale), new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                if (k > 0) Tri(sub, ci, prev, i, want);
                if (first < 0) first = i;
                prev = i;
            }
        }

        public void Cylinder(int sub, Vector3 baseC, float r0, float r1, float h, int seg, bool capTop = true) =>
            Lathe(sub, baseC, new[] { 0f, h }, new[] { r0, r1 }, 1f, seg, false, capTop);

        /// <summary>Ellipsoid (or its upper half, closed underneath) centred at c.</summary>
        public void Ellipsoid(int sub, Vector3 c, Vector3 rad, int seg, int rings, bool upperOnly)
        {
            float lat0 = upperOnly ? 0f : -Mathf.PI * 0.5f;
            var idx = new int[rings + 1, seg + 1];
            for (int j = 0; j <= rings; j++)
            {
                float lat = Mathf.Lerp(lat0, Mathf.PI * 0.5f, j / (float)rings);
                for (int k = 0; k <= seg; k++)
                {
                    float a = k / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(a), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(a));
                    idx[j, k] = Add(c + Vector3.Scale(d, rad), new Vector2(k / (float)seg, j / (float)rings));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int k = 0; k < seg; k++)
                {
                    var want = (_v[idx[j, k]] + _v[idx[j + 1, k + 1]]) * 0.5f - c;
                    Tri(sub, idx[j, k], idx[j + 1, k], idx[j + 1, k + 1], want);
                    Tri(sub, idx[j, k], idx[j + 1, k + 1], idx[j, k + 1], want);
                }
            if (upperOnly) Cap(sub, c, rad.x, rad.z / rad.x, seg, Vector3.down);
        }

        /// <summary>Torus in the local XY plane (axle along Z).</summary>
        public void Torus(int sub, Vector3 c, float R, float r, int segU, int segV)
        {
            var idx = new int[segU + 1, segV + 1];
            for (int i = 0; i <= segU; i++)
            {
                float u = i / (float)segU * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= segV; j++)
                {
                    float v = j / (float)segV * Mathf.PI * 2f;
                    idx[i, j] = Add(c + radial * (R + Mathf.Cos(v) * r) + Vector3.forward * (Mathf.Sin(v) * r), new Vector2(i / (float)segU, j / (float)segV));
                }
            }
            for (int i = 0; i < segU; i++)
            {
                float u = (i + 0.5f) / segU * Mathf.PI * 2f;
                var ring = c + new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) * R;
                for (int j = 0; j < segV; j++)
                {
                    var want = (_v[idx[i, j]] + _v[idx[i + 1, j + 1]]) * 0.5f - ring;
                    Tri(sub, idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], want);
                    Tri(sub, idx[i, j], idx[i + 1, j + 1], idx[i, j + 1], want);
                }
            }
        }

        /// <summary>Flat ring (disc when ri = 0) in the XY plane, <paramref name="thick"/> along Z.</summary>
        public void Annulus(int sub, Vector3 c, float ri, float ro, float thick, int seg)
        {
            float hz = thick * 0.5f;
            for (int k = 0; k < seg; k++)
            {
                float a0 = k / (float)seg * Mathf.PI * 2f, a1 = (k + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 P(float a, float r, float z) => c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
                var mid = new Vector3(Mathf.Cos((a0 + a1) * .5f), Mathf.Sin((a0 + a1) * .5f), 0f);
                var uv = Vector2.zero;
                Quad(sub, P(a0, ri, -hz), P(a0, ro, -hz), P(a1, ro, -hz), P(a1, ri, -hz), uv, uv, uv, uv, Vector3.back);
                Quad(sub, P(a0, ri, hz), P(a0, ro, hz), P(a1, ro, hz), P(a1, ri, hz), uv, uv, uv, uv, Vector3.forward);
                Quad(sub, P(a0, ro, -hz), P(a1, ro, -hz), P(a1, ro, hz), P(a0, ro, hz), uv, uv, uv, uv, mid);
                if (ri > 0.001f) Quad(sub, P(a0, ri, -hz), P(a1, ri, -hz), P(a1, ri, hz), P(a0, ri, hz), uv, uv, uv, uv, -mid);
            }
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_v);
            mesh.SetUVs(0, _u);
            mesh.SetColors(_c);
            mesh.subMeshCount = _t.Length;
            for (int i = 0; i < _t.Length; i++) mesh.SetTriangles(_t[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string path = $"{MeshDir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }

    // =================================================================== shared props

    private static readonly Dictionary<string, Mesh> PropMesh = new Dictionary<string, Mesh>();

    private static void FrameBar(MB mb, int sub, Vector2 a, Vector2 b, float width, float z)
    {
        var d = (b - a).normalized;
        var p = new Vector2(-d.y, d.x) * (width * 0.5f);
        var aa = new Vector3(a.x + p.x, a.y + p.y, z);
        var ab = new Vector3(a.x - p.x, a.y - p.y, z);
        var bb = new Vector3(b.x - p.x, b.y - p.y, z);
        var ba = new Vector3(b.x + p.x, b.y + p.y, z);
        var uv = Vector2.zero;
        mb.Quad(sub, aa, ab, bb, ba, uv, uv, uv, uv, Vector3.back);
        mb.Quad(sub, aa + Vector3.forward * 0.045f, ab + Vector3.forward * 0.045f,
                     bb + Vector3.forward * 0.045f, ba + Vector3.forward * 0.045f,
                     uv, uv, uv, uv, Vector3.forward);
    }

    /// <summary>Street furniture and window merchandise, built once and instanced everywhere.</summary>
    private static void BuildProps()
    {
        PropMesh.Clear();
        MB mb;

        mb = new MB(1);   // brass stanchion
        mb.Cylinder(0, Vector3.zero, 0.15f, 0.15f, 0.03f, 12);
        mb.Cylinder(0, new Vector3(0f, 0.03f, 0f), 0.022f, 0.022f, 0.92f, 8, false);
        mb.Ellipsoid(0, new Vector3(0f, 0.97f, 0f), Vector3.one * 0.045f, 8, 5, false);
        PropMesh["Stanchion"] = mb.Build("MapleRow_Prop_Stanchion");

        mb = new MB(2);   // door topiary: low stone pot + clipped ball, top <= 0.9 m (B1: was a 1.38 m ball)
        mb.Box(0, new Vector3(-0.25f, 0f, -0.25f), new Vector3(0.25f, 0.44f, 0.25f), 0.6f, Faces.All & ~Faces.Bottom);
        mb.Ellipsoid(1, new Vector3(0f, 0.665f, 0f), new Vector3(0.25f, 0.235f, 0.25f), 16, 10, false);
        PropMesh["Topiary"] = mb.Build("MapleRow_Prop_Topiary");

        mb = new MB(3);   // mannequin torso wearing a jersey (0 body, 1 chest band, 2 chrome stand)
        const float T0 = 1.05f;
        mb.Cylinder(2, Vector3.zero, 0.22f, 0.22f, 0.03f, 16);
        mb.Cylinder(2, new Vector3(0f, 0.03f, 0f), 0.022f, 0.022f, T0 - 0.02f, 8, false);
        var o = new Vector3(0f, T0, 0f);
        mb.Lathe(0, o, new[] { 0f, 0.1f, 0.3f }, new[] { 0.15f, 0.165f, 0.17f }, 0.55f, 16, true, false);
        mb.Lathe(1, o, new[] { 0.3f, 0.4f }, new[] { 0.17f, 0.18f }, 0.55f, 16, false, false);
        mb.Lathe(0, o, new[] { 0.4f, 0.52f, 0.6f, 0.66f, 0.7f, 0.74f, 0.84f }, new[] { 0.18f, 0.21f, 0.225f, 0.205f, 0.12f, 0.055f, 0.05f }, 0.55f, 16, false, true);
        PropMesh["Torso"] = mb.Build("MapleRow_Prop_Torso");

        mb = new MB(3);   // deep-section carbon wheel facing the street (0 tyre, 1 carbon rim + spokes, 2 hub accent)
        mb.Torus(0, Vector3.zero, 0.318f, 0.02f, 28, 5);
        mb.Annulus(1, Vector3.zero, 0.235f, 0.302f, 0.03f, 28);
        mb.Annulus(2, Vector3.zero, 0f, 0.036f, 0.07f, 12);
        for (int k = 0; k < 6; k++)
        {
            float a = k / 6f * Mathf.PI * 2f + 0.3f;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f); var perp = new Vector3(-dir.y, dir.x, 0f) * 0.011f;
            var p0 = dir * 0.036f; var p1 = dir * 0.24f; var uv = Vector2.zero;
            mb.Quad(1, p0 - perp, p1 - perp, p1 + perp, p0 + perp, uv, uv, uv, uv, Vector3.back);
            mb.Quad(1, p0 - perp, p1 - perp, p1 + perp, p0 + perp, uv, uv, uv, uv, Vector3.forward);
        }
        mb.Box(2, new Vector3(-0.012f, -0.012f, 0.03f), new Vector3(0.012f, 0.012f, 0.62f), 1f, Faces.All);
        PropMesh["Wheel"] = mb.Build("MapleRow_Prop_Wheel");

        mb = new MB(2);   // complete high-end road frameset silhouette for CARBONFORGE vitrines
        var ra = new Vector2(-0.43f, 0f); var fa = new Vector2(0.43f, 0f);
        var bb = new Vector2(-0.12f, 0.04f); var sc = new Vector2(-0.23f, 0.60f);
        var hb = new Vector2(0.29f, 0.43f); var ht = new Vector2(0.25f, 0.66f);
        FrameBar(mb, 0, bb, sc, 0.060f, 0f);     // seat tube
        FrameBar(mb, 0, sc, ht, 0.055f, 0f);     // top tube
        FrameBar(mb, 0, bb, hb, 0.085f, 0f);     // aero down tube
        FrameBar(mb, 0, hb, ht, 0.070f, 0f);     // head tube
        FrameBar(mb, 1, bb, ra, 0.038f, 0f);     // chainstay
        FrameBar(mb, 1, sc - Vector2.up * 0.16f, ra, 0.032f, 0f); // dropped seatstay
        FrameBar(mb, 1, ht, fa, 0.050f, 0f);     // fork
        FrameBar(mb, 1, new Vector2(-0.29f, 0.68f), new Vector2(-0.16f, 0.68f), 0.028f, 0f); // saddle
        PropMesh["Frame"] = mb.Build("MapleRow_Prop_Frame");

        mb = new MB(2);   // bike computer on a display stand (0 body, 1 lit screen)
        mb.Cylinder(0, Vector3.zero, 0.2f, 0.2f, 0.03f, 16);
        mb.Cylinder(0, new Vector3(0f, 0.03f, 0f), 0.03f, 0.03f, 1.22f, 8, false);
        {
            float tilt = 12f * Mathf.Deg2Rad;
            var n = new Vector3(0f, Mathf.Sin(tilt), -Mathf.Cos(tilt)); var up = new Vector3(0f, Mathf.Cos(tilt), Mathf.Sin(tilt));
            var c = new Vector3(0f, 1.52f, 0f);
            mb.Face(0, c + n * 0.018f, n, up, 0.48f, 0.27f, Vector2.zero, Vector2.one);
            mb.Face(0, c - n * 0.02f, -n, up, 0.48f, 0.27f, Vector2.zero, Vector2.one);
            mb.Face(1, c + n * 0.022f, n, up, 0.45f, 0.24f, Vector2.zero, Vector2.one);
        }
        PropMesh["ScreenStand"] = mb.Build("MapleRow_Prop_ScreenStand");

        mb = new MB(2);   // helmet plinth, unit height (0 body, 1 lit top)
        mb.Box(0, new Vector3(-0.25f, 0f, -0.25f), new Vector3(0.25f, 1f, 0.25f), 1f, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
        mb.Face(1, new Vector3(0f, 1f, 0f), Vector3.up, Vector3.forward, 0.25f, 0.25f, Vector2.zero, Vector2.one);
        PropMesh["Plinth"] = mb.Build("MapleRow_Prop_Plinth");

        mb = new MB(2);   // road helmet: shell + three dark vents along the crown
        mb.Ellipsoid(0, Vector3.zero, new Vector3(0.15f, 0.13f, 0.11f), 16, 7, true);
        for (int k = -1; k <= 1; k++)
        {
            float lat = 1.05f; var d = new Vector3(Mathf.Cos(lat) * k * 0.45f, Mathf.Sin(lat), 0f).normalized;
            var p = Vector3.Scale(d, new Vector3(0.15f, 0.13f, 0.11f));
            var nrm = new Vector3(p.x / 0.0225f, p.y / 0.0169f, 0f).normalized;
            mb.Face(1, p + nrm * 0.003f, nrm, Vector3.forward, 0.03f, 0.075f, Vector2.zero, Vector2.one);
        }
        PropMesh["Helmet"] = mb.Build("MapleRow_Prop_Helmet");

        mb = new MB(3);   // boutique lamp post with a Maple Row banner (0 iron, 1 lantern glow, 2 banner)
        mb.Box(0, new Vector3(-0.16f, 0f, -0.16f), new Vector3(0.16f, 0.45f, 0.16f), 1f, Faces.All & ~Faces.Bottom);
        mb.Cylinder(0, new Vector3(0f, 0.45f, 0f), 0.065f, 0.05f, 3.6f, 8, false);
        mb.Box(0, new Vector3(-0.2f, 4.02f, -0.2f), new Vector3(0.2f, 4.1f, 0.2f), 1f, Faces.All);
        mb.Box(1, new Vector3(-0.16f, 4.1f, -0.16f), new Vector3(0.16f, 4.58f, 0.16f), 1f, Faces.Front | Faces.Back | Faces.Left | Faces.Right);
        mb.Box(0, new Vector3(-0.25f, 4.58f, -0.25f), new Vector3(0.25f, 4.68f, 0.25f), 1f, Faces.All);
        mb.Box(0, new Vector3(-0.05f, 4.68f, -0.05f), new Vector3(0.05f, 4.9f, 0.05f), 1f, Faces.All & ~Faces.Bottom);
        foreach (float by in new[] { 2.76f, 3.86f })
            mb.Box(0, new Vector3(-0.02f, by, -0.62f), new Vector3(0.02f, by + 0.04f, -0.04f), 1f, Faces.All);
        mb.Face(2, new Vector3(0.012f, 3.325f, -0.36f), Vector3.right, Vector3.up, 0.26f, 0.525f, Vector2.zero, Vector2.one);
        mb.Face(2, new Vector3(-0.012f, 3.325f, -0.36f), Vector3.left, Vector3.up, 0.26f, 0.525f, Vector2.zero, Vector2.one);
        PropMesh["Lamp"] = mb.Build("MapleRow_Prop_Lamp");

        // Long planter parallel to the kerb. B1: the hedge was a 1.05 m green box that read as a
        // boxy wall at rider eye level; now a low trough under a row of rounded clipped box-balls
        // on a soft hedge base, top ~0.84 m.
        mb = new MB(2);
        mb.Box(0, new Vector3(-0.8f, 0f, -0.3f), new Vector3(0.8f, 0.46f, 0.3f), 1f, Faces.All & ~Faces.Bottom);
        mb.Ellipsoid(1, new Vector3(0f, 0.46f, 0f), new Vector3(0.74f, 0.20f, 0.25f), 20, 5, true);
        for (int k = 0; k < 4; k++)
        {
            float x = -0.54f + k * 0.36f, rh = (k == 1 || k == 2) ? 0.38f : 0.33f;
            mb.Ellipsoid(1, new Vector3(x, 0.46f, 0f), new Vector3(0.24f, rh, 0.24f), 14, 6, true);
        }
        PropMesh["Planter"] = mb.Build("MapleRow_Prop_Planter");

        mb = new MB(2);   // bench, sitter faces -Z (0 wood, 1 iron)
        mb.Box(0, new Vector3(-0.9f, 0.42f, -0.22f), new Vector3(0.9f, 0.48f, 0.22f), 1f, Faces.All);
        mb.Box(0, new Vector3(-0.9f, 0.56f, 0.18f), new Vector3(0.9f, 0.9f, 0.23f), 1f, Faces.All);
        foreach (float lx in new[] { -0.78f, 0.78f })
        {
            mb.Box(1, new Vector3(lx - 0.03f, 0f, -0.2f), new Vector3(lx + 0.03f, 0.42f, 0.2f), 1f, Faces.All & ~Faces.Bottom);
            mb.Box(1, new Vector3(lx - 0.03f, 0.42f, 0.18f), new Vector3(lx + 0.03f, 0.9f, 0.24f), 1f, Faces.All & ~Faces.Bottom);
        }
        PropMesh["Bench"] = mb.Build("MapleRow_Prop_Bench");

        mb = new MB(1);   // Sheffield bike hoop, perpendicular to the kerb
        mb.Box(0, new Vector3(-0.025f, 0f, -0.36f), new Vector3(0.025f, 0.8f, -0.31f), 1f, Faces.All & ~Faces.Bottom);
        mb.Box(0, new Vector3(-0.025f, 0f, 0.31f), new Vector3(0.025f, 0.8f, 0.36f), 1f, Faces.All & ~Faces.Bottom);
        mb.Box(0, new Vector3(-0.025f, 0.8f, -0.36f), new Vector3(0.025f, 0.86f, 0.36f), 1f, Faces.All);
        PropMesh["Hoop"] = mb.Build("MapleRow_Prop_Hoop");

        mb = new MB(2);   // street-name totem, readable along the street (faces +-X)
        mb.Box(0, new Vector3(-0.2f, 0f, -0.45f), new Vector3(0.2f, 3.4f, 0.45f), 1f, Faces.All & ~Faces.Bottom);
        mb.Face(1, new Vector3(0.202f, 1.75f, 0f), Vector3.right, Vector3.up, 0.4f, 1.55f, Vector2.zero, Vector2.one);
        mb.Face(1, new Vector3(-0.202f, 1.75f, 0f), Vector3.left, Vector3.up, 0.4f, 1.55f, Vector2.zero, Vector2.one);
        PropMesh["Totem"] = mb.Build("MapleRow_Prop_Totem");
    }

    // =================================================================== materials

    private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
    private static readonly Color DefaultShade = new Color(0.44f, 0.50f, 0.68f, 1f);
    /// <summary>MapleCityEnvironment.GroundShade - the cool, non-violet shadow grey floors need.</summary>
    private static readonly Color GroundShade = new Color(0.380f, 0.505f, 0.640f, 1f);

    private static Texture2D Tex(string file)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{file}");
        if (t == null) Debug.LogError($"[maple-row] missing texture {TexDir}/{file} - run: python tools/blender/build_maple_row_textures.py");
        return t;
    }

    private static Material Load(string name, Shader shader)
    {
        if (Mats.TryGetValue(name, out var cached) && cached != null) return cached;
        string path = $"{MatDir}/MapleRow_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.name = $"MapleRow_{name}";
        mat.enableInstancing = true;
        Mats[name] = mat;
        return mat;
    }

    private static Material CelMat(string name, Color col, float gloss, float spec, Texture tex = null, Color? shade = null)
    {
        bool fresh = !Mats.ContainsKey(name);
        var mat = Load(name, MapleRideShaderNames.Find("MapleRide/SakuraCel"));
        if (!fresh) return mat;
        mat.SetColor("_Color", col);
        mat.SetColor("_ShadeColor", shade ?? DefaultShade);
        mat.SetFloat("_ShadeStrength", 0.6f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.08f);
        mat.SetColor("_RimColor", new Color(1f, 0.78f, 0.52f, 1f));
        mat.SetFloat("_RimStrength", 0.18f);
        mat.SetFloat("_Gloss", gloss);
        mat.SetFloat("_SpecStrength", spec);
        mat.SetFloat("_ShadowAmbient", 0.36f);
        mat.SetFloat("_ShadowSoft", 0.14f);
        if (mat.HasProperty("_TintVariation")) mat.SetFloat("_TintVariation", 0.03f);
        if (mat.HasProperty("_WeatherAmount")) mat.SetFloat("_WeatherAmount", 0f);
        mat.SetTexture("_MainTex", tex);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material CelTexMat(string name, string file, Color tint) =>
        CelMat(name, tint, 0.14f, 0.08f, Tex(file), GroundShade);

    private static Material KitMat(Color c) => CelMat("Kit_" + ColorUtility.ToHtmlStringRGB(c), c, 0.28f, 0.18f);

    /// <summary>
    /// Self-lit surfaces (signs, window interiors, lamps). HDRP/Unlit's base colour is NOT
    /// exposure-scaled, so these read at their authored value in every region grade.
    /// </summary>
    private static Material Unlit(string name, Texture tex, Color col)
    {
        bool fresh = !Mats.ContainsKey(name);
        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Texture");
        var mat = Load(name, shader);
        if (!fresh) return mat;
        if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", col);
        if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        try { HDMaterial.ValidateMaterial(mat); } catch (Exception e) { Debug.LogWarning($"[maple-row] ValidateMaterial {name}: {e.Message}"); }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material UnlitTex(string name, float k) => Unlit($"U_{name}", Tex($"MapleRow_{name}.png"), new Color(k, k, k, 1f));
    private static Material UnlitColor(string name, Color col) => Unlit(name, null, col);

    /// <summary>Brand cladding: the city's continuous-light facade shader, no vertex tint, no normal map.</summary>
    private static Material FacadeMat(Brand br)
    {
        string name = $"Facade_{br.Id}";
        bool fresh = !Mats.ContainsKey(name);
        var mat = Load(name, MapleRideShaderNames.Find("MapleRide/MapleCityFacade"));
        if (!fresh) return mat;
        mat.SetColor("_Color", new Color(br.FacadeTint, br.FacadeTint, br.FacadeTint, 1f));
        mat.SetTexture("_MainTex", Tex($"MapleRow_Facade_{br.Id}.png"));
        mat.SetFloat("_BumpStrength", 0f);
        mat.SetFloat("_Glossiness", br.FacadeGloss);
        mat.SetFloat("_Metallic", br.FacadeMetal);
        mat.SetFloat("_Occlusion", 1f);
        mat.SetFloat("_VertexTint", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>The district's own facade material (vertex-tinted), so upper storeys match the neighbours.</summary>
    private static Material CityFacadeMat()
    {
        const string key = "CityFacade";
        if (Mats.TryGetValue(key, out var m) && m != null) return m;
        m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/MapleCity/Materials/MapleCity_Facade.mat");
        if (m == null)
        {
            m = Load("UpperFacade", MapleRideShaderNames.Find("MapleRide/MapleCityFacade"));
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Environment/MapleCity/Textures/MapleCity_Facade_Albedo.png"));
            m.SetFloat("_VertexTint", 1f);
            m.SetFloat("_BumpStrength", 0f);
        }
        Mats[key] = m;
        return m;
    }

    /// <summary>Shared street-furniture materials by role.</summary>
    private static Material[] M(params string[] roles)
    {
        var list = new Material[roles.Length];
        for (int i = 0; i < roles.Length; i++)
            list[i] = roles[i] switch
            {
                "Brass" => CelMat("Brass", Hex("#B8893F"), 0.8f, 0.8f),
                "Chrome" => CelMat("Chrome", Hex("#C9CED6"), 0.9f, 0.9f),
                "Planter" => CelMat("Planter", Hex("#3A3B3F"), 0.25f, 0.12f),
                "Hedge" => CelMat("Hedge", Hex("#3F6B35"), 0.08f, 0.04f),
                "Iron" => CelMat("Iron", Hex("#1C1E21"), 0.5f, 0.3f),
                "Wood" => CelMat("Wood", Hex("#5E4030"), 0.3f, 0.15f),
                "PlinthWhite" => CelMat("PlinthWhite", Hex("#F2F2F0"), 0.3f, 0.15f),
                "VentDark" => CelMat("VentDark", Hex("#1A1B1E"), 0.3f, 0.2f),
                "TotemBody" => CelMat("TotemBody", Hex("#1D1F24"), 0.4f, 0.25f),
                "Banner" => CelMat("Banner", Color.white, 0.1f, 0.05f, Tex("MapleRow_Banner.png")),
                "Glow_Lamp" => UnlitColor("Glow_Lamp", new Color(1.1f, 0.86f, 0.56f)),
                "PlinthGlow" => UnlitColor("PlinthGlow", new Color(1.3f, 1.26f, 1.14f)),
                "Totem" => UnlitTex("Totem", 1.0f),
                _ => throw new ArgumentException(roles[i]),
            };
        return list;
    }

    /// <summary>Imports the PIL textures with signage-friendly settings (clamped, anisotropic).</summary>
    private static void PrepareTextures()
    {
        if (!Directory.Exists(TexDir) || Directory.GetFiles(TexDir, "MapleRow_*.png").Length == 0)
            Debug.LogError("[maple-row] no textures - run: python tools/blender/build_maple_row_textures.py");
        AssetDatabase.Refresh();
        foreach (var file in Directory.GetFiles(TexDir, "MapleRow_*.png"))
        {
            string path = file.Replace('\\', '/');
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            string n = Path.GetFileNameWithoutExtension(path);
            bool tiles = n.StartsWith("MapleRow_Facade_") || n == "MapleRow_Paving" || n == "MapleRow_Carpet";
            var wrap = tiles ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (imp.wrapMode == wrap && imp.anisoLevel == 8 && imp.sRGBTexture && imp.mipmapEnabled) continue;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = true;
            imp.wrapMode = wrap;
            imp.anisoLevel = 8;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
        }
    }

    // =================================================================== play-mode capture

    private const string CapturePref = "mapleride.maplerow.capture";
    private const string CaptureDir = "../reference/copilot/maple_row";
    private static IEnumerator _routine;
    private static int _waitFrames, _lastFrame = -1;
    private static double _deadline;
    private static bool _captureFailed;

    /// <summary>
    /// Rider's-eye frames of the street in play mode (real HDRP grade, region ambience, rider).
    /// Run WITHOUT -quit: run_steps.ps1 "MapleRowBoutiques.Capture|copilot_c6_cap.log|0".
    /// Frames: reference/copilot/maple_row/mr_*.png
    /// </summary>
    [MenuItem("MapleRide/QA/Capture Maple Row (Play Mode)")]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(CapturePref, true);
        Debug.Log("[maple-row-cap] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(CapturePref, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            _captureFailed = false;
            _routine = CaptureRoutine();
            _deadline = EditorApplication.timeSinceStartup + 600.0;
            EditorApplication.update += Tick;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(CapturePref, false);
            EditorApplication.update -= Tick;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log($"[maple-row-cap] left play mode ({(_captureFailed ? "FAILED" : "ok")}).");
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(_captureFailed ? 1 : 0);
        }
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[maple-row-cap] TIMED OUT");
            _captureFailed = true;
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
            return;
        }
        if (Time.frameCount == _lastFrame) return;
        _lastFrame = Time.frameCount;
        if (_waitFrames-- > 0) return;
        bool more;
        try { more = _routine.MoveNext(); }
        catch (Exception e) { Debug.LogException(e); _captureFailed = true; more = false; }
        if (!more)
        {
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
            return;
        }
        _waitFrames = _routine.Current is int n ? n : 0;
    }

    private static IEnumerator CaptureRoutine()
    {
        var boot = UnityEngine.Object.FindAnyObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[maple-row-cap] no RideBootstrap"); _captureFailed = true; yield break; }
        boot.Resolve();
        var cam = boot.rideCamera != null ? boot.rideCamera : Camera.main;
        if (cam == null) { Debug.LogError("[maple-row-cap] no ride camera"); _captureFailed = true; yield break; }
        yield return 2;

        if (boot.regions == null || !boot.regions.FastTravel(RegionCatalog.MapleCity))
            Debug.LogWarning("[maple-row-cap] could not fast travel to Maple City.");
        boot.session.autoLapsFromTarget = false;
        boot.session.plannedLaps = 1;
        boot.session.SelectCourse("maple_city_crit");
        boot.devices.acceptKeyboardEffort = false;
        yield return 10;

        var group = GameObject.Find(GroupName);
        Debug.Log($"[maple-row-cap] '{GroupName}' {(group == null ? "NOT FOUND" : group.activeInHierarchy ? "active" : "INACTIVE")}.");
        if (group == null || !group.activeInHierarchy) _captureFailed = true;

        var st = new Street(CityRoute.Load());
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, CaptureDir));
        Directory.CreateDirectory(dir);
        float mid = (StreetStartM + StreetEndM) * 0.5f, half = st.Half(mid);

        // Chase camera down the street, rider in frame.
        foreach (float d in new[] { 425f, 500f, 585f, 665f })
        {
            boot.session.SeekTo(d);
            if (boot.follower != null) boot.follower.Apply();
            for (int f = 0; f < 45; f++) { boot.devices.EffortInput = 0.35f; SnapChase(cam); yield return 1; }
            SnapChase(cam);
            Shot(cam, dir, $"mr_chase_{d:000}m");
        }

        // Park the rider well behind the street, then photograph every flagship.
        boot.session.SeekTo(StreetStartM - 120f);
        if (boot.follower != null) boot.follower.Apply();
        yield return 20;
        float fov = cam.fieldOfView;
        foreach (var br in Brands)
        {
            var store = group != null ? group.transform.Find($"{(br.Side > 0 ? "Right Side (inside loop)" : "Left Side (far canal bank)")}/{br.Id} Flagship") : null;
            if (store == null) { Debug.LogError($"[maple-row-cap] {br.Id} Flagship missing"); _captureFailed = true; continue; }
            st.Project(store.position, out float s, out _);
            float y = st.PavTop(s);
            // Straight on, standing in the far lane.
            cam.fieldOfView = br.Side > 0 ? 62f : 56f;
            Aim(cam, st.At(s, -br.Side * 2.6f, y + 1.7f), store.position + Vector3.up * 4.6f);
            Shot(cam, dir, $"mr_front_{br.Id}");
            // The rider's view: from the riding lane, 18 m before the door.
            cam.fieldOfView = 58f;
            Aim(cam, st.At(s - 18f, -1.7f, y + 1.55f), store.position + Vector3.up * 3.2f);
            Shot(cam, dir, $"mr_ride_{br.Id}");
            yield return 1;
        }

        cam.fieldOfView = 55f;
        Aim(cam, st.At(StreetStartM - 25f, 0f, st.PavTop(StreetStartM) + 24f), st.At(mid + 20f, 0f, st.PavTop(mid)));
        Shot(cam, dir, "mr_overview");
        Aim(cam, st.At(mid - 37f, half + 2.4f, st.PavTop(mid) + 1.7f), st.At(mid + 5f, -st.Front(mid, -1), st.PavTop(mid) + 4.5f));
        Shot(cam, dir, "mr_canal_view");
        Aim(cam, st.At(mid - 40f, -(half + 1.5f), st.PavTop(mid) + 1.7f), st.At(mid, -(half + 8f), st.PavTop(mid) + 1.2f));
        Shot(cam, dir, "mr_left_promenade");
        cam.fieldOfView = fov;
        Debug.Log($"[maple-row-cap] done -> {dir}");
    }

    private static void SnapChase(Camera cam)
    {
        var follow = cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null) return;
        var t = follow.target;
        cam.transform.position = t.position + t.TransformDirection(follow.offset);
        cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    private static void Aim(Camera cam, Vector3 eye, Vector3 look)
    {
        cam.transform.position = eye;
        cam.transform.LookAt(look);
    }

    private static void Shot(Camera cam, string dir, string name)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        string path = Path.Combine(dir, name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        Debug.Log($"[maple-row-cap] wrote {path}");
    }
}
