// Nagisa Bay overhaul NB1 - photoreal ocean + crashing surf (Claude worker A).
//
// A [NagisaStage] that:
//   1. bakes the depth / coast-distance field texture from the ground (NagisaSurf_Field.asset),
//   2. swaps the material on the existing "Ocean Surface" renderer (found by exact name; BuildSea untouched) to
//      MapleRide/HDRP/NagisaOcean and replaces its coarse 250 m plate with a far mesh that leaves a hole where
//   3. dense near-shore sea tiles (3 m cells within 130 m of the beach, 6 m out to 450 m, LOD-boundary stitched) take over,
//   4. lays a transparent swash / wet-sand overlay on the beach strip (never inside the ride-road corridor),
//   5. hides the old "Shallows_*" overlay tiles (the new shader does its own depth colour) and
//   6. adds the runtime NagisaSurf driver (shared wave clock + the API other systems call).
// Run:  $env:MR_NB_STAGES="Surf"; run_steps.ps1 "NagisaBayEnvironment.ApplyOverhaul|claude_nb1_stage.log|1"
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const string SurfShaderName = "MapleRide/HDRP/NagisaOcean";
    private const string SwashShaderName = "MapleRide/HDRP/NagisaOceanSwash";
    private const string SurfTexDir = Dir + "/Textures/Surf";
    private const float SurfDenseReachM = 400f, SurfFineReachM = 100f, SwashLiftM = 0.035f;
    private const int SurfTileCells = 64;

    [NagisaStage(24, "Surf")]
    private static void BuildSurfStage(Transform group)
    {
        var g = _ground;
        var root = group.root;

        var fieldTex = BakeSurfField(g);
        var ocean = SurfOceanMaterial(fieldTex, SurfShaderName, "Nagisa_OceanSurf");
        var swash = SurfOceanMaterial(fieldTex, SwashShaderName, "Nagisa_OceanSwash");

        // ---- 2. swap material + far mesh on the existing "Ocean Surface" renderer (exact name)
        MeshRenderer plate = null;
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            if (mr.gameObject.name == "Ocean Surface") { plate = mr; break; }
        if (plate == null) Debug.LogWarning("[nagisa-surf] no 'Ocean Surface' renderer found; dense tiles only");

        var state = SurfCellStates(g);
        if (plate != null)
        {
            var far = BuildSurfFarMesh(g);
            string fp = $"{MeshDir}/{far.name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(fp) != null) AssetDatabase.DeleteAsset(fp);
            AssetDatabase.CreateAsset(far, fp);
            plate.GetComponent<MeshFilter>().sharedMesh = far;
            plate.sharedMaterial = ocean;
            plate.shadowCastingMode = ShadowCastingMode.Off;
            plate.receiveShadows = false;
        }

        // ---- 5. old shallows overlay off (depth colour now comes from the ocean shader)
        int hidden = 0;
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            if (mr.gameObject.name.StartsWith("Shallows_", StringComparison.Ordinal)) { mr.enabled = false; hidden++; }

        // ---- 3. dense tiles   4. swash tiles
        bool[] nearRoad = SurfCorridorMask(g);
        int dense = 0, swashTiles = 0; long denseTris = 0, swashTris = 0;
        for (int cz = 0; cz < g.Nz - 1; cz += SurfTileCells)
            for (int cx = 0; cx < g.Nx - 1; cx += SurfTileCells)
            {
                int ex = Mathf.Min(cx + SurfTileCells, g.Nx - 1), ez = Mathf.Min(cz + SurfTileCells, g.Nz - 1);
                var dm = BuildSurfDenseTile(g, state, cx, cz, ex, ez, out int dt);
                if (dm != null)
                {
                    var go = AddMesh(group, $"Surf Dense {cx}_{cz}", dm, ocean, collider: false);
                    var r = go.GetComponent<MeshRenderer>();
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    dense++; denseTris += dt;
                }
                var sm = BuildSwashTile(g, nearRoad, cx, cz, ex, ez, out int st);
                if (sm != null)
                {
                    var go = AddMesh(group, $"Surf Swash {cx}_{cz}", sm, swash, collider: false);
                    var r = go.GetComponent<MeshRenderer>();
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    swashTiles++; swashTris += st;
                }
            }

        // ---- 6. runtime driver
        var driver = new GameObject("NagisaSurf Driver");
        driver.transform.SetParent(group, false);
        var comp = driver.AddComponent<NagisaSurf>();
        comp.groundBytes = AssetDatabase.LoadAssetAtPath<TextAsset>(GroundPath);
        comp.surfHeight = 1.3f; comp.chopAmp = 1f;
        EditorUtility.SetDirty(comp);

        // ---- 7. spray: wind-blown crest mist + big bursts on rocks / breakwater / pier piles standing in the surf zone
        var burst = new List<Vector3>();
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = mr.gameObject.name;
            string pn = mr.transform.parent != null ? mr.transform.parent.name : "";
            bool cand = n.StartsWith("Rock", StringComparison.OrdinalIgnoreCase) || pn.StartsWith("Rock", StringComparison.OrdinalIgnoreCase)
                || n.IndexOf("Breakwater", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Jetty", StringComparison.OrdinalIgnoreCase) >= 0
                || n.StartsWith("PierPile", StringComparison.OrdinalIgnoreCase);
            if (!cand) continue;
            var c = mr.bounds.center;
            float off = Mathf.Max(-g.Coast(c.x, c.z), 0f);
            if (off < 1f || off > 70f) continue;
            bool near = false;
            foreach (var q in burst) if ((q.x - c.x) * (q.x - c.x) + (q.z - c.z) * (q.z - c.z) < 36f) { near = true; break; }
            if (!near && burst.Count < 220) burst.Add(new Vector3(c.x, SeaLevelY, c.z));
        }
        var spray = driver.AddComponent<NagisaSurfSpray>();
        spray.burstPoints = burst.ToArray();
        spray.sprayMaterial = LoadOrCreate("Nagisa_SurfSpray", "MapleRide/HDRP/Snowflake");
        EditorUtility.SetDirty(spray);
        Debug.Log($"[nagisa-surf] spray burst points: {burst.Count}");

        Debug.Log($"[nagisa-surf] stage: {dense} dense tiles ({denseTris:N0} tris), {swashTiles} swash tiles ({swashTris:N0} tris), {hidden} shallows hidden, plate swapped={(plate != null)}");
    }

    // ------------------------------------------------------------------------------------------ field texture
    private static Texture2D BakeSurfField(NagisaGround g)
    {
        Directory.CreateDirectory(SurfTexDir);
        string path = SurfTexDir + "/NagisaSurf_Field.asset";
        var tex = new Texture2D(g.Nx, g.Nz, TextureFormat.RGBAHalf, false, true)
        {
            name = "NagisaSurf_Field", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
        };
        var px = new Color[g.Nx * g.Nz];
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(g.H[i], Mathf.Clamp(g.Sd[i], -3000f, 3000f), 0f, 0f);
        tex.SetPixels(px);
        tex.Apply(false, false);
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(tex, path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Texture2D SurfDetailTex(string file, bool srgb)
    {
        string path = $"{SurfTexDir}/{file}";
        if (!File.Exists(path)) { Debug.LogWarning($"[nagisa-surf] texture missing {path}: run tools/blender/build_nagisa_surf_textures.py"); return null; }
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { AssetDatabase.ImportAsset(path); ti = AssetImporter.GetAtPath(path) as TextureImporter; }
        if (ti != null && (ti.sRGBTexture != srgb || ti.wrapMode != TextureWrapMode.Repeat || ti.anisoLevel < 8 || !ti.mipmapEnabled))
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = srgb; ti.wrapMode = TextureWrapMode.Repeat; ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material SurfOceanMaterial(Texture2D field, string shaderName, string matName)
    {
        var m = LoadOrCreate(matName, shaderName);
        if (m.shader == null || m.shader.name != shaderName) Debug.LogError($"[nagisa-surf] shader {shaderName} did not load");
        m.SetTexture("_NagisaField", field);
        m.SetVector("_FieldRect", new Vector4(_ground.X0, _ground.Z0, _ground.Cell, 0f));
        m.SetVector("_FieldDim", new Vector4(_ground.Nx, _ground.Nz, 0f, 0f));
        m.SetTexture("_NormA", SurfDetailTex("NagisaSurf_NormalA.png", false));
        m.SetTexture("_NormB", SurfDetailTex("NagisaSurf_NormalB.png", false));
        m.SetTexture("_FoamTex", SurfDetailTex("NagisaSurf_Foam.png", false));

        m.SetColor("_ShallowTint", Srgb(0x14, 0x9E, 0xA8));
        m.SetColor("_DeepColor", Srgb(0x05, 0x1F, 0x4C));
        m.SetColor("_SandColor", Srgb(0xDC, 0xC8, 0x9C));
        m.SetColor("_ReefColor", Srgb(0x1C, 0x2C, 0x2A));
        m.SetColor("_SkyZenith", Srgb(0x66, 0x9C, 0xE0));
        m.SetColor("_SkyHorizon", Srgb(0xCC, 0xE0, 0xF0));
        m.SetColor("_SunColor", Srgb(0xFF, 0xF4, 0xD8));
        m.SetColor("_FoamColor", Srgb(0xF8, 0xFC, 0xFC));
        m.SetColor("_SssColor", Srgb(0x40, 0xDC, 0x9E));
        m.SetColor("_WetSandColor", Srgb(0x5C, 0x4A, 0x38));
        m.SetFloat("_SurfHeight", 1.3f);
        m.SetFloat("_SwashHeight", 0.34f);
        m.SetFloat("_SkyGain", 0.9f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------------------------------ meshes
    /// <summary>0 = no ocean, 1 = coarse (6 m) cell, 2 = fine (3 m) cell.</summary>
    private static byte[] SurfCellStates(NagisaGround g)
    {
        var st = new byte[g.Nx * g.Nz];
        for (int iz = 0; iz < g.Nz - 1; iz++)
            for (int ix = 0; ix < g.Nx - 1; ix++)
            {
                int i = iz * g.Nx + ix;
                float a = g.Sd[i], b = g.Sd[i + 1], c = g.Sd[i + g.Nx], d = g.Sd[i + g.Nx + 1];
                float mx = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)), mn = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
                if (mx > -SurfDenseReachM && mn < 1.5f) st[i] = (byte)(mx > -SurfFineReachM ? 2 : 1);
            }
        return st;
    }

    private static byte CellState(byte[] st, NagisaGround g, int ix, int iz) =>
        (ix < 0 || iz < 0 || ix >= g.Nx - 1 || iz >= g.Nz - 1) ? (byte)0 : st[iz * g.Nx + ix];

    private static Mesh BuildSurfDenseTile(NagisaGround g, byte[] st, int cx, int cz, int ex, int ez, out int tris)
    {
        tris = 0; int nTris = 0;
        var verts = new List<Vector3>(); var cols = new List<Color>(); var stitch = new List<Vector2>();
        var idx = new List<int>(); var map = new Dictionary<int, int>();
        int stride = 2 * g.Nx;
        float half = g.Cell * 0.5f;

        int V(int hx, int hz, Vector2 e)
        {
            int key = hz * stride + hx;
            if (map.TryGetValue(key, out int v)) return v;
            float x = g.X0 + hx * half, z = g.Z0 + hz * half;
            float off = Mathf.Max(-g.Coast(x, z), 0f);
            verts.Add(new Vector3(x, SeaLevelY, z));
            cols.Add(new Color(NagisaSurf.EnvelopeAt(off), 0f, 0f, 1f));
            stitch.Add(e);
            map[key] = verts.Count - 1;
            return verts.Count - 1;
        }
        void Quad(int a, int b, int c, int d)
        {   // a=(0,0) b=(+x) c=(+z) d=(+x,+z); same split as the ground
            idx.Add(a); idx.Add(c); idx.Add(d);
            idx.Add(a); idx.Add(d); idx.Add(b);
            nTris += 2;
        }

        for (int iz = cz; iz < ez; iz++)
            for (int ix = cx; ix < ex; ix++)
            {
                byte s = st[iz * g.Nx + ix];
                if (s == 0) continue;
                int hx = ix * 2, hz = iz * 2;
                if (s == 1)
                {
                    Quad(V(hx, hz, Vector2.zero), V(hx + 2, hz, Vector2.zero), V(hx, hz + 2, Vector2.zero), V(hx + 2, hz + 2, Vector2.zero));
                    continue;
                }
                // fine: mid-edge vertices that meet a COARSE neighbour are stitched (the shader averages the two
                // coarse corners' displacement so the boundary has no crack).
                var ex2 = new Vector2(half, 0f); var ez2 = new Vector2(0f, half);
                Vector2 sB = CellState(st, g, ix, iz - 1) == 1 ? ex2 : Vector2.zero;
                Vector2 sT = CellState(st, g, ix, iz + 1) == 1 ? ex2 : Vector2.zero;
                Vector2 sL = CellState(st, g, ix - 1, iz) == 1 ? ez2 : Vector2.zero;
                Vector2 sR = CellState(st, g, ix + 1, iz) == 1 ? ez2 : Vector2.zero;
                int v00 = V(hx, hz, Vector2.zero), v10 = V(hx + 1, hz, sB), v20 = V(hx + 2, hz, Vector2.zero);
                int v01 = V(hx, hz + 1, sL), v11 = V(hx + 1, hz + 1, Vector2.zero), v21 = V(hx + 2, hz + 1, sR);
                int v02 = V(hx, hz + 2, Vector2.zero), v12 = V(hx + 1, hz + 2, sT), v22 = V(hx + 2, hz + 2, Vector2.zero);
                Quad(v00, v10, v01, v11); Quad(v10, v20, v11, v21);
                Quad(v01, v11, v02, v12); Quad(v11, v21, v12, v22);
            }
        tris = nTris;
        if (tris == 0) return null;

        var mesh = new Mesh { name = $"Nagisa_SurfDense_{cx}_{cz}", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetUVs(1, stitch);
        mesh.SetTriangles(idx, 0, false);
        mesh.RecalculateBounds();
        var b = mesh.bounds;
        mesh.bounds = new Bounds(b.center, b.size + new Vector3(10f, 6f, 10f));
        return mesh;
    }

    private static Mesh BuildSurfFarMesh(NagisaGround g)
    {
        const int B = 4;                                   // 24 m blocks
        var verts = new List<Vector3>(); var cols = new List<Color>(); var idx = new List<int>();
        var map = new Dictionary<long, int>();
        float y = SeaLevelY - 0.03f;
        int V(int ix, int iz)
        {
            long key = (long)iz * 100000 + ix;
            if (map.TryGetValue(key, out int v)) return v;
            verts.Add(new Vector3(g.X0 + ix * g.Cell, y, g.Z0 + iz * g.Cell));
            cols.Add(new Color(0f, 0f, 0f, 1f));
            map[key] = verts.Count - 1;
            return verts.Count - 1;
        }
        void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int ia = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++) cols.Add(new Color(0f, 0f, 0f, 1f));
            idx.Add(ia); idx.Add(ia + 2); idx.Add(ia + 3); idx.Add(ia); idx.Add(ia + 3); idx.Add(ia + 1);
        }

        for (int bz = 0; bz < g.Nz - 1; bz += B)
            for (int bx = 0; bx < g.Nx - 1; bx += B)
            {
                int ex = Mathf.Min(bx + B, g.Nx - 1), ez = Mathf.Min(bz + B, g.Nz - 1);
                float mx = float.MinValue;
                for (int iz = bz; iz <= ez; iz++)
                    for (int ix = bx; ix <= ex; ix++)
                        mx = Mathf.Max(mx, g.Sd[iz * g.Nx + ix]);
                if (mx >= -(SurfDenseReachM - 50f)) continue;
                int a = V(bx, bz), b = V(ex, bz), c = V(bx, ez), d = V(ex, ez);
                idx.Add(a); idx.Add(c); idx.Add(d); idx.Add(a); idx.Add(d); idx.Add(b);
            }

        // outer ring to the old plate's 20 km half-extent so the plate edge never shows
        var cen = _route.Plan.center; const float H = 20000f;
        float x0 = g.X0, x1 = g.X0 + (g.Nx - 1) * g.Cell, z0 = g.Z0, z1 = g.Z0 + (g.Nz - 1) * g.Cell;
        float bx0 = Mathf.Min(cen.x - H, x0 - 1000f), bx1 = Mathf.Max(cen.x + H, x1 + 1000f);
        float bz0 = Mathf.Min(cen.z - H, z0 - 1000f), bz1 = Mathf.Max(cen.z + H, z1 + 1000f);
        Q(new Vector3(bx0, y, bz0), new Vector3(x0, y, bz0), new Vector3(bx0, y, bz1), new Vector3(x0, y, bz1));
        Q(new Vector3(x1, y, bz0), new Vector3(bx1, y, bz0), new Vector3(x1, y, bz1), new Vector3(bx1, y, bz1));
        Q(new Vector3(x0, y, bz0), new Vector3(x1, y, bz0), new Vector3(x0, y, z0), new Vector3(x1, y, z0));
        Q(new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x0, y, bz1), new Vector3(x1, y, bz1));

        var mesh = new Mesh { name = "Nagisa_OceanFar", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetColors(cols);
        mesh.SetUVs(1, new List<Vector2>(new Vector2[verts.Count]));
        mesh.SetTriangles(idx, 0, false);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Ground nodes inside the ride-road corridor (+ margin): the swash overlay stays off them.</summary>
    private static bool[] SurfCorridorMask(NagisaGround g)
    {
        var mask = new bool[g.Nx * g.Nz];
        float reach = CorridorKeepOutM + 4f;
        int rc = Mathf.CeilToInt(reach / g.Cell) + 1;
        for (int i = 0; i < _route.Count; i++)
        {
            var p = _route.Position[i];
            int cx = Mathf.RoundToInt((p.x - g.X0) / g.Cell), cz = Mathf.RoundToInt((p.z - g.Z0) / g.Cell);
            for (int dz = -rc; dz <= rc; dz++)
                for (int dx = -rc; dx <= rc; dx++)
                {
                    int ix = cx + dx, iz = cz + dz;
                    if (ix < 0 || iz < 0 || ix >= g.Nx || iz >= g.Nz) continue;
                    float wx = g.X0 + ix * g.Cell - p.x, wz = g.Z0 + iz * g.Cell - p.z;
                    if (wx * wx + wz * wz <= reach * reach) mask[iz * g.Nx + ix] = true;
                }
        }
        return mask;
    }

    private static Mesh BuildSwashTile(NagisaGround g, bool[] nearRoad, int cx, int cz, int ex, int ez, out int tris)
    {
        tris = 0;
        var verts = new List<Vector3>(); var idx = new List<int>(); var map = new Dictionary<int, int>();
        int V(int ix, int iz)
        {
            int key = iz * g.Nx + ix;
            if (map.TryGetValue(key, out int v)) return v;
            verts.Add(new Vector3(g.X0 + ix * g.Cell, g.H[key] + SwashLiftM, g.Z0 + iz * g.Cell));
            map[key] = verts.Count - 1;
            return verts.Count - 1;
        }
        for (int iz = cz; iz < ez; iz++)
            for (int ix = cx; ix < ex; ix++)
            {
                int i = iz * g.Nx + ix;
                int i1 = i + 1, i2 = i + g.Nx, i3 = i + g.Nx + 1;
                if (nearRoad[i] || nearRoad[i1] || nearRoad[i2] || nearRoad[i3]) continue;
                float h0 = g.H[i], h1 = g.H[i1], h2 = g.H[i2], h3 = g.H[i3];
                float hmin = Mathf.Min(Mathf.Min(h0, h1), Mathf.Min(h2, h3)), hmax = Mathf.Max(Mathf.Max(h0, h1), Mathf.Max(h2, h3));
                if (hmax < -0.12f || hmin > 0.9f) continue;
                float s0 = g.Sd[i], s1 = g.Sd[i1], s2 = g.Sd[i2], s3 = g.Sd[i3];
                float smin = Mathf.Min(Mathf.Min(s0, s1), Mathf.Min(s2, s3)), smax = Mathf.Max(Mathf.Max(s0, s1), Mathf.Max(s2, s3));
                if (smax < -14f || smin > 70f) continue;
                int a = V(ix, iz), b = V(ix + 1, iz), c = V(ix, iz + 1), d = V(ix + 1, iz + 1);
                idx.Add(a); idx.Add(c); idx.Add(d); idx.Add(a); idx.Add(d); idx.Add(b);
                tris += 2;
            }
        if (tris == 0) return null;
        var mesh = new Mesh { name = $"Nagisa_SurfSwash_{cx}_{cz}", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(idx, 0, false);
        mesh.RecalculateBounds();
        return mesh;
    }
}

/// <summary>
/// NB1 diagnostics: beach-level frames of the surf at chosen wave-clock times (deterministic: the shader time is forced
/// through the _NagisaSurfTime global, so frames are exactly Dt seconds apart).
///   run_steps.ps1 "NagisaSurfDiagnostics.Capture|claude_nb1_cap.log|1"
/// Frames: reference/good_graphics/nagisa_bay/overhaul/nb1_*.png
/// </summary>
public static class NagisaSurfDiagnostics
{
    private const string OutSub = "overhaul/";

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa Surf")]
    public static void Capture()
    {
        const string scenePath = "Assets/Scenes/SakuraPass.unity";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != scenePath) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = NagisaBayEnvironment.RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        if (GameObject.Find(NagisaBayEnvironment.RootName) == null)
        {
            Debug.LogError("[nagisa-surf] Nagisa Bay root not in scene - run NagisaBayEnvironment.Apply first.");
            return;
        }

        Directory.CreateDirectory(Path.Combine(NagisaBayDiagnostics.OutDir, OutSub));
        var r = NagisaBayEnvironment.NagisaRoute.Load();
        var g = NagisaBayEnvironment.NagisaGround.Load();
        NagisaSurf.LoadFrom(File.ReadAllBytes(NagisaBayEnvironment.Dir + "/NagisaGround.bytes"));
        int shots = 0;
        try
        {
            NagisaSurf.NextSet(20f, out float start, out int count, out float spacing);
            Debug.Log($"[nagisa-surf] next set at t={start:F1}s: {count} breakers {spacing:F1}s apart");

            // beach vantage points: seaward ray from the road, find the shoreline, stand 14 m inland of it.
            var spots = new List<(string name, float d)>
            {
                ("a", r.BeachStartM + 380f), ("b", (r.BeachStartM + r.BeachEndM) * 0.5f), ("c", r.HotelM - 520f),
            };
            foreach (var (name, d) in spots)
            {
                int i = r.IndexAt(d);
                Vector3 pos = r.Position[i], side = r.SideFlat(i);
                Vector2 seaDir2 = Vector2.zero;
                foreach (var sgn in new[] { 1f, -1f })
                {
                    var q = pos + side * sgn * 90f;
                    if (g.Coast(q.x, q.z) < g.Coast(pos.x, pos.z)) { seaDir2 = new Vector2(side.x * sgn, side.z * sgn).normalized; break; }
                }
                if (seaDir2 == Vector2.zero) { Debug.LogWarning($"[nagisa-surf] spot {name}: no seaward side found"); continue; }
                Vector2 shore = new Vector2(pos.x, pos.z);
                float m = 0f;
                for (; m < 600f; m += 2f)
                {
                    var q = shore + seaDir2 * m;
                    if (g.Coast(q.x, q.y) < 0f) break;
                }
                Vector2 sh = shore + seaDir2 * m;
                Vector2 along = new Vector2(-seaDir2.y, seaDir2.x);
                Vector2 eye2 = sh - seaDir2 * 14f;
                var eye = new Vector3(eye2.x, g.Height(eye2.x, eye2.y) + 1.7f, eye2.y);
                Vector2 tg2 = sh + seaDir2 * 40f + along * 45f;
                var tgt = new Vector3(tg2.x, 0.4f, tg2.y);
                Debug.Log($"[nagisa-surf] spot {name}: shoreline {sh} offshore ray m={m:F0} eye {eye}");

                float tBreak = start - 17f;      // first breaker ~35 m offshore, about to break
                float[] times = name == "a" ? new[] { tBreak, tBreak + 3f, tBreak + 6f, tBreak + 12f, tBreak + 17f } : new[] { tBreak + 6f };
                foreach (float t in times)
                {
                    Shader.SetGlobalFloat("_NagisaSurfTimeOn", 1f);
                    Shader.SetGlobalFloat("_NagisaSurfTime", t);
                    NagisaBayDiagnostics.Shot(eye, tgt, 52f, $"{OutSub}nb1_{name}_t{(int)Mathf.Round(t * 10f):D4}.png"); shots++;
                }
                if (name == "a")
                {
                    // low swash close-up looking along the shoreline just after a bore has arrived
                    Vector2 e2 = sh - seaDir2 * 3f - along * 8f;
                    var eyeLow = new Vector3(e2.x, g.Height(e2.x, e2.y) + 0.45f, e2.y);
                    Vector2 t2 = sh + seaDir2 * 10f + along * 40f;
                    foreach (float t in new[] { start + 1.5f, start + 4.5f })
                    {
                        Shader.SetGlobalFloat("_NagisaSurfTime", t);
                        NagisaBayDiagnostics.Shot(eyeLow, new Vector3(t2.x, 0.1f, t2.y), 60f, $"{OutSub}nb1_swash_t{(int)Mathf.Round(t * 10f):D4}.png"); shots++;
                    }
                    // aerial of the whole beach for the pattern of crests
                    Vector2 a2 = sh + seaDir2 * 120f - along * 60f;
                    var air = new Vector3(a2.x, 45f, a2.y);
                    Shader.SetGlobalFloat("_NagisaSurfTime", tBreak + 6f);
                    NagisaBayDiagnostics.Shot(air, new Vector3(sh.x, 0f, sh.y), 55f, $"{OutSub}nb1_aerial.png"); shots++;
                }
            }
        }
        finally
        {
            Shader.SetGlobalFloat("_NagisaSurfTimeOn", 0f);
            if (regions != null && !string.IsNullOrEmpty(previous))
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
        Debug.Log($"[nagisa-surf] captured {shots} frames");
    }
}
