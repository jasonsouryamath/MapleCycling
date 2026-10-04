using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using CityRoute = MapleCityEnvironment.CityRoute;

/// <summary>
/// MAPLE CITY LIFE - phase 1 of the Maple City overhaul (claude-city, 2026-09-25).
///
/// The user's brief: the city must feel ALIVE - pedestrians walking, coffee shops with people
/// chatting and drinking coffee - and it must reuse the existing Minato Coast NPCs rather than
/// invent new ones. So this pass adds, under the "Maple City Environment" root:
///
///  * PEDESTRIANS on both pavements, walking continuously round the closed 5 km loop in both
///    directions (never turning round, never popping), some in pairs.
///  * NEIGHBOURHOOD CAFES: a single-storey cafe front (glazing onto a lit interior, fascia sign,
///    striped awning) with a pavement terrace of bistro tables, seated customers lifting their
///    cups, a standing regular at the door and an A-frame menu board.
///  * STREET-CORNER CHATS: small groups of two or three standing and talking by the shopfronts.
///
/// REUSE, NOT REINVENTION. Every figure is a clone of a Minato Coast crowd figure that is
/// already in the saved scene (<see cref="MinatoCrowdPopulation"/> output): same approved
/// donor rigs, same LOD0 live skin / LOD1 baked 3D / cull LODGroup, same
/// <see cref="MinatoCrowdActor"/> gait and idle. This pass deliberately does NOT call
/// MinatoCrowdPopulation.Prepare(): in a fresh batch process Prepare() regenerates Minato's
/// baked crowd mesh folder, which would orphan every mesh reference Minato's own figures hold.
/// Cloning the scene's figures shares their assets and touches nothing in Minato.
///
/// Walkers get <see cref="MapleCityWalker"/> (where to be) on top of the actor (how to move);
/// cafe and chat figures get <see cref="MapleCityCafeGesture"/> (sip / talk / listen).
///
/// NOT MAPLE ROW. Copilot's C6 boutique street (route metres 450-700) gets no cafes or chats
/// from here; walkers do pass through it (it is a shopping street), steering round its street
/// furniture via the occupancy map below.
///
/// Run standalone (rebuilds only this group inside the saved city):
///   pwsh -NoProfile -File tools/unity/run_steps.ps1 "MapleCityLife.ApplyToScene|claude_city_life.log|1"
/// MapleCityEnvironment.Apply() also calls <see cref="Build"/> so a full city rebuild keeps it.
/// </summary>
public static partial class MapleCityLife
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string Dir = "Assets/Environment/MapleCity/Life";
    private const string TexDir = Dir + "/Textures";
    private const string MatDir = Dir + "/Materials";
    private const string MeshDir = Dir + "/Meshes";
    public const string GroupName = "Maple City Life";

    // Cross-section facts copied from MapleCityEnvironment (same values; they are private there).
    private const float PavementWidthM = 5.0f;
    private const float KerbHeightM = 0.14f;
    private const float CanalFromFrac = 0.06f, CanalToFrac = 0.25f, BridgeFrac = 0.92f;

    // Maple Row (copilot C6) plus a margin. Nothing but walkers enters it.
    private const float RowFromM = 430f, RowToM = 720f;

    private const int Seed = 20260925;

    // ---- density (PROVISIONAL, tuned from captures) ------------------------------------------
    /// <summary>Mean metres between walkers on one lane. 4 lanes x 5 km / 42 m ~ 480 walkers.</summary>
    // 2026-09-25 (claude-cowork) USER: "lived-in and bustling". Denser: ~670 walkers (was ~480),
    // more pairs, a chat group every ~60 m and a cafe every ~280 m. Watch frame time on the
    // Maple Row captures; back WalkerSpacingM off first if it costs too much.
    private const float WalkerSpacingM = 30f;
    private const float CompanionChance = 0.42f;
    private const float CafeSpacingM = 280f;
    private const float ChatSpacingM = 60f;

    /// <summary>Walking lanes, metres in from the kerb face. Street trees stand at 2.75 m.</summary>
    private static readonly float[] LaneFromKerbM = { 1.0f, 1.9f };

    /// <summary>
    /// The seated Minato donors face local -Z: their authored bench (MinatoCrowdPopulation.BenchMesh)
    /// puts the seat centre at z = -0.08 and the backrest at z = +0.17. Chairs here copy that frame
    /// exactly, so if a capture ever shows sitters facing away from their table, flip this.
    /// 2026-09-25 copilot: the first capture (life_cafe_*_terrace/front) showed every sitter facing
    /// its chair's backrest with its back to the table, so the donors face +Z. The chair frame is
    /// now always "backrest away from the table" and only the figure frame follows this flag.
    /// </summary>
    private const bool SitterFacesMinusZ = false;

    // =================================================================== entry points

    [MenuItem("MapleRide/Environment/Build Maple City Life", priority = 25)]
    public static void ApplyToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject root = null;
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == MapleCityEnvironment.RootName) { root = go; break; }
        if (root == null)
        {
            Debug.LogError($"[maple-life] no '{MapleCityEnvironment.RootName}' in the scene - run Build Maple City first.");
            return;
        }

        Build(root.transform, CityRoute.Load());
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[maple-life] saved '{scene.path}'.");
    }

    /// <summary>Idempotent: (re)builds the life group under <paramref name="cityRoot"/>.</summary>
    public static void Build(Transform cityRoot, CityRoute route)
    {
        if (cityRoot == null || route == null || route.Count < 2)
        {
            Debug.LogError("[maple-life] no city root / route.");
            return;
        }
        Directory.CreateDirectory(TexDir);
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(MeshDir);
        Mats.Clear();
        SharedMeshes.Clear();
        PrepareTextures();

        for (int k = cityRoot.childCount - 1; k >= 0; k--)
            if (cityRoot.GetChild(k).name == GroupName)
                UnityEngine.Object.DestroyImmediate(cityRoot.GetChild(k).gameObject);
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == GroupName) UnityEngine.Object.DestroyImmediate(go);
        // Meshes are NOT wiped any more (2026-09-26 copilot): other regions (FujiRidge.Town via
        // Clone) reference these assets, and every save overwrites in place (SaveMeshAsset), so
        // GUIDs stay stable across rebuilds.

        var group = new GameObject(GroupName).transform;
        group.SetParent(cityRoot, false);

        var occ = new Occupancy();
        // Ground clutter only: never the canopies, hanging lanterns or tram wire overhead.
        foreach (var n in new[] { "City Street Props", "City Tree Trunks", "Tram Poles", "Maple Gate" })
            occ.AddCombined(cityRoot, n);
        var row = FindChild(cityRoot, "Maple Row Boutiques");
        if (row != null) occ.AddPieces(row);
        Debug.Log($"[maple-life] occupancy map: {occ.Count} blocked 0.5 m cells" +
                  (row != null ? " (Maple Row included)." : " (no Maple Row group found)."));

        var cast = FindCast(cityRoot);
        Debug.Log($"[maple-life] Minato cast found: {cast.Walkers.Count} walkers, " +
                  $"{cast.Standers.Count} standers, {cast.Sitters.Count} sitters " +
                  $"({string.Join(", ", cast.Walkers.Concat(cast.Standers).Concat(cast.Sitters).Select(a => a.name))}).");

        var rng = new System.Random(Seed);
        _looks = new System.Random(Seed ^ 0x51F3);
        LookCounts.Clear();
        BottomPerScale.Clear();
        RoleCounts.Clear();
        _dressed = _capped = _runners = 0;
        var cafes = BuildCafes(group, route, occ, cast, rng);
        int chats = BuildChats(group, route, occ, cast, rng);
        int walkers = BuildWalkers(group, route, occ, cast, rng);
        // Pocket parks + fountains, zebra crossings, street clutter, facade life
        // (MapleCityLife.LivedIn.cs). Built after the figures, which have already claimed their cells.
        BuildLivedIn(group, cityRoot, route, occ);

        int renderers = group.GetComponentsInChildren<Renderer>(true).Length;
        Debug.Log($"[maple-life] built {cafes} cafes, {chats} street chats, {walkers} walkers, {_runners} runners; " +
                  $"{renderers} renderers under '{GroupName}'. Civilian looks on {_dressed} figures, " +
                  $"hair caps on {_capped}. Roles: " +
                  string.Join(", ", RoleCounts.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")) + ".");
        AssetDatabase.SaveAssets();
    }

    // =================================================================== route frame

    private static readonly MethodInfo HalfWidthFn = typeof(MapleCityEnvironment).GetMethod(
        "CarriagewayHalfWidth", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo RoadYFn = typeof(MapleCityEnvironment).GetMethod(
        "RoadY", BindingFlags.NonPublic | BindingFlags.Static);

    private static float HalfWidth(float frac) =>
        HalfWidthFn != null ? (float)HalfWidthFn.Invoke(null, new object[] { frac }) : 4.6f;

    private static float RoadY(CityRoute r, int i, float offset) =>
        RoadYFn != null ? (float)RoadYFn.Invoke(null, new object[] { r, i, offset }) : r.Position[i].y + 0.07f;

    /// <summary>A point on the pavement top: <paramref name="fromKerb"/> metres in from the kerb face.</summary>
    private static Vector3 PavementPoint(CityRoute r, int i, int side, float fromKerb)
    {
        float half = HalfWidth(r.Frac(i));
        var p = r.Position[i];
        var s = r.SideFlat(i);
        float o = side * (half + fromKerb);
        float y = RoadY(r, i, side * half) + KerbHeightM;
        return new Vector3(p.x + s.x * o, y, p.z + s.z * o);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-8f ? Vector3.forward : v.normalized; }

    private static bool InRow(float d) => d > RowFromM && d < RowToM;

    private static Transform FindChild(Transform root, string exact)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == exact) return t;
        return null;
    }

    // =================================================================== occupancy

    /// <summary>
    /// A 0.5 m XZ grid of ground-level clutter (trees, tram poles, vending machines, the old
    /// terrace props, Maple Row's lamps/planters/benches) so nothing here is placed through it.
    /// Built from the actual meshes in the scene, so it cannot drift from what was built.
    /// </summary>
    private sealed class Occupancy
    {
        private const float Cell = 0.5f;
        private readonly HashSet<long> _cells = new HashSet<long>();
        public int Count => _cells.Count;

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void MarkRect(Vector3 min, Vector3 max)
        {
            int x0 = Mathf.FloorToInt(min.x / Cell), x1 = Mathf.FloorToInt(max.x / Cell);
            int z0 = Mathf.FloorToInt(min.z / Cell), z1 = Mathf.FloorToInt(max.z / Cell);
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                _cells.Add(Key(x, z));
        }

        public void MarkDisc(Vector3 c, float r) =>
            MarkRect(c - new Vector3(r, 0f, r), c + new Vector3(r, 0f, r));

        public bool Free(Vector3 c, float r)
        {
            int x0 = Mathf.FloorToInt((c.x - r) / Cell), x1 = Mathf.FloorToInt((c.x + r) / Cell);
            int z0 = Mathf.FloorToInt((c.z - r) / Cell), z1 = Mathf.FloorToInt((c.z + r) / Cell);
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                if (_cells.Contains(Key(x, z))) return false;
            return true;
        }

        /// <summary>Rasterise the small triangles of the combined mesh object(s) with this exact name.</summary>
        public void AddCombined(Transform cityRoot, string objectName)
        {
            var g = FindChild(cityRoot, objectName);
            if (g == null) return;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var m = mf.transform.localToWorldMatrix;
                var v = mesh.vertices;
                var tri = mesh.triangles;
                for (int k = 0; k + 2 < tri.Length; k += 3)
                {
                    var a = m.MultiplyPoint3x4(v[tri[k]]);
                    var b = m.MultiplyPoint3x4(v[tri[k + 1]]);
                    var c = m.MultiplyPoint3x4(v[tri[k + 2]]);
                    var min = Vector3.Min(a, Vector3.Min(b, c));
                    var max = Vector3.Max(a, Vector3.Max(b, c));
                    // Skip anything huge (a gate lintel, a long kerb run): props are small.
                    if (max.x - min.x > 3f || max.z - min.z > 3f) continue;
                    MarkRect(min, max);
                }
            }
        }

        /// <summary>Every small separately-placed piece under a group (Maple Row's props).</summary>
        public void AddPieces(Transform group)
        {
            foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var b = TransformBounds(mesh.bounds, mf.transform.localToWorldMatrix);
                if (b.size.x > 4f || b.size.z > 4f) continue;   // facades, paving: not clutter
                if (b.size.y < 0.25f) continue;                   // flat decals / paving strips
                MarkRect(b.min, b.max);
            }
        }

    }

    private static Bounds TransformBounds(Bounds b, Matrix4x4 m)
    {
        var c = b.center; var e = b.extents;
        var r = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
        for (int k = 0; k < 8; k++)
        {
            var corner = c + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y,
                                         (k & 4) != 0 ? e.z : -e.z);
            r.Encapsulate(m.MultiplyPoint3x4(corner));
        }
        return r;
    }

    // =================================================================== the Minato cast

    private sealed class Cast
    {
        public readonly List<MinatoCrowdActor> Walkers = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> Standers = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> Sitters = new List<MinatoCrowdActor>();
        public bool Any => Walkers.Count + Standers.Count + Sitters.Count > 0;
    }

    /// <summary>One source figure per Minato crowd archetype key, found in the saved scene.</summary>
    private static Cast FindCast(Transform cityRoot)
    {
        var cast = new Cast();
        var seen = new HashSet<string>();
        var all = UnityEngine.Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include,
                                                                          FindObjectsSortMode.None);
        foreach (var a in all.OrderBy(x => x.name, StringComparer.Ordinal))
        {
            if (a == null || a.transform.IsChildOf(cityRoot)) continue;
            if (!a.name.StartsWith("Crowd_", StringComparison.Ordinal)) continue;
            if (a.transform.Find("LOD0 High Skinned/Rigged Character") == null) continue;
            if (!seen.Add(a.name)) continue;
            switch (a.motion)
            {
                case MinatoCrowdActor.MotionKind.Walk: cast.Walkers.Add(a); break;
                case MinatoCrowdActor.MotionKind.Idle:
                case MinatoCrowdActor.MotionKind.Wave: cast.Standers.Add(a); break;
                case MinatoCrowdActor.MotionKind.Sit: cast.Sitters.Add(a); break;
            }
        }
        if (!cast.Any)
            Debug.LogError("[maple-life] no Minato crowd figures in the scene to clone - build Minato Coast first. Cafes will be empty.");
        return cast;
    }

    /// <summary>
    /// Clone a Minato figure. The bench a Minato sitter brings is removed (cafe chairs replace it)
    /// and the LODGroup is rebuilt from what remains. Returns the clone, its live rig and the
    /// offset from its origin down to its lowest rendered point (for ground contact).
    /// </summary>
    private static GameObject Clone(MinatoCrowdActor src, Transform parent, string name, Vector3 pos,
                                    Quaternion rot, float scale, MinatoCrowdActor.MotionKind kind,
                                    float speed, float phase, out Transform rig, out float bottom)
    {
        var go = (GameObject)UnityEngine.Object.Instantiate(src.gameObject, parent);
        go.name = name;
        go.hideFlags = HideFlags.None;
        go.SetActive(true);
        var bench = go.transform.Find("Timber Waterfront Bench");
        if (bench != null) UnityEngine.Object.DestroyImmediate(bench.gameObject);

        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = Vector3.one * scale;

        var high = go.transform.Find("LOD0 High Skinned");
        var low = go.transform.Find("LOD1 Low 3D");
        rig = high != null ? high.Find("Rigged Character") : null;

        // Civilian look (QA #6): recoloured atlas + hair cap over the baked-in helmet.
        Dress(go, high, src.name.StartsWith("Crowd_", StringComparison.Ordinal) ? src.name.Substring(6) : src.name);
        // Soft contact shadow under anyone standing on the pavement (not the seated customers),
        // part of LOD0 so it culls with the figure. QA #9: the sun shadow of a small figure is
        // often hidden behind it from the road, which read as "hovering".
        if (kind != MinatoCrowdActor.MotionKind.Sit && high != null) ContactShadow(high);

        var group = go.GetComponent<LODGroup>();
        if (group != null && high != null && low != null)
        {
            group.SetLODs(new[]
            {
                new LOD(MinatoCrowdPopulation.HighDetailScreenHeight, high.GetComponentsInChildren<Renderer>(true)),
                new LOD(MinatoCrowdPopulation.LowDetailScreenHeight, low.GetComponentsInChildren<Renderer>(true)),
            });
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            group.RecalculateBounds();
        }

        // Lowest rendered point relative to the origin, for ground contact. 2026-09-25 copilot
        // (QA #9): this used the decimated LOD1 bake, whose soles sit ~2 cm ABOVE the LOD0 live
        // skin's, so every walker/stander was pushed 2 cm into the pavement. Measure the LOD0 skin
        // in its rest pose instead (the tier actually seen up close); LOD1 is the fallback.
        bottom = 0f;
        if (!BottomPerScale.TryGetValue(src, out float unit))
        {
            unit = float.NaN;
            float minY = float.PositiveInfinity;
            if (high != null)
                foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    var mtx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    foreach (var v in baked.vertices) minY = Mathf.Min(minY, mtx.MultiplyPoint3x4(v).y);
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            if (float.IsInfinity(minY) && low != null)
                foreach (var mf in low.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null)
                        minY = Mathf.Min(minY, TransformBounds(mf.sharedMesh.bounds, mf.transform.localToWorldMatrix).min.y);
            if (!float.IsInfinity(minY)) unit = (minY - pos.y) / Mathf.Max(scale, 1e-3f);
            BottomPerScale[src] = unit;
        }
        if (!float.IsNaN(unit)) bottom = unit * scale;

        var actor = go.GetComponent<MinatoCrowdActor>();
        if (actor != null && rig != null)
            actor.Configure(kind, rig, go.transform.position, go.transform.position, speed, phase);
        // Donors are A-posed with the arms held out ~40 deg; walkers and standers let them hang.
        // Sitters keep theirs (MapleCityCafeGesture drives the arm lifting the cup).
        if (actor != null) actor.armDropDegrees = kind == MinatoCrowdActor.MotionKind.Sit ? 0f : 32f;
        return go;
    }

    // =================================================================== civilian looks

    // 2026-09-25 copilot (QA #6): every Minato donor is a dismounted cyclist, and helmet, jersey,
    // bibs, gloves and cleats are all baked into ONE atlas on ONE fused mesh (no helmet slot or
    // sub-object to hide). So each clone gets one of a few per-donor "civilian" looks made by
    // tools/blender/build_maple_city_life_textures.py:
    //   MapleLife_Crowd_<key>_V<n>.png         recoloured atlas (top / trousers / hands / hair)
    //   MapleLife_Crowd_<key>_V<n>_Hair.png     recoloured NpcHair strip (donors that have one)
    //   MapleLife_Crowd_<key>_V<n>_HairCap.png  strand texture for the hair cap
    //   MapleLife_Crowd_<key>_Helmet.png        UV mask of the helmet shell
    // and a smooth hair cap is fitted over the helmet vertices and parented to the Head bone, so
    // the vented shell no longer reads as a helmet. Donor .glb materials are never edited: every
    // look is a CLONED material under MatDir. The picks use their own RNG so the street layout
    // (positions, cafes, chats) is exactly what it was before looks existed.
    private static System.Random _looks = new System.Random(Seed ^ 0x51F3);
    private static readonly Dictionary<string, int> LookCounts = new Dictionary<string, int>();
    private static readonly Dictionary<MinatoCrowdActor, float> BottomPerScale = new Dictionary<MinatoCrowdActor, float>();
    private static int _dressed, _capped;
    /// <summary>Look-atlas prefix other regions set while cloning (e.g. "W" = Azora winter). Null = V/R.</summary>
    internal static string LookPrefixOverride;

    /// <summary>
    /// Civilian-dress an already-instantiated crowd donor clone for another region (Azora winter
    /// townsfolk): look atlas with the given prefix (falls back to V), hair cap / beanie, and a
    /// contact shadow for anyone not seated. The clone's name decides its role (Walker_, Chat_...).
    /// </summary>
    internal static void DressForRegion(GameObject go, string donorName, string lookPrefix, bool seated)
    {
        var high = go.transform.Find("LOD0 High Skinned");
        string key = donorName.StartsWith("Crowd_", StringComparison.Ordinal) ? donorName.Substring(6) : donorName;
        LookPrefixOverride = lookPrefix;
        try { Dress(go, high, key); }
        finally { LookPrefixOverride = null; }
        if (!seated && high != null) ContactShadow(high);
        var low = go.transform.Find("LOD1 Low 3D");
        var lod = go.GetComponent<LODGroup>();
        if (lod != null && high != null && low != null)
        {
            lod.SetLODs(new[]
            {
                new LOD(MinatoCrowdPopulation.HighDetailScreenHeight, high.GetComponentsInChildren<Renderer>(true)),
                new LOD(MinatoCrowdPopulation.LowDetailScreenHeight, low.GetComponentsInChildren<Renderer>(true)),
            });
            lod.fadeMode = LODFadeMode.None;
            lod.animateCrossFading = false;
            lod.RecalculateBounds();
        }
    }

    /// <summary>A soft black ellipse 1.2 cm above the pavement at the figure's feet (LOD0 only).</summary>
    private static void ContactShadow(Transform high)
    {
        bool fresh = !Mats.ContainsKey("U_ContactShadow");
        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null || !shader.isSupported) return;
        var mat = Load("U_ContactShadow", shader);
        if (fresh)
        {
            if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", Tex("MapleLife_ContactShadow.png"));
            if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", new Color(0f, 0f, 0f, 0.42f));
            mat.SetFloat("_SurfaceType", 1f);          // transparent
            mat.SetFloat("_BlendMode", 0f);            // alpha
            if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 0f);
            if (mat.HasProperty("_TransparentZWrite")) mat.SetFloat("_TransparentZWrite", 0f);
            try { HDMaterial.ValidateMaterial(mat); }
            catch (Exception e) { Debug.LogWarning($"[maple-life] ValidateMaterial ContactShadow: {e.Message}"); }
            EditorUtility.SetDirty(mat);
        }
        var mb = new MeshBuilder();
        mb.Quad(new Vector3(-0.28f, 0f, -0.36f), new Vector3(0.56f, 0f, 0f), new Vector3(0f, 0f, 0.72f),
                Vector2.zero, Vector2.one);                      // faces +Y
        var go = Emit(high, "Contact Shadow", mb, mat, sharedName: "MapleLife_ContactShadow");
        if (go == null) return;
        GameObjectUtility.SetStaticEditorFlags(go, 0);           // it walks
        go.transform.localPosition = new Vector3(0f, 0.012f / Mathf.Max(high.lossyScale.y, 1e-3f), 0f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static int LookCount(string key, string prefix = "V")
    {
        string id = $"{key}_{prefix}";
        if (LookCounts.TryGetValue(id, out int n)) return n;
        n = 0;
        while (File.Exists($"{TexDir}/MapleLife_Crowd_{key}_{prefix}{n}.png")) n++;
        LookCounts[id] = n;
        if (n == 0 && prefix == "V") Debug.LogWarning($"[maple-life] no civilian looks for '{key}' - it keeps its cycling kit.");
        return n;
    }

    // C8: hair cap styles. 0/1 = fringe parted left/right, 2 = parted + ponytail.
    private const int HairStyles = 3;
    private static readonly Dictionary<MapleCityLook.Role, int> RoleCounts = new Dictionary<MapleCityLook.Role, int>();

    private static MapleCityLook.Role RoleOf(string name) =>
        name.StartsWith("Walker_", StringComparison.Ordinal) ? MapleCityLook.Role.Walker :
        name.StartsWith("Runner_", StringComparison.Ordinal) ? MapleCityLook.Role.Runner :
        name.StartsWith("Customer_", StringComparison.Ordinal) ? MapleCityLook.Role.Sitter :
        name.StartsWith("Regular_", StringComparison.Ordinal) ? MapleCityLook.Role.CafeRegular :
        name.StartsWith("Chat_", StringComparison.Ordinal) ? MapleCityLook.Role.Chat : MapleCityLook.Role.Other;

    private static void Dress(GameObject go, Transform high, string key)
    {
        var role = RoleOf(go.name);
        var tag = go.GetComponent<MapleCityLook>();
        if (tag == null) tag = go.AddComponent<MapleCityLook>();
        tag.role = role;
        tag.donor = key;
        tag.look = "";
        tag.civilianAtlas = false;
        tag.hairCap = null;
        RoleCounts[role] = RoleCounts.TryGetValue(role, out int rc) ? rc + 1 : 1;

        // Runners wear running kit (R looks) when the donor has them, else a civilian look.
        string prefix = role == MapleCityLook.Role.Runner && LookCount(key, "R") > 0 ? "R" : "V";
        // Other regions can re-dress the same donors (Azora winter "W" looks), falling back to V.
        if (!string.IsNullOrEmpty(LookPrefixOverride) && LookCount(key, LookPrefixOverride) > 0) prefix = LookPrefixOverride;
        int n = LookCount(key, prefix);
        if (n == 0) return;
        int v = _looks.Next(n);
        int style = _looks.Next(HairStyles);
        string look = $"{prefix}{v}";
        var body = Tex($"MapleLife_Crowd_{key}_{look}.png");
        if (body == null) return;
        string hairPath = $"{TexDir}/MapleLife_Crowd_{key}_{look}_Hair.png";
        var hair = File.Exists(hairPath) ? AssetDatabase.LoadAssetAtPath<Texture>(hairPath) : null;

        Material hairMat = null, bodyMat = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                Texture t;
                if (m.HasProperty("baseColorTexture") && (t = m.GetTexture("baseColorTexture")) != null)
                {
                    if (t.name.StartsWith("KuroKit_", StringComparison.Ordinal))
                    {
                        mats[i] = bodyMat = LookMat($"Crowd_{key}_{look}_Body", m, "baseColorTexture", body);
                        changed = true;
                    }
                    else if (hair != null && t.name.StartsWith("NpcHair_", StringComparison.Ordinal))
                    {
                        mats[i] = hairMat = LookMat($"Crowd_{key}_{look}_Hair", m, "baseColorTexture", hair);
                        changed = true;
                    }
                }
                else if (m.HasProperty("_MainTex") && (t = m.GetTexture("_MainTex")) != null &&
                         t.name.EndsWith("_Atlas_Distance", StringComparison.Ordinal))
                {
                    // LOD1: keep the distance clone's tint and cel floors, swap only the atlas.
                    mats[i] = LookMat($"Crowd_{key}_{look}_Dist", m, "_MainTex", body);
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
        _dressed++;
        tag.look = look;
        tag.civilianAtlas = bodyMat != null;
        if (high != null)
        {
            tag.hairCap = HairCap(high, key, look, style, hairMat ?? bodyMat);
            if (tag.hairCap != null)
            {
                _capped++;
                SinkHelmet(high, key);
            }
        }
    }

    /// <summary>
    /// C8: swap the live skin for a per-donor copy whose helmet shell is pulled 18% toward the
    /// skull centre, inside the hair cap. The cap is a MeshRenderer and the skin is a
    /// SkinnedMeshRenderer, so any frame where the two disagree (a capture that renders mid-frame
    /// once showed the vented shell through it) now reveals hair-tinted scalp, never a helmet.
    /// Call after <see cref="HairCap"/>, which fits the cap to the unsunk shell.
    /// </summary>
    private static void SinkHelmet(Transform high, string key)
    {
        SkinnedMeshRenderer smr = null;
        foreach (var s in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.name == "Mesh_0") { smr = s; break; }
        if (smr == null || smr.sharedMesh == null) return;
        var src = smr.sharedMesh;
        if (src.name.StartsWith("MapleLife_Helmetless_", StringComparison.Ordinal)) return;
        string name = $"MapleLife_Helmetless_{key}";
        if (!SharedMeshes.TryGetValue(name, out var mesh))
        {
            SharedMeshes[name] = mesh = null;
            int head = Array.FindIndex(smr.bones, b => b != null && b.name == "Head");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/MapleLife_Crowd_{key}_Helmet.png");
            if (head >= 0 && src.isReadable && mask != null && mask.isReadable)
            {
                var verts = src.vertices;
                var uvs = src.uv;
                var bw = src.boneWeights;
                var centre = Vector3.zero;
                int nHead = 0;
                for (int i = 0; i < verts.Length; i++)
                    if (bw[i].boneIndex0 == head && bw[i].weight0 >= 0.6f) { centre += verts[i]; nHead++; }
                int sunk = 0;
                if (nHead > 0)
                {
                    centre /= nHead;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        if (bw[i].boneIndex0 != head || bw[i].weight0 < 0.6f) continue;
                        if (mask.GetPixelBilinear(uvs[i].x, uvs[i].y).r < 0.5f) continue;
                        // 0.72 (was 0.82): the cap now fits at CapScale 0.88, so the shell must
                        // sit well inside it or its vents show through (2026-09-25 close-ups)
                        verts[i] = centre + (verts[i] - centre) * 0.72f;
                        sunk++;
                    }
                }
                if (sunk > 0)
                {
                    mesh = UnityEngine.Object.Instantiate(src);
                    mesh.name = name;
                    mesh.vertices = verts;
                    mesh.RecalculateBounds();
                    mesh = SaveMeshAsset(mesh, $"{MeshDir}/{name}.asset");
                    SharedMeshes[name] = mesh;
                    Debug.Log($"[maple-life] helmet sunk {key}: {sunk} shell verts inside the hair cap.");
                }
            }
            if (mesh == null) Debug.LogWarning($"[maple-life] helmet sink {key}: skipped (mesh/mask not readable).");
        }
        if (mesh != null) smr.sharedMesh = mesh;
    }

    /// <summary>
    /// Writes <paramref name="mesh"/> to <paramref name="path"/>, overwriting an existing asset IN
    /// PLACE so its GUID survives (2026-09-26 copilot). The old DeleteAsset+CreateAsset minted new
    /// GUIDs, and FujiRidge.Town's clones (made through Clone) and the Maple City figures kept
    /// orphaning each other's helmetless bodies / hair caps / contact shadows. Returns the asset.
    /// </summary>
    private static Mesh SaveMeshAsset(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        if (existing == mesh) return existing;
        string keepName = existing.name;
        CopyMeshInto(mesh, existing);
        existing.name = keepName;
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        return existing;
    }

    /// <summary>
    /// Copies geometry, skinning, blend shapes and bounds through the Mesh API. NOT
    /// EditorUtility.CopySerialized: for the multi-stream skinned donor meshes it kept the vertex
    /// layout but dropped the vertex data (0 vertices saved, invisible bodies - 2026-09-26).
    /// </summary>
    private static void CopyMeshInto(Mesh src, Mesh dst)
    {
        dst.Clear();
        dst.indexFormat = src.indexFormat;
        dst.SetVertices(src.vertices);
        if (src.HasVertexAttribute(VertexAttribute.Normal)) dst.SetNormals(src.normals);
        if (src.HasVertexAttribute(VertexAttribute.Tangent)) dst.SetTangents(src.tangents);
        if (src.HasVertexAttribute(VertexAttribute.Color)) dst.SetColors(src.colors32);
        for (int ch = 0; ch < 8; ch++)
        {
            var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
            if (!src.HasVertexAttribute(attr)) continue;
            int dim = src.GetVertexAttributeDimension(attr);
            if (dim <= 2) { var l = new List<Vector2>(); src.GetUVs(ch, l); dst.SetUVs(ch, l); }
            else if (dim == 3) { var l = new List<Vector3>(); src.GetUVs(ch, l); dst.SetUVs(ch, l); }
            else { var l = new List<Vector4>(); src.GetUVs(ch, l); dst.SetUVs(ch, l); }
        }
        dst.bindposes = src.bindposes;
        var weights = src.GetAllBoneWeights();
        if (weights.Length > 0) dst.SetBoneWeights(src.GetBonesPerVertex(), weights);
        dst.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++)
            dst.SetIndices(src.GetIndices(s), src.GetTopology(s), s, false);
        if (src.blendShapeCount > 0)
        {
            int n = src.vertexCount;
            var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
            for (int b = 0; b < src.blendShapeCount; b++)
                for (int f = 0; f < src.GetBlendShapeFrameCount(b); f++)
                {
                    src.GetBlendShapeFrameVertices(b, f, dv, dn, dt);
                    dst.AddBlendShapeFrame(src.GetBlendShapeName(b), src.GetBlendShapeFrameWeight(b, f), dv, dn, dt);
                }
        }
        dst.bounds = src.bounds;
    }

    /// <summary>A persisted clone of <paramref name="src"/> with one texture swapped. Idempotent.</summary>
    private static Material LookMat(string name, Material src, string texProp, Texture tex)
    {
        if (Mats.TryGetValue(name, out var cached) && cached != null) return cached;
        string path = $"{MatDir}/MapleLife_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(src); AssetDatabase.CreateAsset(mat, path); }
        else { mat.shader = src.shader; mat.CopyPropertiesFromMaterial(src); }
        mat.name = $"MapleLife_{name}";
        mat.enableInstancing = true;
        mat.SetTexture(texProp, tex);
        // The donors' LOD0 materials are the RAW glTF metallic shader: it mirrors the sky, so the
        // crowd's skin rendered icy blue-white and white shoes glowed ("ghost" pedestrians in the
        // 2026-09-25 review). Convert this private clone to the matte CelLit character recipe,
        // which carries the new texture across and zeroes the world-weathering noise.
        if (NpcCelLitConversion.ConvertInPlace(mat)) mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        Mats[name] = mat;
        return mat;
    }

    /// <summary>
    /// Fit a smooth hair cap over the helmet vertices of the live skin and parent it to the Head
    /// bone (vertices are stored in Head-bone space via the bindpose, so it follows the head
    /// exactly like the Head-weighted helmet under it). One mesh per donor, shared by its clones.
    /// </summary>
    private static Renderer HairCap(Transform high, string key, string look, int style, Material like)
    {
        SkinnedMeshRenderer smr = null;
        foreach (var s in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.name == "Mesh_0") { smr = s; break; }
        if (smr == null || like == null) return null;
        var bones = smr.bones;
        int head = Array.FindIndex(bones, b => b != null && b.name == "Head");
        if (head < 0) return null;
        var mesh = HairCapMesh(key, style, smr, head, high.parent != null ? high.parent : high);
        if (mesh == null) return null;

        var capTex = Tex($"MapleLife_Crowd_{key}_{look}_HairCap.png");
        var mat = LookMat($"Crowd_{key}_{look}_HairCap", like, "baseColorTexture", capTex);
        // The cap has its own UVs: never sample the body's normal / metal-rough maps through them.
        foreach (var p in new[] { "normalTexture", "metallicRoughnessTexture", "occlusionTexture" })
            if (mat.HasProperty(p)) mat.SetTexture(p, null);
        if (mat.HasProperty("metallicFactor")) mat.SetFloat("metallicFactor", 0f);
        if (mat.HasProperty("roughnessFactor")) mat.SetFloat("roughnessFactor", 0.8f);
        if (mat.HasProperty("baseColorFactor")) mat.SetColor("baseColorFactor", Color.white);
        // Solid hair colour instead of the strand texture: its high-contrast stripes made every
        // cap read as a knit beanie / striped ball (2026-09-25 review). The cel shading supplies
        // the form; the colour is the strand texture's own average, so each look keeps its hair.
        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTexture("_MainTex", null);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", AverageColor($"{TexDir}/MapleLife_Crowd_{key}_{look}_HairCap.png") * 0.9f);
        }
        EditorUtility.SetDirty(mat);

        var cap = new GameObject("Hair Cap", typeof(MeshFilter), typeof(MeshRenderer));
        cap.transform.SetParent(bones[head], false);
        cap.transform.localPosition = Vector3.zero;
        cap.transform.localRotation = Quaternion.identity;
        cap.transform.localScale = Vector3.one;
        cap.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = cap.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;
        mr.lightProbeUsage = LightProbeUsage.BlendProbes;
        return mr;
    }

    private const float CapScale = 0.88f;

    private static readonly Dictionary<string, Color> AvgColors = new Dictionary<string, Color>();

    /// <summary>Mean colour of the opaque pixels of a PNG on disk (cached per build).</summary>
    private static Color AverageColor(string assetPath)
    {
        if (AvgColors.TryGetValue(assetPath, out var c)) return c;
        c = new Color(0.22f, 0.16f, 0.12f, 1f);   // dark brown fallback
        string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        if (File.Exists(full))
        {
            var t = new Texture2D(2, 2);
            if (t.LoadImage(File.ReadAllBytes(full)))
            {
                var px = t.GetPixels32();
                double r = 0, g = 0, b = 0; int n = 0;
                for (int i = 0; i < px.Length; i += 3)
                {
                    if (px[i].a < 128) continue;
                    r += px[i].r; g += px[i].g; b += px[i].b; n++;
                }
                if (n > 0) c = new Color((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(b / n / 255.0), 1f);
            }
            UnityEngine.Object.DestroyImmediate(t);
        }
        AvgColors[assetPath] = c;
        return c;
    }

    private static Mesh HairCapMesh(string key, int style, SkinnedMeshRenderer smr, int head, Transform figure)
    {
        string name = $"MapleLife_HairCap_{key}_S{style}";
        if (SharedMeshes.TryGetValue(name, out var cached)) return cached;
        SharedMeshes[name] = null;   // one attempt per build, even if it fails

        var src = smr.sharedMesh;
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/MapleLife_Crowd_{key}_Helmet.png");
        if (src == null || !src.isReadable || mask == null || !mask.isReadable)
        {
            Debug.LogWarning($"[maple-life] hair cap {key}: mesh/mask not readable - skipped.");
            return null;
        }
        var verts = src.vertices;
        var uvs = src.uv;
        var bw = src.boneWeights;
        // Mesh-space frame: up/forward of the figure expressed in the skin's bind space.
        var up = smr.transform.InverseTransformDirection(figure.up).normalized;
        var fwd = Vector3.ProjectOnPlane(smr.transform.InverseTransformDirection(figure.forward), up).normalized;
        var right = Vector3.Cross(up, fwd);
        var pts = new List<Vector3>();
        for (int i = 0; i < verts.Length; i++)
        {
            if (bw[i].boneIndex0 != head || bw[i].weight0 < 0.6f) continue;
            if (mask.GetPixelBilinear(uvs[i].x, uvs[i].y).r < 0.5f) continue;
            var p = verts[i];
            pts.Add(new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, fwd)));
        }
        if (pts.Count < 200)
        {
            Debug.LogWarning($"[maple-life] hair cap {key}: only {pts.Count} helmet vertices - skipped.");
            return null;
        }
        Vector3 lo = pts[0], hi = pts[0];
        foreach (var p in pts) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        var c = (lo + hi) * 0.5f;
        c.y = hi.y - 0.5f * Mathf.Max(hi.x - lo.x, hi.z - lo.z);   // crown one half-width above

        const int P = 22, A = 44;
        var r = new float[P, A];
        var has = new bool[P, A];
        var rAll = new float[P, A];                   // outermost Head vertex of ANY colour per cell
        int Row(Vector3 d, float len) => Mathf.Clamp((int)(Mathf.Acos(Mathf.Clamp(d.y / len, -1f, 1f)) / Mathf.PI * P), 0, P - 1);
        int Col(Vector3 d) => (int)(Mathf.Repeat(Mathf.Atan2(d.z, d.x), 2f * Mathf.PI) / (2f * Mathf.PI) * A) % A;
        for (int i = 0; i < verts.Length; i++)
        {
            if (bw[i].boneIndex0 != head || bw[i].weight0 < 0.6f) continue;
            var p = verts[i];
            var d = new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, fwd)) - c;
            float len = d.magnitude;
            if (len < 1e-5f) continue;
            int pi = Row(d, len), ai = Col(d);
            rAll[pi, ai] = Mathf.Max(rAll[pi, ai], len);
        }
        foreach (var p in pts)
        {
            var d = p - c;
            float len = d.magnitude;
            if (len < 1e-5f) continue;
            int pi = Row(d, len), ai = Col(d);
            if (!has[pi, ai] || len > r[pi, ai]) r[pi, ai] = len;
            has[pi, ai] = true;
        }
        // Close vents/gaps: dilate twice, erode twice (azimuth wraps; the crown row is one cell).
        bool[,] Morph(bool[,] g, bool dilate)
        {
            var o = new bool[P, A];
            for (int pi = 0; pi < P; pi++)
            for (int ai = 0; ai < A; ai++)
            {
                bool acc = !dilate;
                for (int dp = -1; dp <= 1; dp++)
                for (int da = -1; da <= 1; da++)
                {
                    int q = pi + dp;
                    bool val = q < 0 ? true : q >= P ? false : g[q, (ai + da + A) % A];
                    acc = dilate ? (acc | val) : (acc & val);
                }
                o[pi, ai] = acc;
            }
            return o;
        }
        // Clean the coverage: an opening drops stray specks (UV-edge leaks), a closing fills the
        // vents; the crown row is one cell. Then keep, per azimuth column, only the run that is
        // contiguous with the crown and extend it two rows to swallow the helmet's rim band
        // (a stripe/trim colour the crown-colour mask misses), but never past the brow at the
        // front, the ears at the sides or the nape at the back. Extension rows take the radius of
        // the outermost head vertex there, so nothing under the cap pokes through.
        var cov = Morph(Morph(has, false), true);
        cov = Morph(Morph(cov, true), true);
        cov = Morph(Morph(cov, false), false);
        for (int ai = 0; ai < A; ai++) cov[0, ai] = true;
        var rim = new int[A];
        for (int ai = 0; ai < A; ai++)
        {
            int run = 0;
            while (run < P && cov[run, ai]) run++;
            float az = (ai + 0.5f) * 360f / A;                    // 90 = forward, 270 = back
            int limit = Mathf.Abs(Mathf.DeltaAngle(az, 90f)) < 55f ? 13
                      : Mathf.Abs(Mathf.DeltaAngle(az, 270f)) < 60f ? 18 : 15;
            rim[ai] = Mathf.Min(run + 2, limit);
            // C8: part the fringe (a notch two columns wide, one side of centre) so the front
            // reads as a hair-cut, not a helmet brim.
            float dPart = Mathf.Abs(Mathf.DeltaAngle(az, style == 1 ? 90f - 24f : 90f + 24f));
            if (dPart < 20f && rim[ai] > 6) rim[ai] -= dPart < 9f ? 2 : 1;
            for (int q = 0; q < P; q++)
            {
                cov[q, ai] = q < rim[ai];
                if (q >= run && q < rim[ai] && rAll[q, ai] > 0f)
                {
                    r[q, ai] = has[q, ai] ? Mathf.Max(r[q, ai], rAll[q, ai]) : rAll[q, ai];
                    has[q, ai] = true;
                }
            }
        }
        // Radii for the filled cells: grow from measured neighbours.
        for (int it = 0; it < 8; it++)
        {
            var nr = (float[,])r.Clone();
            var nh = (bool[,])has.Clone();
            for (int pi = 0; pi < P; pi++)
            for (int ai = 0; ai < A; ai++)
            {
                if (!cov[pi, ai] || has[pi, ai]) continue;
                float s = 0f; int k = 0;
                for (int dp = -1; dp <= 1; dp++)
                for (int da = -1; da <= 1; da++)
                {
                    int q = pi + dp;
                    if (q < 0 || q >= P) continue;
                    int b = (ai + da + A) % A;
                    if (has[q, b]) { s += r[q, b]; k++; }
                }
                if (k > 0) { nr[pi, ai] = s / k; nh[pi, ai] = true; }
            }
            r = nr; has = nh;
        }
        // Smooth outward: local max, then a blur, never inside the shell, plus a small margin.
        var rs = new float[P, A];
        for (int pi = 0; pi < P; pi++)
        for (int ai = 0; ai < A; ai++)
        {
            if (!cov[pi, ai] || !has[pi, ai]) continue;
            float mx = 0f, s = 0f; int k = 0;
            for (int dp = -1; dp <= 1; dp++)
            for (int da = -1; da <= 1; da++)
            {
                int q = pi + dp;
                if (q < 0 || q >= P) continue;
                int b = (ai + da + A) % A;
                if (!has[q, b]) continue;
                mx = Mathf.Max(mx, r[q, b]);
                s += r[q, b]; k++;
            }
            // 0.88 (was 1.02): fitted to the helmet's outer shell, the cap made every pedestrian a
            // big striped dome (2026-09-25 review). SinkHelmet pulls the shell to 0.82, so 0.88
            // still covers it while giving a head-sized hairdo.
            rs[pi, ai] = Mathf.Max(r[pi, ai], 0.35f * mx + 0.65f * s / k) * CapScale;
        }
        int lastRow = 0;
        for (int pi = 0; pi < P; pi++)
        for (int ai = 0; ai < A; ai++)
            if (cov[pi, ai] && has[pi, ai]) lastRow = Mathf.Max(lastRow, pi);

        // Corner grid (P+1 x A); a corner exists when any adjacent cell is covered.
        var cornerIdx = new int[P + 1, A];
        var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>();
        var bind = src.bindposes[head];
        Vector3 ToMesh(Vector3 q) => right * q.x + up * q.y + fwd * q.z;
        for (int pi = 0; pi <= P; pi++)
        for (int ai = 0; ai < A; ai++)
        {
            float s = 0f; int k = 0;
            for (int dp = -1; dp <= 0; dp++)
            for (int da = -1; da <= 0; da++)
            {
                int q = pi + dp;
                if (q < 0 || q >= P) continue;
                int b = (ai + da + A) % A;
                if (cov[q, b] && has[q, b]) { s += rs[q, b]; k++; }
            }
            cornerIdx[pi, ai] = -1;
            if (k == 0) continue;
            float pol = pi * Mathf.PI / P, az = ai * 2f * Mathf.PI / A;
            var dir = new Vector3(Mathf.Sin(pol) * Mathf.Cos(az), Mathf.Cos(pol), Mathf.Sin(pol) * Mathf.Sin(az));
            cornerIdx[pi, ai] = V.Count;
            V.Add(bind.MultiplyPoint3x4(ToMesh(c + dir * (s / k))));
            N.Add(bind.MultiplyVector(ToMesh(dir)).normalized);
            UV.Add(new Vector2(ai / (float)A * 5f, Mathf.Clamp01(pi / (float)(lastRow + 1))));
        }
        var T = new List<int>();
        for (int pi = 0; pi < P; pi++)
        for (int ai = 0; ai < A; ai++)
        {
            if (!cov[pi, ai] || !has[pi, ai]) continue;
            int a0 = cornerIdx[pi, ai], a1 = cornerIdx[pi, (ai + 1) % A];
            int b0 = cornerIdx[pi + 1, ai], b1 = cornerIdx[pi + 1, (ai + 1) % A];
            if (a0 < 0 || a1 < 0 || b0 < 0 || b1 < 0) continue;
            T.Add(a0); T.Add(a1); T.Add(b1);
            T.Add(a0); T.Add(b1); T.Add(b0);
        }
        // C8: ragged strand tips hanging off the whole rim (no clean bowl-cut edge). Short over
        // the brow, longer at the sides and nape; never inside the head (outermost vertex there).
        for (int ai = 0; ai < A; ai++)
        {
            int rr = rim[ai];
            if (rr <= 0 || rr >= P || !cov[rr - 1, ai] || !has[rr - 1, ai]) continue;
            int b0 = cornerIdx[rr, ai], b1 = cornerIdx[rr, (ai + 1) % A];
            if (b0 < 0 || b1 < 0) continue;
            float az = (ai + 0.5f) * 360f / A;
            bool front = Mathf.Abs(Mathf.DeltaAngle(az, 90f)) < 50f;
            float len = (front ? 0.45f : 0.8f) + 0.45f * (((ai * 7 + style * 3) % 3) / 2f);
            float pol = Mathf.Min(P - 0.5f, rr + len) * Mathf.PI / P, azr = (ai + 0.5f) * 2f * Mathf.PI / A;
            var dir = new Vector3(Mathf.Sin(pol) * Mathf.Cos(azr), Mathf.Cos(pol), Mathf.Sin(pol) * Mathf.Sin(azr));
            float rad = rs[rr - 1, ai] * 0.985f;
            int tr = Mathf.Min(P - 1, Mathf.RoundToInt(rr + len));
            if (rAll[tr, ai] > 0f) rad = Mathf.Max(rad, rAll[tr, ai] * 1.012f);
            int tip = V.Count;
            V.Add(bind.MultiplyPoint3x4(ToMesh(c + dir * rad)));
            N.Add(bind.MultiplyVector(ToMesh(dir)).normalized);
            UV.Add(new Vector2((ai + 0.5f) / A * 5f, 1f));
            T.Add(b0); T.Add(b1); T.Add(tip);
        }
        // Face every triangle outward (away from the cap centre). No inward copy: the donor
        // materials are double-sided, so a coincident reversed shell z-fights and renders dark.
        var cm = bind.MultiplyPoint3x4(ToMesh(c));
        for (int t = 0; t + 2 < T.Count; t += 3)
        {
            var p0 = V[T[t]]; var p1 = V[T[t + 1]]; var p2 = V[T[t + 2]];
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), (p0 + p1 + p2) / 3f - cm) < 0f)
            { int sw = T[t + 1]; T[t + 1] = T[t + 2]; T[t + 2] = sw; }
        }
        if (style == 2)
        {
            // C8: a short ponytail tied at the back of the head, sweeping down and back.
            float R = 0f; int kr = 0;
            for (int ai = 0; ai < A; ai++)
                if (cov[8, ai] && has[8, ai]) { R += rs[8, ai]; kr++; }
            R = kr > 0 ? R / kr : 0.1f;
            float pol0 = 0.47f * Mathf.PI, az0 = 1.5f * Mathf.PI;          // az 270 = back
            var baseDir = new Vector3(Mathf.Sin(pol0) * Mathf.Cos(az0), Mathf.Cos(pol0), Mathf.Sin(pol0) * Mathf.Sin(az0));
            var root = c + baseDir * R * 0.93f;
            var axis = new Vector3(0f, -0.62f, -0.78f).normalized;
            float L = 1.15f * R;
            const int Rings = 7, Sides = 8;
            var side1 = Vector3.right;
            int start = V.Count;
            var ringCentre = new Vector3[Rings + 1];
            for (int k = 0; k <= Rings; k++)
            {
                float t = k / (float)Rings;
                var ctr = root + axis * (L * t) + Vector3.down * (L * 0.30f * t * t);
                ringCentre[k] = ctr;
                var tan = (axis + Vector3.down * (0.6f * t)).normalized;
                var side2 = Vector3.Cross(tan, side1).normalized;
                float rad = R * (k == 1 ? 0.13f : 0.07f + 0.17f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.15f + 0.08f)) * (1f - 0.55f * t));
                if (k == Rings) rad = R * 0.02f;
                for (int sI = 0; sI < Sides; sI++)
                {
                    float ang = sI * 2f * Mathf.PI / Sides;
                    var nrm = side1 * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
                    V.Add(bind.MultiplyPoint3x4(ToMesh(ctr + nrm * rad)));
                    N.Add(bind.MultiplyVector(ToMesh(nrm)).normalized);
                    UV.Add(new Vector2(sI / (float)Sides * 2f, 0.3f + 0.7f * t));
                }
            }
            for (int k = 0; k < Rings; k++)
            for (int sI = 0; sI < Sides; sI++)
            {
                int a0 = start + k * Sides + sI, a1 = start + k * Sides + (sI + 1) % Sides;
                int b0 = a0 + Sides, b1 = a1 + Sides;
                var axisPt = bind.MultiplyPoint3x4(ToMesh((ringCentre[k] + ringCentre[k + 1]) * 0.5f));
                foreach (var (x, y, z) in new[] { (a0, a1, b1), (a0, b1, b0) })
                {
                    var q0 = V[x]; var q1 = V[y]; var q2 = V[z];
                    bool outward = Vector3.Dot(Vector3.Cross(q1 - q0, q2 - q0), (q0 + q1 + q2) / 3f - axisPt) >= 0f;
                    T.Add(x); if (outward) { T.Add(y); T.Add(z); } else { T.Add(z); T.Add(y); }
                }
            }
        }
        int nt = T.Count;

        var m = new Mesh { name = name };
        m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        m = SaveMeshAsset(m, $"{MeshDir}/{name}.asset");
        SharedMeshes[name] = m;
        Debug.Log($"[maple-life] hair cap {key}: {pts.Count} helmet verts -> {nt / 3} tris, rim row " +
                  $"front {rim[A / 4]} / back {rim[3 * A / 4]} / sides {rim[0]},{rim[A / 2]} of {P}.");
        return m;
    }

    // =================================================================== walkers

    private static int BuildWalkers(Transform group, CityRoute route, Occupancy occ, Cast cast,
                                    System.Random rng)
    {
        var holder = new GameObject("Pedestrians").transform;
        holder.SetParent(group, false);
        var lanesComp = holder.gameObject.AddComponent<MapleCityWalkLanes>();

        var lanes = new List<MapleCityWalkLanes.Lane>();
        var laneHeading = new List<int>();
        int nudged = 0;
        foreach (int side in new[] { -1, 1 })
        for (int li = 0; li < LaneFromKerbM.Length; li++)
        {
            var lane = BuildLane(route, side, LaneFromKerbM[li], occ, ref nudged);
            lanes.Add(lane);
            // Inner and outer lanes walk opposite ways, mirrored across the street.
            laneHeading.Add(((li == 0) ^ (side > 0)) ? 1 : -1);
        }
        lanesComp.lanes = lanes.ToArray();
        Debug.Log($"[maple-life] {lanes.Count} walking lanes, {lanes.Sum(l => l.length) / 1000f:0.0} km; " +
                  $"{nudged} lane samples steered round street clutter.");

        if (cast.Walkers.Count == 0) return 0;
        int count = 0;
        for (int l = 0; l < lanes.Count; l++)
        {
            float s = (float)rng.NextDouble() * WalkerSpacingM;
            while (s < lanes[l].length)
            {
                int n = rng.NextDouble() < CompanionChance ? 2 : 1;
                float speed = Mathf.Lerp(0.68f, 1.02f, (float)rng.NextDouble());
                for (int k = 0; k < n; k++)
                {
                    var src = cast.Walkers[rng.Next(cast.Walkers.Count)];
                    // Companions walk side by side, slightly staggered, at the same pace.
                    float lateral = n == 1 ? ((float)rng.NextDouble() - 0.5f) * 0.3f
                                           : (k == 0 ? -0.28f : 0.28f);
                    float start = s - k * 0.35f;
                    var pos = lanesComp.Sample(l, start, out var dir);
                    float scale = 0.94f + (float)rng.NextDouble() * 0.16f;
                    var go = Clone(src, holder, $"Walker_{l}_{count:000}_{src.name.Substring(6)}", pos,
                                   Quaternion.LookRotation(dir, Vector3.up), scale,
                                   MinatoCrowdActor.MotionKind.Walk, speed, (float)rng.NextDouble(),
                                   out _, out float bottom);
                    var w = go.AddComponent<MapleCityWalker>();
                    w.lanes = lanesComp;
                    w.lane = l;
                    w.startS = start;
                    w.heading = laneHeading[l];
                    w.speed = speed;
                    w.lateral = lateral;
                    // Minato walkers are authored with their soles at the origin; keep any
                    // small measured offset but never let a bad bound launch a figure.
                    w.lift = Mathf.Clamp(-bottom, -0.05f, 0.25f);
                    w.Place(0f);
                    count++;
                }
                s += WalkerSpacingM * Mathf.Lerp(0.45f, 1.55f, (float)rng.NextDouble());
            }
        }
        _runners = BuildRunners(group, lanesComp, laneHeading, cast);
        return count;
    }

    // =================================================================== runners (C8)

    private const int RunnerTarget = 60;
    private static int _runners;

    /// <summary>
    /// ~60 joggers on the walking lanes: civilian-free running kit, no helmet (hair cap), Kuro's
    /// run clip retargeted per donor (MapleCityRunner), 2.8-3.4 m/s, overtaking walkers inside the
    /// lane's clear range. Own RNG, so the walkers, cafes and chats are unchanged by them.
    /// </summary>
    private static int BuildRunners(Transform group, MapleCityWalkLanes lanesComp, List<int> laneHeading, Cast cast)
    {
        if (cast.Walkers.Count == 0 || lanesComp.lanes.Length == 0) return 0;
        var cycles = new Dictionary<MinatoCrowdActor, TextAsset>();
        foreach (var w in cast.Walkers)
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Dir}/Anim/MapleLife_RunCycle_{w.name.Substring(6)}.json");
            if (ta != null) cycles[w] = ta;
        }
        if (cycles.Count == 0)
        {
            Debug.LogError("[maple-life] no run cycles in Life/Anim - run: python tools/blender/build_maple_city_life_textures.py --run-only");
            return 0;
        }
        var donors = cycles.Keys.ToList();
        var holder = new GameObject("Runners").transform;
        holder.SetParent(group, false);
        var rr = new System.Random(Seed ^ 0x2A7F);
        float total = lanesComp.lanes.Sum(l => l.length);
        float spacing = total / RunnerTarget;
        int count = 0;
        for (int l = 0; l < lanesComp.lanes.Length; l++)
        {
            float s = (float)rr.NextDouble() * spacing;
            while (s < lanesComp.lanes[l].length)
            {
                var src = donors[rr.Next(donors.Count)];
                var cycle = cycles[src];
                float speed = Mathf.Lerp(2.8f, 3.4f, (float)rr.NextDouble());
                int heading = rr.NextDouble() < 0.7 ? laneHeading[l] : -laneHeading[l];
                float scale = 0.95f + (float)rr.NextDouble() * 0.10f;
                var pos = lanesComp.Sample(l, s, out var dir);
                if (heading < 0) dir = -dir;
                var go = Clone(src, holder, $"Runner_{l}_{count:000}_{src.name.Substring(6)}", pos,
                               Quaternion.LookRotation(dir, Vector3.up), scale,
                               MinatoCrowdActor.MotionKind.Idle, 0f, (float)rr.NextDouble(),
                               out var rig, out float bottom);
                var w = go.AddComponent<MapleCityWalker>();
                w.lanes = lanesComp;
                w.lane = l;
                w.startS = s;
                w.heading = heading;
                w.speed = speed;
                w.lateral = 0f;
                w.lift = Mathf.Clamp(-bottom, -0.05f, 0.25f);
                w.Place(0f);
                var run = go.AddComponent<MapleCityRunner>();
                run.cycle = cycle;
                run.rigRoot = rig;
                run.walker = w;
                run.phase = (float)rr.NextDouble();
                run.baseLateral = 0f;
                float natural = 2.82f;
                try { natural = Mathf.Max(0.5f, JsonUtility.FromJson<RunCycleHeader>(cycle.text).naturalSpeed); }
                catch (Exception) { }
                run.playback = Mathf.Clamp(speed / (natural * scale), 0.8f, 1.4f);
                count++;
                s += spacing * Mathf.Lerp(0.5f, 1.5f, (float)rr.NextDouble());
            }
        }
        Debug.Log($"[maple-life] {count} runners on {lanesComp.lanes.Length} lanes ({donors.Count} donors with run cycles).");
        return count;
    }

    [Serializable] private sealed class RunCycleHeader { public float naturalSpeed; }

    /// <summary>
    /// One closed lane along one pavement. Each station is pushed sideways (within the pavement)
    /// off any clutter cell, then the offsets are smoothed so a walker drifts round an obstacle
    /// instead of zig-zagging.
    /// </summary>
    private static MapleCityWalkLanes.Lane BuildLane(CityRoute route, int side, float fromKerb,
                                                     Occupancy occ, ref int nudged)
    {
        int n = route.Count - 1;                 // last station duplicates the first
        var off = new float[n];
        float[] tries = { 0f, 0.3f, -0.3f, 0.6f, -0.6f, 0.9f, -0.9f, 1.2f, -1.2f };
        for (int i = 0; i < n; i++)
        {
            off[i] = fromKerb;
            foreach (var dt in tries)
            {
                float f = Mathf.Clamp(fromKerb + dt, 0.55f, PavementWidthM - 0.55f);
                if (occ.Free(PavementPoint(route, i, side, f), 0.35f)) { off[i] = f; break; }
            }
            if (!Mathf.Approximately(off[i], fromKerb)) nudged++;
        }
        var sm = new float[n];
        for (int i = 0; i < n; i++)
        {
            float acc = 0f, wsum = 0f;
            for (int k = -3; k <= 3; k++)
            {
                float w = 4f - Mathf.Abs(k);
                acc += off[(i + k + n) % n] * w;
                wsum += w;
            }
            // never smooth AWAY from a nudge: keep whichever is further from the original line
            float avg = acc / wsum;
            sm[i] = Mathf.Abs(off[i] - fromKerb) > Mathf.Abs(avg - fromKerb) ? off[i] : avg;
        }

        var lane = new MapleCityWalkLanes.Lane
        {
            points = new Vector3[n],
            dist = new float[n + 1],
        };
        for (int i = 0; i < n; i++) lane.points[i] = PavementPoint(route, i, side, sm[i]);
        for (int i = 0; i < n; i++)
            lane.dist[i + 1] = lane.dist[i] + Vector3.Distance(lane.points[i], lane.points[(i + 1) % n]);
        lane.length = lane.dist[n];
        // C8: clear lateral room at each point (right-positive for the point order), so runners
        // can step round walkers without leaving the pavement or hitting clutter.
        lane.latMin = new float[n];
        lane.latMax = new float[n];
        for (int i = 0; i < n; i++)
        {
            var p = lane.points[i];
            var right = Vector3.Cross(Vector3.up, Flat(lane.points[(i + 1) % n] - p));
            var outward = Flat(PavementPoint(route, i, side, sm[i] + 0.1f) - p);
            float kerbPerRight = Vector3.Dot(right, outward);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                float ok = 0f;
                for (float o = 0.1f; o <= 0.95f; o += 0.1f)
                {
                    float kerb = sm[i] + sgn * o * kerbPerRight;
                    if (kerb < 0.45f || kerb > PavementWidthM - 0.45f) break;
                    if (!occ.Free(p + right * (sgn * o), 0.3f)) break;
                    ok = o;
                }
                if (sgn < 0) lane.latMin[i] = -ok; else lane.latMax[i] = ok;
            }
        }
        return lane;
    }

    // =================================================================== street-corner chats

    private static int BuildChats(Transform group, CityRoute route, Occupancy occ, Cast cast,
                                  System.Random rng)
    {
        if (cast.Standers.Count == 0) return 0;
        var holder = new GameObject("Street Chats").transform;
        holder.SetParent(group, false);
        int groups = 0;
        for (float d = 25f; d < route.Length - 25f; d += ChatSpacingM * Mathf.Lerp(0.5f, 1.5f, (float)rng.NextDouble()))
        {
            if (InRow(d)) continue;
            int i = route.IndexAt(d);
            float frac = route.Frac(i);
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            if (side < 0 && frac > CanalFromFrac - 0.01f && frac < CanalToFrac + 0.01f) side = 1;
            // Against the shopfronts, clear of both walking lanes (1.0 / 1.9 m from the kerb).
            float fromKerb = PavementWidthM - Mathf.Lerp(0.9f, 1.3f, (float)rng.NextDouble());
            var c = PavementPoint(route, i, side, fromKerb);
            if (!occ.Free(c, 0.9f)) continue;

            var t = Flat(route.Tangent[i]);
            int size = rng.NextDouble() < 0.4 ? 3 : 2;
            float radius = size == 2 ? 0.42f : 0.5f;
            float spin = (float)rng.NextDouble() * 360f;
            for (int k = 0; k < size; k++)
            {
                float a = spin + k * 360f / size;
                var dir = Quaternion.Euler(0f, a, 0f) * t;
                var pos = c + dir * radius;
                var look = Flat(c - pos);
                var src = cast.Standers[rng.Next(cast.Standers.Count)];
                var go = Clone(src, holder, $"Chat_{groups:00}_{k}_{src.name.Substring(6)}", pos,
                               Quaternion.LookRotation(look, Vector3.up),
                               0.94f + (float)rng.NextDouble() * 0.16f,
                               MinatoCrowdActor.MotionKind.Idle, 0f, (float)rng.NextDouble(),
                               out var rig, out float bottom);
                go.transform.position = pos + Vector3.up * Mathf.Clamp(-bottom, -0.05f, 0.25f);
                bool holdsCup = rng.NextDouble() < 0.35;
                var mode = holdsCup ? MapleCityCafeGesture.Mode.Sip
                         : k == 0 ? MapleCityCafeGesture.Mode.Talk
                         : MapleCityCafeGesture.Mode.Listen;
                AddGesture(go, rig, mode, holdsCup ? MakeCup(holder, pos, "Chat") : null, false,
                           Vector3.zero, rng);
            }
            occ.MarkDisc(c, radius + 0.4f);
            groups++;
        }
        return groups;
    }

    private static void AddGesture(GameObject go, Transform rig, MapleCityCafeGesture.Mode mode,
                                   Transform cup, bool rests, Vector3 rest, System.Random rng)
    {
        if (rig == null) return;
        var g = go.AddComponent<MapleCityCafeGesture>();
        g.mode = mode;
        g.rigRoot = rig;
        g.cup = cup;
        g.cupRests = rests;
        g.cupRestWorld = rest;
        g.period = Mathf.Lerp(7f, 12f, (float)rng.NextDouble());
        g.phase = (float)rng.NextDouble();
    }

    // =================================================================== cafes

    private sealed class Brand
    {
        public string Id;
        public Color Fascia, Frame, Wall, Plaster;
        public float Width;
    }

    private static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

    // Mirrors CAFES in tools/blender/build_maple_city_life_textures.py (all names fictional).
    private static readonly Brand[] Brands =
    {
        new Brand { Id = "KOMOREBI",  Fascia = Hex("#1f3b2d"), Frame = Hex("#2b2019"), Wall = Hex("#6b4a33"), Plaster = Hex("#e8e0cf"), Width = 7.6f },
        new Brand { Id = "GINKGO",    Fascia = Hex("#262427"), Frame = Hex("#1c1b1d"), Wall = Hex("#3b3431"), Plaster = Hex("#c9c3bb"), Width = 8.4f },
        new Brand { Id = "TRAMLINE",  Fascia = Hex("#1c2d4f"), Frame = Hex("#1a2438"), Wall = Hex("#7a5a3c"), Plaster = Hex("#efe8da"), Width = 7.2f },
        new Brand { Id = "HIKARI",    Fascia = Hex("#5a1a22"), Frame = Hex("#3a1a14"), Wall = Hex("#4e2c22"), Plaster = Hex("#e6d9c2"), Width = 6.8f },
        new Brand { Id = "MAPLEMILK", Fascia = Hex("#f2e8d9"), Frame = Hex("#f4efe6"), Wall = Hex("#8b6446"), Plaster = Hex("#d9b89a"), Width = 7.8f },
        new Brand { Id = "CANALSIDE", Fascia = Hex("#0f4b4f"), Frame = Hex("#12302f"), Wall = Hex("#5d4636"), Plaster = Hex("#dfe3de"), Width = 8.0f },
    };

    private static int BuildCafes(Transform group, CityRoute route, Occupancy occ, Cast cast,
                                  System.Random rng)
    {
        var holder = new GameObject("Cafes").transform;
        holder.SetParent(group, false);
        int built = 0, brand = 0;
        int sideFlip = 1;
        for (float d = 150f; d < route.Length - 60f; d += CafeSpacingM * Mathf.Lerp(0.7f, 1.3f, (float)rng.NextDouble()))
        {
            if (InRow(d)) continue;
            float bridgeD = BridgeFrac * route.Length;
            if (Mathf.Abs(d - bridgeD) < 60f) continue;

            // Try a few nearby stations / both sides until the frontage and terrace are clear.
            bool placed = false;
            for (int attempt = 0; attempt < 8 && !placed; attempt++)
            {
                float dd = d + attempt * 11f;
                if (InRow(dd) || dd > route.Length - 40f) break;
                int side = (attempt & 1) == 0 ? sideFlip : -sideFlip;
                int i = route.IndexAt(dd);
                float frac = route.Frac(i);
                if (side < 0 && frac > CanalFromFrac - 0.02f && frac < CanalToFrac + 0.02f) continue;
                var br = Brands[brand % Brands.Length];
                if (!CafeSiteFree(route, i, side, br.Width, occ)) continue;
                BuildCafe(holder, route, i, side, br, occ, cast, rng, built);
                placed = true;
                built++;
                brand++;
                sideFlip = -sideFlip;
            }
        }
        return built;
    }

    /// <summary>The cafe's local frame at a station: origin on the pavement's back edge.</summary>
    private static void CafeFrame(CityRoute route, int i, int side, out Vector3 origin,
                                  out Quaternion rot)
    {
        origin = PavementPoint(route, i, side, PavementWidthM);
        var toRoad = Flat(-route.SideFlat(i) * side);          // local +Z points at the road
        rot = Quaternion.LookRotation(toRoad, Vector3.up);
    }

    private static bool CafeSiteFree(CityRoute route, int i, int side, float width, Occupancy occ)
    {
        CafeFrame(route, i, side, out var o, out var rot);
        for (float x = -width * 0.5f - 0.6f; x <= width * 0.5f + 0.6f; x += 0.5f)
        for (float z = 0.3f; z <= 2.3f; z += 0.5f)
            if (!occ.Free(o + rot * new Vector3(x, 0f, z), 0.2f)) return false;
        return true;
    }

    private static void BuildCafe(Transform holder, CityRoute route, int i, int side, Brand br,
                                  Occupancy occ, Cast cast, System.Random rng, int index)
    {
        CafeFrame(route, i, side, out var origin, out var rot);
        // "@<metres>" lets the play-mode capture seek the rider to this cafe.
        var root = new GameObject($"Cafe_{index:00}_{br.Id}@{route.Distance[i]:0}").transform;
        root.SetParent(holder, false);
        root.SetPositionAndRotation(origin, rot);

        float W = br.Width, hw = W * 0.5f;
        const float Depth = 6.5f, Height = 4.3f, Riser = 0.5f, GlassTop = 2.75f;

        // --------------------------------------------------------------- shell (cel-lit)
        var plaster = new MeshBuilder();
        plaster.Box(new Vector3(0f, (Height - 0.6f) * 0.5f, -Depth * 0.5f - 0.25f),
                    new Vector3(W, Height + 0.6f, Depth));                        // body, 0.6 m buried
        plaster.Box(new Vector3(0f, Height + 0.15f, -Depth * 0.5f), new Vector3(W + 0.3f, 0.3f, Depth + 0.5f)); // cornice

        var fascia = new MeshBuilder();
        fascia.Box(new Vector3(0f, (GlassTop + 0.1f + Height) * 0.5f, 0.0f),
                   new Vector3(W, Height - GlassTop - 0.1f, 0.3f));              // sign band
        fascia.Box(new Vector3(0f, Riser * 0.5f, -0.05f), new Vector3(W, Riser, 0.3f)); // stall riser

        var frame = new MeshBuilder();
        // Mullions and door: shopfront joinery proud of the glass.
        float doorX0 = hw - 1.75f, doorX1 = hw - 0.45f;
        foreach (float x in new[] { -hw + 0.12f, doorX0, doorX1, hw - 0.12f })
            frame.Box(new Vector3(x, (Riser + GlassTop) * 0.5f, 0.02f), new Vector3(0.14f, GlassTop - Riser, 0.16f));
        int bays = Mathf.Max(1, Mathf.RoundToInt((doorX0 - (-hw)) / 1.6f));
        for (int b = 1; b < bays; b++)
        {
            float x = Mathf.Lerp(-hw, doorX0, b / (float)bays);
            frame.Box(new Vector3(x, (Riser + GlassTop) * 0.5f, 0.0f), new Vector3(0.07f, GlassTop - Riser, 0.1f));
        }
        frame.Box(new Vector3(0f, GlassTop + 0.05f, 0.02f), new Vector3(W, 0.12f, 0.18f));       // transom
        frame.Box(new Vector3(0f, Riser + 0.03f, 0.08f), new Vector3(doorX0 + hw, 0.06f, 0.22f)); // sill
        frame.Box(new Vector3(doorX1 - 0.18f, 1.25f, 0.1f), new Vector3(0.04f, 0.5f, 0.04f));   // door pull
        // awning arms
        frame.Box(new Vector3(-hw + 0.3f, 2.55f, 1.05f), new Vector3(0.05f, 0.05f, 2.0f), Quaternion.Euler(-14f, 0f, 0f));
        frame.Box(new Vector3(hw - 0.3f, 2.55f, 1.05f), new Vector3(0.05f, 0.05f, 2.0f), Quaternion.Euler(-14f, 0f, 0f));

        // --------------------------------------------------------------- lit glazing
        var glass = new MeshBuilder();
        // Quads here take u = the VIEWER'S right as seen from the road (-X) and v = up, so they
        // face the road (+Z) and read the right way round.
        glass.Quad(new Vector3(doorX0, Riser, -0.06f), new Vector3(-(doorX0 + hw - 0.1f), 0f, 0f),
                   new Vector3(0f, GlassTop - Riser, 0f), new Vector2(0.22f, 0f), new Vector2(1f, 1f));
        glass.Quad(new Vector3(doorX1, 0.02f, -0.06f), new Vector3(-(doorX1 - doorX0), 0f, 0f),
                   new Vector3(0f, GlassTop - 0.02f, 0f), new Vector2(0.02f, 0.05f), new Vector2(0.20f, 1f));

        // --------------------------------------------------------------- sign face (lit)
        var sign = new MeshBuilder();
        float sw = Mathf.Min(W - 0.7f, 4.6f), sh = sw * 0.25f;
        float sy = (GlassTop + 0.1f + Height) * 0.5f;
        sign.Quad(new Vector3(sw * 0.5f, sy - sh * 0.5f, 0.155f), new Vector3(-sw, 0f, 0f),
                  new Vector3(0f, sh, 0f), Vector2.zero, Vector2.one);
        // projecting blade sign, visible along the street
        var blade = new MeshBuilder();
        blade.Box(new Vector3(-hw - 0.05f, 3.35f, 0.55f), new Vector3(0.08f, 0.62f, 0.9f));
        var bladeFace = new MeshBuilder();
        foreach (float sx in new[] { -1f, 1f })
        {
            var o = new Vector3(-hw - 0.05f + sx * 0.042f, 3.1f, sx > 0 ? 0.13f : 0.97f);
            bladeFace.Quad(o, new Vector3(0f, 0f, sx > 0 ? 0.84f : -0.84f), new Vector3(0f, 0.5f, 0f),
                           new Vector2(0.03f, 0.1f), new Vector2(0.22f, 0.9f));
        }

        // --------------------------------------------------------------- awning (fabric)
        var awning = new MeshBuilder();
        float aw = W - 0.2f, u = aw / 1.3f;
        var a0 = new Vector3(-aw * 0.5f, 2.9f, 0.16f);
        var along = new Vector3(aw, 0f, 0f);
        var slope = new Vector3(0f, -0.55f, 1.95f);
        awning.QuadTwoSided(a0, along, slope, new Vector2(0f, 0f), new Vector2(u, 1f));
        awning.QuadTwoSided(a0 + slope, along, new Vector3(0f, -0.28f, 0f), new Vector2(0f, 0f), new Vector2(u, 0.25f)); // valance

        // --------------------------------------------------------------- terrace furniture
        var wood = new MeshBuilder();
        var metal = new MeshBuilder();
        var china = new MeshBuilder();
        var green = new MeshBuilder();
        var chalk = new MeshBuilder();

        int tables = Mathf.Max(2, Mathf.FloorToInt((W - 1.4f) / 2.3f));
        float span = W - 1.6f;
        var seats = new List<(Vector3 seat, Vector3 faceDir, Vector3 cup)>();
        for (int k = 0; k < tables; k++)
        {
            float x = -span * 0.5f + span * (k + 0.5f) / tables - 0.3f;
            var tc = new Vector3(x, 0f, 1.25f);
            metal.Cylinder(tc + Vector3.up * 0.015f, 0.2f, 0.03f, 12);             // foot
            metal.Cylinder(tc + Vector3.up * 0.28f, 0.03f, 0.5f, 8);               // pedestal
            wood.Cylinder(tc + Vector3.up * 0.545f, 0.33f, 0.035f, 16);            // round top
            for (int c = -1; c <= 1; c += 2)
            {
                // Chair either side of the table along the street, so seated figures are seen in
                // profile from the road. Seat frame copies the Minato bench (seat centre -0.08,
                // backrest +0.17 in the figure's own frame).
                var seatOrigin = tc + new Vector3(c * 0.64f, 0f, 0f);
                var toTable = new Vector3(-c, 0f, 0f);
                var chairRot = Quaternion.LookRotation(-toTable, Vector3.up); // chair +Z = backrest side
                Chair(wood, metal, seatOrigin, chairRot);
                var cupSpot = tc + new Vector3(c * 0.17f, 0.565f, -0.04f * c);
                china.Cylinder(cupSpot + Vector3.up * 0.006f, 0.065f, 0.012f, 12);   // saucer
                seats.Add((seatOrigin, toTable, cupSpot));
            }
        }
        // planters at both ends of the terrace
        foreach (float x in new[] { -hw + 0.3f, hw + 0.25f })
        {
            wood.Box(new Vector3(x, 0.21f, 0.9f), new Vector3(0.45f, 0.42f, 1.2f));
            green.Blob(new Vector3(x, 0.62f, 0.9f), new Vector3(0.5f, 0.42f, 1.2f), rng);   // B1: top ~0.83 m
        }
        // A-frame menu board by the door
        {
            var bx = new Vector3(doorX0 - 0.1f, 0f, 2.05f);
            var legRot1 = Quaternion.Euler(-12f, 0f, 0f);
            var legRot2 = Quaternion.Euler(12f, 0f, 0f);
            wood.Box(bx + new Vector3(0f, 0.45f, 0.1f), new Vector3(0.52f, 0.92f, 0.04f), legRot1);
            wood.Box(bx + new Vector3(0f, 0.45f, -0.1f), new Vector3(0.52f, 0.92f, 0.04f), legRot2);
            chalk.Quad(bx + legRot1 * new Vector3(0.23f, -0.4f, 0.03f) + new Vector3(0f, 0.45f, 0.1f),
                       legRot1 * new Vector3(-0.46f, 0f, 0f), legRot1 * new Vector3(0f, 0.8f, 0f),
                       Vector2.zero, Vector2.one);
            chalk.Quad(bx + legRot2 * new Vector3(-0.23f, -0.4f, -0.03f) + new Vector3(0f, 0.45f, -0.1f),
                       legRot2 * new Vector3(0.46f, 0f, 0f), legRot2 * new Vector3(0f, 0.8f, 0f),
                       Vector2.zero, Vector2.one);
        }

        // --------------------------------------------------------------- customers
        if (cast.Sitters.Count > 0)
        {
            for (int k = 0; k < seats.Count; k++)
            {
                if (rng.NextDouble() > 0.68) continue;
                var (seat, toTable, cupLocal) = seats[k];
                var figDir = SitterFacesMinusZ ? -toTable : toTable;
                var wPos = root.TransformPoint(seat);
                var wRot = root.rotation * Quaternion.LookRotation(figDir, Vector3.up);
                var src = cast.Sitters[rng.Next(cast.Sitters.Count)];
                var go = Clone(src, root, $"Customer_{k}_{src.name.Substring(6)}", wPos, wRot,
                               0.96f + (float)rng.NextDouble() * 0.08f, MinatoCrowdActor.MotionKind.Sit,
                               0f, (float)rng.NextDouble(), out var rig, out _);
                var cupWorld = root.TransformPoint(cupLocal) + Vector3.up * 0.04f;
                bool talker = k % 2 == 1 && rng.NextDouble() < 0.5;
                var cup = talker ? null : MakeCup(root, cupWorld, $"Cup_{k}");
                AddGesture(go, rig, talker ? MapleCityCafeGesture.Mode.Talk : MapleCityCafeGesture.Mode.Sip,
                           cup, true, cupWorld, rng);
                var gst = go.GetComponent<MapleCityCafeGesture>();
                if (gst != null) gst.facesMinusZ = SitterFacesMinusZ;
                if (talker) china.Cylinder(cupLocal + Vector3.up * 0.04f, 0.035f, 0.06f, 10);
            }
        }
        if (cast.Standers.Count > 0 && rng.NextDouble() < 0.75)
        {
            // a regular by the door, cup in hand
            var local = new Vector3(doorX0 + 0.3f, 0f, 0.75f);
            var wPos = root.TransformPoint(local);
            var face = root.rotation * Flat(new Vector3(-0.8f, 0f, 0.6f));
            var src = cast.Standers[rng.Next(cast.Standers.Count)];
            var go = Clone(src, root, $"Regular_{src.name.Substring(6)}", wPos,
                           Quaternion.LookRotation(face, Vector3.up), 0.95f + (float)rng.NextDouble() * 0.1f,
                           MinatoCrowdActor.MotionKind.Idle, 0f, (float)rng.NextDouble(), out var rig, out float bottom);
            go.transform.position = wPos + Vector3.up * Mathf.Clamp(-bottom, -0.05f, 0.25f);
            AddGesture(go, rig, MapleCityCafeGesture.Mode.Sip, MakeCup(root, wPos, "Cup_Regular"), false,
                       Vector3.zero, rng);
        }

        Emit(root, "Shell", plaster, CelMat($"Plaster_{br.Id}", br.Plaster, 0.12f, 0.06f));
        Emit(root, "Fascia", fascia, CelMat($"Fascia_{br.Id}", br.Fascia, 0.25f, 0.15f));
        Emit(root, "Joinery", frame, CelMat($"Frame_{br.Id}", br.Frame, 0.35f, 0.2f));
        Emit(root, "Glazing", glass, Unlit($"Interior_{br.Id}", Tex($"MapleLife_Interior_{br.Id}.png"), new Color(1.05f, 1.0f, 0.95f)));
        Emit(root, "Sign", sign, Unlit($"Sign_{br.Id}", Tex($"MapleLife_Sign_{br.Id}.png"), new Color(1.0f, 1.0f, 1.0f)));
        Emit(root, "Blade", blade, CelMat($"Fascia_{br.Id}", br.Fascia, 0.25f, 0.15f));
        Emit(root, "Blade Face", bladeFace, Unlit($"Sign_{br.Id}", Tex($"MapleLife_Sign_{br.Id}.png"), Color.white));
        Emit(root, "Awning", awning, CelMat($"Awning_{br.Id}", Color.white, 0.1f, 0.05f, Tex($"MapleLife_Awning_{br.Id}.png")));
        Emit(root, "Terrace Wood", wood, CelMat("Wood", Color.white, 0.3f, 0.15f, Tex("MapleLife_Wood.png")));
        Emit(root, "Terrace Metal", metal, CelMat("BistroMetal", new Color(0.13f, 0.13f, 0.14f), 0.55f, 0.4f));
        Emit(root, "Terrace China", china, CelMat("China", new Color(0.95f, 0.94f, 0.9f), 0.5f, 0.35f));
        Emit(root, "Planting", green, CelMat("Planting", new Color(0.33f, 0.47f, 0.25f), 0.1f, 0.05f));
        // Per-brand A-board (QA #10): board A the first time a brand comes round the loop, B the second.
        string board = $"Chalkboard_{br.Id}_{((index / Brands.Length) % 2 == 0 ? "A" : "B")}";
        Emit(root, "Menu Board", chalk, Unlit(board, Tex($"MapleLife_{board}.png"), new Color(0.85f, 0.85f, 0.85f)));

        // block the whole terrace for everything placed after this
        for (float x = -hw - 0.5f; x <= hw + 0.5f; x += 0.5f)
        for (float z = 0f; z <= 2.4f; z += 0.5f)
            occ.MarkDisc(origin + rot * new Vector3(x, 0f, z), 0.2f);
    }

    /// <summary>A bistro chair in the Minato bench frame of <paramref name="figRot"/> (backrest on its +Z side).</summary>
    private static void Chair(MeshBuilder wood, MeshBuilder metal, Vector3 origin, Quaternion figRot)
    {
        // Seat top at the Minato bench height (0.38 m); seat centre 0.08 m behind the origin.
        float seatTop = MinatoCrowdPopulation.BenchSeatHeight;
        wood.Box(origin + figRot * new Vector3(0f, seatTop - 0.025f, -0.08f), new Vector3(0.44f, 0.05f, 0.4f), figRot);
        foreach (float x in new[] { -0.18f, 0.18f })
        foreach (float z in new[] { -0.25f, 0.09f })
            metal.Box(origin + figRot * new Vector3(x, (seatTop - 0.05f) * 0.5f, z), new Vector3(0.03f, seatTop - 0.05f, 0.03f), figRot);
        var back = figRot * Quaternion.Euler(-8f, 0f, 0f);
        wood.Box(origin + figRot * new Vector3(0f, seatTop + 0.24f, 0.16f), new Vector3(0.42f, 0.3f, 0.035f), back);
        foreach (float x in new[] { -0.18f, 0.18f })
            metal.Box(origin + figRot * new Vector3(x, seatTop + 0.15f, 0.12f), new Vector3(0.03f, 0.34f, 0.03f), back);
    }

    private static Transform MakeCup(Transform parent, Vector3 world, string name)
    {
        var mb = new MeshBuilder();
        mb.Cylinder(new Vector3(0f, 0.03f, 0f), 0.034f, 0.06f, 10);
        mb.Box(new Vector3(0.042f, 0.032f, 0f), new Vector3(0.018f, 0.03f, 0.008f));
        var go = Emit(parent, name, mb, CelMat("China", new Color(0.95f, 0.94f, 0.9f), 0.5f, 0.35f), sharedName: "MapleLife_Cup");
        go.transform.position = world;
        return go.transform;
    }

    // =================================================================== mesh building

    private sealed class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        public bool Empty => T.Count == 0;

        private void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uv0, Vector2 uv1)
        {
            int o = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (int k = 0; k < 4; k++) N.Add(n);
            UV.Add(new Vector2(uv0.x, uv0.y)); UV.Add(new Vector2(uv1.x, uv0.y));
            UV.Add(new Vector2(uv1.x, uv1.y)); UV.Add(new Vector2(uv0.x, uv1.y));
            // Unity's front face for (p0, p1, p2) points along (p1 - p0) x (p2 - p0).
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f)
            {
                T.Add(o); T.Add(o + 1); T.Add(o + 2);
                T.Add(o); T.Add(o + 2); T.Add(o + 3);
            }
            else
            {
                T.Add(o); T.Add(o + 2); T.Add(o + 1);
                T.Add(o); T.Add(o + 3); T.Add(o + 2);
            }
        }

        /// <summary>
        /// Quad from corner <paramref name="o"/>: u = the viewer's RIGHT, v = the viewer's UP.
        /// In Unity's left-handed frame that faces the viewer along v x u.
        /// </summary>
        public void Quad(Vector3 o, Vector3 u, Vector3 v, Vector2 uv0, Vector2 uv1)
        {
            var n = Vector3.Cross(v, u).normalized;
            Face(o, o + u, o + u + v, o + v, n, uv0, uv1);
        }

        public void QuadTwoSided(Vector3 o, Vector3 u, Vector3 v, Vector2 uv0, Vector2 uv1)
        {
            Quad(o, u, v, uv0, uv1);
            Quad(o + u, -u, v, new Vector2(uv1.x, uv0.y), new Vector2(uv0.x, uv1.y));
        }

        public void Box(Vector3 c, Vector3 size, Quaternion? rotation = null)
        {
            var r = rotation ?? Quaternion.identity;
            var e = size * 0.5f;
            Vector3 X = r * new Vector3(e.x, 0f, 0f), Y = r * new Vector3(0f, e.y, 0f), Z = r * new Vector3(0f, 0f, e.z);
            float ux = size.x, uy = size.y, uz = size.z;
            Face(c - X - Y + Z, c + X - Y + Z, c + X + Y + Z, c - X + Y + Z, Z.normalized, Vector2.zero, new Vector2(ux, uy));   // +Z
            Face(c + X - Y - Z, c - X - Y - Z, c - X + Y - Z, c + X + Y - Z, -Z.normalized, Vector2.zero, new Vector2(ux, uy));  // -Z
            Face(c + X - Y + Z, c + X - Y - Z, c + X + Y - Z, c + X + Y + Z, X.normalized, Vector2.zero, new Vector2(uz, uy));   // +X
            Face(c - X - Y - Z, c - X - Y + Z, c - X + Y + Z, c - X + Y - Z, -X.normalized, Vector2.zero, new Vector2(uz, uy));  // -X
            Face(c - X + Y + Z, c + X + Y + Z, c + X + Y - Z, c - X + Y - Z, Y.normalized, Vector2.zero, new Vector2(ux, uz));   // +Y
            Face(c - X - Y - Z, c + X - Y - Z, c + X - Y + Z, c - X - Y + Z, -Y.normalized, Vector2.zero, new Vector2(ux, uz));  // -Y
        }

        public void Cylinder(Vector3 c, float r, float h, int sides)
        {
            float y0 = c.y - h * 0.5f, y1 = c.y + h * 0.5f;
            int top = V.Count;
            V.Add(new Vector3(c.x, y1, c.z)); N.Add(Vector3.up); UV.Add(new Vector2(0.5f, 0.5f));
            for (int k = 0; k <= sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                V.Add(new Vector3(c.x + d.x * r, y1, c.z + d.z * r)); N.Add(Vector3.up);
                UV.Add(new Vector2(0.5f + d.x * 0.5f, 0.5f + d.z * 0.5f));
            }
            for (int k = 0; k < sides; k++) { T.Add(top); T.Add(top + k + 2); T.Add(top + k + 1); }
            int side = V.Count;
            for (int k = 0; k <= sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                V.Add(new Vector3(c.x + d.x * r, y0, c.z + d.z * r)); N.Add(d); UV.Add(new Vector2(k / (float)sides, 0f));
                V.Add(new Vector3(c.x + d.x * r, y1, c.z + d.z * r)); N.Add(d); UV.Add(new Vector2(k / (float)sides, 1f));
            }
            for (int k = 0; k < sides; k++)
            {
                int a0 = side + k * 2;
                T.Add(a0); T.Add(a0 + 1); T.Add(a0 + 3);
                T.Add(a0); T.Add(a0 + 3); T.Add(a0 + 2);
            }
        }

        /// <summary>
        /// A clipped shrub filling roughly <paramref name="size"/>: three overlapping smooth
        /// ellipsoid lumps along local z. Top is at most c.y + size.y/2. B1: was three coarse
        /// 8x5 spheres sized by min(x,y), which read as chunky low-poly boulders. Still exactly
        /// three rng draws, so nothing scattered after a shrub moves.
        /// </summary>
        public void Blob(Vector3 c, Vector3 size, System.Random rng)
        {
            const int lumps = 3;
            var half = size * 0.5f;
            for (int k = 0; k < lumps; k++)
            {
                float s = 0.86f + (float)rng.NextDouble() * 0.14f;
                float lift = k == 1 ? 1f : 0.88f;
                var o = c + new Vector3(0f, (lift * s - 1f) * half.y * 0.5f, (k - 1) * size.z * 0.26f);
                var rad = new Vector3(half.x * s, half.y * s * lift, Mathf.Max(half.z * 0.56f, half.x * 0.8f) * s);
                Ellipsoid(o, rad, 14, 8);
            }
        }

        private void Ellipsoid(Vector3 c, Vector3 rad, int seg, int rings)
        {
            int o = V.Count;
            var inv = new Vector3(1f / rad.x, 1f / rad.y, 1f / rad.z);
            for (int j = 0; j <= rings; j++)
            {
                float v = j / (float)rings, phi = v * Mathf.PI;
                for (int k = 0; k <= seg; k++)
                {
                    float u = k / (float)seg, th = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    V.Add(c + Vector3.Scale(n, rad)); N.Add(Vector3.Scale(n, inv).normalized); UV.Add(new Vector2(u, v));
                }
            }
            for (int j = 0; j < rings; j++)
            for (int k = 0; k < seg; k++)
            {
                int a = o + j * (seg + 1) + k, b = a + seg + 1;
                T.Add(a); T.Add(a + 1); T.Add(b);
                T.Add(a + 1); T.Add(b + 1); T.Add(b);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, UV);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    private static readonly Dictionary<string, Mesh> SharedMeshes = new Dictionary<string, Mesh>();

    private static GameObject Emit(Transform parent, string name, MeshBuilder mb, Material mat,
                                   string sharedName = null)
    {
        if (mb.Empty) return null;
        Mesh mesh;
        if (sharedName != null && SharedMeshes.TryGetValue(sharedName, out var cached) && cached != null)
        {
            mesh = cached;
        }
        else
        {
            string meshName = sharedName ?? $"MapleLife_{parent.name}_{name}".Replace(' ', '_').Replace('@', '_');
            mesh = SaveMeshAsset(mb.ToMesh(meshName), $"{MeshDir}/{meshName}.asset");
            if (sharedName != null) SharedMeshes[sharedName] = mesh;
        }
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
        return go;
    }

    // =================================================================== materials / textures

    private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
    private static readonly Color DefaultShade = new Color(0.44f, 0.50f, 0.68f, 1f);

    private static Material Load(string name, Shader shader)
    {
        if (Mats.TryGetValue(name, out var cached) && cached != null) return cached;
        string path = $"{MatDir}/MapleLife_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.name = $"MapleLife_{name}";
        mat.enableInstancing = true;
        Mats[name] = mat;
        return mat;
    }

    /// <summary>Same cel tuning recipe as MapleRowBoutiques.CelMat / MapleCityEnvironment.CelMaterial.</summary>
    private static Material CelMat(string name, Color col, float gloss, float spec, Texture tex = null)
    {
        bool fresh = !Mats.ContainsKey(name);
        var shader = Shader.Find("MapleRide/HDRP/CelLit");
        if (shader == null) shader = Shader.Find(MapleRideShaderNames.Resolve("MapleRide/SakuraCel"));
        var mat = Load(name, shader);
        if (!fresh) return mat;
        mat.SetColor("_Color", col);
        mat.SetColor("_ShadeColor", DefaultShade);
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

    /// <summary>Self-lit (interiors, signs). HDRP/Unlit is not exposure-scaled, as in Maple Row.</summary>
    private static Material Unlit(string name, Texture tex, Color col)
    {
        bool fresh = !Mats.ContainsKey($"U_{name}");
        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Texture");
        var mat = Load($"U_{name}", shader);
        if (!fresh) return mat;
        if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", col);
        if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        try { HDMaterial.ValidateMaterial(mat); }
        catch (Exception e) { Debug.LogWarning($"[maple-life] ValidateMaterial {name}: {e.Message}"); }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Texture Tex(string file)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture>($"{TexDir}/{file}");
        if (t == null)
            Debug.LogError($"[maple-life] missing texture {TexDir}/{file} - run: python tools/blender/build_maple_city_life_textures.py");
        return t;
    }

    private static void PrepareTextures()
    {
        AssetDatabase.Refresh();
        if (!Directory.Exists(TexDir) || Directory.GetFiles(TexDir, "MapleLife_*.png").Length == 0)
        {
            Debug.LogError("[maple-life] no textures - run: python tools/blender/build_maple_city_life_textures.py");
            return;
        }
        foreach (var file in Directory.GetFiles(TexDir, "MapleLife_*.png"))
        {
            string path = file.Replace('\\', '/');
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            string n = Path.GetFileNameWithoutExtension(path);
            if (n.EndsWith("_Helmet", StringComparison.Ordinal))
            {
                // Helmet UV mask: read on the CPU by HairCapMesh, never rendered.
                if (imp.isReadable && !imp.sRGBTexture && !imp.mipmapEnabled &&
                    imp.textureCompression == TextureImporterCompression.Uncompressed) continue;
                imp.textureType = TextureImporterType.Default;
                imp.isReadable = true;
                imp.sRGBTexture = false;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
                continue;
            }
            bool tiles = n.StartsWith("MapleLife_Awning_") || n == "MapleLife_Wood" ||
                         n.EndsWith("_HairCap", StringComparison.Ordinal);
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
}
