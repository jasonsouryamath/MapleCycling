using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Minato Coast redesign workstream: START PRECINCT (board row E7 "Minato polish", which also
/// finishes B2's "real facades within 150 m of the start").
///
/// The Minato ride starts at route metre 0 (9000, 6, 4200, heading +X; landward = -Z) on an
/// empty lawn. This pass builds a red-brick harbour precinct there:
///   * two Aka-renga style brick warehouses (tools/blender/build_minato_precinct2.py) parallel
///     to the road on the landward side, 28-60 m off the carriageway;
///   * a granite-sett plaza between them and the road, and a paved terminus behind the start
///     line with the MINATO PORT totem on the road axis;
///   * plaza life: street trees, promenade lamps, benches, planters, cafe tables;
///   * every legacy flat 'Minato_Port_Warehouse' box (kept by PruneLegacyHarbor as city
///     backdrop) is hidden and replaced by the modern corrugated Port Shed at its footprint.
///
/// Runs inside Apply() (hooked at the end of RedesignHarbor) and standalone through
/// <see cref="ApplyStartPrecinct"/>, which rebuilds only this group. Both are idempotent: the
/// group is matched by exact name, and the legacy boxes are deactivated, not destroyed, so a
/// re-run finds them again.
/// </summary>
public static partial class MinatoCoastEnvironment
{
    private const string PrecinctName = "Start Precinct";
    private static readonly Vector3 PrecinctStart = new Vector3(9000f, 6f, 4200f);

    /// <summary>Standalone entry: rebuild only the Start Precinct group and save the scene.</summary>
    public static void ApplyStartPrecinct()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = SceneRoots().FirstOrDefault(g => g.name == RootName);
        if (root == null) { Debug.LogError("[precinct] no Minato root; run Apply first"); return; }
        var chapter = root.transform.Find("Chapter 1 - Port City Departure");
        if (chapter == null) { Debug.LogError("[precinct] no Chapter 1"); return; }

        MaterialCache.Clear();
        var route = MinatoRoute.Load();
        BuildLandform(route);
        BuildStartPrecinct(route, chapter);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[precinct] saved '{scene.path}'");
    }

    // =================================================================== materials

    private static (string token, Material mat)[] PrecinctSlotMap(Color clad)
    {
        var brickTex = Tex(MinatoTex, "Minato_Precinct_Brick_Albedo.png");
        var slateTex = Tex(MinatoTex, "Minato_Precinct_Slate_Albedo.png");
        var signTex = Tex(MinatoTex, "Minato_Precinct_Sign_Albedo.png");
        var brick = Weathered(CelMaterial("Minato_Precinct_Brick", new Color(1f, 0.96f, 0.93f), 0.10f, 0.06f, 0.30f,
                                          brickTex), 0.30f, 0.30f, 0.06f);
        var stone = Weathered(CelMaterial("Minato_Precinct_Stone", new Color(0.88f, 0.84f, 0.76f), 0.12f, 0.08f, 0.32f),
                              0.30f, 0.30f, 0.08f);
        var iron = CelMaterial("Minato_Precinct_Iron", new Color(0.12f, 0.24f, 0.21f), 0.35f, 0.25f, 0.40f);
        var glass = CelMaterial("Minato_Precinct_Glass", new Color(0.20f, 0.30f, 0.38f), 0.85f, 0.70f, 0.55f);
        var warm = CelMaterial("Minato_Precinct_GlassWarm", new Color(1.0f, 0.80f, 0.52f), 0.70f, 0.50f, 0.70f);
        var frame = CelMaterial("Minato_Precinct_Frame", new Color(0.93f, 0.92f, 0.88f), 0.20f, 0.12f, 0.35f);
        var timber = CelMaterial("Minato_Precinct_Timber", new Color(0.36f, 0.22f, 0.13f), 0.25f, 0.12f, 0.30f);
        var slate = Weathered(CelMaterial("Minato_Precinct_Slate", Color.white, 0.20f, 0.12f, 0.30f, slateTex),
                              0.25f, 0.25f, 0.05f);
        var sign = CelMaterial("Minato_Precinct_Sign", Color.white, 0.30f, 0.20f, 0.35f, signTex);
        var copper = CelMaterial("Minato_Precinct_Copper", new Color(0.45f, 0.68f, 0.60f), 0.30f, 0.22f, 0.40f);
        string cladKey = $"Minato_Precinct_ShedClad_{ColorUtility.ToHtmlStringRGB(clad)}";
        var cladM = Weathered(CelMaterial(cladKey, clad, 0.28f, 0.20f, 0.40f,
                                          Tex(MinatoTex, "Minato_Precinct_Corrugated_Albedo.png")), 0.35f, 0.30f, 0.10f);
        var trim = CelMaterial("Minato_Precinct_ShedTrim", new Color(0.16f, 0.32f, 0.58f), 0.30f, 0.22f, 0.40f);
        var door = CelMaterial("Minato_Precinct_ShedDoor", new Color(0.80f, 0.82f, 0.84f), 0.25f, 0.18f, 0.35f,
                               Tex(MinatoTex, "Minato_Precinct_RollerDoor_Albedo.png"));
        var panel = CelMaterial("Minato_Precinct_ShedPanel", new Color(0.84f, 0.90f, 0.93f), 0.55f, 0.40f, 0.55f);
        var dark = CelMaterial("Minato_Precinct_Dark", new Color(0.13f, 0.14f, 0.15f), 0.20f, 0.10f, 0.25f);
        var hazard = CelMaterial("Minato_Harbor_Hazard", Color.white, 0.18f, 0.10f, 0.35f,
                                 Tex(MinatoTex, "Minato_Harbor_Hazard_Albedo.png"), GroundShade);
        var shedGlass = CelMaterial("Minato_Precinct_ShedGlass", new Color(0.22f, 0.32f, 0.40f), 0.80f, 0.65f, 0.50f);
        // Longest tokens first: RetintBySlot matches by Contains.
        return new (string, Material)[]
        {
            ("prec_glass_warm", warm), ("prec_glass", glass), ("prec_brick", brick), ("prec_stone", stone),
            ("prec_iron", iron), ("prec_frame", frame), ("prec_timber", timber), ("prec_slate", slate),
            ("prec_sign", sign), ("prec_copper", copper),
            ("shed_concrete", ConcreteMaterial()), ("shed_hazard", hazard), ("shed_glass", shedGlass),
            ("shed_panel", panel), ("shed_clad", cladM), ("shed_trim", trim), ("shed_door", door),
            ("shed_dark", dark), ("shed_sign", sign),
        };
    }

    // =================================================================== build

    private static void BuildStartPrecinct(MinatoRoute route, Transform chapter)
    {
        for (int i = chapter.childCount - 1; i >= 0; i--)
            if (chapter.GetChild(i).name == PrecinctName) Object.DestroyImmediate(chapter.GetChild(i).gameObject);
        var root = new GameObject(PrecinctName).transform;
        root.SetParent(chapter, false);
        Physics.SyncTransforms();

        var map = PrecinctSlotMap(new Color(0.93f, 0.94f, 0.95f));
        var fwd = Vector3.right;                      // route heading at metre 0 (MinatoRoute.json)
        var rot = Quaternion.LookRotation(fwd, Vector3.up);
        int placed = 0;

        GameObject Place(string model, Vector3 pos, Quaternion r, (string, Material)[] m, Vector3? scale = null)
        {
            var src = Model(model);
            if (src == null) return null;
            var go = Inst(src, root, Vector3.zero, Quaternion.identity, null);
            RetintBySlot(go, m, m[5].Item2);
            go.transform.SetPositionAndRotation(pos, r);
            go.transform.localScale = scale ?? Vector3.one;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                mr.shadowCastingMode = ShadowCastingMode.On;
            placed++;
            return go;
        }

        // ---- the two brick warehouses (long axis along the road, landward side)
        var whA = new Vector3(8990f, 0f, 4151f);
        var whB = new Vector3(9069f, 0f, 4127f);
        foreach (var (c, model, len, label) in new[]
                 { (whA, "Minato_Precinct_BrickWarehouseA", 76f, "No.1"), (whB, "Minato_Precinct_BrickWarehouseB", 57f, "No.2") })
        {
            float y = FootprintBase(route, c, rot, 10.5f, len * 0.5f, out float spread);
            var go = Place(model, new Vector3(c.x, y, c.z), rot, map);
            if (go != null) go.name = $"Precinct Brick Warehouse {label}";
            Debug.Log($"[precinct] brick warehouse {label} at ({c.x:0},{y:0.00},{c.z:0}); ground spread {spread:0.00} m");
        }

        // ---- paving: forecourt between the warehouses and the road, and the terminus behind the line
        var pavers = CelMaterial("Minato_Precinct_Pavers", Color.white, 0.10f, 0.06f, 0.25f,
                                 Tex(MinatoTex, "Minato_Precinct_Paver_Albedo.png"), GroundShade);
        var kerb = map[3].Item2;
        AddPlaza(route, root, "Precinct Forecourt", new Rect(8928f, 4104f, 172f, 82f), pavers, kerb);
        AddPlaza(route, root, "Precinct Terminus", new Rect(8918f, 4186f, 58f, 70f), pavers, kerb);

        // ---- the MINATO PORT totem on the road axis behind the start line
        {
            var q = new Vector3(8946f, 0f, 4200f);
            var go = Place("Minato_Precinct_Totem", new Vector3(q.x, PrecinctSurface(route, q) + 0.02f, q.z), rot, map);
            if (go != null) go.name = "Precinct Totem";
        }

        // ---- plaza life
        var rng = new System.Random(1911);
        var leaf = FoliageMaterial("Minato_Leaf", new Color(0.88f, 0.95f, 0.80f),
                                   Tex(ShiosaiTex, "Shiosai_Leaf_Albedo_HQ.png"), 0.24f);
        var bark = CelMaterial("Minato_Bark", Color.white, gloss: 0.10f, spec: 0.05f, rim: 0.40f,
                               texture: Tex(ShiosaiTex, "Shiosai_Bark_Albedo.png"));
        var broad = new[] { Glb(ShiosaiGlb, "Shiosai_Broadleaf"), Glb(ShiosaiGlb, "Shiosai_Broadleaf_B") };
        var bench = Model("Minato_Shore_Bench");
        var lamp = Model("Minato_Shore_PromenadeLamp");
        var planter = Model("Minato_Skyline_Planter");
        var cafe = new[] { Model("Minato_City_CafeSet"), Model("Minato_City_CafeSetB") };
        int trees = 0, lamps = 0, benches = 0, cafes = 0, planters = 0;

        // street-tree avenue + lamps along the forecourt (stops short of the boulevard median palms at 9057 m)
        for (float x = 8934f; x <= 9046f; x += 12f)
        {
            var t = new Vector3(x, 0f, 4180f);
            var src = broad[trees % 2];
            if (src != null)
            {
                var go = Inst(src, root, Vector3.zero, Quaternion.identity, leaf, bark);
                go.transform.SetPositionAndRotation(new Vector3(t.x, PrecinctSurface(route, t) - 0.05f, t.z),
                                                    Quaternion.Euler(0f, rng.Next(360), 0f));
                go.transform.localScale = Vector3.one * (0.85f + (float)rng.NextDouble() * 0.2f);
                go.isStatic = true;
                trees++;
            }
            var l = new Vector3(x + 6f, 0f, 4181.5f);
            if (ShorePlace(lamp, root, new Vector3(l.x, PrecinctSurface(route, l), l.z), Quaternion.LookRotation(Vector3.forward)) != null) lamps++;
            var b = new Vector3(x + 3f, 0f, 4177.2f);
            if (ShorePlace(bench, root, new Vector3(b.x, PrecinctSurface(route, b), b.z), Quaternion.LookRotation(Vector3.forward)) != null) benches++;
        }
        // cafe terrace in front of warehouse No.1's arcade, planters framing it
        for (float x = 8960f; x <= 9020f; x += 7.5f)
        {
            var q = new Vector3(x + (float)(rng.NextDouble() - 0.5) * 1.5f, 0f, 4167.5f + (float)rng.NextDouble() * 1.5f);
            var src = cafe[rng.Next(cafe.Length)];
            if (src == null) continue;
            var cs = Inst(src, root, Vector3.zero, Quaternion.Euler(0f, rng.Next(360), 0f), null);
            cs.transform.position = new Vector3(q.x, PrecinctSurface(route, q) - 0.02f, q.z);
            RetintMarket(cs);
            cs.isStatic = true;
            cafes++;
        }
        foreach (float x in new[] { 8955f, 9025f, 9044f, 9094f })
        {
            if (planter == null) break;
            var q = new Vector3(x, 0f, x < 9030f ? 4168.5f : 4145f);
            var pl = Inst(planter, root, Vector3.zero, Quaternion.identity, null);
            pl.transform.SetPositionAndRotation(new Vector3(q.x, PrecinctSurface(route, q) - 0.02f, q.z), rot);
            SkyRetint(pl);
            pl.isStatic = true;
            planters++;
        }
        // terminus: trees and lamps flanking the totem
        foreach (var (dx, dz) in new[] { (-8f, -22f), (-8f, 22f), (14f, -30f), (14f, 30f), (-24f, -10f), (-24f, 10f) })
        {
            var t = new Vector3(8946f + dx, 0f, 4200f + dz);
            var src = broad[(trees++) % 2];
            if (src == null) continue;
            var go = Inst(src, root, Vector3.zero, Quaternion.identity, leaf, bark);
            go.transform.SetPositionAndRotation(new Vector3(t.x, PrecinctSurface(route, t) - 0.05f, t.z),
                                                Quaternion.Euler(0f, rng.Next(360), 0f));
            go.transform.localScale = Vector3.one * 0.95f;
            go.isStatic = true;
        }
        foreach (float dz in new[] { -9f, 9f })
        {
            var l = new Vector3(8950f, 0f, 4200f + dz);
            if (ShorePlace(lamp, root, new Vector3(l.x, PrecinctSurface(route, l), l.z), rot) != null) lamps++;
            var b = new Vector3(8940f, 0f, 4200f + dz * 1.4f);
            if (ShorePlace(bench, root, new Vector3(b.x, PrecinctSurface(route, b), b.z), rot) != null) benches++;
        }

        int sheds = ReplaceLegacyWarehouses(route, chapter, root);
        Debug.Log($"[precinct] built: {placed} buildings/landmarks, {trees} trees, {lamps} lamps, {benches} benches, " +
                  $"{cafes} cafe sets, {planters} planters, {sheds} legacy warehouses -> Port Shed; " +
                  $"{root.GetComponentsInChildren<Renderer>(true).Length} renderers");
    }

    /// <summary>Plaza / apron top at a point: the analytic ground, or an apron collider above it.</summary>
    private static float PrecinctSurface(MinatoRoute route, Vector3 q)
    {
        float g = GroundAt(route, q.x, q.z);
        var hits = Physics.RaycastAll(new Vector3(q.x, g + 30f, q.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        float best = g;
        foreach (var h in hits)
            if (h.collider.name.Contains("Apron") && h.point.y > best && h.point.y < g + 3f) best = h.point.y;
        return best;
    }

    /// <summary>Base height for a building footprint: the LOWEST ground under it, so the 1.5 m
    /// below-grade plinth buries the high side and nothing floats.</summary>
    private static float FootprintBase(MinatoRoute route, Vector3 c, Quaternion rot, float hx, float hz, out float spread)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = -2; i <= 2; i++)
            for (int j = -4; j <= 4; j++)
            {
                var q = c + rot * new Vector3(hx * i / 2f, 0f, hz * j / 4f);
                float y = PrecinctSurface(route, q);
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
        spread = hi - lo;
        if (spread > 1.3f) Debug.LogWarning($"[precinct] footprint at ({c.x:0},{c.z:0}) spans {spread:0.00} m of ground");
        return lo - 0.05f;
    }

    /// <summary>Conforming granite-sett plaza (2 m grid, +4 cm) with a stone kerb skirt.</summary>
    private static void AddPlaza(MinatoRoute route, Transform parent, string name, Rect r, Material pavers, Material kerbMat)
    {
        const float cell = 2f, lift = 0.04f, uvk = 1f / 2.4f;
        int nx = Mathf.CeilToInt(r.width / cell), nz = Mathf.CeilToInt(r.height / cell);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var kt = new List<int>();
        float Y(float x, float z) => PrecinctSurface(route, new Vector3(x, 0f, z)) + lift;
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = r.xMin + Mathf.Min(r.width, i * cell), z = r.yMin + Mathf.Min(r.height, j * cell);
                v.Add(new Vector3(x, Y(x, z), z)); uv.Add(new Vector2(x * uvk, z * uvk));
            }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tri.AddRange(new[] { a, c, b, b, c, d });
            }
        // perimeter skirt: a 30 cm stone kerb face going down into the ground
        void Skirt(int a, int b, Vector3 outward)
        {
            int s = v.Count;
            var pa = v[a]; var pb = v[b];
            v.Add(pa); v.Add(pb); v.Add(pa + Vector3.down * 0.3f); v.Add(pb + Vector3.down * 0.3f);
            uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
            var n = Vector3.Cross(pb - pa, Vector3.down);
            if (Vector3.Dot(n, outward) >= 0f) kt.AddRange(new[] { s, s + 1, s + 2, s + 1, s + 3, s + 2 });
            else kt.AddRange(new[] { s, s + 2, s + 1, s + 1, s + 2, s + 3 });
        }
        for (int i = 0; i < nx; i++)
        {
            Skirt(i, i + 1, Vector3.back);
            Skirt(nz * (nx + 1) + i, nz * (nx + 1) + i + 1, Vector3.forward);
        }
        for (int j = 0; j < nz; j++)
        {
            Skirt(j * (nx + 1), (j + 1) * (nx + 1), Vector3.left);
            Skirt(j * (nx + 1) + nx, (j + 1) * (nx + 1) + nx, Vector3.right);
        }
        var mesh = new Mesh { name = "Minato_" + name.Replace(" ", "_") };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(v); mesh.SetUVs(0, uv);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(tri, 0); mesh.SetTriangles(kt, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        // Unity winding: (a, c, b) on a +X/+Z grid faces up; verify once and flip if not.
        if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
        {
            tri.Clear();
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    tri.AddRange(new[] { a, b, c, b, d, c });
                }
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
        }
        var go = AddMesh(parent, name, mesh, pavers, false);
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { pavers, kerbMat };
        go.isStatic = true;
    }

    /// <summary>
    /// Hide each legacy flat 'Minato_Port_Warehouse' box and stand a Port Shed on its footprint
    /// (rotated so the long axes match, scaled to its plan, cladding tint varied per instance).
    /// </summary>
    private static int ReplaceLegacyWarehouses(MinatoRoute route, Transform chapter, Transform root)
    {
        var port = chapter.Find("Port Infrastructure");
        if (port == null) return 0;
        var tints = new[] { new Color(0.93f, 0.94f, 0.95f), new Color(0.70f, 0.80f, 0.88f), new Color(0.95f, 0.62f, 0.48f),
                            new Color(0.80f, 0.84f, 0.80f) };
        var parent = new GameObject("Port Sheds").transform;
        parent.SetParent(root, false);
        int n = 0;
        foreach (Transform c in port)
        {
            if (!c.name.StartsWith("Minato_Port_Warehouse")) continue;
            var mf = c.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.sharedMesh != null && !m.name.Contains("_LOD1") && !m.name.Contains("_LOD2"));
            if (mf == null) continue;
            var lb = mf.sharedMesh.bounds;
            var s = Vector3.Scale(lb.size, mf.transform.lossyScale);
            var centre = mf.transform.TransformPoint(lb.center);
            c.gameObject.SetActive(false);
            bool longX = s.x > s.z;
            var rot = c.rotation * (longX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
            float across = longX ? s.z : s.x, along = longX ? s.x : s.z;
            var scale = new Vector3(Mathf.Clamp(across / 26f, 0.6f, 1.8f), Mathf.Clamp(s.y / 12.5f, 0.8f, 1.5f),
                                    Mathf.Clamp(along / 48f, 0.6f, 1.8f));
            var src = Model("Minato_Precinct_PortShed");
            if (src == null) break;
            var go = Inst(src, parent, Vector3.zero, Quaternion.identity, null);
            var map = PrecinctSlotMap(tints[n % tints.Length]);
            RetintBySlot(go, map, map[14].Item2);
            float y = FootprintBase(route, new Vector3(centre.x, 0f, centre.z), rot, 13f * scale.x, 24f * scale.z, out _);
            go.transform.SetPositionAndRotation(new Vector3(centre.x, y, centre.z), rot);
            go.transform.localScale = scale;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            go.name = "Precinct Port Shed " + n;
            n++;
        }
        return n;
    }

    /// <summary>Lists every renderer near the Minato start, grouped by hierarchy, for planning.</summary>
    public static void ProbeStartArea()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = SceneRoots().FirstOrDefault(g => g.name == RootName);
        var sb = new StringBuilder();
        if (root == null) { Debug.LogError("[e7] no Minato root"); return; }
        var start = PrecinctStart;
        var groups = new Dictionary<string, (int n, float near, float top, Bounds b)>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds;
            var c = b.center;
            if (c.x < start.x - 300f || c.x > start.x + 260f || Mathf.Abs(c.z - start.z) > 300f) continue;
            if (b.size.x > 900f || b.size.z > 900f) continue;
            var t = r.transform;
            var chain = new List<string>();
            while (t != null && t != root.transform) { chain.Insert(0, t.name); t = t.parent; }
            string key = string.Join("/", chain.Take(Mathf.Min(chain.Count, 4)));
            float near = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(start.x, start.z));
            if (groups.TryGetValue(key, out var g))
            { g.b.Encapsulate(b); groups[key] = (g.n + 1, Mathf.Min(g.near, near), Mathf.Max(g.top, b.max.y), g.b); }
            else groups[key] = (1, near, b.max.y, b);
        }
        foreach (var kv in groups.OrderBy(k => k.Value.near))
            sb.AppendLine($"{kv.Value.near,6:0} m  n={kv.Value.n,4}  top={kv.Value.top,6:0.0}  " +
                          $"x {kv.Value.b.min.x:0}..{kv.Value.b.max.x:0} z {kv.Value.b.min.z:0}..{kv.Value.b.max.z:0}  {kv.Key}");
        string path = Path.Combine(Application.dataPath, "..", "reference", "copilot", "e7");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "start_probe.txt"), sb.ToString());
        Debug.Log($"[e7] start probe: {groups.Count} groups written");
    }
}
