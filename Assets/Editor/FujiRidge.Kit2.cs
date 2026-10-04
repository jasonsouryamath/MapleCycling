using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// FUJI RIDGE - E1 milestone 2 kit stager. Claude worker D, 2026-09-30.
///
/// Authored in tools/blender/build_fuji_kit2.py as FujiK2_*_LOD0/1/2.glb (Assets/Environment/
/// FujiRidge/Models): Italian cypress (2 sizes), olive, vineyard trellis bay, dry-stone terrace
/// wall, wayside shrine, hermitage, brick campanile, tiered piazza fountain, volcanic scoria
/// boulders and a sulphur fumarole.
///
/// Town.cs calls FujiKit2Add* (false = kit missing, so each call site keeps its procedural
/// fallback); placements accumulate in a list and FujiKit2Flush merges them into world-grid chunks
/// with a 3-level LODGroup. BuildFujiKit2 adds the pieces no call site owns (cypress avenue, olives,
/// terraces, shrines, hermitage, fumaroles, scoria). Every number is PROVISIONAL art/perf tuning.
/// </summary>
public static partial class FujiRidgeEnvironment
{
    private const bool Kit2Enabled = true;
    private const string Kit2ModelDir = "Assets/Environment/FujiRidge/Models";

    private struct F2Part { public Mesh mesh; public int sub; public Matrix4x4 rel; public string mat; }

    private sealed class F2Variant
    {
        public string name;
        public readonly List<F2Part>[] lods = { new List<F2Part>(), new List<F2Part>(), new List<F2Part>() };
    }

    private enum F2Cat { Landmark = 0, Tree = 1, Rock = 2, Small = 3 }

    private static readonly (float chunk, float lod0, float lod1, float cull, bool shadowLod1)[] F2Params =
    {
        (80f, 130f, 320f, 1500f, true),   // landmarks: campanile, hermitage, shrine
        (70f, 70f, 200f, 900f, false),    // cypress, olive
        (90f, 60f, 180f, 800f, false),    // scoria / fumaroles
        (60f, 55f, 150f, 500f, false),    // vine bays, terrace walls, fountain
    };

    private static readonly Dictionary<string, F2Variant> _f2 = new Dictionary<string, F2Variant>();
    private static readonly List<(F2Variant v, F2Cat cat, Matrix4x4 m)> _f2Places = new List<(F2Variant, F2Cat, Matrix4x4)>();
    private static readonly Dictionary<string, Material> _f2Mats = new Dictionary<string, Material>();
    private static readonly HashSet<string> _f2Missing = new HashSet<string>();

    private static void FujiKit2Reset()
    {
        _f2Places.Clear();
        _f2Mats.Clear();
        _f2Missing.Clear();
        _f2.Clear();
    }

    private static List<F2Part> LoadF2Glb(string path)
    {
        var parts = new List<F2Part>();
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) return parts;
        var inv = go.transform.worldToLocalMatrix;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            var rel = inv * mf.transform.localToWorldMatrix;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string mname = (mr != null && s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null)
                    ? mr.sharedMaterials[s].name : $"sub{s}";
                parts.Add(new F2Part { mesh = mesh, sub = s, rel = rel, mat = mname });
            }
        }
        return parts;
    }

    private static F2Variant FujiKit2Get(string name)
    {
        if (!Kit2Enabled) return null;
        if (_f2.TryGetValue(name, out var v)) return v;
        if (_f2Missing.Contains(name)) return null;
        v = new F2Variant { name = name };
        bool ok = true;
        for (int l = 0; l < 3; l++)
        {
            v.lods[l].AddRange(LoadF2Glb($"{Kit2ModelDir}/FujiK2_{name}_LOD{l}.glb"));
            ok &= v.lods[l].Count > 0;
        }
        if (!ok)
        {
            _f2Missing.Add(name);
            Debug.LogWarning($"[fuji] kit2 '{name}' missing/empty LOD GLB - procedural fallback.");
            return null;
        }
        _f2[name] = v;
        return v;
    }

    private static bool FujiKit2Add(string name, F2Cat cat, Vector3 pos, float yawDeg, Vector3 scale)
    {
        var v = FujiKit2Get(name);
        if (v == null) return false;
        _f2Places.Add((v, cat, Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), scale)));
        return true;
    }

    private static bool FujiKit2AddFacing(string name, F2Cat cat, Vector3 pos, Vector3 facing, Vector3 scale)
    {
        var v = FujiKit2Get(name);
        if (v == null) return false;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        _f2Places.Add((v, cat, Matrix4x4.TRS(pos, Quaternion.LookRotation(facing.normalized, Vector3.up), scale)));
        return true;
    }

    /// <summary>Kit +x runs ALONG 'along' (vine bays, terrace walls); +z (the face) is Cross(along, up).</summary>
    private static bool FujiKit2AddAlong(string name, F2Cat cat, Vector3 pos, Vector3 along, Vector3 scale)
    {
        var v = FujiKit2Get(name);
        if (v == null) return false;
        along.y = 0f;
        if (along.sqrMagnitude < 1e-6f) return false;
        along.Normalize();
        var fwd = Vector3.Cross(along, Vector3.up);
        _f2Places.Add((v, cat, Matrix4x4.TRS(pos, Quaternion.LookRotation(fwd, Vector3.up), scale)));
        return true;
    }

    // ------------------------------------------------------------------ materials

    private struct F2Role
    {
        public Color tint; public float gloss, spec, rim; public string tex, nrm; public float nrmK; public bool neutral;
        public F2Role(Color t, float g, float s, float r, string tx = null, string n = null, float nk = 0f, bool neutral = false)
        { tint = t; gloss = g; spec = s; rim = r; tex = tx; nrm = n; nrmK = nk; this.neutral = neutral; }
    }

    private static readonly Dictionary<string, F2Role> F2Roles = new Dictionary<string, F2Role>
    {
        { "Cypress",    new F2Role(new Color(0.09f, 0.20f, 0.10f), 0.08f, 0.04f, 0.30f) },
        { "Olive",      new F2Role(new Color(0.42f, 0.49f, 0.37f), 0.10f, 0.06f, 0.32f) },
        { "VineLeaf",   new F2Role(new Color(0.30f, 0.46f, 0.15f), 0.08f, 0.05f, 0.32f) },
        { "Grape",      new F2Role(new Color(0.24f, 0.08f, 0.28f), 0.45f, 0.30f, 0.30f) },
        { "Bark",       new F2Role(new Color(0.80f, 0.70f, 0.60f), 0.06f, 0.04f, 0.18f, "Fuji_K2_Bark_Albedo.png", "Fuji_K2_Bark_Normal.png", 0.8f) },
        { "Stone",      new F2Role(new Color(0.96f, 0.93f, 0.88f), 0.08f, 0.05f, 0.18f, "Fuji_Town_Masonry_Albedo.png", "Fuji_Town_Masonry_Normal.png", 0.9f, true) },
        { "DryStone",   new F2Role(new Color(0.84f, 0.80f, 0.72f), 0.06f, 0.04f, 0.16f, "Fuji_Town_Masonry_Albedo.png", "Fuji_Town_Masonry_Normal.png", 0.9f, true) },
        { "Brick",      new F2Role(new Color(1.00f, 0.80f, 0.68f), 0.10f, 0.06f, 0.20f, "Fuji_Town_Masonry_Albedo.png", "Fuji_Town_Masonry_Normal.png", 0.9f) },
        { "Travertine", new F2Role(new Color(1.00f, 0.97f, 0.92f), 0.14f, 0.08f, 0.22f, "Fuji_Town_Travertine_Albedo.png", "Fuji_Town_Travertine_Normal.png", 0.5f, true) },
        { "Coppi",      new F2Role(new Color(1.00f, 0.96f, 0.92f), 0.16f, 0.10f, 0.20f, "Fuji_Town_Coppi_Albedo.png", "Fuji_Town_Coppi_Normal.png", 0.9f) },
        { "Slate",      new F2Role(new Color(0.30f, 0.31f, 0.33f), 0.30f, 0.20f, 0.20f) },
        { "Copper",     new F2Role(new Color(0.32f, 0.56f, 0.50f), 0.50f, 0.35f, 0.25f) },
        { "Iron",       new F2Role(new Color(0.08f, 0.08f, 0.085f), 0.45f, 0.30f, 0.20f) },
        { "Wood",       new F2Role(new Color(1.00f, 0.95f, 0.90f), 0.18f, 0.10f, 0.18f, "Fuji_Town_Wood_Albedo.png", "Fuji_Town_Wood_Normal.png", 0.7f) },
        { "Water",      new F2Role(new Color(0.22f, 0.40f, 0.46f), 0.95f, 0.85f, 0.35f) },
        { "Interior",   new F2Role(new Color(0.10f, 0.09f, 0.08f), 0.05f, 0.02f, 0.05f) },
        { "Terracotta", new F2Role(new Color(0.68f, 0.36f, 0.22f), 0.10f, 0.06f, 0.20f) },
        { "Flower",     new F2Role(new Color(0.86f, 0.16f, 0.18f), 0.12f, 0.08f, 0.35f) },
        { "Scoria",     new F2Role(new Color(0.95f, 0.88f, 0.84f), 0.08f, 0.05f, 0.14f, "Fuji_K2_Scoria_Albedo.png", "Fuji_K2_Scoria_Normal.png", 1.0f, true) },
        { "Sulphur",    new F2Role(new Color(0.84f, 0.74f, 0.18f), 0.10f, 0.06f, 0.25f) },
    };

    private static Texture F2Normal(string file)
    {
        string path = $"{TextureDir}/{file}";
        if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }

    private static Material FujiKit2Material(string slotName)
    {
        string role = slotName.StartsWith("FujiK2_") ? slotName.Substring("FujiK2_".Length) : slotName;
        int sp = role.IndexOfAny(new[] { ' ', '.', '(' });
        if (sp > 0) role = role.Substring(0, sp);
        if (!F2Roles.TryGetValue(role, out var r)) role = "Stone";
        if (_f2Mats.TryGetValue(role, out var cached) && cached != null) return cached;
        r = F2Roles[role];
        var mat = CelMaterial($"FujiK2_{role}", r.tint, gloss: r.gloss, spec: r.spec, rim: r.rim,
                              texture: r.tex != null ? FujiTexture(r.tex) : null,
                              shade: r.neutral ? GroundShade : (Color?)null);
        if (mat.HasProperty("_NormalMap"))
        {
            if (r.nrm != null && r.nrmK > 0f)
            {
                var n = F2Normal(r.nrm);
                if (n != null) { mat.SetTexture("_NormalMap", n); mat.SetFloat("_NormalStrength", r.nrmK); }
            }
            else mat.SetFloat("_NormalStrength", 0f);
        }
        if (r.tex == null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
        if (mat.HasProperty("_WeatherAmount")) mat.SetFloat("_WeatherAmount", role == "Stone" || role == "DryStone" || role == "Brick" ? 0.3f : 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 2f);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        _f2Mats[role] = mat;
        return mat;
    }

    private static float F2LodHeight(float size, float dist) => Mathf.Clamp(size / (dist * 1.155f), 0.0005f, 0.99f);

    private static void FujiKit2Flush(Transform parent)
    {
        if (_f2Places.Count == 0) return;
        var root = new GameObject("Fuji Kit2 (E1)").transform;
        root.SetParent(parent, false);
        string[] catNames = { "Landmarks", "Trees", "Rocks", "Small" };
        int totalRenderers = 0, lod0Tris = 0, chunksMade = 0;
        for (int c = 0; c < 4; c++)
        {
            var prm = F2Params[c];
            var chunks = new Dictionary<long, List<(F2Variant v, Matrix4x4 m)>>();
            int n = 0;
            foreach (var p in _f2Places)
            {
                if ((int)p.cat != c) continue;
                Vector3 pos = p.m.GetColumn(3);
                long key = ((long)Mathf.FloorToInt(pos.x / prm.chunk) << 32) ^ (uint)Mathf.FloorToInt(pos.z / prm.chunk);
                if (!chunks.TryGetValue(key, out var l)) chunks[key] = l = new List<(F2Variant, Matrix4x4)>();
                l.Add((p.v, p.m));
                n++;
            }
            if (n == 0) continue;
            var catRoot = new GameObject($"Kit2 {catNames[c]} ({n})").transform;
            catRoot.SetParent(root, false);
            foreach (var kv in chunks)
            {
                int kx = (int)(kv.Key >> 32), kz = (int)(kv.Key & 0xffffffff);
                var chunk = new GameObject($"{catNames[c]} {kx},{kz}").transform;
                chunk.SetParent(catRoot, false);
                var lodRenderers = new List<Renderer>[3];
                for (int l = 0; l < 3; l++)
                {
                    lodRenderers[l] = new List<Renderer>();
                    var lists = new Dictionary<Material, List<CombineInstance>>();
                    foreach (var (v, m) in kv.Value)
                        foreach (var part in v.lods[l])
                        {
                            var mat = FujiKit2Material(part.mat);
                            if (!lists.TryGetValue(mat, out var ci)) lists[mat] = ci = new List<CombineInstance>();
                            ci.Add(new CombineInstance { mesh = part.mesh, subMeshIndex = part.sub, transform = m * part.rel });
                        }
                    var lt = new GameObject($"LOD{l}").transform;
                    lt.SetParent(chunk, false);
                    foreach (var ml in lists)
                    {
                        var mesh = new Mesh { name = $"FujiK2_{catNames[c]}_{kx}_{kz}_L{l}_{ml.Key.name}", indexFormat = IndexFormat.UInt32 };
                        mesh.CombineMeshes(ml.Value.ToArray(), true, true, false);
                        mesh.RecalculateBounds();
                        if (l == 0) lod0Tris += mesh.triangles.Length / 3;
                        var go = AddMesh(lt, ml.Key.name, mesh, ml.Key, collider: false);
                        var mr = go.GetComponent<MeshRenderer>();
                        mr.shadowCastingMode = (l == 0 || (l == 1 && prm.shadowLod1)) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        lodRenderers[l].Add(mr);
                        totalRenderers++;
                    }
                }
                var lg = chunk.gameObject.AddComponent<LODGroup>();
                lg.SetLODs(new[]
                {
                    new LOD(0.5f, lodRenderers[0].ToArray()), new LOD(0.1f, lodRenderers[1].ToArray()),
                    new LOD(0.01f, lodRenderers[2].ToArray()),
                });
                lg.RecalculateBounds();
                float s = Mathf.Max(lg.size, 1f);
                lg.SetLODs(new[]
                {
                    new LOD(F2LodHeight(s, prm.lod0), lodRenderers[0].ToArray()),
                    new LOD(F2LodHeight(s, prm.lod1), lodRenderers[1].ToArray()),
                    new LOD(F2LodHeight(s, prm.cull), lodRenderers[2].ToArray()),
                });
                lg.fadeMode = LODFadeMode.None;
                chunksMade++;
            }
        }
        Debug.Log($"[fuji] E1 kit2: {_f2Places.Count} placements in {chunksMade} chunks, {totalRenderers} renderers, {lod0Tris} LOD0 tris.");
        _f2Places.Clear();
    }

    // ------------------------------------------------------------------ call-site helpers (Town.cs)

    private static bool FujiKit2Cypress(Vector3 foot, float h, System.Random rnd)
    {
        bool a = h > 11f;
        return FujiKit2Add(a ? "CypressA" : "CypressB", F2Cat.Tree, foot, (float)rnd.NextDouble() * 360f,
                           Vector3.one * (h / (a ? 13f : 9.5f)));
    }

    /// <summary>One trellis row piece between two route stations (f0 -> f1): as many 2.4 m bays as fit.</summary>
    private static bool FujiKit2VineRow(Vector3 f0, Vector3 f1, System.Random rnd)
    {
        var d = f1 - f0; d.y = 0f;
        float len = d.magnitude;
        if (len < 0.5f) return false;
        int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f));
        float sx = len / (n * 2.4f);
        var along = d / len;
        for (int i = 0; i < n; i++)
        {
            var p = Vector3.Lerp(f0, f1, (i + 0.5f) / n);
            if (!FujiKit2AddAlong("VineBay", F2Cat.Small, p, along, new Vector3(sx, 0.9f + (float)rnd.NextDouble() * 0.2f, 1f))) return false;
        }
        return true;
    }

    private static bool FujiKit2Fountain(Vector3 centre) => FujiKit2Add("Fountain", F2Cat.Small, centre, 0f, Vector3.one);

    private static bool FujiKit2Campanile(Vector3 foot, Vector3 facing) =>
        FujiKit2AddFacing("Campanile", F2Cat.Landmark, foot, facing, Vector3.one);

    // ------------------------------------------------------------------ stage: everything no call site owns

    private static void BuildFujiKit2(Transform root, FujiRoute route)
    {
        var rnd = new System.Random(2209);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        int uphillSide(int i)
        {
            Foot(route, i, 9f, out var a, out _);
            Foot(route, i, -9f, out var b, out _);
            return a.y >= b.y ? 1 : -1;
        }
        int cyp = 0, oli = 0, walls = 0, shrines = 0, scoria = 0, fum = 0;

        // ---- cypress avenue: a double row on the verge before the vineyards open up
        for (float d = TownToM + 60f; d < TownToM + 60f + 520f; d += 9f)
        {
            int i = route.IndexAt(d);
            for (int s = -1; s <= 1; s += 2)
            {
                Foot(route, i, s * (CorridorClearM + 1.7f), out var f, out _);
                if (FujiKit2Cypress(f, R(11f, 13.5f), rnd)) cyp++;
            }
        }

        // ---- vineyard belt (TownToM .. VineToM): olives on the edges, dry-stone terraces on the uphill bank
        int iA = route.IndexAt(TownToM + 30f), iB = route.IndexAt(VineToM);
        for (int i = iA; i < iB - 3; i += 3)
        {
            float d = route.Distance[i];
            int up = uphillSide(i);
            // terrace wall on the uphill bank (continuous, 4 m bays) where there is a real cut bank
            Foot(route, i, up * (CorridorClearM + 0.75f), out var wf, out _);
            Foot(route, i, up * 9f, out var far, out _);
            if (far.y - wf.y > 0.8f && Mathf.PerlinNoise(d * 0.006f, 4.4f) > 0.3f)
            {
                var t = route.Tangent[i];
                var toRoad = -up * route.SideFlat(i);
                if (Vector3.Dot(Vector3.Cross(new Vector3(t.x, 0f, t.z), Vector3.up), toRoad) < 0f) t = -t;   // wall face (+z) looks at the road
                wf.y -= 0.35f;
                if (FujiKit2AddAlong("TerraceWall", F2Cat.Small, wf, t, new Vector3(Mathf.Clamp(route.Distance[Mathf.Min(i + 3, route.Count - 1)] - d, 2f, 6f) / 4f, 1f, 1f))) walls++;
            }
            // olives scattered between vineyard blocks
            if (i % 9 == 0 && Mathf.PerlinNoise(d * 0.01f, 7.7f) > 0.3f)
            {
                int s = rnd.NextDouble() < 0.5 ? -1 : 1;
                Foot(route, i, s * (CorridorClearM + R(3.5f, 7f)), out var of, out _);
                if (FujiKit2Add("Olive", F2Cat.Tree, of, R(0f, 360f), Vector3.one * R(0.8f, 1.3f))) oli++;
            }
        }

        // ---- pilgrimage road: wayside shrines every ~900 m, with a stone-wall cheek, one hermitage
        for (float d = 1500f; d < 6000f; d += 880f)
        {
            int i = route.IndexAt(d);
            int up = uphillSide(i);
            Foot(route, i, up * (CorridorClearM + 2.2f), out var f, out var outward);
            f.y = Mathf.Max(f.y, route.Position[i].y);
            if (FujiKit2AddFacing("Shrine", F2Cat.Landmark, f, -up * route.SideFlat(i), Vector3.one * 1.1f)) shrines++;
        }
        {
            int i = route.IndexAt(5200f);
            int up = uphillSide(i);
            Foot(route, i, up * (CorridorClearM + 14f), out var f, out _);
            if (FujiKit2AddFacing("Hermitage", F2Cat.Landmark, f, -up * route.SideFlat(i), Vector3.one)) shrines++;
        }

        // ---- volcano: scoria boulder fields on the upper slopes, fumaroles near the cone
        for (float d = 7000f; d < route.Length - 60f; d += 22f)
        {
            int i = route.IndexAt(d);
            float volc = Mathf.InverseLerp(7000f, 11500f, d);
            int tries = 1 + Mathf.RoundToInt(volc * 3f);
            for (int k = 0; k < tries; k++)
            {
                if (rnd.NextDouble() > 0.55) continue;
                int s = rnd.NextDouble() < 0.5 ? -1 : 1;
                float off = s * (CorridorClearM + R(2.5f, 45f) * (float)(0.3 + rnd.NextDouble()));
                Foot(route, i, off, out var f, out _);
                string v = new[] { "ScoriaA", "ScoriaB", "ScoriaC" }[rnd.Next(3)];
                if (FujiKit2Add(v, F2Cat.Rock, f, R(0f, 360f), Vector3.one * R(0.6f, 2.2f))) scoria++;
            }
            if (d > 9500f && rnd.NextDouble() < 0.12)
            {
                int s = rnd.NextDouble() < 0.5 ? -1 : 1;
                Foot(route, i, s * (CorridorClearM + R(7f, 30f)), out var f, out _);
                if (FujiKit2Add("Fumarole", F2Cat.Rock, f, R(0f, 360f), Vector3.one * R(0.9f, 1.6f))) fum++;
            }
        }
        Debug.Log($"[fuji] kit2 stage: {cyp} avenue cypress, {oli} olives, {walls} terrace walls, {shrines} shrines/hermitage, {scoria} scoria, {fum} fumaroles.");
    }
}
