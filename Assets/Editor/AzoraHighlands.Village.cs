using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 2026-09-26 user direction: Azora = SWISS ALPS. Two alpine villages on the route (timber
/// chalets with flower boxes and wide eaves, a church with a spire, a fountain square, a
/// Konditorei cafe terrace), a cable car up the slope from the upper village, cows in the
/// meadows, and people cloned from the Minato crowd figures already in the scene (same donor
/// approach as MapleCityLife; that file is not touched). All static geometry is merged per
/// material per village.
/// </summary>
public static partial class AzoraHighlandsEnvironment
{
    private static readonly float[] VillageCentres = { 2600f, 12600f };
    private const float VillageHalfM = 260f;

    private static bool InVillage(float d)
    {
        foreach (var c in VillageCentres) if (Mathf.Abs(d - c) < VillageHalfM + 40f) return true;
        return false;
    }

    private class Bin
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> tri = new List<int>();
        public readonly List<Color> col = new List<Color>();
    }

    private class Bins
    {
        public readonly Dictionary<Material, Bin> map = new Dictionary<Material, Bin>();
        public Bin this[Material m] { get { if (!map.TryGetValue(m, out var b)) { b = new Bin(); map[m] = b; } return b; } }

        public int Flush(Transform parent, string label)
        {
            int n = 0;
            foreach (var kv in map)
            {
                if (kv.Value.tri.Count == 0) continue;
                var mesh = Finish($"Azora_{label}_{kv.Key.name}", kv.Value.v.ToArray(), kv.Value.uv.ToArray(), kv.Value.tri);
                AddMesh(parent, $"{label} {kv.Key.name}", mesh, kv.Key, collider: false);
                n++;
            }
            map.Clear();
            return n;
        }
    }

    private static void BoxB(Bin b, Vector3 centre, Vector3 right, Vector3 back, float height, bool centredY = false) =>
        BoxO(b.v, b.uv, b.tri, b.col, centre, right, back, height, centredY);

    /// <summary>
    /// Box() with OUTWARD faces. claude-azora2 2026-09-26: after its handedness guard, Box() always
    /// emits the (right, back, up) orientation whose Cross(v1-v0, v2-v0) normals point INWARDS, so
    /// with MapleRide/HDRP/CelLit's default back-face cull every box rendered inside-out: the near
    /// walls vanished and you saw the far walls' inner faces (the "floating chalet" with a white
    /// ceiling-slab under its timber storey). This re-winds the triangles Box() just added.
    /// Box() itself is left alone because the main file's structures were tuned against it.
    /// </summary>
    private static void BoxO(List<Vector3> v, List<Vector2> uv, List<int> tri, List<Color> col,
                             Vector3 centre, Vector3 right, Vector3 back, float height, bool centredY = false)
    {
        int t0 = tri.Count;
        Box(v, uv, tri, col, centre, right, back, height, Color.white, centredY);
        for (int k = t0; k + 2 < tri.Count; k += 3) { int x = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = x; }
    }

    private static void TriDS(Bin b, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        int i = b.v.Count;
        b.v.Add(p0); b.v.Add(p1); b.v.Add(p2);
        b.v.Add(p0); b.v.Add(p2); b.v.Add(p1);
        for (int k = 0; k < 2; k++) { b.uv.Add(Vector2.zero); b.uv.Add(Vector2.right); b.uv.Add(Vector2.up); }
        b.tri.Add(i); b.tri.Add(i + 1); b.tri.Add(i + 2);
        b.tri.Add(i + 3); b.tri.Add(i + 4); b.tri.Add(i + 5);
    }

    private static void QuadDS(Bin b, Vector3 a, Vector3 c, Vector3 d, Vector3 e)
    {
        TriDS(b, a, c, d); TriDS(b, a, d, e);
    }

    private static Material _chaletTimber, _chaletRender, _chaletRoof, _flowerRed, _flowerPink, _window,
                            _churchWhite, _spireGreen, _cobble, _water, _steel, _cowWhite, _cowBlack,
                            _cowBrown, _awningRed, _tableTop, _stoneBase, _door, _shutter, _bikeFrameA, _bikeFrameB,
                            _bikeFrameC, _tyre, _goatWhite;

    private static void VillageMaterials()
    {
        _chaletTimber = CelMaterial("Azora_Vil_Timber", new Color(0.46f, 0.28f, 0.15f), gloss: 0.06f, spec: 0.03f, rim: 0.12f, shade: GroundShade);
        // Whitewash toned down from 0.93 (it clipped to a featureless white slab in the captures).
        _chaletRender = CelMaterial("Azora_Vil_Render", new Color(0.84f, 0.81f, 0.74f), gloss: 0.05f, spec: 0.03f, rim: 0.10f, shade: GroundShade);
        _stoneBase = CelMaterial("Azora_Vil_StoneBase", new Color(0.60f, 0.59f, 0.55f), gloss: 0.06f, spec: 0.04f, rim: 0.08f,
                                 texture: AzoraTexture("Azora_Stone_Albedo.png"), shade: GroundShade);
        _door = CelMaterial("Azora_Vil_Door", new Color(0.30f, 0.17f, 0.09f), gloss: 0.1f, spec: 0.05f, rim: 0.1f, shade: GroundShade);
        _shutter = CelMaterial("Azora_Vil_Shutter", new Color(0.20f, 0.40f, 0.26f), gloss: 0.12f, spec: 0.06f, rim: 0.12f, shade: GroundShade);
        _bikeFrameA = CelMaterial("Azora_Vil_BikeRed", new Color(0.78f, 0.12f, 0.12f), gloss: 0.5f, spec: 0.35f, rim: 0.3f);
        _bikeFrameB = CelMaterial("Azora_Vil_BikeBlue", new Color(0.15f, 0.35f, 0.72f), gloss: 0.5f, spec: 0.35f, rim: 0.3f);
        _bikeFrameC = CelMaterial("Azora_Vil_BikeWhite", new Color(0.90f, 0.90f, 0.88f), gloss: 0.5f, spec: 0.35f, rim: 0.3f, shade: GroundShade);
        _tyre = CelMaterial("Azora_Vil_Tyre", new Color(0.07f, 0.07f, 0.08f), gloss: 0.2f, spec: 0.1f, rim: 0.1f, shade: GroundShade);
        _goatWhite = CelMaterial("Azora_Vil_Goat", new Color(0.86f, 0.82f, 0.74f), gloss: 0.05f, spec: 0.03f, rim: 0.2f, shade: GroundShade);
        _chaletRoof = CelMaterial("Azora_Vil_Roof", new Color(0.32f, 0.27f, 0.25f), gloss: 0.12f, spec: 0.08f, rim: 0.10f, shade: GroundShade);
        _flowerRed = CelMaterial("Azora_Vil_Geranium", new Color(0.86f, 0.14f, 0.16f), gloss: 0.1f, spec: 0.05f, rim: 0.3f);
        _flowerPink = CelMaterial("Azora_Vil_Petunia", new Color(0.93f, 0.40f, 0.62f), gloss: 0.1f, spec: 0.05f, rim: 0.3f);
        _window = CelMaterial("Azora_Vil_Window", new Color(0.12f, 0.16f, 0.22f), gloss: 0.7f, spec: 0.5f, rim: 0.1f, shade: GroundShade);
        _churchWhite = CelMaterial("Azora_Vil_ChurchWhite", new Color(0.97f, 0.96f, 0.92f), gloss: 0.06f, spec: 0.04f, rim: 0.2f, shade: GroundShade);
        _spireGreen = CelMaterial("Azora_Vil_Spire", new Color(0.30f, 0.46f, 0.42f), gloss: 0.3f, spec: 0.2f, rim: 0.2f, shade: GroundShade);
        _cobble = CelMaterial("Azora_Vil_Cobble", new Color(0.60f, 0.58f, 0.54f), gloss: 0.08f, spec: 0.05f, rim: 0.05f, shade: GroundShade);
        _water = CelMaterial("Azora_Vil_Water", new Color(0.35f, 0.58f, 0.70f), gloss: 0.8f, spec: 0.6f, rim: 0.3f, shade: GroundShade);
        _steel = CelMaterial("Azora_Vil_Steel", new Color(0.55f, 0.57f, 0.60f), gloss: 0.4f, spec: 0.3f, rim: 0.15f, shade: GroundShade);
        _cowWhite = CelMaterial("Azora_Vil_CowWhite", new Color(0.92f, 0.90f, 0.86f), gloss: 0.05f, spec: 0.03f, rim: 0.2f, shade: GroundShade);
        _cowBlack = CelMaterial("Azora_Vil_CowBlack", new Color(0.12f, 0.11f, 0.10f), gloss: 0.05f, spec: 0.03f, rim: 0.2f, shade: GroundShade);
        _cowBrown = CelMaterial("Azora_Vil_CowBrown", new Color(0.52f, 0.34f, 0.20f), gloss: 0.05f, spec: 0.03f, rim: 0.2f, shade: GroundShade);
        _awningRed = CelMaterial("Azora_Vil_Awning", new Color(0.80f, 0.12f, 0.14f), gloss: 0.1f, spec: 0.05f, rim: 0.2f);
        _tableTop = CelMaterial("Azora_Vil_Table", new Color(0.95f, 0.95f, 0.93f), gloss: 0.1f, spec: 0.05f, rim: 0.1f, shade: GroundShade);
    }

    /// <summary>Lowest terrain height under a rectangle (4 corners, edge midpoints, centre).</summary>
    private static float MinGround(Vector3 c, Vector3 right, Vector3 fwd)
    {
        float m = c.y;
        for (int a = -1; a <= 1; a++)
            for (int b = -1; b <= 1; b++)
            {
                var p = c + right * a + fwd * b;
                float h = GH(p.x, p.z);
                if (h != 0f && h < m) m = h;
            }
        return m;
    }

    /// <summary>Square-section beam between two points (double-sided quads, 4 sides).</summary>
    private static void Beam(Bin b, Vector3 a, Vector3 c, float r)
    {
        var ax = c - a; if (ax.sqrMagnitude < 1e-6f) return; ax.Normalize();
        var n1 = Vector3.Cross(ax, Mathf.Abs(ax.y) > 0.9f ? Vector3.right : Vector3.up).normalized * r;
        var n2 = Vector3.Cross(ax, n1).normalized * r;
        QuadDS(b, a + n1 + n2, c + n1 + n2, c + n1 - n2, a + n1 - n2);
        QuadDS(b, a - n1 + n2, c - n1 + n2, c - n1 - n2, a - n1 - n2);
        QuadDS(b, a + n1 + n2, c + n1 + n2, c - n1 + n2, a - n1 + n2);
        QuadDS(b, a + n1 - n2, c + n1 - n2, c - n1 - n2, a - n1 - n2);
    }

    /// <summary>A road bike standing on its wheels at foot, pointing along nose.</summary>
    private static void ParkedBike(Bins bins, Vector3 foot, Vector3 nose, Vector3 lateral, Material frame)
    {
        nose.y = 0f; nose.Normalize();
        lateral.y = 0f; lateral.Normalize();
        var up = Vector3.up;
        const float R = 0.34f, wb = 0.50f;
        var rearHub = foot - nose * wb + up * R; var frontHub = foot + nose * wb + up * R;
        foreach (var hub in new[] { rearHub, frontHub })
        {
            Vector3 prev = hub + up * R;
            for (int k = 1; k <= 14; k++)
            {
                float a = k * Mathf.PI * 2f / 14f;
                var p = hub + (up * Mathf.Cos(a) + nose * Mathf.Sin(a)) * R;
                Beam(bins[_tyre], prev, p, 0.02f);
                prev = p;
            }
            Beam(bins[_steel], hub - lateral * 0.05f, hub + lateral * 0.05f, 0.02f);
        }
        var bb = foot + up * 0.28f;
        var seatTop = bb - nose * 0.16f + up * 0.52f;
        var headTop = frontHub - nose * 0.12f + up * 0.48f;
        var headLow = frontHub - nose * 0.07f + up * 0.30f;
        var fb = bins[frame];
        Beam(fb, bb, seatTop, 0.022f);            // seat tube
        Beam(fb, seatTop, headTop, 0.02f);        // top tube
        Beam(fb, bb, headLow, 0.026f);            // down tube
        Beam(fb, headLow, headTop, 0.024f);       // head tube
        Beam(fb, bb, rearHub, 0.016f);            // chainstay
        Beam(fb, seatTop, rearHub, 0.014f);       // seatstay
        Beam(fb, headLow, frontHub, 0.016f);      // fork
        Beam(bins[_steel], seatTop, seatTop + up * 0.12f - nose * 0.02f, 0.012f);
        var saddle = seatTop + up * 0.13f - nose * 0.04f;
        BoxB(bins[_tyre], saddle, lateral * 0.06f, nose * 0.13f, 0.035f, true);
        var stem = headTop + up * 0.06f + nose * 0.08f;
        Beam(bins[_steel], headTop, stem, 0.014f);
        Beam(bins[_tyre], stem - lateral * 0.21f, stem + lateral * 0.21f, 0.014f); // bars
        for (int s = -1; s <= 1; s += 2)
            Beam(bins[_tyre], stem + lateral * s * 0.21f, stem + lateral * s * 0.21f + nose * 0.07f - up * 0.13f, 0.013f); // drops
    }

    private static void Goat(Bins bins, Vector3 f, Vector3 dir, float seed)
    {
        var right = new Vector3(dir.z, 0f, -dir.x);
        var body = seed < 0.5f ? _goatWhite : _cowBrown;
        BoxB(bins[body], f + Vector3.up * 0.42f, right * 0.17f, dir * 0.42f, 0.36f);
        BoxB(bins[body], f + Vector3.up * 0.66f + dir * 0.5f, right * 0.09f, dir * 0.13f, 0.2f);
        BoxB(bins[_cowBlack], f + Vector3.up * 0.86f + dir * 0.44f, right * 0.07f, dir * 0.03f, 0.14f); // horns
        for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
                BoxB(bins[body], f + right * a * 0.11f + dir * b * 0.3f - Vector3.up * 0.1f, right * 0.035f, dir * 0.035f, 0.54f);
    }

    private static void Cow(Bins bins, Vector3 f, Vector3 dir, float seed)
    {
        var right = new Vector3(dir.z, 0f, -dir.x);
        var body = seed < 0.6f ? _cowWhite : _cowBrown;
        BoxB(bins[body], f + Vector3.up * 0.7f, right * 0.36f, dir * 0.85f, 0.75f);
        if (seed < 0.6f) BoxB(bins[_cowBlack], f + Vector3.up * 0.95f + dir * 0.2f + right * 0.05f, right * 0.38f, dir * 0.35f, 0.4f);
        BoxB(bins[body], f + Vector3.up * 1.0f + dir * 1.05f, right * 0.2f, dir * 0.3f, 0.42f);
        BoxB(bins[_cowBlack], f + Vector3.up * 1.05f + dir * 1.36f, right * 0.16f, dir * 0.05f, 0.2f);
        for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
                BoxB(bins[body], f + right * a * 0.24f + dir * b * 0.6f - Vector3.up * 0.1f, right * 0.07f, dir * 0.07f, 0.82f);
        BoxB(bins[_steel], f + Vector3.up * 0.82f + dir * 0.95f, right * 0.08f, dir * 0.06f, 0.14f); // bell
    }

    // ------------------------------------------------------------------ people

    private class CrowdCast
    {
        public readonly List<MinatoCrowdActor> walk = new List<MinatoCrowdActor>(), stand = new List<MinatoCrowdActor>(),
                                               sit = new List<MinatoCrowdActor>();
    }

    private static CrowdCast FindCrowdCast()
    {
        var cast = new CrowdCast();
        var all = Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in all)
        {
            if (a == null || !a.name.StartsWith("Crowd_", System.StringComparison.Ordinal)) continue;
            if (a.transform.Find("LOD0 High Skinned/Rigged Character") == null) continue;
            switch (a.motion)
            {
                case MinatoCrowdActor.MotionKind.Walk: cast.walk.Add(a); break;
                case MinatoCrowdActor.MotionKind.Idle:
                case MinatoCrowdActor.MotionKind.Wave: cast.stand.Add(a); break;
                case MinatoCrowdActor.MotionKind.Sit: cast.sit.Add(a); break;
            }
        }
        if (cast.stand.Count == 0) cast.stand.AddRange(cast.walk);
        return cast;
    }

    private static int _people;

    private static void Person(List<MinatoCrowdActor> pool, Transform parent, Vector3 pos, Vector3 face,
                               MinatoCrowdActor.MotionKind kind, Vector3 end, float seed, bool keepBench)
    {
        if (pool.Count == 0) return;
        var src = pool[Mathf.Abs((int)(Mathf.Abs(seed) * 9973f)) % pool.Count];
        var go = (GameObject)Object.Instantiate(src.gameObject, parent);
        go.name = $"Azora Villager {_people:D3}";
        go.hideFlags = HideFlags.None;
        go.SetActive(true);
        if (!keepBench)
        {
            var bench = go.transform.Find("Timber Waterfront Bench");
            if (bench != null) Object.DestroyImmediate(bench.gameObject);
        }
        face.y = 0f; if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
        if (kind == MinatoCrowdActor.MotionKind.Sit) face = -face; // seated donors face local -Z
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face.normalized, Vector3.up));
        go.transform.localScale = src.transform.localScale * Mathf.Lerp(0.95f, 1.05f, seed);
        var high = go.transform.Find("LOD0 High Skinned");
        var rig = high != null ? high.Find("Rigged Character") : null;
        var actor = go.GetComponent<MinatoCrowdActor>();
        if (actor != null && rig != null)
        {
            actor.Configure(kind, rig, pos, end, kind == MinatoCrowdActor.MotionKind.Walk ? Mathf.Lerp(0.7f, 1.0f, seed) : 0f, seed);
            actor.armDropDegrees = kind == MinatoCrowdActor.MotionKind.Sit ? 0f : 32f;
        }
        _people++;
    }

    // ================================================================== WP-C winter village kit
    // copilot session 2, 2026-09-26 (USER DIRECTIVE: AZORA IS WINTER). Chalets, cafe, bakery,
    // church, fountain, AZORA sign arch, closed umbrellas and flag poles are Blender GLBs from
    // tools/blender/build_azora_village.py (authored in Unity coords via u2b, LOD0 + LOD1, explicit
    // roof snow slabs + icicles, warm lit windows). This C# only PLACES them on the ground, dresses
    // the street and merges everything per material. Every distance below is PROVISIONAL tuning.
    private const string VillageKitDir = "Assets/Environment/AzoraHighlands/Models";
    // ---- LOD / perf (azC2 perf pass 2026-09-26; ALL PROVISIONAL tuning). Buildings are merged per
    // material into chunks of VillageChunkM along the road, one chunk per SIDE of the road, each
    // with its own LODGroup. Transition heights are derived from each chunk's real LODGroup.size so
    // the switches happen at these camera distances (at VillageLodRefFovDeg and lodBias 1):
    private const float VillageChunkM = 52f;            // chunk length along the road (per side)
    private const float VillageLodRefFovDeg = 60f;      // reference vertical FOV for the distances
    private const float VillageLod0DistM = 80f;         // full-detail buildings + shadows within this
    private const float VillageLod1DistM = 200f;        // LOD1 buildings + street props (no shadows)
    private const float VillageCullDistM = 450f;        // LOD1 buildings only, culled beyond
    private const float VillageChurchLod0DistM = 110f;  // the church is its own LODGroup (landmark)
    private const float VillageChurchCullDistM = 1500f; // spire LOD1 stays visible across the valley
    private const float VillageSetCullDistM = 1400f;    // village-wide street mesh / pines / arches
    // Only these kit roles cast shadows (LOD0 only); trim, flowers, garlands, icicles, glass,
    // metal and signs are too small for their shadow to read.
    private static readonly HashSet<string> VillageShadowRoles = new HashSet<string>
        { "Stone", "Render", "Timber", "TimberDark", "Roof", "Snow", "Copper" };
    private const float VillageFrontWallM = 9.0f;     // front-row chalet front wall from the centreline
    private const float VillageShopWallM = 10.0f;     // cafe / bakery front wall (terrace in front)
    private const float VillageBackRowGapM = 9f;      // plinth-to-plinth gap front row -> back row
    private const float VillageBankOffsetM = 4.05f;   // plough-bank centreline from the road centre
    private const float VillageLampEveryM = 24f;
    private const float VillageFlagPoleOffsetM = 6.3f; // street-crossing bunting poles (pavement edge)
    private const float VillageBuntingAnchorM = 6.9f;  // rope height on the pole
    private const float VillageBuntingSagM = 0.55f;    // lowest pennant tip stays > 5.9 m
    private const float VillageArchBeyondM = 14f;      // AZORA arches this far outside VillageHalfM

    private struct VillageType { public string name; public float W, D; }
    private static readonly VillageType[] VillageChaletTypes =
    {
        new VillageType { name = "ChaletA", W = 4.2f, D = 5.0f },
        new VillageType { name = "ChaletB", W = 5.0f, D = 6.0f },
        new VillageType { name = "ChaletC", W = 3.6f, D = 4.6f },
    };
    private static readonly VillageType VillageCafeType = new VillageType { name = "Cafe", W = 4.6f, D = 5.4f };
    private static readonly VillageType VillageBakeryType = new VillageType { name = "Bakery", W = 4.4f, D = 5.2f };

    private static readonly Dictionary<string, Material> _vkMat = new Dictionary<string, Material>();
    private static readonly HashSet<string> _vkMissing = new HashSet<string>();

    private static Texture2D VillageKitTex(string n) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"{VillageKitDir}/{n}.png");

    /// <summary>Warm lit-window / lantern glass. HDRP/Unlit (same pattern as Maple City's shop
    /// glazing) so the windows read lit in shade and on the snow-dimmed north faces.</summary>
    private static Material VillageGlow(string name, Color c)
    {
        var hd = Shader.Find("HDRP/Unlit");
        var mat = LoadOrCreate(name, hd != null ? "HDRP/Unlit" : "Unlit/Color");
        if (hd != null)
        {
            mat.SetColor("_UnlitColor", c);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
        }
        else mat.SetColor("_Color", c);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>One CelLit material per Blender role (material slot "Azora_Village_&lt;Role&gt;").
    /// Textures are the pre-coloured PNGs the Blender builder writes next to the GLBs.</summary>
    private static void VillageKitMaterials()
    {
        _vkMat.Clear();
        var snowShade = new Color(0.60f, 0.70f, 0.88f, 1f);
        void C(string role, Color c, float gloss, float spec, float rim, Texture t = null, Color? shade = null) =>
            _vkMat[role] = CelMaterial("Azora_Village_" + role, c, gloss, spec, rim, t, shade ?? GroundShade);

        C("Stone", new Color(0.70f, 0.69f, 0.65f), 0.07f, 0.05f, 0.10f, AzoraTexture("Azora_Stone_Albedo.png"));
        C("Render", new Color(0.93f, 0.91f, 0.88f), 0.05f, 0.03f, 0.10f, VillageKitTex("Azora_Village_Render"));
        C("Timber", new Color(1.00f, 0.96f, 0.92f), 0.06f, 0.03f, 0.12f, VillageKitTex("Azora_Village_Timber"));
        C("TimberDark", new Color(0.52f, 0.46f, 0.42f), 0.06f, 0.03f, 0.10f, VillageKitTex("Azora_Village_Timber"));
        C("Roof", new Color(1.00f, 1.00f, 1.00f), 0.10f, 0.06f, 0.10f, VillageKitTex("Azora_Village_Shingle"));
        C("Snow", new Color(0.93f, 0.95f, 0.98f), 0.30f, 0.18f, 0.30f, null, snowShade);
        C("Ice", new Color(0.78f, 0.89f, 0.97f), 0.85f, 0.60f, 0.55f, null, snowShade);
        C("GlassDark", new Color(0.10f, 0.13f, 0.18f), 0.75f, 0.50f, 0.10f);
        C("Trim", new Color(0.92f, 0.90f, 0.85f), 0.10f, 0.05f, 0.12f);
        C("Shutter", new Color(0.17f, 0.40f, 0.26f), 0.12f, 0.06f, 0.12f);
        C("ShutterRed", new Color(0.64f, 0.12f, 0.12f), 0.12f, 0.06f, 0.14f);
        C("Geranium", new Color(0.88f, 0.12f, 0.15f), 0.10f, 0.05f, 0.30f);
        C("Garland", new Color(0.13f, 0.31f, 0.18f), 0.06f, 0.04f, 0.14f);
        C("Metal", new Color(0.19f, 0.19f, 0.21f), 0.40f, 0.30f, 0.15f);
        C("Gold", new Color(0.86f, 0.66f, 0.26f), 0.60f, 0.50f, 0.30f);
        C("Copper", new Color(0.34f, 0.57f, 0.50f), 0.30f, 0.20f, 0.20f);
        C("Fabric", new Color(0.78f, 0.12f, 0.14f), 0.08f, 0.04f, 0.20f);
        C("FabricBlue", new Color(0.16f, 0.30f, 0.62f), 0.08f, 0.04f, 0.20f);
        C("SignRed", new Color(0.80f, 0.10f, 0.12f), 0.15f, 0.08f, 0.15f);
        C("SignWhite", new Color(0.97f, 0.97f, 0.95f), 0.15f, 0.08f, 0.12f);
        C("Belfry", new Color(0.07f, 0.07f, 0.09f), 0.10f, 0.05f, 0.05f);
        C("Flag", new Color(1f, 1f, 1f), 0.10f, 0.05f, 0.20f, VillageKitTex("Azora_Village_SwissFlag"));
        C("Cobble", new Color(0.95f, 0.94f, 0.92f), 0.10f, 0.06f, 0.06f, VillageKitTex("Azora_Village_Cobble"));
        _vkMat["Glow"] = VillageGlow("Azora_Village_Glow", new Color(1.35f, 1.00f, 0.58f, 1f));
    }

    private static Material VillageKitRole(string m)
    {
        const string P = "Azora_Village_";
        int i = m.IndexOf(P, System.StringComparison.Ordinal);
        string r = i >= 0 ? m.Substring(i + P.Length) : m;
        int cut = r.IndexOfAny(new[] { ' ', '.', '(' });
        if (cut > 0) r = r.Substring(0, cut);
        if (_vkMat.TryGetValue(r, out var mat)) return mat;
        if (_vkMissing.Add(m)) Debug.LogWarning($"[azora] village kit: unmapped material '{m}' -> Render");
        return _vkMat["Render"];
    }

    private static GlbSource VKit(string n) => LoadGlb($"{VillageKitDir}/Azora_Village_{n}.glb");

    /// <summary>Wraps a saved Mesh asset (Maple City street furniture, read-only) as a GlbSource;
    /// roles[s] = kit role for sub-mesh s, null = skip that sub-mesh.</summary>
    private static GlbSource VillageMeshSource(string path, params string[] roles)
    {
        var src = new GlbSource { name = path };
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { Debug.LogWarning($"[azora] village: missing {path}"); return src; }
        for (int s = 0; s < mesh.subMeshCount && s < roles.Length; s++)
            if (roles[s] != null)
                src.parts.Add(new GlbPart { mesh = mesh, sub = s, rel = Matrix4x4.identity, mat = "Azora_Village_" + roles[s] });
        src.bounds = mesh.bounds;
        src.ok = src.parts.Count > 0;
        return src;
    }

    /// <summary>
    /// Places a kit building whose origin is its ground-floor datum (footprint centre; the GLB
    /// plinth runs from about -plinthDepth up to +0.45) with its front (+z) facing the road.
    /// Datum = max(ground in front of the door + 0.15, highest ground under the footprint - 0.4),
    /// so nothing is buried past the plinth coping; where the GLB plinth cannot reach the lowest
    /// ground a stone skirt is added down to MinGround - 0.4 (same rule azora2 used).
    /// </summary>
    private static Vector3 PlaceVillageBuilding(ChunkedBatch hi, ChunkedBatch lo, Bins bins, GlbSource src, GlbSource srcLo,
                                                Vector3 centre, Vector3 toRoad, float xMin, float xMax, float zHalf,
                                                float plinthDepth = 1.15f)
    {
        var fwd = new Vector3(toRoad.x, 0f, toRoad.z).normalized;
        var right = Vector3.Cross(Vector3.up, fwd).normalized;
        float hMin = float.MaxValue, hMax = float.MinValue;
        for (int a = 0; a <= 4; a++)
            for (int b = 0; b <= 2; b++)
            {
                var p = centre + right * Mathf.Lerp(xMin, xMax, a / 4f) + fwd * ((b - 1) * zHalf);
                float h = GH(p.x, p.z);
                if (h == 0f) continue;
                hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
            }
        if (hMin > hMax) { hMin = hMax = centre.y; }
        var fp = centre + fwd * (zHalf + 0.7f);
        float hFront = GH(fp.x, fp.z); if (hFront == 0f) hFront = centre.y;
        float datum = Mathf.Max(hFront + 0.15f, hMax - 0.4f);
        var pos = new Vector3(centre.x, datum, centre.z);
        var m = Matrix4x4.TRS(pos, Quaternion.LookRotation(fwd, Vector3.up), Vector3.one);
        if (hi != null) hi.Add(src, m, VillageKitRole);
        if (lo != null) lo.Add(srcLo, m, VillageKitRole);
        if (datum - plinthDepth > hMin)
        {
            var c = centre + right * ((xMin + xMax) * 0.5f);
            float bottom = hMin - 0.4f;
            BoxB(bins[_vkMat["Stone"]], new Vector3(c.x, bottom, c.z), right * ((xMax - xMin) * 0.5f - 0.03f),
                 fwd * (zHalf - 0.03f), datum - plinthDepth + 0.3f - bottom);
        }
        return pos;
    }

    /// <summary>Up-facing textured quad (winding picked so the normal points up).</summary>
    private static void QuadUp(Bin b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                               Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
    {
        int i = b.v.Count;
        b.v.Add(p0); b.v.Add(p1); b.v.Add(p2); b.v.Add(p3);
        b.uv.Add(u0); b.uv.Add(u1); b.uv.Add(u2); b.uv.Add(u3);
        if (Vector3.Cross(p1 - p0, p2 - p0).y >= 0f) { b.tri.Add(i); b.tri.Add(i + 1); b.tri.Add(i + 2); b.tri.Add(i); b.tri.Add(i + 2); b.tri.Add(i + 3); }
        else { b.tri.Add(i); b.tri.Add(i + 2); b.tri.Add(i + 1); b.tri.Add(i); b.tri.Add(i + 3); b.tri.Add(i + 2); }
    }

    /// <summary>Quad whose normal is forced towards `facing`.</summary>
    private static void QuadFacing(Bin b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 facing, float uScale)
    {
        int i = b.v.Count;
        b.v.Add(p0); b.v.Add(p1); b.v.Add(p2); b.v.Add(p3);
        float w = (p1 - p0).magnitude * uScale, h = (p3 - p0).magnitude * uScale;
        b.uv.Add(new Vector2(0, 0)); b.uv.Add(new Vector2(w, 0)); b.uv.Add(new Vector2(w, h)); b.uv.Add(new Vector2(0, h));
        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), facing) >= 0f) { b.tri.Add(i); b.tri.Add(i + 1); b.tri.Add(i + 2); b.tri.Add(i); b.tri.Add(i + 2); b.tri.Add(i + 3); }
        else { b.tri.Add(i); b.tri.Add(i + 2); b.tri.Add(i + 1); b.tri.Add(i); b.tri.Add(i + 3); b.tri.Add(i + 2); }
    }

    /// <summary>Smooth plough bank: a half-ellipse profile swept along `path` (shared vertices so
    /// it shades as a soft drift, not a faceted prism), wound outward per quad.</summary>
    private static void SnowBank(Bin b, List<Vector3> path, List<Vector3> lat, float halfW, float h, float seed)
    {
        const int J = 6;
        if (path.Count < 2) return;
        int v0 = b.v.Count;
        for (int i = 0; i < path.Count; i++)
        {
            float end = Mathf.Min(1f, Mathf.Min(i, path.Count - 1 - i) / 2f);  // taper the ends
            float hh = h * Mathf.Lerp(0.72f, 1.15f, H01(i * 0.37f, seed)) * Mathf.Lerp(0.25f, 1f, end);
            float ww = halfW * Mathf.Lerp(0.85f, 1.1f, H01(i * 0.53f, seed + 3f));
            for (int j = 0; j <= J; j++)
            {
                float a = Mathf.PI * j / J;
                b.v.Add(path[i] + lat[i] * (-Mathf.Cos(a) * ww) + Vector3.up * (Mathf.Sin(a) * hh - 0.06f));
                b.uv.Add(new Vector2(j / (float)J, i * 0.5f));
            }
        }
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var inner = (path[i] + path[i + 1]) * 0.5f - Vector3.up * 0.4f;
            for (int j = 0; j < J; j++)
            {
                int a00 = v0 + i * (J + 1) + j, a01 = a00 + 1, a10 = a00 + J + 1, a11 = a10 + 1;
                var cq = (b.v[a00] + b.v[a01] + b.v[a10] + b.v[a11]) * 0.25f;
                var n = Vector3.Cross(b.v[a01] - b.v[a00], b.v[a10] - b.v[a00]);
                if (Vector3.Dot(n, cq - inner) >= 0f) { b.tri.Add(a00); b.tri.Add(a01); b.tri.Add(a10); b.tri.Add(a01); b.tri.Add(a11); b.tri.Add(a10); }
                else { b.tri.Add(a00); b.tri.Add(a10); b.tri.Add(a01); b.tri.Add(a01); b.tri.Add(a10); b.tri.Add(a11); }
            }
        }
    }

    /// <summary>Red / white pennant bunting on a sagging rope from a to b.</summary>
    private static void Bunting(Bin rope, Bin red, Bin white, Vector3 a, Vector3 b, float sag)
    {
        const int N = 16;
        Vector3 P(float t) => Vector3.Lerp(a, b, t) - Vector3.up * (sag * 4f * t * (1f - t));
        for (int i = 0; i < N; i++) Beam(rope, P(i / (float)N), P((i + 1) / (float)N), 0.012f);
        int flags = Mathf.Max(3, Mathf.RoundToInt((b - a).magnitude / 0.55f));
        for (int k = 0; k < flags; k++)
        {
            var p0 = P((k + 0.12f) / flags); var p1 = P((k + 0.88f) / flags);
            TriDS(k % 2 == 0 ? red : white, p0, p1, (p0 + p1) * 0.5f - Vector3.up * 0.36f);
        }
    }

    /// <summary>TileBatch split into position-routed buckets (one per village chunk).</summary>
    private class ChunkedBatch
    {
        public readonly TileBatch[] b;
        private readonly System.Func<Vector3, int> pick;
        public ChunkedBatch(int n, System.Func<Vector3, int> pick)
        {
            b = new TileBatch[n];
            for (int i = 0; i < n; i++) b[i] = new TileBatch();
            this.pick = pick;
        }
        public void Add(GlbSource src, Matrix4x4 place, System.Func<string, Material> role) =>
            b[pick(place.GetColumn(3))].Add(src, place, role);
    }

    private static float VillageLodHeight(float size, float dist) =>
        Mathf.Clamp(size / (dist * 2f * Mathf.Tan(VillageLodRefFovDeg * 0.5f * Mathf.Deg2Rad)), 0.0005f, 0.99f);

    private static Renderer[] Rs(Transform t) => t.GetComponentsInChildren<Renderer>(true);

    /// <summary>LOD0 renderers cast shadows only for the big structural kit roles.</summary>
    private static void VillageRoleShadows(Transform t)
    {
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
        {
            var m = r.sharedMaterial;
            string role = m != null && m.name.StartsWith("Azora_Village_") ? m.name.Substring("Azora_Village_".Length) : "";
            r.shadowCastingMode = VillageShadowRoles.Contains(role)
                ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>Shadowless twin of every renderer under src (shares the mesh asset), for LOD1+.</summary>
    private static Transform ShadowlessTwin(Transform src, Transform parent, string name)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false);
        foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
        {
            var go = new GameObject(mr.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(t, false);
            go.GetComponent<MeshFilter>().sharedMesh = mr.GetComponent<MeshFilter>().sharedMesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
        return t;
    }

    /// <summary>
    /// Flushes one chunk under a 3-level LODGroup (azC2):
    ///   LOD0 (&lt; VillageLod0DistM)  full kit buildings (role shadows) + back row (shadows) + street props
    ///   LOD1 (&lt; VillageLod1DistM)  LOD1 buildings + back row + street props, nothing casts
    ///   LOD2 (&lt; VillageCullDistM)  LOD1 buildings + back row, nothing casts; culled beyond.
    /// Street props are already shadowless, so the same renderers serve LOD0 and LOD1.
    /// </summary>
    private static int FlushVillageChunk(Transform group, string label, TileBatch hi, TileBatch lo, TileBatch back,
                                         TileBatch props, int tile)
    {
        if (hi.lists.Count == 0 && lo.lists.Count == 0 && back.lists.Count == 0 && props.lists.Count == 0) return 0;
        var chunk = new GameObject(label).transform; chunk.SetParent(group, false);
        var t0 = new GameObject("LOD0").transform; t0.SetParent(chunk, false);
        var tb = new GameObject("Back Row").transform; tb.SetParent(chunk, false);
        var tp = new GameObject("Street Props (no shadows)").transform; tp.SetParent(chunk, false);
        var t1 = new GameObject("LOD1").transform; t1.SetParent(chunk, false);
        int n = hi.Flush(t0, label + "_L0", tile) + lo.Flush(t1, label + "_L1", tile) +
                back.Flush(tb, label + "_Back", tile) + props.Flush(tp, label + "_Props", tile);
        VillageRoleShadows(t0);
        NoShadows(t1); NoShadows(tp);
        foreach (var r in tb.GetComponentsInChildren<MeshRenderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        var tbLo = ShadowlessTwin(tb, chunk, "Back Row (LOD1+, no shadows)");
        n += tbLo.childCount;

        var r0 = new List<Renderer>(Rs(t0)); r0.AddRange(Rs(tb)); r0.AddRange(Rs(tp));
        var r1 = new List<Renderer>(Rs(t1)); r1.AddRange(Rs(tbLo)); r1.AddRange(Rs(tp));
        var r2 = new List<Renderer>(Rs(t1)); r2.AddRange(Rs(tbLo));
        var lg = chunk.gameObject.AddComponent<LODGroup>();
        lg.SetLODs(new[] { new LOD(0.5f, r0.ToArray()), new LOD(0.1f, r1.ToArray()), new LOD(0.01f, r2.ToArray()) });
        lg.RecalculateBounds();
        float s = lg.size;
        lg.SetLODs(new[]
        {
            new LOD(VillageLodHeight(s, VillageLod0DistM), r0.ToArray()),
            new LOD(VillageLodHeight(s, VillageLod1DistM), r1.ToArray()),
            new LOD(VillageLodHeight(s, VillageCullDistM), r2.ToArray()),
        });
        lg.fadeMode = LODFadeMode.None;
        return n;
    }

    /// <summary>Church: its own 2-level LODGroup (the spire is the village landmark).</summary>
    private static int FlushVillageChurch(Transform group, string label, TileBatch hi, TileBatch lo)
    {
        if (hi.lists.Count == 0 && lo.lists.Count == 0) return 0;
        var g = new GameObject(label).transform; g.SetParent(group, false);
        var t0 = new GameObject("LOD0").transform; t0.SetParent(g, false);
        var t1 = new GameObject("LOD1").transform; t1.SetParent(g, false);
        int n = hi.Flush(t0, label + "_L0", 0) + lo.Flush(t1, label + "_L1", 0);
        VillageRoleShadows(t0); NoShadows(t1);
        var lg = g.gameObject.AddComponent<LODGroup>();
        lg.SetLODs(new[] { new LOD(0.5f, Rs(t0)), new LOD(0.01f, Rs(t1)) });
        lg.RecalculateBounds();
        float s = lg.size;
        lg.SetLODs(new[]
        {
            new LOD(VillageLodHeight(s, VillageChurchLod0DistM), Rs(t0)),
            new LOD(VillageLodHeight(s, VillageChurchCullDistM), Rs(t1)),
        });
        return n;
    }

    /// <summary>Single-level LODGroup that culls a whole village-wide group beyond `dist`.</summary>
    private static void VillageFarCull(Transform t, float dist)
    {
        var rs = Rs(t);
        if (rs.Length == 0) return;
        var lg = t.gameObject.AddComponent<LODGroup>();
        lg.SetLODs(new[] { new LOD(0.01f, rs) });
        lg.RecalculateBounds();
        lg.SetLODs(new[] { new LOD(VillageLodHeight(lg.size, dist), rs) });
    }

    private static void NoShadows(Transform t)
    {
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static long CountTris(Transform t)
    {
        long n = 0;
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null)
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) n += mf.sharedMesh.GetIndexCount(s) / 3;
        return n;
    }

    // ------------------------------------------------------------------ entry

    private static void BuildSwissVillages(Transform root, AzoraRoute route)
    {
        VillageMaterials();
        VillageKitMaterials();
        if (_needleA == null) DressingMaterials();
        var group = new GameObject("Azora Swiss Villages").transform;
        group.SetParent(root, false);
        var peopleRoot = new GameObject("Azora Villagers").transform;
        peopleRoot.SetParent(group, false);
        var cast = FindCrowdCast();
        _people = 0;
        int renderers = 0, chalets = 0, cows = 0;
        var pines = new[] { Glb(MinatoGlbDir, "Minato_SakuraPass_Pine_A"), Glb(MinatoGlbDir, "Minato_SakuraPass_Pine_B") };
        var lantern = Glb(SakuraGlbDir, "SakuraPass_Stone_Lantern");

        // WP-C kit (Blender) + Maple City street furniture (read-only mesh assets)
        var chaletHi = new GlbSource[3]; var chaletLo = new GlbSource[3];
        for (int k = 0; k < 3; k++) { chaletHi[k] = VKit(VillageChaletTypes[k].name); chaletLo[k] = VKit(VillageChaletTypes[k].name + "_LOD1"); }
        GlbSource cafeHi = VKit("Cafe"), cafeLo = VKit("Cafe_LOD1"), bakeHi = VKit("Bakery"), bakeLo = VKit("Bakery_LOD1"),
                  churchHi = VKit("Church"), churchLo = VKit("Church_LOD1"), fountain = VKit("Fountain"),
                  arch = VKit("SignArch"), umbrella = VKit("Umbrella"), flagPole = VKit("FlagPole");
        const string MapleProps = "Assets/Environment/MapleCity/MapleRow/Meshes";
        var lamp = VillageMeshSource($"{MapleProps}/MapleRow_Prop_Lamp.asset", "Metal", "Glow", null); // banner dropped
        var bench = VillageMeshSource($"{MapleProps}/MapleRow_Prop_Bench.asset", "TimberDark", "Metal");
        var hoop = VillageMeshSource($"{MapleProps}/MapleRow_Prop_Hoop.asset", "Metal");
        var planter = VillageMeshSource($"{MapleProps}/MapleRow_Prop_Planter.asset", "Stone", "Garland");
        int kitOk = 0;
        foreach (var s in new[] { chaletHi[0], chaletHi[1], chaletHi[2], cafeHi, bakeHi, churchHi, fountain, arch, umbrella, flagPole })
            if (s.ok) kitOk++;
        long trisTotal = 0;

        for (int vi = 0; vi < VillageCentres.Length; vi++)
        {
            float dc = VillageCentres[vi];
            if (dc + VillageHalfM + VillageArchBeyondM >= route.Length) continue;
            var bins = new Bins();
            var small = new Bins();          // shadowless merged dressing (bunting, pennants)
            var props = new TileBatch();     // pines, lanterns, fountain, arches (cast shadows)
            int nRows = Mathf.CeilToInt(2f * VillageHalfM / VillageChunkM);
            int nChunks = nRows * 2;         // one chunk per road side per row
            int iLo = route.IndexAt(Mathf.Max(0f, dc - VillageHalfM - VillageArchBeyondM - 40f));
            int iHi = route.IndexAt(Mathf.Min(route.Length, dc + VillageHalfM + VillageArchBeyondM + 40f));
            int ChunkAt(Vector3 p)
            {
                int best = iLo; float bd = float.MaxValue;
                for (int q = iLo; q <= iHi; q++)
                {
                    var dp = route.Position[q] - p; dp.y = 0f;
                    float dd2 = dp.sqrMagnitude;
                    if (dd2 < bd) { bd = dd2; best = q; }
                }
                int row = Mathf.Clamp(Mathf.FloorToInt((route.Distance[best] - (dc - VillageHalfM)) / VillageChunkM), 0, nRows - 1);
                var sfl = route.SideFlat(best);
                bool right = Vector3.Dot(p - route.Position[best], new Vector3(sfl.x, 0f, sfl.z)) > 0f;
                return row * 2 + (right ? 1 : 0);
            }
            var hi = new ChunkedBatch(nChunks, ChunkAt);
            var lo = new ChunkedBatch(nChunks, ChunkAt);
            var backRow = new ChunkedBatch(nChunks, ChunkAt); // back-row chalets: LOD1 mesh, in the chunk LODs
            var streetProps = new ChunkedBatch(nChunks, ChunkAt); // umbrellas, lamps, benches, hoops, planters, flags
            var churchHiB = new ChunkedBatch(1, _ => 0);
            var churchLoB = new ChunkedBatch(1, _ => 0);
            var stoneBin = bins[_vkMat["Stone"]];
            var cobbleBin = bins[_vkMat["Cobble"]];
            var snowBin = bins[_vkMat["Snow"]];

            // ---- chalets: front row (LOD0/LOD1 chunks) + sparse back row (LOD1), gable to the road
            for (float d = dc - VillageHalfM; d <= dc + VillageHalfM; d += 26f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int row = 0; row < 2; row++)
                    {
                        float seed = H01(d + row * 7f, side * 3.7f + vi);
                        if (row == 1 && seed < 0.35f) continue;
                        float dd = d + (row == 1 ? 13f : 0f) + (seed - 0.5f) * 6f;
                        // leave the square, cafe, bakery and church plots free
                        if (Mathf.Abs(dd - dc) < 30f && side > 0 && row == 0) continue;
                        if (Mathf.Abs(dd - dc) < 24f && side < 0 && row == 0) continue;           // cafe
                        if (Mathf.Abs(dd - (dc - 40f)) < 17f && side > 0 && row == 0) continue;  // bakery
                        if (Mathf.Abs(dd - (dc + 90f)) < 28f && side < 0) continue;              // church
                        int ti = Mathf.Min(2, (int)(H01(dd, 5.5f) * 3f));
                        var ty = VillageChaletTypes[ti];
                        float wall = VillageFrontWallM + seed * 1.5f +
                                     (row == 1 ? 2f * ty.D + VillageBackRowGapM + 2f : 0f);
                        int i = route.IndexAt(dd);
                        DFoot(route, i, side * (wall + ty.D), out var f);
                        var s = route.SideFlat(i);
                        var toRoad = -new Vector3(s.x, 0f, s.z) * side;
                        float yaw = (H01(dd, 8.1f) - 0.5f) * 8f;
                        toRoad = Quaternion.Euler(0f, yaw, 0f) * toRoad;
                        var pos = PlaceVillageBuilding(row == 0 ? hi : null, row == 0 ? lo : backRow,
                                                       bins, chaletHi[ti], chaletLo[ti], f, toRoad, -ty.W - 0.15f, ty.W + 0.15f, ty.D + 0.15f);
                        chalets++;
                        if (row == 0 && seed > 0.3f && !WinterTownsfolkEnabled)
                        {
                            var right = Vector3.Cross(Vector3.up, toRoad).normalized;
                            var pp = pos + toRoad.normalized * (ty.D + 2.2f) + right * (seed - 0.5f) * 3f; pp.y = GH(pp.x, pp.z);
                            Person(cast.stand, peopleRoot, pp, -right + toRoad * 0.3f, MinatoCrowdActor.MotionKind.Idle, pp, seed, false);
                        }
                    }
                }
                // pavement walkers both sides
                if (!WinterTownsfolkEnabled)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int i = route.IndexAt(d);
                        DFoot(route, i, side * (RoadHalfWidth + ShoulderWidth + 1.4f), out var w0);
                        int j = route.IndexAt(Mathf.Min(d + 22f, route.Length - 12f));
                        DFoot(route, j, side * (RoadHalfWidth + ShoulderWidth + 1.4f), out var w1);
                        Person(cast.walk, peopleRoot, w0, w1 - w0, MinatoCrowdActor.MotionKind.Walk, w1, H01(d, side + 40f), false);
                    }
            }

            // ---- village street: cobbled pavements with a granite kerb, plough banks, lamps, benches
            const float Step = 2f;
            float paveIn = RoadHalfWidth + ShoulderWidth + 0.3f, paveOut = RoadHalfWidth + ShoulderWidth + 2.8f;
            var bankPath = new[] { new List<Vector3>(), new List<Vector3>() };
            var bankLat = new[] { new List<Vector3>(), new List<Vector3>() };
            for (float d = dc - VillageHalfM; d < dc + VillageHalfM; d += Step)
            {
                int i = route.IndexAt(d), j = route.IndexAt(d + Step);
                var si = route.SideFlat(i);
                for (int side = -1; side <= 1; side += 2)
                {
                    int sIdx = side < 0 ? 0 : 1;
                    float[] offs = { paveIn, (paveIn + paveOut) * 0.5f, paveOut };
                    for (int c = 0; c < 2; c++)
                    {
                        DFoot(route, i, side * offs[c], out var a); DFoot(route, i, side * offs[c + 1], out var b);
                        DFoot(route, j, side * offs[c + 1], out var cc); DFoot(route, j, side * offs[c], out var e);
                        var lift = Vector3.up * 0.12f;
                        float u0 = offs[c] / 2f, u1 = offs[c + 1] / 2f, v0 = d / 2f, v1 = (d + Step) / 2f;
                        QuadUp(cobbleBin, a + lift, b + lift, cc + lift, e + lift,
                               new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
                    }
                    // kerb face towards the road
                    DFoot(route, i, side * paveIn, out var k0); DFoot(route, j, side * paveIn, out var k1);
                    QuadFacing(stoneBin, k0 - Vector3.up * 0.12f, k1 - Vector3.up * 0.12f, k1 + Vector3.up * 0.12f, k0 + Vector3.up * 0.12f,
                               -new Vector3(si.x, 0f, si.z) * side, 1f / 3f);

                    // plough bank (skipped where the cafe rail, bakery bikes and the square meet the kerb)
                    bool gap = (side < 0 && Mathf.Abs(d - dc) < 17f) || (side > 0 && Mathf.Abs(d - (dc - 36f)) < 16f) ||
                               (side > 0 && Mathf.Abs(d - dc) < 18f) || (side < 0 && Mathf.Abs(d - (dc + 90f)) < 5f) ||
                               H01(d * 0.13f, side + vi * 5f) < 0.06f;
                    if (!gap)
                    {
                        DFoot(route, i, side * VillageBankOffsetM, out var bp);
                        bankPath[sIdx].Add(bp); bankLat[sIdx].Add(new Vector3(si.x, 0f, si.z) * side);
                    }
                    else if (bankPath[sIdx].Count > 0)
                    {
                        SnowBank(snowBin, bankPath[sIdx], bankLat[sIdx], 0.5f, 0.5f, d + side);
                        bankPath[sIdx].Clear(); bankLat[sIdx].Clear();
                    }
                }
                // warm lamps every VillageLampEveryM (Maple Row lamp posts, banner dropped)
                if (Mathf.Repeat(d - dc, VillageLampEveryM) < Step)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (side < 0 && Mathf.Abs(d - dc) < 13f) continue; // cafe rail
                        DFoot(route, i, side * (RoadHalfWidth + ShoulderWidth + 0.6f), out var lp);
                        streetProps.Add(lamp, Matrix4x4.TRS(lp - Vector3.up * 0.05f,
                                       Quaternion.LookRotation(new Vector3(si.x, 0f, si.z) * side, Vector3.up), Vector3.one), VillageKitRole);
                    }
                // benches facing the street, half-way between lamps
                if (Mathf.Repeat(d - dc + 12f, 48f) < Step)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (Mathf.Abs(d - dc) < 20f || Mathf.Abs(d - (dc - 40f)) < 12f || (side < 0 && Mathf.Abs(d - (dc + 90f)) < 12f)) continue;
                        DFoot(route, i, side * (paveOut - 0.45f), out var bp);
                        streetProps.Add(bench, Matrix4x4.TRS(bp + Vector3.up * 0.1f,
                                       Quaternion.LookRotation(new Vector3(si.x, 0f, si.z) * side, Vector3.up), Vector3.one), VillageKitRole);
                    }
            }
            for (int sIdx = 0; sIdx < 2; sIdx++)
                if (bankPath[sIdx].Count > 1) SnowBank(snowBin, bankPath[sIdx], bankLat[sIdx], 0.5f, 0.5f, sIdx + 99f);

            // ---- street-crossing Swiss bunting between flag poles (low point > 5.9 m)
            for (int k = -4; k <= 4; k++)
            {
                if (k == 0) continue;
                float d = dc + k * 52f;
                int i = route.IndexAt(d);
                var s = route.SideFlat(i); var sf = new Vector3(s.x, 0f, s.z); var t = new Vector3(-s.z, 0f, s.x);
                DFoot(route, i, -VillageFlagPoleOffsetM, out var pa);
                DFoot(route, i, VillageFlagPoleOffsetM, out var pb);
                streetProps.Add(flagPole, Matrix4x4.TRS(pa - Vector3.up * 0.1f, Quaternion.LookRotation(t, Vector3.up), Vector3.one), VillageKitRole);
                streetProps.Add(flagPole, Matrix4x4.TRS(pb - Vector3.up * 0.1f, Quaternion.LookRotation(-t, Vector3.up), Vector3.one), VillageKitRole);
                var ra = pa + Vector3.up * VillageBuntingAnchorM; var rb = pb + Vector3.up * VillageBuntingAnchorM;
                Bunting(small[_vkMat["Metal"]], small[_vkMat["SignRed"]], small[_vkMat["SignWhite"]], ra, rb, VillageBuntingSagM);
            }

            // ---- fountain square (right side at the centre) with the frozen fountain, benches, flags
            {
                int i = route.IndexAt(dc);
                var s = route.SideFlat(i); var sf = new Vector3(s.x, 0f, s.z); var t = new Vector3(-s.z, 0f, s.x);
                DFoot(route, i, RoadHalfWidth + ShoulderWidth + 12f, out var sq);
                float sqBottom = MinGround(sq, sf * 9f, t * 16f) - 0.3f;
                BoxB(cobbleBin, new Vector3(sq.x, sqBottom, sq.z), sf * 9f, t * 16f, sq.y + 0.12f - sqBottom);
                var sqTop = sq + Vector3.up * 0.12f;
                props.Add(fountain, Matrix4x4.TRS(sqTop, Quaternion.LookRotation(-sf, Vector3.up), Vector3.one), VillageKitRole);
                for (int k = 0; k < 4; k++)
                {
                    var bp = sqTop + sf * (k < 2 ? 5.5f : -5.5f) + t * (k % 2 == 0 ? 5f : -5f);
                    var away = bp - sqTop; away.y = 0f;
                    streetProps.Add(bench, Matrix4x4.TRS(bp, Quaternion.LookRotation(away, Vector3.up), Vector3.one), VillageKitRole);
                    streetProps.Add(planter, Matrix4x4.TRS(sqTop + sf * (k < 2 ? 8f : -8f) + t * (k % 2 == 0 ? 14f : -14f),
                                   Quaternion.LookRotation(t, Vector3.up), Vector3.one), VillageKitRole);
                }
                for (int k = -1; k <= 1; k += 2)
                    streetProps.Add(flagPole, Matrix4x4.TRS(sqTop + sf * 7.8f + t * k * 9f, Quaternion.LookRotation(t, Vector3.up), Vector3.one), VillageKitRole);
                for (int k = 0; k < 5; k++)   // bike hoops at the square's kerb
                    streetProps.Add(hoop, Matrix4x4.TRS(sqTop - sf * 7.6f + t * (10f + k * 1.0f), Quaternion.LookRotation(sf, Vector3.up), Vector3.one), VillageKitRole);

                if (!WinterTownsfolkEnabled)
                {
                    for (int k = 0; k < 10; k++)
                    {
                        float a = k * 0.628f;
                        var pp = sq + (sf * Mathf.Cos(a) + t * Mathf.Sin(a)) * Mathf.Lerp(3f, 7f, H01(k, vi + 3f)); pp.y = GH(pp.x, pp.z) + 0.12f;
                        if (k % 3 == 0)
                            Person(cast.walk, peopleRoot, pp, t * (k % 2 == 0 ? 1f : -1f), MinatoCrowdActor.MotionKind.Walk, pp + t * 10f, H01(k, vi + 5f), false);
                        else
                            Person(cast.stand, peopleRoot, pp, sq - pp, MinatoCrowdActor.MotionKind.Idle, pp, H01(k, vi + 6f), false);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        var bp = sq + sf * (k < 2 ? 7f : -7f) + t * (k % 2 == 0 ? 6f : -6f); bp.y = GH(bp.x, bp.z) + 0.12f;
                        Person(cast.sit, peopleRoot, bp, bp - sq, MinatoCrowdActor.MotionKind.Sit, bp, H01(k, vi + 8f), true);
                    }
                }

                // church (left side at +90 m): nave along the road, tower at local +x, door to the road
                int ci = route.IndexAt(dc + 90f);
                var cs = route.SideFlat(ci); var csf = new Vector3(cs.x, 0f, cs.z); var ct = new Vector3(-cs.z, 0f, cs.x);
                DFoot(route, ci, -(RoadHalfWidth + ShoulderWidth + 11.5f + 5.15f), out var ch);
                var chPos = PlaceVillageBuilding(churchHiB, churchLoB, bins, churchHi, churchLo,
                                                 ch, csf, -9.3f, 14.5f, 5.3f, 0.75f);
                var chRight = Vector3.Cross(Vector3.up, csf).normalized;
                for (int k = -1; k <= 1; k += 2)
                    streetProps.Add(planter, Matrix4x4.TRS(chPos + csf * 6.4f + chRight * k * 2.2f, Quaternion.LookRotation(chRight, Vector3.up), Vector3.one), VillageKitRole);
                streetProps.Add(flagPole, Matrix4x4.TRS(chPos + csf * 6.6f + chRight * 11.7f - Vector3.up * 0.1f, Quaternion.LookRotation(ct, Vector3.up), Vector3.one), VillageKitRole);
                // forecourt from the pavement to the church door
                {
                    DFoot(route, ci, -paveOut, out var fa);
                    var fb = chPos + csf * 5.3f;
                    float fBottom = Mathf.Min(fa.y, fb.y) - 0.3f;
                    var fc = (fa + fb) * 0.5f;
                    float half = Mathf.Max(0.5f, (new Vector3(fb.x - fa.x, 0f, fb.z - fa.z)).magnitude * 0.5f);
                    BoxB(cobbleBin, new Vector3(fc.x, fBottom, fc.z), chRight * 3.2f, csf * half, Mathf.Max(fa.y, chPos.y) + 0.1f - fBottom);
                }
            }

            // ---- Konditorei terrace in front of the cafe (left side at the centre)
            {
                int i = route.IndexAt(dc);
                var s = route.SideFlat(i); var sf = new Vector3(s.x, 0f, s.z); var t = new Vector3(-s.z, 0f, s.x);
                DFoot(route, i, -(VillageShopWallM + VillageCafeType.D), out var cafeC);
                PlaceVillageBuilding(hi, lo, bins, cafeHi, cafeLo, cafeC, sf,
                                     -VillageCafeType.W - 0.15f, VillageCafeType.W + 0.15f, VillageCafeType.D + 0.15f);
                chalets++;
                DFoot(route, i, -(RoadHalfWidth + ShoulderWidth + 3.2f), out var tc);
                BoxB(bins[_chaletTimber], tc - Vector3.up * 0.25f, sf * 2.4f, t * 12f, 0.4f); // deck
                for (int k = 0; k < 5; k++)
                {
                    var tp = tc + t * (k - 2) * 4.5f + Vector3.up * 0.15f;
                    BoxB(bins[_tableTop], tp + Vector3.up * 0.72f, sf * 0.45f, t * 0.45f, 0.05f);
                    BoxB(bins[_steel], tp, sf * 0.05f, t * 0.05f, 0.72f);
                    // closed, snow-dusted umbrella through the table (winter)
                    streetProps.Add(umbrella, Matrix4x4.TRS(tp + Vector3.up * 0.02f, Quaternion.Euler(0f, k * 47f, 0f), Vector3.one), VillageKitRole);
                    if (!WinterTownsfolkEnabled)
                    {
                        Person(cast.sit, peopleRoot, tp + t * 1.1f, -t, MinatoCrowdActor.MotionKind.Sit, tp, H01(k, vi + 11f), true);
                        Person(cast.sit, peopleRoot, tp - t * 1.1f, t, MinatoCrowdActor.MotionKind.Sit, tp, H01(k, vi + 12f), true);
                        if (k % 2 == 0)
                            Person(cast.stand, peopleRoot, tp + sf * 1.6f, -sf, MinatoCrowdActor.MotionKind.Idle, tp, H01(k, vi + 13f), false);
                    }
                }
                // bike rail along the road side of the terrace (riders' stop)
                BoxB(bins[_steel], tc + sf * 2.6f + Vector3.up * 0.8f, sf * 0.04f, t * 11f, 0.06f);
                for (int k = -5; k <= 5; k++) BoxB(bins[_steel], tc + sf * 2.6f + t * k * 2.2f - Vector3.up * 0.1f, sf * 0.04f, t * 0.04f, 0.95f);
                // parked road bikes stood along the rail
                var frames = new[] { _bikeFrameA, _bikeFrameB, _bikeFrameC };
                for (int k = 0; k < 8; k++)
                {
                    if (H01(k, vi + 70f) < 0.2f) continue;
                    var bp = tc + sf * 2.88f + t * (k - 3.5f) * 2.6f; bp.y = GH(bp.x, bp.z);
                    ParkedBike(bins, bp, k % 2 == 0 ? t : -t, sf, frames[(int)(H01(k, vi + 71f) * 3f) % 3]);
                }
                // second terrace across the road: the Baeckerei (GLB with its own rolled blue awning)
                {
                    int bi = route.IndexAt(dc - 40f);
                    var bs = route.SideFlat(bi); var bsf = new Vector3(bs.x, 0f, bs.z); var bt = new Vector3(-bs.z, 0f, bs.x);
                    DFoot(route, bi, VillageShopWallM + VillageBakeryType.D, out var bakeC);
                    PlaceVillageBuilding(hi, lo, bins, bakeHi, bakeLo, bakeC, -bsf,
                                         -VillageBakeryType.W - 0.15f, VillageBakeryType.W + 0.15f, VillageBakeryType.D + 0.15f);
                    chalets++;
                    DFoot(route, bi, RoadHalfWidth + ShoulderWidth + 3.4f, out var bc);
                    BoxB(cobbleBin, bc - Vector3.up * 0.25f, bsf * 2.3f, bt * 8f, 0.38f);
                    for (int k = 0; k < 3; k++)
                    {
                        var tp = bc + bt * (k - 1) * 4.5f + Vector3.up * 0.13f;
                        BoxB(bins[_tableTop], tp + Vector3.up * 0.72f, bsf * 0.4f, bt * 0.4f, 0.05f);
                        BoxB(bins[_steel], tp, bsf * 0.05f, bt * 0.05f, 0.72f);
                        streetProps.Add(umbrella, Matrix4x4.TRS(tp + Vector3.up * 0.02f, Quaternion.Euler(0f, k * 61f, 0f), Vector3.one), VillageKitRole);
                        if (!WinterTownsfolkEnabled)
                        {
                            Person(cast.sit, peopleRoot, tp + bt * 1.1f, -bt, MinatoCrowdActor.MotionKind.Sit, tp, H01(k, vi + 81f), true);
                            if (k != 1) Person(cast.sit, peopleRoot, tp - bt * 1.1f, bt, MinatoCrowdActor.MotionKind.Sit, tp, H01(k, vi + 82f), true);
                        }
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        var bp = bc - bsf * 2.5f + bt * (k * 2.0f + 9f); bp.y = GH(bp.x, bp.z);
                        ParkedBike(bins, bp, bt, bsf, frames[(k + vi) % 3]);
                        streetProps.Add(hoop, Matrix4x4.TRS(bp + bt * 0.9f, Quaternion.LookRotation(bsf, Vector3.up), Vector3.one), VillageKitRole);
                    }
                    if (!WinterTownsfolkEnabled)
                    {
                        var qp = bc - bsf * 0.6f - bt * 6.5f; qp.y = GH(qp.x, qp.z);
                        Person(cast.stand, peopleRoot, qp, -bsf, MinatoCrowdActor.MotionKind.Idle, qp, H01(vi, 90f), false);
                        Person(cast.stand, peopleRoot, qp + bt * 0.9f, -bsf + bt * 0.3f, MinatoCrowdActor.MotionKind.Idle, qp, H01(vi, 91f), false);
                    }
                }
                // cyclists stood at the rail (Minato donors are dismounted riders in kit)
                if (!WinterTownsfolkEnabled)
                    for (int k = 0; k < 4; k++)
                    {
                        var rp = tc + sf * 3.3f + t * (k - 1.5f) * 4f; rp.y = GH(rp.x, rp.z);
                        Person(cast.stand, peopleRoot, rp, -sf + t * 0.4f, MinatoCrowdActor.MotionKind.Idle, rp, H01(k, vi + 14f), false);
                    }
            }

            // Cows in the meadow behind the village, and pines framing it.
            for (int k = 0; k < 18; k++)
            {
                float d = dc + (H01(k, vi + 20f) - 0.5f) * 2f * (VillageHalfM + 120f);
                float side = H01(k, vi + 21f) > 0.5f ? 1f : -1f;
                int i = route.IndexAt(Mathf.Clamp(d, 12f, route.Length - 12f));
                DFoot(route, i, side * Mathf.Lerp(45f, 110f, H01(k, vi + 22f)), out var cf);
                float a = H01(k, vi + 23f) * 6.283f;
                Cow(bins, cf, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), H01(k, vi + 24f));
                cows++;
            }
            for (int k = 0; k < 40; k++)
            {
                float d = dc + (H01(k, vi + 30f) - 0.5f) * 2f * (VillageHalfM + 60f);
                float side = H01(k, vi + 31f) > 0.5f ? 1f : -1f;
                int i = route.IndexAt(Mathf.Clamp(d, 12f, route.Length - 12f));
                DFoot(route, i, side * Mathf.Lerp(42f, 95f, H01(k, vi + 32f)), out var pf);
                var src = pines[k & 1];
                props.Add(src, PlaceOnGround(src, pf, k * 37f, Mathf.Lerp(9f, 18f, H01(k, vi + 33f)), 1f, 0.3f), m => TreeRole(m, _needleA));
            }
            // Stone lanterns at the village gates.
            foreach (float gd in new[] { dc - VillageHalfM, dc + VillageHalfM })
                for (int side = -1; side <= 1; side += 2)
                {
                    DFoot(route, route.IndexAt(gd), side * (RoadHalfWidth + ShoulderWidth + 1.2f), out var lf);
                    props.Add(lantern, PlaceOnGround(lantern, lf, 0f, 1.8f, 1f, 0.05f), StoneRole);
                }

            // Cable car from the upper village up the slope.
            if (vi == 1)
            {
                int i = route.IndexAt(dc + 150f);
                var s = route.SideFlat(i); var sf = new Vector3(s.x, 0f, s.z); var t = new Vector3(-s.z, 0f, s.x);
                Vector3 prevTop = Vector3.zero;
                for (int k = 0; k < 5; k++)
                {
                    DFoot(route, i, 30f + k * 140f, out var pf);
                    float h = k == 0 ? 8f : 16f;
                    if (k == 0) // valley station on a stone base down to the terrain
                    {
                        float vb = MinGround(pf, sf * 5.2f, t * 6.2f) - 0.4f;
                        BoxB(bins[_stoneBase], new Vector3(pf.x, vb, pf.z), sf * 5.1f, t * 6.1f, pf.y + 1.2f - vb);
                        BoxB(bins[_chaletTimber], pf + Vector3.up * 1.1f, sf * 5f, t * 6f, 5f);
                    }
                    float pb = MinGround(pf, sf * 0.5f, t * 0.5f) - 0.3f;
                    BoxB(bins[_stoneBase], new Vector3(pf.x, pb, pf.z), sf * 0.9f, t * 0.9f, pf.y + 0.4f - pb);
                    BoxB(bins[_steel], pf, sf * 0.45f, t * 0.45f, h);
                    BoxB(bins[_steel], pf + Vector3.up * h, sf * 0.3f, t * 2.4f, 0.4f);
                    var topPt = pf + Vector3.up * (h + 0.2f);
                    if (k > 0)
                        for (int c = -1; c <= 1; c += 2)
                        {
                            var a = prevTop + t * c * 2.2f; var b = topPt + t * c * 2.2f;
                            var mid = (a + b) * 0.5f; var dir = (b - a);
                            var r = Vector3.Cross(dir.normalized, Vector3.up).normalized * 0.04f;
                            // cable as a thin double-sided ribbon
                            QuadDS(bins[_steel], a - r, a + r, b + r, b - r);
                            QuadDS(bins[_steel], a - Vector3.up * 0.04f, a + Vector3.up * 0.04f, b + Vector3.up * 0.04f, b - Vector3.up * 0.04f);
                            if (k == 2 && c == 1) BoxB(bins[_awningRed], mid - Vector3.up * 2.6f, t * 1.0f, sf * 1.2f, 2.0f); // cabin
                            if (k == 3 && c == -1) BoxB(bins[_awningRed], mid - Vector3.up * 2.6f, t * 1.0f, sf * 1.2f, 2.0f);
                        }
                    prevTop = topPt;
                }
            }

            // AZORA welcome arches just outside both village gates, straddling the road (pillar inner
            // faces at +-6.6 m, beam underside 5.15 m: clear of the ~6 m riding corridor).
            foreach (float gd in new[] { dc - VillageHalfM - VillageArchBeyondM, dc + VillageHalfM + VillageArchBeyondM })
            {
                int i = route.IndexAt(gd);
                var s = route.SideFlat(i); var sf = new Vector3(s.x, 0f, s.z); var t = new Vector3(-s.z, 0f, s.x);
                DFoot(route, i, 0f, out var ac);
                var ap = ac - Vector3.up * 0.05f;
                var rot = Quaternion.LookRotation(t, Vector3.up);
                props.Add(arch, Matrix4x4.TRS(ap, rot, Vector3.one), VillageKitRole);
                var ax = rot * Vector3.right;
                for (int k = -1; k <= 1; k += 2)   // stone skirt under a pillar standing over lower ground
                {
                    var pc = ap + ax * (k * 7.05f);
                    float g = MinGround(pc, ax * 0.5f, t * 0.5f);
                    if (g < ap.y - 0.25f)
                        BoxB(stoneBin, new Vector3(pc.x, g - 0.3f, pc.z), ax * 0.44f, t * 0.44f, ap.y - 0.2f - (g - 0.3f));
                }
            }

            var vgroup = new GameObject($"WPC Village {vi}").transform; vgroup.SetParent(group, false);
            var chunksT = new GameObject("WPC Chunks (LOD)").transform; chunksT.SetParent(vgroup, false);
            int nLod = 0;
            for (int c = 0; c < nChunks; c++)
            {
                int k = FlushVillageChunk(chunksT, $"WPC_V{vi}_R{c / 2:D2}{(c % 2 == 0 ? "L" : "R")}",
                                          hi.b[c], lo.b[c], backRow.b[c], streetProps.b[c], vi * 100 + c);
                renderers += k; if (k > 0) nLod++;
            }
            renderers += FlushVillageChurch(vgroup, $"WPC_V{vi}_Church", churchHiB.b[0], churchLoB.b[0]);
            // village-wide merged street mesh / pines / lanterns / fountain / arches / bunting:
            // one far-cull LODGroup so the other valley stations never draw them
            var setT = new GameObject("WPC Village Set (far cull)").transform; setT.SetParent(vgroup, false);
            var smallT = new GameObject("WPC Bunting (no shadows)").transform; smallT.SetParent(setT, false);
            renderers += small.Flush(smallT, $"WPC_V{vi}_Bunting");
            NoShadows(smallT);
            var binT = new GameObject("WPC Street").transform; binT.SetParent(setT, false);
            renderers += bins.Flush(binT, $"Village{vi}");
            foreach (var r in binT.GetComponentsInChildren<MeshRenderer>(true))   // flat paving / kerbs / skirts
                if (r.sharedMaterial == _vkMat["Cobble"] || r.sharedMaterial == _vkMat["Stone"])
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderers += props.Flush(setT, $"VillageProps{vi}", vi);
            VillageFarCull(setT, VillageSetCullDistM);
            long vt = CountTris(vgroup);
            long lod0 = 0, lod1 = 0;
            foreach (var lg in vgroup.GetComponentsInChildren<LODGroup>(true))
            {
                var ls = lg.GetLODs();
                if (ls.Length < 2) continue;
                foreach (var r in ls[0].renderers) if (r != null) lod0 += CountTris(r.transform);
                foreach (var r in ls[1].renderers) if (r != null) lod1 += CountTris(r.transform);
            }
            trisTotal += vt;
            Debug.Log($"[azora] village {vi} @ {dc:0} m: {vt:N0} tris (all levels); chunk+church LOD0 {lod0:N0}, LOD1 {lod1:N0}; " +
                      $"{nLod} chunk LODGroups of {VillageChunkM:0} m per side (LOD0 <{VillageLod0DistM:0} m, props <{VillageLod1DistM:0} m, " +
                      $"cull >{VillageCullDistM:0} m; church cull >{VillageChurchCullDistM:0} m).");
        }

        Debug.Log($"[azora] swiss villages (WP-C winter kit {kitOk}/10 GLBs): {chalets} buildings, {cows} cows, {_people} villagers " +
                  $"(townsfolk gate {(WinterTownsfolkEnabled ? "WP-F" : "legacy")}; cast walk {cast.walk.Count}/stand {cast.stand.Count}/sit {cast.sit.Count}), " +
                  $"{renderers} renderers, {trisTotal:N0} tris total.");
    }
}

/// <summary>
/// azC2 village-only perf probe (copilot session 2). Same camera and LOD estimate as
/// AzoraPerfCapture (frustum + LODGroup screen-relative height, FOV 60, 4 m behind / 1.9 m up),
/// restricted to the 'Azora Swiss Villages' group and split by category (chunks, church,
/// village set, villagers). Also logs the LOD estimate WITH QualitySettings.lodBias, and the
/// shadow-casting tris the LOD selection leaves active at each station. Nothing is saved.
///   run_steps.ps1 "AzoraVillagePerfProbe.Run|copilot_azC2_probe.log|1"
/// </summary>
public static class AzoraVillagePerfProbe
{
    static readonly (string, float)[] Stations =
    {
        ("village1", 2560f), ("village1_centre", 2600f), ("village1_approach", 2200f),
        ("village2", 12560f), ("village2_church", 12690f), ("village2_approach", 12200f),
    };

    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[azc2-probe] no RegionDirector / route graph"); return; }
            regions.Resolve();
            var region = System.Linq.Enumerable.First(RegionCatalog.Regions, r => r.Id == RegionCatalog.AzoraHighlands);
            var course = graph.BuildCourse(region.BuiltCourseId);
            regions.currentRegionId = region.Id;
            regions.ApplyEnvironmentVisibility();
            var root = GameObject.Find(AzoraHighlandsEnvironment.RootName);
            var vg = root != null ? root.transform.Find("Azora Swiss Villages") : null;
            if (vg == null) { Debug.LogError("[azc2-probe] no 'Azora Swiss Villages'"); return; }

            Debug.Log($"[azc2-probe] QualitySettings.lodBias {QualitySettings.lodBias:0.00}, maximumLODLevel {QualitySettings.maximumLODLevel}, " +
                      $"quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', shadowDistance {QualitySettings.shadowDistance:0}");
            var tot = new Dictionary<string, (int r, long t, long st)>();
            foreach (var r in vg.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                string c = Cat(r.transform, vg); long t = Tris(r);
                bool sh = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                tot.TryGetValue(c, out var e); tot[c] = (e.r + 1, e.t + t, e.st + (sh ? t : 0));
            }
            foreach (var kv in tot)
                Debug.Log($"[azc2-probe] total '{kv.Key}': {kv.Value.r} renderers, {kv.Value.t:N0} tris, shadow-casting {kv.Value.st:N0}");
            foreach (var lg in vg.GetComponentsInChildren<LODGroup>(false))
            {
                if (Cat(lg.transform, vg) == "villagers") continue;
                var ls = lg.GetLODs(); var sb = new System.Text.StringBuilder();
                for (int i = 0; i < ls.Length; i++)
                {
                    long t = 0; int sh = 0;
                    foreach (var r in ls[i].renderers) if (r != null) { t += Tris(r); if (r.shadowCastingMode != 0) sh++; }
                    float dist = lg.size / (ls[i].screenRelativeTransitionHeight * 2f * Mathf.Tan(30f * Mathf.Deg2Rad));
                    sb.Append($" L{i}: {ls[i].renderers.Length} r ({sh} cast) {t:N0} tris h={ls[i].screenRelativeTransitionHeight:0.0000} (~{dist:0} m);");
                }
                Debug.Log($"[azc2-probe] lodgroup {lg.name} size {lg.size:0.0}{sb}");
            }

            foreach (var (name, d) in Stations)
            {
                if (d > course.Length) continue;
                var p = course.PositionAt(d);
                var tg = course.TangentAt(d); tg.y = 0f; tg.Normalize();
                Shot(vg, name, d, p - tg * 4.0f + Vector3.up * 1.9f, p + tg * 25f + Vector3.up * 0.8f, 1f);
                if (!Mathf.Approximately(QualitySettings.lodBias, 1f))
                    Shot(vg, name + "@lodBias", d, p - tg * 4.0f + Vector3.up * 1.9f, p + tg * 25f + Vector3.up * 0.8f, QualitySettings.lodBias);
            }
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static string Cat(Transform t, Transform vg)
    {
        for (var g = t; g != null && g != vg; g = g.parent)
        {
            if (g.name == "Azora Villagers") return "villagers";
            if (g.name.StartsWith("WPC Chunks")) return "chunks";
            if (g.name.EndsWith("_Church")) return "church";
            if (g.name.StartsWith("WPC Village Set")) return "set";
        }
        return "other";
    }

    static long Tris(Renderer r)
    {
        Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        if (m == null) return 0;
        long t = 0; for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3; return t;
    }

    static void Shot(Transform vg, string name, float d, Vector3 pos, Vector3 look, float bias)
    {
        var go = new GameObject("~AzC2ProbeCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look);
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 20000f;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        float k2 = 2f * Mathf.Tan(30f * Mathf.Deg2Rad);
        var inLod = new HashSet<Renderer>(); var lodOn = new HashSet<Renderer>();
        foreach (var g in vg.GetComponentsInChildren<LODGroup>(false))
        {
            var ls = g.GetLODs();
            foreach (var l in ls) foreach (var r in l.renderers) if (r != null) inLod.Add(r);
            var refp = g.transform.TransformPoint(g.localReferencePoint);
            float sc = Mathf.Max(g.transform.lossyScale.x, Mathf.Max(g.transform.lossyScale.y, g.transform.lossyScale.z));
            float rel = g.size * sc * bias / (Vector3.Distance(pos, refp) * k2 + 1e-4f);
            for (int i = 0; i < ls.Length; i++)
                if (rel >= ls[i].screenRelativeTransitionHeight) { foreach (var r in ls[i].renderers) if (r != null) lodOn.Add(r); break; }
        }
        var vis = new Dictionary<string, long>(); long all = 0, shadow = 0; int n = 0;
        foreach (var r in vg.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (inLod.Contains(r) && !lodOn.Contains(r)) continue;
            long t = Tris(r);
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off &&
                Vector3.Distance(r.bounds.ClosestPoint(pos), pos) < QualitySettings.shadowDistance) shadow += t;
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            string c = Cat(r.transform, vg);
            vis.TryGetValue(c, out var e); vis[c] = e + t; all += t; n++;
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in vis) sb.Append($" {kv.Key} {kv.Value:N0};");
        Debug.Log($"[azc2-probe] station {name} ({d:0} m): village visible {n} renderers, {all:N0} tris ({sb}); " +
                  $"LOD-active shadow casters within shadowDistance {shadow:N0} tris");
        Object.DestroyImmediate(go);
    }
}
