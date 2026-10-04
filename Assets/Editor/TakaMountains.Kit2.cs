using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// TAKA MOUNTAINS (Mt. Ventoux) - E2 photoreal kit stager. Claude worker D, 2026-09-30.
///
/// Authored in tools/blender/build_taka_kit.py as TakaK2_*_LOD0/1/2.glb (Assets/Environment/
/// TakaMountains/Models): Provencal village houses, plane trees, Atlas cedar, beech, black pine,
/// village fountain, limestone scree / outcrops / cairns, kilometre bornes with painted text,
/// the red-and-white weather tower with its lattice mast, summit hall and a memorial stele.
///
/// Other Taka files only call Kit2Add(...) (returning false when the kit is missing, so each call
/// site keeps its old procedural fallback). Every placement is recorded, then Kit2Flush merges
/// them into world-grid chunks with a 3-level LODGroup (merged meshes are the instancing story in
/// this editor-built scene: one draw per material per chunk per LOD).
///
/// KIT FRAME: +z = front (faces the road), x along the frontage, origin at the base centre.
/// All numbers PROVISIONAL art / perf tuning.
/// </summary>
public static partial class TakaMountainsEnvironment
{
    private const bool Kit2Enabled = true;
    private const string Kit2ModelDir = "Assets/Environment/TakaMountains/Models";

    private struct K2Part { public Mesh mesh; public int sub; public Matrix4x4 rel; public string mat; }

    private sealed class K2Variant
    {
        public string name;
        public readonly List<K2Part>[] lods = { new List<K2Part>(), new List<K2Part>(), new List<K2Part>() };
    }

    private enum K2Cat { Building = 0, Tree = 1, Rock = 2, Small = 3 }

    // chunk size, LOD0/LOD1/cull distance (m), shadows beyond LOD0
    private static readonly (float chunk, float lod0, float lod1, float cull, bool shadowLod1)[] K2Params =
    {
        (70f, 90f, 260f, 1100f, true),    // buildings
        (80f, 70f, 200f, 800f, false),    // trees
        (90f, 60f, 180f, 700f, false),    // rocks / scree
        (60f, 60f, 160f, 500f, false),    // bornes, cairns, fountain...
    };

    private static readonly Dictionary<string, K2Variant> _k2 = new Dictionary<string, K2Variant>();
    private static readonly List<(K2Variant v, K2Cat cat, Matrix4x4 m)> _k2Places = new List<(K2Variant, K2Cat, Matrix4x4)>();
    private static readonly Dictionary<string, Material> _k2Mats = new Dictionary<string, Material>();
    private static readonly Dictionary<string, bool> _k2Missing = new Dictionary<string, bool>();

    private static void Kit2Reset()
    {
        _k2Places.Clear();
        _k2Mats.Clear();
        _k2Missing.Clear();
        _k2.Clear();
    }

    private static List<K2Part> LoadK2Glb(string path)
    {
        var parts = new List<K2Part>();
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
                parts.Add(new K2Part { mesh = mesh, sub = s, rel = rel, mat = mname });
            }
        }
        return parts;
    }

    /// <summary>Loads (once) variant 'name'. lod0Name overrides the LOD0 file (bornes carry per-km text).</summary>
    private static K2Variant Kit2Get(string name, string lod0Name = null)
    {
        if (!Kit2Enabled) return null;
        string key = lod0Name ?? name;
        if (_k2.TryGetValue(key, out var v)) return v;
        if (_k2Missing.ContainsKey(key)) return null;
        v = new K2Variant { name = key };
        bool ok = true;
        for (int l = 0; l < 3; l++)
        {
            string file = (l == 0 && lod0Name != null) ? lod0Name : name;
            v.lods[l].AddRange(LoadK2Glb($"{Kit2ModelDir}/TakaK2_{file}_LOD{l}.glb"));
            ok &= v.lods[l].Count > 0;
        }
        if (!ok)
        {
            _k2Missing[key] = true;
            Debug.LogWarning($"[taka] kit2 '{key}' missing/empty LOD GLB - procedural fallback.");
            return null;
        }
        _k2[key] = v;
        return v;
    }

    private static bool Kit2Add(string name, K2Cat cat, Vector3 pos, float yawDeg, Vector3 scale, string lod0Name = null)
    {
        var v = Kit2Get(name, lod0Name);
        if (v == null) return false;
        _k2Places.Add((v, cat, Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), scale)));
        return true;
    }

    private static bool Kit2AddFacing(string name, K2Cat cat, Vector3 pos, Vector3 facing, Vector3 scale, string lod0Name = null)
    {
        var v = Kit2Get(name, lod0Name);
        if (v == null) return false;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        _k2Places.Add((v, cat, Matrix4x4.TRS(pos, Quaternion.LookRotation(facing.normalized, Vector3.up), scale)));
        return true;
    }

    // ------------------------------------------------------------------ materials

    // role -> (tint, gloss, spec, rim, albedo file (Textures/), normal file or null, normal strength, shade override)
    private struct K2Role
    {
        public Color tint; public float gloss, spec, rim; public string tex, nrm; public float nrmK; public bool neutral;
        public K2Role(Color t, float g, float s, float r, string tx = null, string n = null, float nk = 0f, bool neutral = false)
        { tint = t; gloss = g; spec = s; rim = r; tex = tx; nrm = n; nrmK = nk; this.neutral = neutral; }
    }

    private static readonly Dictionary<string, K2Role> K2Roles = new Dictionary<string, K2Role>
    {
        { "StuccoOchre",  new K2Role(new Color(0.96f, 0.76f, 0.50f), 0.10f, 0.06f, 0.22f, "TakaK2_Stucco_Albedo.png", "TakaK2_Stucco_Normal.png", 0.6f) },
        { "StuccoRose",   new K2Role(new Color(0.96f, 0.70f, 0.60f), 0.10f, 0.06f, 0.22f, "TakaK2_Stucco_Albedo.png", "TakaK2_Stucco_Normal.png", 0.6f) },
        { "StuccoCream",  new K2Role(new Color(1.00f, 0.90f, 0.72f), 0.10f, 0.06f, 0.22f, "TakaK2_Stucco_Albedo.png", "TakaK2_Stucco_Normal.png", 0.6f) },
        { "Stone",        new K2Role(new Color(0.96f, 0.93f, 0.88f), 0.08f, 0.05f, 0.18f, "TakaK2_Masonry_Albedo.png", "TakaK2_Masonry_Normal.png", 0.9f, true) },
        { "Limestone",    new K2Role(new Color(1.00f, 0.98f, 0.94f), 0.10f, 0.06f, 0.18f, "Taka_Limestone_Albedo.png", "Taka_Limestone_Normal.png", 0.7f, true) },
        { "Tile",         new K2Role(new Color(1.00f, 0.92f, 0.86f), 0.16f, 0.10f, 0.20f, "TakaK2_Tile_Albedo.png", "TakaK2_Tile_Normal.png", 0.9f) },
        { "ShutterBlue",  new K2Role(new Color(0.34f, 0.54f, 0.66f), 0.22f, 0.12f, 0.25f) },
        { "ShutterGreen", new K2Role(new Color(0.40f, 0.55f, 0.38f), 0.22f, 0.12f, 0.25f) },
        { "ShutterLilac", new K2Role(new Color(0.56f, 0.50f, 0.70f), 0.22f, 0.12f, 0.25f) },
        { "Wood",         new K2Role(new Color(1.00f, 0.92f, 0.84f), 0.18f, 0.10f, 0.18f, "TakaK2_Wood_Albedo.png", "TakaK2_Wood_Normal.png", 0.7f) },
        { "Iron",         new K2Role(new Color(0.08f, 0.08f, 0.085f), 0.45f, 0.30f, 0.20f) },
        { "Glass",        new K2Role(new Color(0.12f, 0.15f, 0.19f), 0.85f, 0.90f, 0.35f) },
        { "Interior",     new K2Role(new Color(0.10f, 0.09f, 0.08f), 0.05f, 0.02f, 0.05f) },
        { "Terracotta",   new K2Role(new Color(0.68f, 0.36f, 0.22f), 0.10f, 0.06f, 0.20f) },
        { "Geranium",     new K2Role(new Color(0.88f, 0.12f, 0.14f), 0.12f, 0.08f, 0.35f) },
        { "Leaf",         new K2Role(new Color(0.18f, 0.36f, 0.14f), 0.08f, 0.05f, 0.30f) },
        { "BarkPlane",    new K2Role(new Color(0.80f, 0.76f, 0.64f), 0.06f, 0.04f, 0.18f, "TakaK2_Bark_Albedo.png", "TakaK2_Bark_Normal.png", 0.8f) },
        { "Bark",         new K2Role(new Color(0.78f, 0.70f, 0.62f), 0.06f, 0.04f, 0.18f, "TakaK2_Bark_Albedo.png", "TakaK2_Bark_Normal.png", 0.8f) },
        { "PineBark",     new K2Role(new Color(0.95f, 0.66f, 0.52f), 0.06f, 0.04f, 0.18f, "TakaK2_Bark_Albedo.png", "TakaK2_Bark_Normal.png", 0.8f) },
        { "PlaneLeaf",    new K2Role(new Color(0.30f, 0.47f, 0.20f), 0.08f, 0.05f, 0.32f) },
        { "CedarLeaf",    new K2Role(new Color(0.20f, 0.34f, 0.31f), 0.10f, 0.05f, 0.32f) },
        { "BeechLeaf",    new K2Role(new Color(0.26f, 0.43f, 0.16f), 0.08f, 0.05f, 0.32f) },
        { "PineLeaf",     new K2Role(new Color(0.12f, 0.25f, 0.12f), 0.10f, 0.05f, 0.30f) },
        { "Water",        new K2Role(new Color(0.25f, 0.45f, 0.52f), 0.95f, 0.85f, 0.35f) },
        { "Scree",        new K2Role(new Color(1.00f, 0.99f, 0.96f), 0.10f, 0.06f, 0.16f, "Taka_Limestone_Albedo.png", "Taka_Limestone_Normal.png", 0.9f, true) },
        { "Concrete",     new K2Role(new Color(0.92f, 0.92f, 0.90f), 0.10f, 0.05f, 0.18f, "Taka_Concrete_Albedo.png", "Taka_Concrete_Normal.png", 0.5f, true) },
        { "TowerRed",     new K2Role(new Color(0.78f, 0.17f, 0.14f), 0.30f, 0.18f, 0.25f) },
        { "TowerWhite",   new K2Role(new Color(0.92f, 0.92f, 0.90f), 0.30f, 0.18f, 0.25f) },
        { "Steel",        new K2Role(new Color(0.55f, 0.57f, 0.60f), 0.50f, 0.40f, 0.25f) },
        { "Granite",      new K2Role(new Color(0.85f, 0.85f, 0.88f), 0.40f, 0.30f, 0.20f, "Taka_Granite_Albedo.png", "Taka_Granite_Normal.png", 0.7f, true) },
        { "Brass",        new K2Role(new Color(0.72f, 0.56f, 0.22f), 0.55f, 0.50f, 0.25f) },
        { "BorneWhite",   new K2Role(new Color(0.93f, 0.92f, 0.88f), 0.12f, 0.06f, 0.22f) },
        { "BorneYellow",  new K2Role(new Color(0.95f, 0.78f, 0.12f), 0.20f, 0.12f, 0.25f) },
        { "BorneBlack",   new K2Role(new Color(0.05f, 0.05f, 0.055f), 0.20f, 0.10f, 0.10f) },
        { "Red",          new K2Role(new Color(0.90f, 0.10f, 0.08f), 0.4f, 0.3f, 0.5f) },
    };

    private static Texture K2Normal(string file)
    {
        string path = $"{TextureDir}/{file}";
        if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }

    private static Material Kit2Material(string slotName)
    {
        string role = slotName.StartsWith("TakaK2_") ? slotName.Substring("TakaK2_".Length) : slotName;
        int sp = role.IndexOfAny(new[] { ' ', '.', '(' });
        if (sp > 0) role = role.Substring(0, sp);
        if (!K2Roles.TryGetValue(role, out var r)) role = "Stone";
        if (_k2Mats.TryGetValue(role, out var cached) && cached != null) return cached;
        r = K2Roles[role];
        Texture albedo = r.tex != null ? TakaTexture(r.tex) : null;
        var mat = CelMaterial($"TakaK2_{role}", r.tint, gloss: r.gloss, spec: r.spec, rim: r.rim,
                              texture: albedo, shade: r.neutral ? GroundShade : (Color?)null);
        if (mat.HasProperty("_NormalMap"))
        {
            if (r.nrm != null && r.nrmK > 0f)
            {
                var n = K2Normal(r.nrm);
                if (n != null) { mat.SetTexture("_NormalMap", n); mat.SetFloat("_NormalStrength", r.nrmK); }
            }
            else mat.SetFloat("_NormalStrength", 0f);
        }
        if (r.tex == null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
        if (mat.HasProperty("_WeatherAmount")) mat.SetFloat("_WeatherAmount", role.StartsWith("Stucco") || role == "Stone" ? 0.3f : 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 2f);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        _k2Mats[role] = mat;
        return mat;
    }

    // ------------------------------------------------------------------ flush

    private static float K2LodHeight(float size, float dist) =>
        Mathf.Clamp(size / (dist * 1.155f), 0.0005f, 0.99f);

    /// <summary>Merges every recorded placement into per-category world-grid chunks with LOD0/1/2.</summary>
    private static void Kit2Flush(Transform parent)
    {
        if (_k2Places.Count == 0) return;
        var root = new GameObject("Taka Kit2 (E2)").transform;
        root.SetParent(parent, false);
        int totalRenderers = 0, lod0Tris = 0, chunksMade = 0;
        string[] catNames = { "Buildings", "Trees", "Rocks", "Small" };
        for (int c = 0; c < 4; c++)
        {
            var prm = K2Params[c];
            var chunks = new Dictionary<long, List<(K2Variant v, Matrix4x4 m)>>();
            int n = 0;
            foreach (var p in _k2Places)
            {
                if ((int)p.cat != c) continue;
                Vector3 pos = p.m.GetColumn(3);
                long key = ((long)Mathf.FloorToInt(pos.x / prm.chunk) << 32) ^ (uint)Mathf.FloorToInt(pos.z / prm.chunk);
                if (!chunks.TryGetValue(key, out var l)) chunks[key] = l = new List<(K2Variant, Matrix4x4)>();
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
                            var mat = Kit2Material(part.mat);
                            if (!lists.TryGetValue(mat, out var ci)) lists[mat] = ci = new List<CombineInstance>();
                            ci.Add(new CombineInstance { mesh = part.mesh, subMeshIndex = part.sub, transform = m * part.rel });
                        }
                    var lt = new GameObject($"LOD{l}").transform;
                    lt.SetParent(chunk, false);
                    foreach (var ml in lists)
                    {
                        var mesh = new Mesh { name = $"TakaK2_{catNames[c]}_{kx}_{kz}_L{l}_{ml.Key.name}", indexFormat = IndexFormat.UInt32 };
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
                    new LOD(K2LodHeight(s, prm.lod0), lodRenderers[0].ToArray()),
                    new LOD(K2LodHeight(s, prm.lod1), lodRenderers[1].ToArray()),
                    new LOD(K2LodHeight(s, prm.cull), lodRenderers[2].ToArray()),
                });
                lg.fadeMode = LODFadeMode.None;
                chunksMade++;
            }
        }
        Debug.Log($"[taka] E2 kit2: {_k2Places.Count} placements in {chunksMade} chunks, {totalRenderers} renderers, {lod0Tris} LOD0 tris.");
        _k2Places.Clear();
    }

    // ------------------------------------------------------------------ stage: bornes, summit, memorial, fountain

    private static void BuildKit2(Transform root, TakaRoute route)
    {
        float kerb = RoadHalfWidth + ShoulderWidth;
        int bornes = 0;

        // Kilometre bornes: km-to-summit, altitude and gradient painted on a white post with a yellow
        // head, on the rider's right, facing the oncoming climber (front +z = back down the road).
        for (int n = 15; n >= 1; n--)
        {
            float d = SummitM - n * 1000f;
            if (d < 100f) continue;
            int i = route.IndexAt(d);
            Foot(route, i, kerb + 0.9f, out var f, out _);
            var tan = route.Tangent[i]; tan.y = 0f;
            if (Kit2AddFacing("BorneBlank", K2Cat.Small, new Vector3(f.x, Height(f.x, f.z), f.z), -tan, Vector3.one, lod0Name: $"Borne_{n}")) bornes++;
        }

        // Summit: hall behind the road, memorial stele below it.
        int iS = route.IndexAt(SummitM);
        int s = -1;
        Foot(route, iS, s * 34f, out var tf, out _);
        if (NearestDist(tf) < 26f) s = 1;
        {
            int ih = route.IndexAt(SummitM + 22f);
            Foot(route, ih, s * (kerb + 24f), out var hf, out var hout);
            hf.y = Height(hf.x, hf.z);
            // front (+z) toward the road = -outward; kit hall is 22 wide along x.
            Kit2AddFacing("SummitHall", K2Cat.Building, hf + Vector3.up * 0.3f, -hout, Vector3.one);
        }
        {
            int im = route.IndexAt(SummitM - 260f);
            Foot(route, im, -s * (kerb + 6.5f), out var mf, out var mout);
            mf.y = Height(mf.x, mf.z);
            Kit2AddFacing("Memorial", K2Cat.Small, mf, -mout, Vector3.one);
        }

        // Village fountain at the end of the Provencal main street.
        {
            int ifn = route.IndexAt(700f);
            Foot(route, ifn, kerb + 8.5f, out var ff, out _);
            ff.y = Mathf.Max(Height(ff.x, ff.z), RoadY(route, ifn, 0f) - 0.2f);
            Kit2Add("Fountain", K2Cat.Small, ff, 0f, Vector3.one);
        }
        Debug.Log($"[taka] kit2 stage: {bornes} bornes, summit hall + memorial + fountain.");
    }

    // ------------------------------------------------------------------ call-site helpers (return true when the kit handled it)

    private static bool Kit2House(Vector3 front, Vector3 outward, float w, System.Random rng)
    {
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-6f) return false;
        string[] names = { "ProvenceA", "ProvenceB", "ProvenceC" };
        float[] kw = { 8.0f, 9.5f, 7.5f };
        int k = rng.Next(3);
        float sx = Mathf.Clamp(w / kw[k], 0.85f, 1.25f);
        return Kit2AddFacing(names[k], K2Cat.Building, front, -outward.normalized, new Vector3(sx, 1f, 1f));
    }

    private static bool Kit2PlaneTree(Vector3 foot, float h, System.Random rng)
    {
        bool a = rng.NextDouble() < 0.5;
        return Kit2Add(a ? "PlaneTreeA" : "PlaneTreeB", K2Cat.Tree, foot, (float)rng.NextDouble() * 360f,
                       Vector3.one * (h / (a ? 11.5f : 9.5f)));
    }

    /// <summary>Forest band: pine low, Atlas cedar higher, a few beech in the damp lower belt.</summary>
    private static bool Kit2Tree(Vector3 foot, float h, bool cedarWanted, System.Random rng)
    {
        float yaw = (float)rng.NextDouble() * 360f;
        if (cedarWanted)
        {
            bool a = rng.NextDouble() < 0.5;
            return Kit2Add(a ? "CedarA" : "CedarB", K2Cat.Tree, foot, yaw, Vector3.one * (h / (a ? 19f : 14f)) * 1.15f);
        }
        double u = rng.NextDouble();
        if (foot.y < 1600f && u < 0.35) return Kit2Add("Beech", K2Cat.Tree, foot, yaw, Vector3.one * (h / 16f) * 1.2f);
        return Kit2Add("Pine", K2Cat.Tree, foot, yaw, Vector3.one * (h / 15f) * 1.1f);
    }

    /// <summary>Moonscape rocks. big=true -> bedded outcrop, else a scree block (random variant).</summary>
    private static bool Kit2Rock(Vector3 centre, float rad, bool big, System.Random rng)
    {
        float yaw = (float)rng.NextDouble() * 360f;
        if (big)
        {
            bool a = rng.NextDouble() < 0.5;
            return Kit2Add(a ? "OutcropA" : "OutcropB", K2Cat.Rock, centre, yaw, Vector3.one * Mathf.Clamp(rad / 3f, 0.8f, 2.6f));
        }
        string[] names = { "ScreeA", "ScreeB", "ScreeC" };
        return Kit2Add(names[rng.Next(3)], K2Cat.Rock, centre, yaw, Vector3.one * Mathf.Clamp(rad / 1.3f, 0.5f, 2.2f));
    }

    private static bool Kit2Cairn(Vector3 foot, float height)
    {
        bool tall = height > 1.9f;
        return Kit2Add(tall ? "CairnTall" : "Cairn", K2Cat.Small, foot, foot.x * 7.3f % 360f, Vector3.one * (height / (tall ? 3.0f : 1.9f)));
    }

    private static bool Kit2WeatherTower(Vector3 foot) => Kit2Add("WeatherTower", K2Cat.Building, foot, 0f, Vector3.one);
}
