using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Minato Coast redesign workstream: SHORES. See MINATO_VISUAL_DESIGN.md at the project root and
/// the concept targets in design_assets/concepts/ (SeawallSprint, MarinaRibbon,
/// StormglassCauseway, BeyondTheHorizon). Owned by one workstream only; called from
/// MinatoCoastEnvironment.Apply().
///
/// What this builds (all placement is DATA-DRIVEN from the route-metre ranges below, so the lead
/// can re-map zones by editing constants, not code):
///   * Causeway body   - the solid concrete/rock embankment under the sea-level road ribbon
///                       (SeaCrossStartM .. BridgeStartM), so no gap ever shows under the edges.
///   * Seawall Sprint  - concrete parapet + brushed-metal railing on the sea side, tetrapod and
///                       armour-stone revetment with breaking surf, pampas, wind-bent pines, a
///                       stepped terrace with the stainless ring sculpture.
///   * Marina Ribbon   - quay edge with bollards + rope, gangways, pontoons with moored sailing and
///                       motor yachts on the sea side; a reclaimed quay platform on the land side
///                       with the marina cafe, boat hoist (travel lift), boathouse and white/navy
///                       apartments.
///   * Stormglass Causeway - parapet/railing and pampas verges both sides, tetrapod + armour
///                       revetments with surf both sides, red/green harbour lights on rock spurs.
///   * Beyond the Horizon - coastal landfall town (white/navy buildings, coastal pines) with the
///                       hero lighthouse on its own promontory, then the headland climb dressed as
///                       coastal scrub, grass, pampas, wind-bent pines and stratified cliff rock,
///                       ending at a lookout terrace + headland lighthouse at the finish.
///
/// Assets: tools/blender/build_minato_shores.py -> Assets/Environment/MinatoCoast/Models/
/// Minato_Shore_* / Minato_Marina_*. No Sakura Pass assets are used anywhere in this file.
/// </summary>
public static partial class MinatoCoastEnvironment
{
    /// <summary>Flip to true once this workstream's replacement is verified in gameplay captures;
    /// Apply() then skips the legacy builders it replaces.</summary>
    private const bool ShoresReplacesLegacy = true;

    // ================================================================ zone map (route metres)
    // UPDATE 2 zone map. Everything below keys off these; re-map zones HERE.
    private const float ShoreSeawallFromM = 1800f;
    private const float ShoreSeawallToM = 2600f;
    private const float ShoreMarinaFromM = 2600f;
    private const float ShoreMarinaToM = 3600f;
    private const float ShoreCausewayFromM = 3600f;
    private const float ShoreCausewayToM = 7400f;
    /// <summary>Embankment body under the road: from where the road leaves land to where the
    /// elevated bridge structure takes over (matches SeaCrossStartM .. BridgeStartM).</summary>
    private const float ShoreBodyFromM = SeaCrossStartM;
    private const float ShoreBodyToM = BridgeStartM + 20f;
    private const float ShoreLandfallFromM = BridgeEndM;       // far-shore coastal town
    private const float ShoreLandfallToM = 13400f;
    private const float ShoreHeadlandFromM = 13400f;           // headland climb to the finish
    /// <summary>Route metres where red/green harbour lights stand on short rock spurs.</summary>
    private static readonly float[] ShoreHarbourLightsM = { 3640f, 5520f, 7330f };
    /// <summary>Stepped terrace + ring sculpture (landward side of the Seawall Sprint).</summary>
    private const float ShoreRingTerraceM = 2470f;
    /// <summary>Lookout terrace + headland lighthouse near the finish.</summary>
    private const float ShoreLookoutM = 18935f;
    private const float ShoreFinishLighthouseM = 18985f;
    /// <summary>Hero lighthouse promontory on the far-shore coast (legacy replacement).</summary>
    private const float ShoreHeroLighthouseM = 11650f;

    // ================================================================ cross-section (metres)
    // Measured against the road ribbon: carriageway half-width 4.0 + shoulder 0.55 = 4.55 m.
    private const float ShoreDeckDropM = 0.22f;       // body top sits just under the road edge
    private const float ShoreParapetOffsetM = 6.55f;  // landward face of the seawall parapet
    private const float ShoreBodyHalfWidthM = 7.2f;   // outer edge of the embankment top
    private const float ShoreFaceDropM = 1.4f;        // concrete face below the top, then armour
    private const float ShoreArmourSlope = 1.5f;      // horizontal : vertical of the revetment
    private const float ShoreArmourToeY = -7f;        // hidden under the (opaque) sea surface
    private const float ShoreQuayBottomY = -4f;       // vertical marina quay wall
    private const float ShoreMarinaPlatformM = 36f;   // land-side quay platform reach

    // ================================================================ entry

    private static void RedesignShores(MinatoRoute route, Transform[] chapters)
    {
        _shoreBatches.Clear();
        _shorePlaced = 0;
        _shoreInstanced = 0;
        _shoreSlotMap = null;
        ShoreMatCache.Clear();
        ShoreLoadModels();
        var root = new GameObject("Shores Redesign").transform;
        root.SetParent(chapters[1], false);
        var inst = new GameObject("Shores Instanced Cover").transform;
        inst.SetParent(root, false);

        if (ShoresReplacesLegacy) ShorePruneLegacy(route, chapters);

        BuildShoreCausewayBody(route, root);
        BuildShoreSeawallSprint(route, root);
        BuildShoreMarinaRibbon(route, root);
        BuildShoreStormglassCauseway(route, root);

        var far = new GameObject("Shores Beyond the Horizon").transform;
        far.SetParent(chapters[4], false);
        var shell = ShoreShellBegin(chapters[4]);
        BuildShoreLandfallTown(route, far);
        BuildShoreHeadland(route, far);
        ShoreShellEnd(shell);

        int batches = ShoreFlush(inst);
        Debug.Log($"[shores] built: {_shorePlaced:N0} placed props (LODGroup GameObjects), " +
                  $"{_shoreInstanced:N0} GPU-instanced transforms in {batches:N0} batches");
    }

    // ================================================================ legacy pruning
    //
    // INTERIM: the legacy per-chapter scatter (ScatterDressing), verge (VergeDressing) and the
    // hero-mountain tree/rock pass (DressHeroMountains) live in MinatoCoastEnvironment.cs, which
    // this workstream may not edit. They place Sakura Pass GLB pines/broadleaf/shrubs/grass/fern/
    // flower/rock clusters and Shiosai houses across the Shores zones. Until the lead gates those
    // loops at source (exact lines listed in the Shores report) they are removed here, after the
    // fact, so the replacement can be verified. Once gated at source this is a no-op.

    private static void ShorePruneLegacy(MinatoRoute route, Transform[] chapters)
    {
        int removed = 0;
        bool InShores(Vector3 pos)
        {
            NearestLand(route, pos.x, pos.z, out int idx);
            if (idx < 0) return false;
            float d = route.Distance[idx];
            return (d >= ShoreSeawallFromM && d <= ShoreBodyToM) || d >= ShoreLandfallFromM;
        }
        void PruneChildren(Transform t, Func<Transform, bool> test)
        {
            if (t == null) return;
            for (int c = t.childCount - 1; c >= 0; c--)
            {
                var ch = t.GetChild(c);
                if (!test(ch)) continue;
                UnityEngine.Object.DestroyImmediate(ch.gameObject);
                removed++;
            }
        }
        // ScatterDressing: chapters 2/3/5 scatter lands in chapters[4]/Flora; chapter 4 in
        // chapters[3]/Settlement (Shiosai houses + Sakura trees across the far shore).
        PruneChildren(chapters[4].Find("Flora"), ch => InShores(ch.position));
        PruneChildren(chapters[3].Find("Settlement"), ch => true);
        // DressHeroMountains: 900 Sakura/Shiosai trees + 220 Sakura rock clusters on the shell.
        PruneChildren(chapters[4], ch => ch.name.StartsWith("Hero Mountain Tree", StringComparison.Ordinal) ||
                                         ch.name.StartsWith("Hero Mountain Rock", StringComparison.Ordinal));
        // VergeDressing now only runs beyond BridgeEndM - i.e. entirely inside Beyond the Horizon -
        // and is built from Sakura grass/fern/flower/shrub/rock/pine families: remove it whole.
        var verge = chapters[1].Find("Verge Dressing");
        if (verge != null) { UnityEngine.Object.DestroyImmediate(verge.gameObject); removed++; }
        Debug.Log($"[shores] pruned {removed:N0} legacy Sakura/Shiosai scatter roots from the Shores zones");
    }

    // ================================================================ materials

    private static (string token, Material mat)[] _shoreSlotMap;
    private static readonly Dictionary<string, Material> ShoreMatCache = new Dictionary<string, Material>();

    private static Material ShoreMat(string slot)
    {
        if (ShoreMatCache.TryGetValue(slot, out var m) && m != null) return m;
        switch (slot)
        {
            case "white": m = CelMaterial("Minato_Shore_White", new Color(0.91f, 0.91f, 0.88f), 0.16f, 0.10f, 0.30f); break;
            case "trim": m = CelMaterial("Minato_Shore_Trim", new Color(0.84f, 0.83f, 0.78f), 0.14f, 0.08f, 0.28f); break;
            case "navy": m = CelMaterial("Minato_Shore_Navy", new Color(0.13f, 0.21f, 0.38f), 0.22f, 0.14f, 0.34f); break;
            case "blue": m = CelMaterial("Minato_Shore_Blue", new Color(0.16f, 0.42f, 0.78f), 0.24f, 0.16f, 0.34f); break;
            case "coral": m = CelMaterial("Minato_Shore_Coral", new Color(0.96f, 0.46f, 0.33f), 0.16f, 0.10f, 0.36f); break;
            case "red": m = CelMaterial("Minato_Shore_HarbourRed", new Color(0.84f, 0.17f, 0.13f), 0.22f, 0.14f, 0.36f); break;
            case "green": m = CelMaterial("Minato_Shore_HarbourGreen", new Color(0.09f, 0.55f, 0.36f), 0.22f, 0.14f, 0.36f); break;
            case "glass": m = CelMaterial("Minato_Shore_Glass", new Color(0.24f, 0.46f, 0.56f), 0.72f, 0.60f, 0.48f, null, GroundShade); break;
            case "metal": m = CelMaterial("Minato_Shore_BrushedMetal", new Color(0.80f, 0.82f, 0.85f), 0.62f, 0.55f, 0.52f, null, GroundShade); break;
            case "darkmetal": m = CelMaterial("Minato_Shore_DarkMetal", new Color(0.20f, 0.21f, 0.23f), 0.34f, 0.26f, 0.30f, null, GroundShade); break;
            case "concrete": m = ConcreteMaterial(); break;
            case "tetra":
                m = CelMaterial("Minato_Shore_Tetrapod", new Color(0.84f, 0.83f, 0.80f), 0.10f, 0.06f, 0.34f,
                                Tex(TakaTex, "Taka_Concrete_Albedo.png"), GroundShade);
                break;
            case "rock":
                m = CelMaterial("Minato_Shore_Rock", new Color(0.70f, 0.67f, 0.62f), 0.10f, 0.06f, 0.30f,
                                Tex(ShiosaiTex, "Shiosai_Rock_Albedo.png"), GroundShade);
                break;
            case "teak": m = CelMaterial("Minato_Shore_Teak", new Color(0.64f, 0.47f, 0.32f), 0.14f, 0.08f, 0.28f); break;
            case "rope": m = CelMaterial("Minato_Shore_Rope", new Color(0.93f, 0.89f, 0.79f), 0.10f, 0.06f, 0.30f); break;
            case "scrub": m = CelMaterial("Minato_Shore_Scrub", new Color(0.34f, 0.50f, 0.28f), 0.08f, 0.04f, 0.40f); break;
            case "grass": m = CelMaterial("Minato_Shore_Grass", new Color(0.60f, 0.64f, 0.36f), 0.08f, 0.04f, 0.42f); break;
            case "plume": m = CelMaterial("Minato_Shore_Plume", new Color(0.96f, 0.91f, 0.77f), 0.08f, 0.04f, 0.50f); break;
            case "foam": m = CelMaterial("Minato_Shore_Foam", new Color(0.97f, 0.99f, 1.00f), 0.20f, 0.10f, 0.60f); break;
            case "hazard": m = CelMaterial("Minato_Shore_Hazard", new Color(0.96f, 0.76f, 0.12f), 0.20f, 0.12f, 0.30f); break;
            case "lamp": m = CelMaterial("Minato_Shore_LampLens", new Color(1.00f, 0.94f, 0.78f), 0.60f, 0.50f, 0.60f); break;
            case "soil": m = CelMaterial("Minato_Shore_Soil", new Color(0.34f, 0.27f, 0.20f), 0.06f, 0.03f, 0.20f); break;
            case "canvas": m = CelMaterial("Minato_Shore_Canvas", new Color(0.94f, 0.93f, 0.89f), 0.08f, 0.05f, 0.30f); break;
            case "rubber": m = CelMaterial("Minato_Shore_Rubber", new Color(0.09f, 0.09f, 0.10f), 0.20f, 0.10f, 0.20f); break;
            case "paving":
                m = CelMaterial("Minato_Shore_Paving", new Color(0.80f, 0.79f, 0.75f), 0.10f, 0.06f, 0.22f,
                                Tex(MapleTex, "MapleCity_Pavement_Albedo.png"), GroundShade);
                break;
            case "needle":
                m = FoliageMaterial("Minato_Shore_PineNeedle", new Color(0.80f, 0.90f, 0.74f),
                                    Tex(ShiosaiTex, "Shiosai_Needle_Albedo.png"), 0.26f);
                break;
            case "bark":
                m = CelMaterial("Minato_Shore_PineBark", new Color(0.92f, 0.88f, 0.84f), 0.10f, 0.05f, 0.38f,
                                Tex(ShiosaiTex, "Shiosai_Bark_Albedo.png"));
                break;
            default: m = CelMaterial("Minato_Shore_White", new Color(0.91f, 0.91f, 0.88f), 0.16f, 0.10f, 0.30f); break;
        }
        m.enableInstancing = true;
        ShoreMatCache[slot] = m;
        return m;
    }

    private static readonly string[] ShoreSlots =
    {
        "darkmetal", "metal", "white", "trim", "navy", "blue", "coral", "glass", "concrete",
        "tetra", "rock", "teak", "rope", "scrub", "grass", "plume", "foam", "hazard", "lamp",
        "soil", "canvas", "rubber",
    };

    /// <summary>Imported slot "Minato_Shores_shw_&lt;slot&gt;" -> Shores cel material.</summary>
    private static (string token, Material mat)[] ShoreSlotMap(string towerPaint = "red")
    {
        if (towerPaint == "red" && _shoreSlotMap != null) return _shoreSlotMap;
        var list = new List<(string, Material)>();
        list.Add(("shw_towerpaint", ShoreMat(towerPaint)));
        foreach (var s in ShoreSlots) list.Add(($"shw_{s}", ShoreMat(s)));
        var arr = list.ToArray();
        if (towerPaint == "red") _shoreSlotMap = arr;
        return arr;
    }

    private static Material ShoreLookup((string token, Material mat)[] map, Material imported)
    {
        string n = imported != null ? imported.name.ToLowerInvariant() : "";
        foreach (var (token, mat) in map)
            if (n.Contains(token)) return mat;
        return ShoreMat("white");
    }

    // ================================================================ placement helpers

    private static int _shorePlaced, _shoreInstanced;

    /// <summary>A Shores prop as a real GameObject (LODGroup from the FBX ladder, cel materials by slot).</summary>
    private static GameObject ShorePlace(GameObject src, Transform parent, Vector3 pos, Quaternion rot,
                                         Vector3 scale, string towerPaint = "red")
    {
        if (src == null) return null;
        var go = Inst(src, parent, Vector3.zero, Quaternion.identity, null);
        RetintBySlot(go, ShoreSlotMap(towerPaint), ShoreMat("white"));
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.isStatic = true;
        _shorePlaced++;
        return go;
    }

    private static GameObject ShorePlace(GameObject src, Transform parent, Vector3 pos, Quaternion rot,
                                         float scale = 1f)
        => ShorePlace(src, parent, pos, rot, Vector3.one * scale);

    /// <summary>A reused Shiosai coastal black pine (LOD ladder) with the Shores needle/bark.</summary>
    private static GameObject ShorePine(GameObject src, Transform parent, Vector3 pos, float yaw, float scale)
    {
        if (src == null) return null;
        var go = Inst(src, parent, Vector3.zero, Quaternion.identity, ShoreMat("needle"), ShoreMat("bark"));
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        go.isStatic = true;
        _shorePlaced++;
        return go;
    }

    private sealed class ShoreBatch
    {
        public Mesh mesh; public int sub; public Material mat;
        public readonly List<Matrix4x4> m = new List<Matrix4x4>();
        public Bounds b; public bool has; public float dist; public bool shadows; public string label;
    }

    private static readonly Dictionary<string, ShoreBatch> _shoreBatches = new Dictionary<string, ShoreBatch>();

    /// <summary>
    /// Queue one GPU-instanced copy of a small repeated Shores prop (tetrapods, armour, surf,
    /// grass, pampas, scrub). Batches are keyed per 200 m route cell so the existing
    /// MinatoInstancedBatch frustum/distance cull works per cell.
    /// </summary>
    private static void ShoreQueue(GameObject src, Vector3 pos, Quaternion rot, Vector3 scale,
                                   float routeM, float maxDist, bool shadows)
    {
        if (src == null) return;
        int cell = Mathf.FloorToInt(routeM / 200f);
        var map = ShoreSlotMap();
        var root = Matrix4x4.TRS(pos, rot, scale);
        foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.name.Contains("_LOD") && !mr.name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var matrix = root * (src.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix);
            int subs = Mathf.Max(1, mf.sharedMesh.subMeshCount);
            for (int s = 0; s < subs; s++)
            {
                var imported = mr.sharedMaterials.Length == 0 ? null
                             : mr.sharedMaterials[Mathf.Min(s, mr.sharedMaterials.Length - 1)];
                var mat = ShoreLookup(map, imported);
                string key = $"{cell}|{mf.sharedMesh.GetInstanceID()}|{s}|{mat.GetInstanceID()}";
                if (!_shoreBatches.TryGetValue(key, out var b))
                {
                    b = new ShoreBatch { mesh = mf.sharedMesh, sub = s, mat = mat, dist = maxDist,
                                         shadows = shadows, label = $"{src.name}_{cell:D3}" };
                    _shoreBatches.Add(key, b);
                }
                b.m.Add(matrix);
                var wb = TransformBounds(mf.sharedMesh.bounds, matrix);
                if (b.has) b.b.Encapsulate(wb); else { b.b = wb; b.has = true; }
            }
        }
        _shoreInstanced++;
    }

    private static int ShoreFlush(Transform parent)
    {
        int n = 0;
        foreach (var kv in _shoreBatches)
        {
            var b = kv.Value;
            if (b.m.Count == 0) continue;
            var go = new GameObject($"Shore_{b.label}_{n:D4}");
            go.transform.SetParent(parent, false);
            var draw = go.AddComponent<MinatoInstancedBatch>();
            draw.mesh = b.mesh;
            draw.subMeshIndex = b.sub;
            draw.material = b.mat;
            draw.matrices = b.m.ToArray();
            draw.worldBounds = b.b;
            draw.maxDistanceM = b.dist;
            draw.castShadows = b.shadows;
            go.isStatic = true;
            n++;
        }
        _shoreBatches.Clear();
        return n;
    }

    private static Quaternion ShoreYaw(System.Random rng) =>
        Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

    private static float ShoreRand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Unit forward (flat) at a route distance, from the chord to the next few metres.</summary>
    private static Vector3 ShoreForward(MinatoRoute route, float d)
    {
        var a = route.Position[route.IndexAt(d)];
        var b = route.Position[route.IndexAt(Mathf.Min(d + 6f, route.Length - 0.5f))];
        var f = b - a; f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) { f = route.Tangent[route.IndexAt(d)]; f.y = 0f; }
        return f.normalized;
    }

    /// <summary>Clearance from EVERY station of the route (land or sea), for placements near
    /// switchbacks or the causeway where NearestLand does not see the sea stations.</summary>
    private static bool ShoreClearOfRoad(MinatoRoute route, Vector3 q, float clearM, float nearM)
    {
        int i0 = route.IndexAt(Mathf.Max(0f, nearM - 700f));
        int i1 = route.IndexAt(Mathf.Min(route.Length, nearM + 700f));
        float c2 = clearM * clearM;
        for (int i = i0; i <= i1; i++)
        {
            float dx = route.Position[i].x - q.x, dz = route.Position[i].z - q.z;
            if (dx * dx + dz * dz < c2) return false;
        }
        // Far-away legs of the switchback climb can double back past this point too.
        return NearestLand(route, q.x, q.z, out _) >= clearM;
    }

    // ================================================================ causeway body

    /// <summary>Per-side edge treatment of the embankment.</summary>
    private enum ShoreEdge { Armour, Quay }

    private static ShoreEdge ShoreEdgeAt(float d, int s, MinatoRoute route, int i)
    {
        // The marina is quay-walled on BOTH sides: pontoons on the sea side, the reclaimed quay
        // platform (its own outer wall) on the land side.
        return d >= ShoreMarinaFromM && d < ShoreMarinaToM ? ShoreEdge.Quay : ShoreEdge.Armour;
    }

    /// <summary>
    /// The solid embankment under the sea-level road: a concrete top that runs just under the
    /// road ribbon out to the parapet, a short concrete face, then a rock-armour revetment at
    /// 1:1.5 down below the sea surface (or a vertical quay wall along the marina). Chunked for
    /// culling like the road. No collider - the road ribbon and ride boundary already carry
    /// physics, and nothing else ever touches this.
    /// </summary>
    private static void BuildShoreCausewayBody(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Causeway Body").transform;
        parent.SetParent(root, false);
        var concrete = ShoreMat("concrete");
        var rock = ShoreMat("rock");
        int i0 = route.IndexAt(ShoreBodyFromM), i1 = route.IndexAt(Mathf.Min(ShoreBodyToM, route.Length - 1f));
        const int PerChunk = 110;
        int chunks = 0, tris = 0;
        for (int start = i0; start < i1; start += PerChunk)
        {
            int end = Mathf.Min(start + PerChunk, i1);
            var mesh = ShoreCausewayChunk(route, start, end);
            tris += mesh.triangles.Length / 3;
            var go = new GameObject($"Causeway_Body_{chunks:D3}", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { concrete, rock };
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.isStatic = true;
            string path = $"{MeshDir}/{mesh.name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            chunks++;
        }
        Debug.Log($"[shores] causeway body {ShoreBodyFromM:0}-{ShoreBodyToM:0} m: {chunks} chunks, {tris:N0} tris " +
                  $"(top to {ShoreBodyHalfWidthM} m, armour 1:{ShoreArmourSlope} to y {ShoreArmourToeY})");
    }

    private static Mesh ShoreCausewayChunk(MinatoRoute route, int start, int end)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triC = new List<int>();
        var triR = new List<int>();

        // A strip is a list of (lateral, absoluteY) pairs per station; consecutive stations are
        // joined into quads. Each strip gets its own vertices (flat, crisp creases).
        void Strip(Func<int, Vector2> a, Func<int, Vector2> b, List<int> tris, Vector3 expected, bool sided, int s,
                   int from = -1, int to = -1)
        {
            if (from < 0) { from = start; to = end; }
            if (to <= from) return;
            int baseV = verts.Count;
            for (int i = from; i <= to; i++)
            {
                var p = route.Position[i];
                var side = route.SideFlat(i);
                var pa = a(i); var pb = b(i);
                verts.Add(new Vector3(p.x + side.x * pa.x, pa.y, p.z + side.z * pa.x));
                verts.Add(new Vector3(p.x + side.x * pb.x, pb.y, p.z + side.z * pb.x));
                float v = route.Distance[i] * 0.2f;
                uvs.Add(new Vector2(pa.x * 0.2f + pa.y * 0.2f, v));
                uvs.Add(new Vector2(pb.x * 0.2f + pb.y * 0.2f, v));
            }
            for (int k = 0; k < to - from; k++)
            {
                int q0 = baseV + k * 2, q1 = q0 + 1, q2 = q0 + 2, q3 = q0 + 3;
                var n = Vector3.Cross(verts[q2] - verts[q0], verts[q1] - verts[q0]);
                var side = route.SideFlat(from + k);
                var want = sided ? (side * s * expected.x + Vector3.up * expected.y) : Vector3.up;
                if (Vector3.Dot(n, want) >= 0f) { tris.Add(q0); tris.Add(q2); tris.Add(q1); tris.Add(q1); tris.Add(q2); tris.Add(q3); }
                else { tris.Add(q0); tris.Add(q1); tris.Add(q2); tris.Add(q1); tris.Add(q3); tris.Add(q2); }
            }
        }

        float Y(int i) => route.Position[i].y;
        // top: across the whole width, just under the road
        Strip(i => new Vector2(-ShoreBodyHalfWidthM, Y(i) - ShoreDeckDropM),
              i => new Vector2(ShoreBodyHalfWidthM, Y(i) - ShoreDeckDropM), triC, Vector3.up, false, 1);
        for (int s = -1; s <= 1; s += 2)
        {
            int ss = s;
            // concrete face, then either armour or a quay wall, decided per station and built
            // over contiguous runs, so the marina quay starts exactly at its route metre.
            int runStart = start;
            for (int i = start + 1; i <= end + 1; i++)
            {
                bool boundary = i > end || ShoreEdgeAt(route.Distance[i], s, route, i) != ShoreEdgeAt(route.Distance[runStart], s, route, runStart);
                if (!boundary) continue;
                int runEnd = Mathf.Min(i, end);
                var edge = ShoreEdgeAt(route.Distance[runStart], s, route, runStart);
                if (edge == ShoreEdge.Quay)
                {
                    Strip(k => new Vector2(ss * ShoreBodyHalfWidthM, Y(k) - ShoreDeckDropM),
                          k => new Vector2(ss * ShoreBodyHalfWidthM, ShoreQuayBottomY), triC, new Vector3(1f, 0f, 0f), true, s,
                          runStart, runEnd);
                }
                else
                {
                    float faceX = ShoreBodyHalfWidthM + 0.15f;
                    Strip(k => new Vector2(ss * ShoreBodyHalfWidthM, Y(k) - ShoreDeckDropM),
                          k => new Vector2(ss * faceX, Y(k) - ShoreDeckDropM - ShoreFaceDropM), triC, new Vector3(1f, 0.1f, 0f), true, s,
                          runStart, runEnd);
                    Strip(k => new Vector2(ss * faceX, Y(k) - ShoreDeckDropM - ShoreFaceDropM),
                          k => new Vector2(ss * (faceX + (Y(k) - ShoreDeckDropM - ShoreFaceDropM - ShoreArmourToeY) * ShoreArmourSlope),
                                           ShoreArmourToeY), triR, new Vector3(1f, 1f, 0f), true, s, runStart, runEnd);
                }
                runStart = runEnd;
            }
        }
        var mesh = new Mesh { name = $"Minato_Shore_CausewayBody_{start:D5}" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(triC, 0);
        mesh.SetTriangles(triR, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Lateral distance (m) from the centreline to where the armour slope meets the sea.</summary>
    private static float ShoreWaterlineLateral(float roadY) =>
        ShoreBodyHalfWidthM + 0.15f + Mathf.Max(0f, roadY - ShoreDeckDropM - ShoreFaceDropM) * ShoreArmourSlope;

    /// <summary>Height of the armour slope at a lateral offset (for seating tetrapods on it).</summary>
    private static float ShoreArmourY(float roadY, float lateral)
    {
        float faceX = ShoreBodyHalfWidthM + 0.15f;
        float top = roadY - ShoreDeckDropM - ShoreFaceDropM;
        return top - Mathf.Max(0f, lateral - faceX) / ShoreArmourSlope;
    }

    // ================================================================ shared families

    private static GameObject _mSeawall, _mTetra, _mArmA, _mArmB, _mSurfA, _mSurfB, _mPampas, _mGrass,
                              _mScrubA, _mScrubB, _mCliffA, _mCliffB, _mRing, _mTerrace, _mBench,
                              _mBollard, _mHarbourLight, _mHeadLight, _mLookout, _mLamp, _mPontoon,
                              _mFinger, _mGangway, _mYachtA, _mYachtB, _mMotor, _mLift, _mRope,
                              _mCafe, _mApt, _mTerraceBlock, _mHouse, _mBoathouse, _mPineA, _mPineB;

    private static void ShoreLoadModels()
    {
        _mSeawall = Model("Minato_Shore_SeawallRail");
        _mTetra = Model("Minato_Shore_Tetrapod");
        _mArmA = Model("Minato_Shore_ArmourA");
        _mArmB = Model("Minato_Shore_ArmourB");
        _mSurfA = Model("Minato_Shore_SurfA");
        _mSurfB = Model("Minato_Shore_SurfB");
        _mPampas = Model("Minato_Shore_Pampas");
        _mGrass = Model("Minato_Shore_GrassTuft");
        _mScrubA = Model("Minato_Shore_ScrubA");
        _mScrubB = Model("Minato_Shore_ScrubB");
        _mCliffA = Model("Minato_Shore_CliffRockA");
        _mCliffB = Model("Minato_Shore_CliffRockB");
        _mRing = Model("Minato_Shore_RingSculpture");
        _mTerrace = Model("Minato_Shore_TerraceSteps");
        _mBench = Model("Minato_Shore_Bench");
        _mBollard = Model("Minato_Shore_Bollard");
        _mHarbourLight = Model("Minato_Shore_HarbourLight");
        _mHeadLight = Model("Minato_Shore_HeadlandLighthouse");
        _mLookout = Model("Minato_Shore_Lookout");
        _mLamp = Model("Minato_Shore_PromenadeLamp");
        _mPontoon = Model("Minato_Marina_PontoonWalk");
        _mFinger = Model("Minato_Marina_PontoonFinger");
        _mGangway = Model("Minato_Marina_Gangway");
        _mYachtA = Model("Minato_Marina_YachtA");
        _mYachtB = Model("Minato_Marina_YachtB");
        _mMotor = Model("Minato_Marina_MotorYacht");
        _mLift = Model("Minato_Marina_TravelLift");
        _mRope = Model("Minato_Marina_BollardRope");
        _mCafe = Model("Minato_Marina_Cafe");
        _mApt = Model("Minato_Marina_Apartment");
        _mTerraceBlock = Model("Minato_Marina_TerraceBlock");
        _mHouse = Model("Minato_Marina_House");
        _mBoathouse = Model("Minato_Marina_Boathouse");
        // Reused Shiosai coastal black pine (not Sakura): the wind-bent silhouette the targets show.
        _mPineA = Glb(ShiosaiGlb, "Shiosai_Pine");
        _mPineB = Glb(ShiosaiGlb, "Shiosai_Pine_B");
    }

    /// <summary>
    /// Seawall parapet + railing along one side of [from,to). Module pitch 6 m. The parapet's
    /// local -X is its seaward face, so the rotation is chosen per side.
    /// </summary>
    private static int ShoreParapetRun(MinatoRoute route, Transform parent, float from, float to, int s,
                                       Func<float, float> baseY = null)
    {
        int n = 0;
        for (float d = from; d + 6f <= to; d += 6f)
        {
            int i = route.IndexAt(d + 3f);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            var fwd = ShoreForward(route, d);
            var rot = Quaternion.LookRotation(s < 0 ? fwd : -fwd, Vector3.up);
            var pos = p + side * (s * ShoreParapetOffsetM);
            pos.y = baseY != null ? baseY(d) : p.y - ShoreDeckDropM;
            // Stretch the 6 m module to the actual chord so bends never open joints.
            var a = route.Position[route.IndexAt(d)] + route.SideFlat(route.IndexAt(d)) * (s * ShoreParapetOffsetM);
            var b = route.Position[route.IndexAt(d + 6f)] + route.SideFlat(route.IndexAt(d + 6f)) * (s * ShoreParapetOffsetM);
            a.y = b.y = 0f;
            float stretch = Mathf.Clamp(Vector3.Distance(a, b) / 6f + 0.01f, 0.9f, 1.2f);
            ShorePlace(_mSeawall, parent, pos, rot, new Vector3(1f, 1f, stretch));
            n++;
        }
        return n;
    }

    /// <summary>
    /// Tetrapod / armour-stone revetment with breaking surf along one side of the embankment.
    /// Seated ON the 1:1.5 armour slope around the waterline, instanced.
    /// </summary>
    private static int ShoreRevetment(MinatoRoute route, float from, float to, int s, System.Random rng,
                                      float tetraShare, float density)
    {
        int n = 0;
        for (float d = from; d < to; d += 2.3f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            var fwd = ShoreForward(route, d);
            float water = ShoreWaterlineLateral(p.y);
            // Only where the embankment really faces water (land may border it on one side).
            var probe = p + side * (s * (water + 4f));
            if (GroundAt(route, probe.x, probe.z) > SeaLevelY + 0.4f) continue;

            int count = rng.NextDouble() < density ? 2 : 1;
            for (int k = 0; k < count; k++)
            {
                float lat = water + ShoreRand(rng, -3.2f, 2.8f);
                float y = Mathf.Max(ShoreArmourY(p.y, lat), -0.9f);
                var q = p + side * (s * lat) + fwd * ShoreRand(rng, -1.1f, 1.1f);
                q.y = y - 0.35f;
                if (rng.NextDouble() < tetraShare)
                    ShoreQueue(_mTetra, q, Quaternion.Euler(ShoreRand(rng, -25f, 25f), ShoreRand(rng, 0f, 360f), ShoreRand(rng, -25f, 25f)),
                               Vector3.one * ShoreRand(rng, 0.85f, 1.15f), d, 1400f, true);
                else
                    ShoreQueue(rng.NextDouble() < 0.5 ? _mArmA : _mArmB, q, ShoreYaw(rng),
                               new Vector3(ShoreRand(rng, 0.8f, 1.2f), ShoreRand(rng, 0.7f, 1.1f), ShoreRand(rng, 0.8f, 1.2f)),
                               d, 1400f, true);
                n++;
            }
            // a second, higher row of tetrapods stacked up the slope (the target's tumbled wall)
            if (rng.NextDouble() < tetraShare * 0.55)
            {
                float lat = water - ShoreRand(rng, 3.0f, 5.5f);
                var q = p + side * (s * lat) + fwd * ShoreRand(rng, -1f, 1f);
                q.y = ShoreArmourY(p.y, lat) - 0.4f;
                ShoreQueue(_mTetra, q, Quaternion.Euler(ShoreRand(rng, -30f, 30f), ShoreRand(rng, 0f, 360f), ShoreRand(rng, -30f, 30f)),
                           Vector3.one * ShoreRand(rng, 0.85f, 1.1f), d, 1400f, true);
                n++;
            }
            // breaking surf at the waterline, heavier on the open-sea side
            if (rng.NextDouble() < 0.22)
            {
                var q = p + side * (s * (water + ShoreRand(rng, 0.8f, 2.8f)));
                q.y = SeaLevelY - 0.15f;
                var rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, ShoreRand(rng, -12f, 12f), 0f);
                ShoreQueue(rng.NextDouble() < 0.6 ? _mSurfA : _mSurfB, q, rot,
                           new Vector3(ShoreRand(rng, 0.8f, 1.3f), ShoreRand(rng, 0.7f, 1.2f), ShoreRand(rng, 0.8f, 1.4f)), d, 900f, false);
            }
        }
        return n;
    }

    /// <summary>Pampas + low grass in the verge strip between the road edge and the parapet.</summary>
    private static int ShoreVerge(MinatoRoute route, float from, float to, int s, System.Random rng,
                                  float pampas, float grass, Func<float, float> baseY = null)
    {
        int n = 0;
        for (float d = from; d < to; d += 1.7f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            float y = baseY != null ? baseY(d) : p.y - ShoreDeckDropM;
            if (rng.NextDouble() < pampas)
            {
                var q = p + side * (s * ShoreRand(rng, 5.25f, 6.0f));
                q.y = y - 0.05f;
                ShoreQueue(_mPampas, q, ShoreYaw(rng), Vector3.one * ShoreRand(rng, 0.8f, 1.2f), d, 520f, false);
                n++;
            }
            if (rng.NextDouble() < grass)
            {
                var q = p + side * (s * ShoreRand(rng, 4.95f, 6.2f));
                q.y = y - 0.03f;
                ShoreQueue(_mGrass, q, ShoreYaw(rng), Vector3.one * ShoreRand(rng, 0.9f, 1.4f), d, 320f, false);
                n++;
            }
        }
        return n;
    }

    /// <summary>A red or green harbour light on a short tetrapod/armour spur off the embankment.</summary>
    private static void ShoreHarbourSpur(MinatoRoute route, Transform parent, float d, int s, string paint,
                                         System.Random rng, float length)
    {
        int i = route.IndexAt(d);
        var p = route.Position[i];
        var side = route.SideFlat(i);
        var outDir = side * s;
        float start = ShoreWaterlineLateral(p.y) - 2f;
        for (float t = 0f; t < length; t += 1.6f)
        {
            float w = Mathf.Lerp(7f, 5f, t / length);
            for (int k = 0; k < 3; k++)
            {
                var q = p + outDir * (start + t) + ShoreForward(route, d) * ShoreRand(rng, -w * 0.5f, w * 0.5f);
                q.y = ShoreRand(rng, -0.6f, 0.4f);
                if (rng.NextDouble() < 0.6)
                    ShoreQueue(_mTetra, q, Quaternion.Euler(ShoreRand(rng, -30f, 30f), ShoreRand(rng, 0f, 360f), ShoreRand(rng, -30f, 30f)),
                               Vector3.one * ShoreRand(rng, 0.9f, 1.15f), d, 1600f, true);
                else
                    ShoreQueue(rng.NextDouble() < 0.5 ? _mArmA : _mArmB, q, ShoreYaw(rng), Vector3.one * ShoreRand(rng, 0.9f, 1.3f), d, 1600f, true);
            }
        }
        var tip = p + outDir * (start + length + 2.5f);
        tip.y = SeaLevelY + 0.2f;
        ShorePlace(_mHarbourLight, parent, tip, Quaternion.Euler(0f, ShoreRand(rng, 0f, 360f), 0f),
                   Vector3.one, paint);
        for (int k = 0; k < 4; k++)
        {
            var q = tip + outDir * ShoreRand(rng, 1f, 4f) + ShoreForward(route, d) * ShoreRand(rng, -4f, 4f);
            q.y = SeaLevelY - 0.2f;
            ShoreQueue(_mSurfA, q, ShoreYaw(rng), Vector3.one * ShoreRand(rng, 0.8f, 1.2f), d, 1200f, false);
        }
    }

    // ================================================================ Seawall Sprint

    private static void BuildShoreSeawallSprint(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Seawall Sprint").transform;
        parent.SetParent(root, false);
        var rng = new System.Random(18002600);
        int seaI = route.IndexAt(ShoreSeawallFromM + 50f);
        int sea = SeaSideSign(route, seaI) < 0f ? -1 : 1;

        // Parapet + railing on the sea side for the whole sprint. On the land stretch it stands
        // on the ground at the road edge; on the embankment it stands on the body top.
        float Base(float d)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            if (d >= ShoreBodyFromM) return p.y - ShoreDeckDropM;
            var q = p + route.SideFlat(i) * (sea * ShoreParapetOffsetM);
            return Mathf.Min(p.y - 0.05f, GroundAt(route, q.x, q.z));
        }
        int rails = ShoreParapetRun(route, parent, ShoreSeawallFromM, ShoreSeawallToM, sea, Base);

        // Revetment where the embankment meets the water (the over-water part of the sprint)...
        int armour = ShoreRevetment(route, ShoreBodyFromM, ShoreSeawallToM, sea, rng, 0.85f, 0.75f);
        // ...and a tetrapod breakwater along the natural waterline on the land stretch, which is
        // what the rider sees across the open shelf toward the bridge.
        for (float d = ShoreSeawallFromM; d < ShoreBodyFromM; d += 2.6f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * sea;
            float w = -1f;
            for (float o = 8f; o < 420f; o += 3f)
            {
                var q = p + side * o;
                if (GroundAt(route, q.x, q.z) < SeaLevelY + 0.6f) { w = o; break; }
            }
            if (w < 0f) continue;
            for (int k = 0; k < 2; k++)
            {
                var q = p + side * (w + ShoreRand(rng, -3f, 4f)) + ShoreForward(route, d) * ShoreRand(rng, -1.2f, 1.2f);
                q.y = Mathf.Max(GroundAt(route, q.x, q.z), -0.8f) - 0.3f;
                ShoreQueue(_mTetra, q, Quaternion.Euler(ShoreRand(rng, -25f, 25f), ShoreRand(rng, 0f, 360f), ShoreRand(rng, -25f, 25f)),
                           Vector3.one * ShoreRand(rng, 0.9f, 1.2f), d, 1400f, true);
                armour++;
            }
            if (rng.NextDouble() < 0.25)
            {
                var q = p + side * (w + ShoreRand(rng, 3f, 6f));
                q.y = SeaLevelY - 0.15f;
                ShoreQueue(_mSurfA, q, Quaternion.LookRotation(ShoreForward(route, d)), Vector3.one * ShoreRand(rng, 0.9f, 1.4f), d, 900f, false);
            }
        }
        int verge = ShoreVerge(route, ShoreBodyFromM, ShoreSeawallToM, sea, rng, 0.30f, 0.55f);
        // Landward verge on the embankment part (the land side of the sprint beyond the city).
        verge += ShoreVerge(route, ShoreBodyFromM + 20f, ShoreSeawallToM, -sea, rng, 0.45f, 0.6f,
                            d => ShoreLandsideY(route, d, -sea, 5.6f));

        // Landward terrace: three planter tiers + the stainless ring sculpture, pampas in the
        // tiers, wind-bent pines behind (Seawall target, right-hand side).
        {
            float d = ShoreRingTerraceM;
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * -sea;
            var fwd = ShoreForward(route, d);
            var tpos = p + side * 11.5f;
            tpos.y = ShoreLandsideY(route, d, -sea, 11.5f);
            var trot = Quaternion.LookRotation(side, Vector3.up);
            ShorePlace(_mTerrace, parent, tpos, trot, 1f);
            var ring = tpos + side * 8.5f + fwd * 4.2f;
            ring.y = tpos.y + 2.4f;
            ShorePlace(_mRing, parent, ring, Quaternion.LookRotation(-side + fwd * 0.4f, Vector3.up), 1f);
            for (int t = 0; t < 3; t++)
                for (int k = 0; k < 7; k++)
                {
                    float sx = (k < 4 ? -1f : 1f) * ShoreRand(rng, 3.0f, 5.4f);
                    var q = tpos + fwd * sx + side * (t * 2.2f + 1.25f);
                    q.y = tpos.y + 0.8f * (t + 1) + 0.02f;
                    ShoreQueue(_mPampas, q, ShoreYaw(rng), Vector3.one * ShoreRand(rng, 0.8f, 1.15f), d, 520f, false);
                }
            for (int k = 0; k < 3; k++)
            {
                var b = p + side * 6.4f + fwd * (-9f + k * 9f);
                b.y = ShoreLandsideY(route, d, -sea, 6.4f);
                ShorePlace(_mBench, parent, b, Quaternion.LookRotation(fwd, Vector3.up), 1f);
            }
        }
        // Wind-bent pines on the landward bank, in irregular clumps (never a row).
        int pines = 0;
        for (float d = ShoreBodyFromM + 10f; d < ShoreSeawallToM; d += 26f)
        {
            if (rng.NextDouble() < 0.35) continue;
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * -sea;
            float off = ShoreRand(rng, 12f, 24f);
            var q = p + side * off + ShoreForward(route, d) * ShoreRand(rng, -6f, 6f);
            q.y = ShoreLandsideY(route, d, -sea, off) - 0.1f;
            if (Mathf.Abs(d - ShoreRingTerraceM) < 16f) continue;
            if (ShorePine(rng.NextDouble() < 0.5 ? _mPineA : _mPineB, parent, q, ShoreRand(rng, 0f, 360f), ShoreRand(rng, 1.15f, 1.6f)) != null) pines++;
        }
        // Dark kerb bollards with a yellow band on the landward kerb (target foreground).
        int bollards = 0;
        for (float d = ShoreBodyFromM + 8f; d < ShoreSeawallToM; d += 9f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var q = p + route.SideFlat(i) * (-sea * 5.0f);
            q.y = ShoreLandsideY(route, d, -sea, 5.0f);
            ShorePlace(_mBollard, parent, q, Quaternion.identity, 1f);
            bollards++;
        }
        Debug.Log($"[shores] seawall sprint {ShoreSeawallFromM:0}-{ShoreSeawallToM:0} m: {rails} parapet/rail bays, " +
                  $"{armour} tetrapod/armour pieces, {verge} verge plants, {pines} pines, {bollards} bollards, ring terrace at {ShoreRingTerraceM:0} m");
    }

    /// <summary>
    /// Surface height on the LAND side of an over-water stretch at a lateral offset: the
    /// embankment top where the ground is lower, otherwise the (higher) terrain bank.
    /// </summary>
    private static float ShoreLandsideY(MinatoRoute route, float d, int s, float lateral)
    {
        int i = route.IndexAt(d);
        var p = route.Position[i];
        var q = p + route.SideFlat(i) * (s * lateral);
        float deck = p.y - ShoreDeckDropM;
        // Whichever is higher: the embankment top, or a terrain bank that rises over it.
        return Mathf.Max(deck, GroundAt(route, q.x, q.z) + 0.25f);
    }

    // ================================================================ Marina Ribbon

    private static void BuildShoreMarinaRibbon(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Marina Ribbon").transform;
        parent.SetParent(root, false);
        var rng = new System.Random(26003600);
        int seaI = route.IndexAt(ShoreMarinaFromM + 50f);
        int sea = SeaSideSign(route, seaI) < 0f ? -1 : 1;
        int land = -sea;

        // ---- quay edge on the sea side: bollards + rope right at the road (target foreground)
        int ropes = 0;
        for (float d = ShoreMarinaFromM; d + 4f <= ShoreMarinaToM; d += 4f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var q = p + route.SideFlat(i) * (sea * 6.3f);
            q.y = p.y - ShoreDeckDropM;
            ShorePlace(_mRope, parent, q, Quaternion.LookRotation(ShoreForward(route, d), Vector3.up), 1f);
            ropes++;
        }

        // ---- pontoons, fingers and moored yachts
        int mains = 0, fingers = 0, boats = 0;
        const float MainPitchM = 46f;
        const float WalkStartM = 15.2f;   // lateral where the main pontoon starts (gangway foot)
        const int WalkModules = 3;        // 36 m of main pontoon
        for (float d = ShoreMarinaFromM + 24f; d < ShoreMarinaToM - 20f; d += MainPitchM)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * sea;
            var fwd = ShoreForward(route, d);
            var outRot = Quaternion.LookRotation(side, Vector3.up);
            // gangway from the quay top down to the pontoon
            var g = p + side * (ShoreBodyHalfWidthM + 0.1f);
            g.y = p.y - ShoreDeckDropM;
            float drop = g.y - (SeaLevelY + 0.5f);
            ShorePlace(_mGangway, parent, g, outRot, new Vector3(1f, Mathf.Max(0.5f, drop / 1.6f), 1f));
            for (int m = 0; m < WalkModules; m++)
            {
                var c = p + side * (WalkStartM + 6f + m * 12f);
                c.y = SeaLevelY;
                ShorePlace(_mPontoon, parent, c, outRot, 1f);
            }
            mains++;
            // fingers every 7 m on both sides of the main walk, a boat on each
            for (float t = 3f; t < WalkModules * 12f - 1f; t += 7f)
                for (int fs = -1; fs <= 1; fs += 2)
                {
                    var root2 = p + side * (WalkStartM + t) + fwd * (fs * 1.3f);
                    root2.y = SeaLevelY;
                    var fRot = Quaternion.LookRotation(fwd * fs, Vector3.up);
                    ShorePlace(_mFinger, parent, root2, fRot, 1f);
                    fingers++;
                    if (rng.NextDouble() > 0.82) continue;
                    double pick = rng.NextDouble();
                    var src = pick < 0.5 ? _mYachtA : pick < 0.8 ? _mYachtB : _mMotor;
                    float len = src == _mYachtA ? 11f : src == _mYachtB ? 9f : 13f;
                    var bpos = root2 + fwd * (fs * (len * 0.5f + 0.4f)) + side * 2.6f;
                    bpos.y = SeaLevelY - 0.05f;
                    // bow toward the main walk, like a stern-to berth reversed; small yaw noise
                    var bRot = Quaternion.LookRotation(-fwd * fs, Vector3.up) * Quaternion.Euler(0f, ShoreRand(rng, -3f, 3f), 0f);
                    ShorePlace(src, parent, bpos, bRot, 1f);
                    boats++;
                }
        }

        // ---- land side: quay platform with cafe, boat hoist, boathouse, apartments
        BuildShoreMarinaPlatform(route, parent, land);
        float LY(float d, float lat) => ShoreLandsideY(route, d, land, lat);
        var cafeD = ShoreMarinaFromM + 190f;
        ShoreFacingRoad(route, parent, _mCafe, cafeD, land, 21f, LY(cafeD, 21f));
        var liftD = ShoreMarinaFromM + 430f;
        {
            int i = route.IndexAt(liftD);
            var p = route.Position[i];
            var side = route.SideFlat(i) * land;
            var fwd = ShoreForward(route, liftD);
            var pos = p + side * 20f; pos.y = LY(liftD, 20f);
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            ShorePlace(_mLift, parent, pos, rot, 1f);
            var hung = pos + Vector3.up * 3.35f;
            ShorePlace(_mMotor, parent, hung, rot, 0.92f);
        }
        var shedD = ShoreMarinaFromM + 520f;
        ShoreFacingRoad(route, parent, _mBoathouse, shedD, land, 26f, LY(shedD, 26f));
        int homes = 0;
        for (float d = ShoreMarinaFromM + 40f; d < ShoreMarinaToM - 30f; d += 58f)
        {
            if (Mathf.Abs(d - cafeD) < 30f || Mathf.Abs(d - liftD) < 30f || Mathf.Abs(d - shedD) < 34f) continue;
            float lat = ShoreRand(rng, 40f, 52f);
            var src = rng.NextDouble() < 0.55 ? _mApt : rng.NextDouble() < 0.5 ? _mTerraceBlock : _mHouse;
            if (ShoreFacingRoad(route, parent, src, d, land, lat, LY(d, lat) - 0.2f) != null) homes++;
        }
        // lamps + planters on the promenade edge
        int lamps = 0;
        for (float d = ShoreMarinaFromM + 10f; d < ShoreMarinaToM; d += 32f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * land;
            var q = p + side * 7.6f; q.y = LY(d, 7.6f);
            // the lamp arm points back over the road
            ShorePlace(_mLamp, parent, q, Quaternion.LookRotation(ShoreForward(route, d) * (land < 0 ? 1f : -1f), Vector3.up), 1f);
            lamps++;
        }
        int verge = ShoreVerge(route, ShoreMarinaFromM, ShoreMarinaToM, land, rng, 0.40f, 0.5f, d => LY(d, 5.6f));
        int pines = 0;
        for (float d = ShoreMarinaFromM + 15f; d < ShoreMarinaToM; d += 34f)
        {
            if (rng.NextDouble() < 0.3) continue;
            int i = route.IndexAt(d);
            var p = route.Position[i];
            float lat = ShoreRand(rng, 9.5f, 13f);
            var q = p + route.SideFlat(i) * (land * lat) + ShoreForward(route, d) * ShoreRand(rng, -5f, 5f);
            q.y = LY(d, lat) - 0.1f;
            if (Mathf.Abs(d - cafeD) < 14f || Mathf.Abs(d - liftD) < 12f || Mathf.Abs(d - shedD) < 14f) continue;
            if (ShorePine(rng.NextDouble() < 0.5 ? _mPineA : _mPineB, parent, q, ShoreRand(rng, 0f, 360f), ShoreRand(rng, 1.0f, 1.35f)) != null) pines++;
        }
        Debug.Log($"[shores] marina ribbon {ShoreMarinaFromM:0}-{ShoreMarinaToM:0} m: {ropes} bollard+rope bays, {mains} pontoon mains, " +
                  $"{fingers} fingers, {boats} moored yachts, cafe/boat hoist/boathouse, {homes} waterfront buildings, " +
                  $"{lamps} lamps, {pines} pines, {verge} verge plants");
    }

    private static GameObject ShoreFacingRoad(MinatoRoute route, Transform parent, GameObject src, float d,
                                              int s, float lateral, float y)
    {
        int i = route.IndexAt(d);
        var p = route.Position[i];
        var side = route.SideFlat(i) * s;
        var pos = p + side * lateral; pos.y = y;
        if (!ShoreClearOfRoad(route, pos, lateral - 10f, d)) return null;
        return ShorePlace(src, parent, pos, Quaternion.LookRotation(-side, Vector3.up), 1f);
    }

    /// <summary>
    /// Reclaimed quay platform on the land side of the marina: paving that follows
    /// max(embankment top, terrain + 0.25) so it meets the terrain bank where there is one and
    /// stands on its own quay wall where there is water. Near-vertical strips (the step up to a
    /// higher terrain bank) become concrete retaining wall.
    /// </summary>
    private static void BuildShoreMarinaPlatform(MinatoRoute route, Transform parent, int s)
    {
        int i0 = route.IndexAt(ShoreMarinaFromM), i1 = route.IndexAt(ShoreMarinaToM);
        float[] lanes = { ShoreBodyHalfWidthM - 0.05f, ShoreBodyHalfWidthM + 0.05f, 10f, 14f, 19f, 25f, 31f, ShoreMarinaPlatformM };
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triPave = new List<int>();
        var triWall = new List<int>();
        int stride = lanes.Length;
        var stations = new List<int>();
        for (int i = i0; i <= i1; i += 3) stations.Add(i);
        foreach (int i in stations)
        {
            var p = route.Position[i];
            var side = route.SideFlat(i) * s;
            float deck = p.y - ShoreDeckDropM;
            for (int l = 0; l < stride; l++)
            {
                var q = p + side * lanes[l];
                float y = l == 0 ? deck : Mathf.Max(deck, GroundAt(route, q.x, q.z) + 0.25f);
                verts.Add(new Vector3(q.x, y, q.z));
                uvs.Add(new Vector2(lanes[l] * 0.18f, route.Distance[i] * 0.18f));
            }
        }
        for (int k = 0; k < stations.Count - 1; k++)
            for (int l = 0; l < stride - 1; l++)
            {
                int a = k * stride + l, b = a + 1, c = a + stride, e = c + 1;
                var n = Vector3.Cross(verts[c] - verts[a], verts[b] - verts[a]);
                bool up = n.y >= 0f;
                int[] t = up ? new[] { a, c, b, b, c, e } : new[] { a, b, c, b, e, c };
                var nn = up ? n : -n;
                var list = nn.normalized.y < 0.55f ? triWall : triPave;
                list.AddRange(t);
            }
        // outer quay face where the platform stands over water
        int faceBase = verts.Count;
        foreach (int i in stations)
        {
            var p = route.Position[i];
            var side = route.SideFlat(i) * s;
            var q = p + side * ShoreMarinaPlatformM;
            float top = Mathf.Max(p.y - ShoreDeckDropM, GroundAt(route, q.x, q.z) + 0.25f);
            verts.Add(new Vector3(q.x, top, q.z)); uvs.Add(new Vector2(0f, route.Distance[i] * 0.18f));
            verts.Add(new Vector3(q.x, ShoreQuayBottomY, q.z)); uvs.Add(new Vector2(1f, route.Distance[i] * 0.18f));
        }
        for (int k = 0; k < stations.Count - 1; k++)
        {
            int a = faceBase + k * 2, b = a + 1, c = a + 2, e = a + 3;
            var n = Vector3.Cross(verts[c] - verts[a], verts[b] - verts[a]);
            var outward = route.SideFlat(stations[k]) * s;
            if (Vector3.Dot(n, outward) >= 0f) triWall.AddRange(new[] { a, c, b, b, c, e });
            else triWall.AddRange(new[] { a, b, c, b, e, c });
        }
        var mesh = new Mesh { name = "Minato_Shore_MarinaPlatform" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(triPave, 0);
        mesh.SetTriangles(triWall, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var go = new GameObject("Marina Quay Platform", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { ShoreMat("paving"), ShoreMat("concrete") };
        go.isStatic = true;
        string path = $"{MeshDir}/{mesh.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
    }

    // ================================================================ Stormglass Causeway

    private static void BuildShoreStormglassCauseway(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Stormglass Causeway").transform;
        parent.SetParent(root, false);
        var rng = new System.Random(36007400);
        int rails = 0, armour = 0, verge = 0;
        for (int s = -1; s <= 1; s += 2)
        {
            rails += ShoreParapetRun(route, parent, ShoreCausewayFromM, ShoreBodyToM - 20f, s);
            // the open-sea side takes the heavier tetrapod wall; the lee side more armour stone
            bool seaSide = s * SeaSideSign(route, route.IndexAt(ShoreCausewayFromM + 50f)) > 0f;
            armour += ShoreRevetment(route, ShoreMarinaToM - 10f, ShoreBodyToM, s, rng,
                                     seaSide ? 0.8f : 0.45f, seaSide ? 0.7f : 0.45f);
            verge += ShoreVerge(route, ShoreCausewayFromM, ShoreCausewayToM, s, rng, 0.34f, 0.5f);
        }
        int lights = 0;
        foreach (float d in ShoreHarbourLightsM)
        {
            if (d < ShoreBodyFromM || d > ShoreBodyToM) continue;
            int i = route.IndexAt(d);
            int sea = SeaSideSign(route, i) < 0f ? -1 : 1;
            ShoreHarbourSpur(route, parent, d, sea, "red", rng, ShoreRand(rng, 26f, 40f));
            ShoreHarbourSpur(route, parent, d + 60f, -sea, "green", rng, ShoreRand(rng, 22f, 34f));
            lights += 2;
        }
        Debug.Log($"[shores] stormglass causeway {ShoreCausewayFromM:0}-{ShoreCausewayToM:0} m: {rails} parapet/rail bays (both sides), " +
                  $"{armour} tetrapod/armour pieces, {verge} verge plants, {lights} harbour lights on rock spurs");
    }

    // ================================================================ Beyond the Horizon

    private static MeshCollider _shoreShell;
    private static Bounds _shoreShellBounds;

    /// <summary>The authored chapter-5 landscape shell replaces the generated terrain renderers
    /// inside its bounds; dressing there must sit on the SHELL, so give it a temporary collider.</summary>
    private static MeshCollider ShoreShellBegin(Transform chapter)
    {
        _shoreShell = null;
        var mountain = chapter.Find("Authored Mountain Landscape");
        if (mountain == null) return null;
        MeshFilter pick = null;
        foreach (var mf in mountain.GetComponentsInChildren<MeshFilter>(true))
            if (mf.name.EndsWith("_LOD1", StringComparison.Ordinal)) { pick = mf; break; }
        if (pick == null)
            foreach (var mf in mountain.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.EndsWith("_LOD0", StringComparison.Ordinal)) { pick = mf; break; }
        if (pick == null || pick.sharedMesh == null) return null;
        var c = pick.gameObject.AddComponent<MeshCollider>();
        c.sharedMesh = pick.sharedMesh;
        Physics.SyncTransforms();
        _shoreShell = c;
        _shoreShellBounds = c.bounds;
        return c;
    }

    private static void ShoreShellEnd(MeshCollider c)
    {
        if (c != null) UnityEngine.Object.DestroyImmediate(c);
        _shoreShell = null;
    }

    /// <summary>Visible ground height: the authored shell where it exists, else THE height function.</summary>
    private static float ShoreSurfaceY(MinatoRoute route, float x, float z, out Vector3 normal)
    {
        normal = Vector3.up;
        if (_shoreShell != null && x > _shoreShellBounds.min.x && x < _shoreShellBounds.max.x &&
            z > _shoreShellBounds.min.z && z < _shoreShellBounds.max.z)
        {
            var ray = new Ray(new Vector3(x, _shoreShellBounds.max.y + 50f, z), Vector3.down);
            if (_shoreShell.Raycast(ray, out var hit, _shoreShellBounds.size.y + 200f))
            {
                normal = hit.normal;
                return hit.point.y;
            }
        }
        float y = GroundAt(route, x, z);
        float yx = GroundAt(route, x + 3f, z), yz = GroundAt(route, x, z + 3f);
        normal = new Vector3(-(yx - y) / 3f, 1f, -(yz - y) / 3f).normalized;
        return y;
    }

    /// <summary>
    /// Far-shore landfall as a coastal town: irregular clusters of white/navy apartments,
    /// terrace blocks and houses with coastal pine groves, alternating sides with open gaps so
    /// the ride keeps its views; the hero lighthouse on its own promontory at the real waterline.
    /// </summary>
    private static GameObject[] _townHouses, _townShrubs;
    private static Material[] _townFacades, _townRoofs;

    private static void BuildShoreLandfallTown(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Landfall Town").transform;
        parent.SetParent(root, false);
        _townHouses = new[] { Glb(ShiosaiGlb, "Shiosai_House_A"), Glb(ShiosaiGlb, "Shiosai_House_B"),
                              Glb(ShiosaiGlb, "Shiosai_House_C"), Glb(ShiosaiGlb, "Shiosai_HarbourHouse_A"),
                              Glb(ShiosaiGlb, "Shiosai_HarbourHouse_B"), Glb(ShiosaiGlb, "Shiosai_HarbourHouse_C") };
        _townShrubs = new[] { Glb(ShiosaiGlb, "Shiosai_Hydrangea"), Glb(ShiosaiGlb, "Shiosai_Hydrangea_B"),
                              Glb(ShiosaiGlb, "Shiosai_Hydrangea_C") };
        _townFacades = SettlementFacades();
        _townRoofs = SettlementRoofs();
        var rng = new System.Random(10301340);
        int buildings = 0, pines = 0;
        for (float d = ShoreLandfallFromM + 120f; d < ShoreLandfallToM - 60f; d += 70f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var sideR = route.SideFlat(i);
            var fwd = ShoreForward(route, d);
            int primary = (((int)(d / 420f)) & 1) == 0 ? -1 : 1;
            // every fourth block is left open - a view gap, not a street wall
            if (((int)(d / 70f)) % 4 == 3) continue;
            for (int s = -1; s <= 1; s += 2)
            {
                int count = s == primary ? 3 + rng.Next(2) : 1 + rng.Next(2);
                for (int k = 0; k < count; k++)
                {
                    float lat = ShoreRand(rng, 22f, 44f) + k * 16f;
                    var q = p + sideR * (s * lat) + fwd * ShoreRand(rng, -22f, 22f);
                    float y = ShoreSurfaceY(route, q.x, q.z, out var nrm);
                    if (y < SeaLevelY + 1.2f || Mathf.Abs(y - p.y) > 14f || nrm.y < 0.86f) continue;
                    if (!ShoreClearOfRoad(route, q, 22f, d)) continue;
                    // OVERHAUL (user: "last two checkpoints look very low quality"): the quick
                    // Minato_Marina_* boxes read as white blocks with flat blue lids. Use the
                    // production Shiosai coastal houses (tiled roofs, real facades) in the Minato
                    // settlement palette, each with a planted front garden.
                    var src = _townHouses[rng.Next(_townHouses.Length)];
                    if (src == null) continue;
                    var face = Quaternion.LookRotation(-sideR * s, Vector3.up) * Quaternion.Euler(0f, ShoreRand(rng, -10f, 10f), 0f);
                    var go = Inst(src, parent, Vector3.zero, face,
                                  _townFacades[rng.Next(_townFacades.Length)], _townRoofs[rng.Next(_townRoofs.Length)]);
                    go.transform.position = new Vector3(q.x, y - 0.30f, q.z);
                    float sc = ShoreRand(rng, 0.95f, 1.25f);
                    go.transform.localScale = new Vector3(sc, sc * ShoreRand(rng, 0.9f, 1.15f), sc);
                    go.isStatic = true; buildings++;
                    int shrubs = 2 + rng.Next(3);
                    for (int h = 0; h < shrubs; h++)
                    {
                        var gq = q - sideR * (s * ShoreRand(rng, 7f, 11f)) + fwd * ShoreRand(rng, -7f, 7f);
                        float gy = ShoreSurfaceY(route, gq.x, gq.z, out _);
                        if (!ShoreClearOfRoad(route, gq, 7f, d)) continue;
                        var hs = _townShrubs[rng.Next(_townShrubs.Length)];
                        if (hs == null) continue;
                        var sh = Inst(hs, parent, Vector3.zero, Quaternion.Euler(0f, ShoreRand(rng, 0f, 360f), 0f), null);
                        sh.transform.position = new Vector3(gq.x, gy - 0.05f, gq.z);
                        sh.transform.localScale = Vector3.one * ShoreRand(rng, 0.9f, 1.4f);
                        sh.isStatic = true;
                    }
                }
                // a pine grove behind/among each cluster
                int grove = 2 + rng.Next(4);
                for (int k = 0; k < grove; k++)
                {
                    var q = p + sideR * (s * ShoreRand(rng, 14f, 80f)) + fwd * ShoreRand(rng, -30f, 30f);
                    float y = ShoreSurfaceY(route, q.x, q.z, out var nrm);
                    if (y < SeaLevelY + 1.0f || nrm.y < 0.8f) continue;
                    if (!ShoreClearOfRoad(route, q, 10f, d)) continue;
                    if (ShorePine(rng.NextDouble() < 0.5 ? _mPineA : _mPineB, parent, new Vector3(q.x, y - 0.1f, q.z),
                                  ShoreRand(rng, 0f, 360f), ShoreRand(rng, 1.0f, 1.7f)) != null) pines++;
                }
            }
        }
        // Hero lighthouse: legacy asset (Minato-owned), now seated AT the real waterline so its
        // rock promontory stands in the sea instead of being buried in a hill 320 m inland.
        var hero = Glb(MinatoGlb, "Minato_Lighthouse_Hero");
        if (hero != null)
        {
            int i = route.IndexAt(ShoreHeroLighthouseM);
            var p = route.Position[i];
            int sea = SeaSideSign(route, i) < 0f ? -1 : 1;
            var side = route.SideFlat(i) * sea;
            float w = -1f;
            for (float o = 30f; o < 1500f; o += 6f)
            {
                var q = p + side * o;
                if (GroundAt(route, q.x, q.z) < SeaLevelY) { w = o; break; }
            }
            if (w > 0f)
            {
                var c = p + side * (w + 70f);
                c.y = SeaLevelY;
                var go = Inst(hero, parent, Vector3.zero, Quaternion.identity, null);
                go.transform.SetPositionAndRotation(c, Quaternion.Euler(0f, 18f, 0f));
                go.transform.localScale = Vector3.one * 1.2f;
                go.name = "Minato_Lighthouse_Hero";
                go.isStatic = true;
                ShoreRetintHeroLighthouse(go);
                ShoreSeatHeroLighthouse(route, parent, p, side, w, c);
                _shorePlaced++;
                Debug.Log($"[shores] hero lighthouse on its promontory at {c}, waterline {w:0} m from the road");
            }
        }
        Debug.Log($"[shores] landfall town {ShoreLandfallFromM:0}-{ShoreLandfallToM:0} m: {buildings} white/navy buildings, {pines} coastal pines");
    }

    /// <summary>
    /// ChatGPT request 2026-09-30 (lighthouse islet read as a dark slab, floating green dots, razor-cut
    /// grass bank): ring the promontory with authored sculpted islet rock + canopy so its rim is broken,
    /// and run a line of the same rocks along the waterline of the grass bank so its straight edge is
    /// seated in boulders instead of cutting clean into the water.
    /// </summary>
    private static void ShoreSeatHeroLighthouse(MinatoRoute route, Transform parent, Vector3 roadP,
                                                Vector3 side, float waterline, Vector3 centre)
    {
        var isletA = Model("Minato_Sea_IsletA");
        var isletB = Model("Minato_Sea_IsletB");
        if (isletA == null && isletB == null) return;
        var rockMat = CelMaterial("Minato_IsletRock", new Color(0.50f, 0.53f, 0.52f), 0.08f, 0.05f, 0.30f,
                                  Tex(TakaTex, "Taka_Granite_Albedo.png"), GroundShade);
        // The islets' canopy clumps are neon and huge at this range, so they are painted as dark wind-clipped scrub.
        var scrubMat = CelMaterial("Minato_Shore_Scrub", new Color(0.34f, 0.50f, 0.28f), 0.08f, 0.04f, 0.40f);
        var rng = new System.Random(5517);
        var holder = new GameObject("Lighthouse Seating").transform;
        holder.SetParent(parent, false);
        int placed = 0;
        void Put(GameObject src, Vector3 pos, float scale)
        {
            if (src == null) return;
            var g = Inst(src, holder, Vector3.zero, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), null);
            RetintBySlot(g, new[] { ("foliage", scrubMat), ("rock", rockMat) }, rockMat);
            g.transform.localScale = Vector3.one * scale;
            g.transform.position = new Vector3(pos.x, SeaLevelY, pos.z);
            SeatInWater(g, SeaLevelY - 0.8f);
            g.isStatic = true; placed++;
        }
        // 1. Rim ring: five small islets around the promontory break its slab outline.
        for (int k = 0; k < 5; k++)
        {
            float a = (k + (float)rng.NextDouble() * 0.5f) * Mathf.PI * 2f / 5f;
            Put(k % 2 == 0 ? isletA ?? isletB : isletB ?? isletA,
                centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (16f + (float)rng.NextDouble() * 6f),
                0.14f + (float)rng.NextDouble() * 0.08f);
        }
        // 2. Bank toe: boulders along the waterline of the grass bank, +-140 m of the lighthouse.
        var along = Vector3.Cross(Vector3.up, side).normalized;
        float lateral = Vector3.Dot(centre - roadP, along);
        for (float t = -140f; t <= 140f; t += 9f + (float)rng.NextDouble() * 7f)
        {
            var q = roadP + along * (lateral + t) + side * (waterline + 1.5f);
            Put(rng.NextDouble() < 0.5 ? isletA ?? isletB : isletB ?? isletA, q,
                0.07f + (float)rng.NextDouble() * 0.07f);
        }
        Debug.Log($"[shores] lighthouse seating: {placed} islet/boulder pieces");
    }

    private static void ShoreRetintHeroLighthouse(GameObject go)
    {
        // Lighter, warmer granite than before: the old 0.52 grey + GroundShade read as a dark slab.
        var rock = CelMaterial("Minato_PromontoryRock", new Color(0.78f, 0.76f, 0.72f), 0.08f, 0.05f, 0.30f,
                               Tex(TakaTex, "Taka_Granite_Albedo.png"));
        var plant = CelMaterial("Minato_Shore_Scrub", new Color(0.34f, 0.50f, 0.28f), 0.08f, 0.04f, 0.40f);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.name;
            // The hero GLB's scrub cards read as tiny floating green dots at range: the islet canopy replaces them.
            if (n.Contains("_Plant_")) { r.enabled = false; continue; }
            Material m = n.Contains("_Rock_") ? rock
                       : n.Contains("_Body_") ? ShoreMat("white")
                       : n.Contains("_Band_") ? ShoreMat("coral")
                       : n.Contains("_Roof_") ? ShoreMat("navy")
                       : n.Contains("_Glass_") ? ShoreMat("glass")
                       : n.Contains("_Light_") ? ShoreMat("lamp")
                       : n.Contains("_Plant_") ? plant
                       : ShoreMat("darkmetal");
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int k = 0; k < mats.Length; k++) mats[k] = m;
            r.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// The headland climb: coastal grass and pampas at the verge, wind-clipped scrub masses,
    /// wind-bent pines in clumps (kept mostly UPHILL so the downhill side stays open to the view),
    /// stratified cliff rock on steep ground; density falls off with distance from the road.
    /// Ends at the lookout terrace + headland lighthouse.
    /// </summary>
    private static void BuildShoreHeadland(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Headland Climb").transform;
        parent.SetParent(root, false);
        var rng = new System.Random(13401901);
        int grass = 0, scrubs = 0, pines = 0, rocks = 0;
        for (float d = ShoreLandfallFromM + 30f; d < route.Length - 6f; d += 3f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            var fwd = ShoreForward(route, d);
            bool town = d < ShoreLandfallToM;
            bool finish = d > ShoreLookoutM - 40f;
            for (int s = -1; s <= 1; s += 2)
            {
                var probe = p + side * (s * 40f);
                bool downhill = ShoreSurfaceY(route, probe.x, probe.z, out _) < p.y - 4f;

                // near verge: grass tufts + pampas, 5-12 m
                if (rng.NextDouble() < 0.75)
                    for (int k = 0; k < 3; k++)
                    {
                        var q = p + side * (s * ShoreRand(rng, 5.3f, 12f)) + fwd * ShoreRand(rng, -1.5f, 1.5f);
                        if (!ShoreClearOfRoad(route, q, 5.0f, d)) continue;
                        float y = ShoreSurfaceY(route, q.x, q.z, out var n);
                        if (y < SeaLevelY + 0.8f || n.y < 0.5f) continue;
                        bool pam = rng.NextDouble() < 0.16;
                        ShoreQueue(pam ? _mPampas : _mGrass, new Vector3(q.x, y - 0.04f, q.z), ShoreYaw(rng),
                                   Vector3.one * ShoreRand(rng, 0.85f, 1.35f), d, pam ? 520f : 320f, false);
                        grass++;
                    }
                // mid band: scrub masses, 10-45 m
                if (rng.NextDouble() < (town ? 0.20 : 0.42))
                {
                    var c = p + side * (s * ShoreRand(rng, 10f, 45f)) + fwd * ShoreRand(rng, -2f, 2f);
                    for (int k = 0; k < 2 + rng.Next(3); k++)
                    {
                        var q = c + new Vector3(ShoreRand(rng, -3f, 3f), 0f, ShoreRand(rng, -3f, 3f));
                        if (!ShoreClearOfRoad(route, q, 7f, d)) continue;
                        float y = ShoreSurfaceY(route, q.x, q.z, out var n);
                        if (y < SeaLevelY + 0.8f || n.y < 0.45f) continue;
                        ShoreQueue(rng.NextDouble() < 0.6 ? _mScrubA : _mScrubB, new Vector3(q.x, y - 0.12f, q.z), ShoreYaw(rng),
                                   new Vector3(ShoreRand(rng, 0.9f, 1.8f), ShoreRand(rng, 0.8f, 1.3f), ShoreRand(rng, 0.9f, 1.8f)), d, 700f, true);
                        scrubs++;
                    }
                }
                // far band: scrub over the slopes, 45-320 m, thinning with distance
                if (rng.NextDouble() < 0.30)
                {
                    float off = ShoreRand(rng, 45f, 320f);
                    if (rng.NextDouble() < Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(45f, 320f, off)))
                    {
                        var c = p + side * (s * off) + fwd * ShoreRand(rng, -8f, 8f);
                        for (int k = 0; k < 3 + rng.Next(4); k++)
                        {
                            var q = c + new Vector3(ShoreRand(rng, -9f, 9f), 0f, ShoreRand(rng, -9f, 9f));
                            if (!ShoreClearOfRoad(route, q, 8f, d)) continue;
                            float y = ShoreSurfaceY(route, q.x, q.z, out var n);
                            if (y < SeaLevelY + 0.8f || n.y < 0.45f) continue;
                            ShoreQueue(rng.NextDouble() < 0.5 ? _mScrubA : _mScrubB, new Vector3(q.x, y - 0.15f, q.z), ShoreYaw(rng),
                                       new Vector3(ShoreRand(rng, 1.4f, 2.6f), ShoreRand(rng, 1.0f, 1.6f), ShoreRand(rng, 1.4f, 2.6f)), d, 1400f, false);
                            scrubs++;
                        }
                    }
                }
                // wind-bent pines in clumps; the downhill (view) side stays mostly open
                if (!town && !finish && rng.NextDouble() < (downhill ? 0.012 : 0.045))
                {
                    float off = ShoreRand(rng, 13f, 90f);
                    var c = p + side * (s * off) + fwd * ShoreRand(rng, -5f, 5f);
                    int clump = 2 + rng.Next(3);
                    for (int k = 0; k < clump; k++)
                    {
                        var q = c + new Vector3(ShoreRand(rng, -7f, 7f), 0f, ShoreRand(rng, -7f, 7f));
                        if (!ShoreClearOfRoad(route, q, 10f, d)) continue;
                        float y = ShoreSurfaceY(route, q.x, q.z, out var n);
                        if (y < SeaLevelY + 1f || n.y < 0.7f) continue;
                        if (ShorePine(rng.NextDouble() < 0.5 ? _mPineA : _mPineB, parent, new Vector3(q.x, y - 0.15f, q.z),
                                      ShoreRand(rng, 0f, 360f), ShoreRand(rng, 1.0f, 1.9f)) != null) pines++;
                    }
                }
                // stratified cliff rock where the ground is steep (the sea-cliff vocabulary)
                if (!town && rng.NextDouble() < 0.05)
                {
                    float off = ShoreRand(rng, 16f, 160f);
                    var q = p + side * (s * off) + fwd * ShoreRand(rng, -4f, 4f);
                    if (!ShoreClearOfRoad(route, q, 14f, d)) continue;
                    float y = ShoreSurfaceY(route, q.x, q.z, out var n);
                    if (n.y > 0.93f || y < SeaLevelY + 1f) continue;
                    // lean the strata INTO the slope
                    var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, n, 0.5f)) * ShoreYaw(rng);
                    bool big = off > 50f && rng.NextDouble() < 0.6;
                    ShorePlace(big ? _mCliffA : _mCliffB, parent, new Vector3(q.x, y - (big ? 2.2f : 1.2f), q.z), rot,
                               new Vector3(ShoreRand(rng, 0.8f, 1.5f), ShoreRand(rng, 0.8f, 1.3f), ShoreRand(rng, 0.8f, 1.5f)));
                    rocks++;
                }
            }
        }
        BuildShoreLookout(route, parent);
        Debug.Log($"[shores] headland {ShoreLandfallFromM:0}-{route.Length:0} m: {grass:N0} grass/pampas, {scrubs:N0} scrub, " +
                  $"{pines} wind-bent pines, {rocks} stratified cliff outcrops");
    }

    /// <summary>
    /// Panoramic finish: the lookout terrace on the downhill (bay) side right at the road edge,
    /// the compact headland lighthouse beside it on the point, benches/pampas around.
    /// </summary>
    private static void BuildShoreLookout(MinatoRoute route, Transform parent)
    {
        float d = Mathf.Min(ShoreLookoutM, route.Length - 60f);
        int i = route.IndexAt(d);
        var p = route.Position[i];
        var sideR = route.SideFlat(i);
        // the view side is whichever side falls away
        var l = p - sideR * 30f; var r = p + sideR * 30f;
        int s = ShoreSurfaceY(route, l.x, l.z, out _) < ShoreSurfaceY(route, r.x, r.z, out _) ? -1 : 1;
        var side = sideR * s;
        var pos = p + side * 5.2f; pos.y = p.y - 0.12f;
        ShorePlace(_mLookout, parent, pos, Quaternion.LookRotation(side, Vector3.up), 1f);

        float dl = Mathf.Min(ShoreFinishLighthouseM, route.Length - 5f);
        int il = route.IndexAt(dl);
        var pl = route.Position[il] + route.SideFlat(il) * (s * 24f);
        pl.y = ShoreSurfaceY(route, pl.x, pl.z, out _);
        ShorePlace(_mHeadLight, parent, new Vector3(pl.x, pl.y, pl.z),
                   Quaternion.LookRotation(-route.SideFlat(il) * s, Vector3.up), 1f);
        var rng = new System.Random(19010);
        for (int k = 0; k < 40; k++)
        {
            var q = pl + new Vector3(ShoreRand(rng, -14f, 14f), 0f, ShoreRand(rng, -14f, 14f));
            if ((q - pl).sqrMagnitude < 36f || !ShoreClearOfRoad(route, q, 6f, dl)) continue;
            float y = ShoreSurfaceY(route, q.x, q.z, out _);
            ShoreQueue(rng.NextDouble() < 0.5 ? _mPampas : _mGrass, new Vector3(q.x, y, q.z), ShoreYaw(rng),
                       Vector3.one * ShoreRand(rng, 0.9f, 1.3f), dl, 520f, false);
        }
        Debug.Log($"[shores] lookout terrace at {d:0} m ({(s < 0 ? "left" : "right")} / downhill side), " +
                  $"headland lighthouse at {dl:0} m");
    }

    // ================================================================ design probe

    /// <summary>
    /// Design probe (batch entry point, no scene changes): samples THE height function along the
    /// Shores zones and writes a top-down height map, so placement is designed against the real
    /// shoreline rather than guessed. Run: -executeMethod MinatoCoastEnvironment.ShoresProbe
    /// </summary>
    public static void ShoresProbe()
    {
        var route = MinatoRoute.Load();
        BuildLandform(route);
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath,
                                         "../reference/good_graphics/minato_shores"));
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        sb.AppendLine("d,x,y,z,seaSign,waterSeaward,waterLandward,g_s10,g_s20,g_s40,g_s80,g_l10,g_l20,g_l40,g_l80");
        for (float d = 1500f; d < route.Length; d += 50f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            float sgn = SeaSideSign(route, i);
            var sea = route.SideFlat(i) * sgn;
            float Water(Vector3 dir)
            {
                for (float o = 6f; o < 1500f; o += 3f)
                {
                    var q = p + dir * o;
                    if (GroundAt(route, q.x, q.z) < SeaLevelY + 0.3f) return o;
                }
                return -1f;
            }
            sb.Append($"{d:0},{p.x:0},{p.y:0.0},{p.z:0},{sgn:0},{Water(sea):0},{Water(-sea):0}");
            foreach (float o in new[] { 10f, 20f, 40f, 80f }) { var q = p + sea * o; sb.Append($",{GroundAt(route, q.x, q.z):0.0}"); }
            foreach (float o in new[] { 10f, 20f, 40f, 80f }) { var q = p - sea * o; sb.Append($",{GroundAt(route, q.x, q.z):0.0}"); }
            sb.AppendLine();
        }
        File.WriteAllText(Path.Combine(outDir, "probe_shoreline.csv"), sb.ToString());
        Debug.Log($"[shores-probe] wrote {outDir}");
    }
}
