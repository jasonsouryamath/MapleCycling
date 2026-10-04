using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// FUJI RIDGE - E1 pass 1 (copilot CLI, 2026-09-28): the hill town's HERO TOWNHOUSE KIT.
///
/// Six stone / stucco townhouses authored in tools/blender/build_fuji_hilltown.py
/// (Fuji_Town_&lt;Variant&gt;_LOD0/1/2.glb in Assets/Environment/FujiRidge/Models) replace the
/// palette-box bodies that FujiRidge.Town.cs::House() used to build. House() still owns WHERE a
/// house goes (frontage width, side, idx-based ground-floor kind) and still builds the street
/// furniture in front of it; this file only chooses a variant, records a placement and, at flush
/// time, merges every placement into 120 m world-grid chunks with a 3-level LODGroup.
///
/// KIT FRAME: +z = facade normal (faces the road), x along the street, origin = centre of the
/// frontage on the facade plane at pavement level; the plinth runs 3 m below it. So placement is
/// just TRS(front, LookRotation(-sf), (w / W, 1, 1)).
///
/// MATERIALS: the GLBs carry flat-colour slots named Fuji_Town_&lt;Role&gt;; they are re-materialised
/// here by name onto CelLit with the kit's albedo + normal maps (Assets/Environment/FujiRidge/Textures).
/// Every number below is PROVISIONAL art/perf tuning.
/// </summary>
public static partial class FujiRidgeEnvironment
{
    private const bool HillTownKitEnabled = true;
    private const string HillTownKitDir = "Assets/Environment/FujiRidge/Models";
    private const float TownKitChunkM = 60f;           // PROVISIONAL: merge grid
    private const float TownKitLod0M = 75f;            // PROVISIONAL: hero detail out to here
    private const float TownKitLod1M = 220f;           // PROVISIONAL: reveal-only LOD1 out to here
    private const float TownKitCullM = 900f;           // PROVISIONAL: LOD2 massing out to here (town is seen from the climb above)
    private const float TownKitMinScaleX = 0.8f, TownKitMaxScaleX = 1.3f;

    // Mirrors VARIANTS in build_fuji_hilltown.py (W = frontage, floors incl. ground). Keep in sync.
    private static readonly (string name, float W, int floors, string ground)[] TownKitSpec =
    {
        ("CasaBar",     7.0f, 2, "shop"),
        ("CasaBottega", 8.5f, 3, "shop"),
        ("CasaPortico", 9.5f, 3, "arcade"),
        ("CasaAlta",    7.5f, 4, "door"),
        ("CasaPietra",  9.0f, 2, "arcade"),
        ("CasaForno",   8.0f, 4, "shop"),
    };

    private struct TownKitPart { public Mesh mesh; public int sub; public Matrix4x4 rel; public string mat; }

    private sealed class TownKitVariant
    {
        public string name, ground;
        public float W;
        public int floors;
        public readonly List<TownKitPart>[] lods = { new List<TownKitPart>(), new List<TownKitPart>(), new List<TownKitPart>() };
    }

    private static List<TownKitVariant> _townKit;
    private static readonly List<(TownKitVariant v, Matrix4x4 m)> _townKitPlacements = new List<(TownKitVariant, Matrix4x4)>();

    private static void ResetHillTownKit()
    {
        _townKit = null;
        _townKitPlacements.Clear();
    }

    private static List<TownKitPart> LoadTownKitGlb(string path)
    {
        var parts = new List<TownKitPart>();
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
                parts.Add(new TownKitPart { mesh = mesh, sub = s, rel = rel, mat = mname });
            }
        }
        return parts;
    }

    /// <summary>Loads every variant once per build. False (box fallback) if any LOD is missing.</summary>
    private static bool TownKitReady()
    {
        if (!HillTownKitEnabled) return false;
        if (_townKit != null) return _townKit.Count > 0;
        _townKit = new List<TownKitVariant>();
        foreach (var spec in TownKitSpec)
        {
            var v = new TownKitVariant { name = spec.name, W = spec.W, floors = spec.floors, ground = spec.ground };
            bool ok = true;
            for (int l = 0; l < 3; l++)
            {
                v.lods[l].AddRange(LoadTownKitGlb($"{HillTownKitDir}/Fuji_Town_{spec.name}_LOD{l}.glb"));
                ok &= v.lods[l].Count > 0;
            }
            if (ok) _townKit.Add(v);
            else Debug.LogWarning($"[fuji] E1 hill-town kit: {spec.name} has a missing/empty LOD GLB - skipped.");
        }
        Debug.Log($"[fuji] E1 hill-town kit: {_townKit.Count}/{TownKitSpec.Length} variants loaded.");
        return _townKit.Count > 0;
    }

    /// <summary>
    /// Records a kit townhouse for the frontage House() is about to build. kind = House's idx % 5
    /// (2 = arcade; 0 / 3 = cafe + pasticceria terraces, which want an awning; 1 / 4 = anything
    /// non-arcade). Returns false to let House() fall back to its box body.
    /// </summary>
    private static bool TryKitHouse(Vector3 front, Vector3 sf, float w, int kind, System.Random rnd, out int floors)
    {
        floors = 0;
        if (!TownKitReady()) return false;
        bool wantArcade = kind == 2, wantShop = kind == 0 || kind == 3;
        TownKitVariant best = null;
        float bestScore = float.MaxValue;
        foreach (var v in _townKit)
        {
            if ((v.ground == "arcade") != wantArcade) continue;
            if (wantShop && v.ground != "shop") continue;
            float sx = w / v.W;
            if (sx < TownKitMinScaleX || sx > TownKitMaxScaleX) continue;
            // Width fit plus a random term so neighbours of similar width still vary.
            float score = Mathf.Abs(w - v.W) + (float)rnd.NextDouble() * 2.2f;
            if (score < bestScore) { bestScore = score; best = v; }
        }
        if (best == null) return false;
        var rot = Quaternion.LookRotation(-sf, Vector3.up);
        _townKitPlacements.Add((best, Matrix4x4.TRS(front, rot, new Vector3(w / best.W, 1f, 1f))));
        floors = best.floors;
        return true;
    }

    // ------------------------------------------------------------------ materials

    // role -> (tint, gloss, spec, rim, texture stem or null, normal strength). PROVISIONAL.
    private static readonly Dictionary<string, (Color tint, float gloss, float spec, float rim, string tex, float nrm)> TownKitRoles =
        new Dictionary<string, (Color, float, float, float, string, float)>
    {
        { "RenderOchre",  (new Color(0.90f, 0.66f, 0.38f), 0.10f, 0.06f, 0.22f, "Stucco", 0.6f) },
        { "RenderRosa",   (new Color(0.90f, 0.66f, 0.56f), 0.10f, 0.06f, 0.22f, "Stucco", 0.6f) },
        { "RenderCream",  (new Color(0.96f, 0.86f, 0.66f), 0.10f, 0.06f, 0.22f, "Stucco", 0.6f) },
        { "RenderSiena",  (new Color(0.78f, 0.47f, 0.30f), 0.10f, 0.06f, 0.22f, "Stucco", 0.6f) },
        { "Masonry",      (new Color(0.96f, 0.93f, 0.88f), 0.08f, 0.05f, 0.18f, "Masonry", 0.9f) },
        { "Travertine",   (new Color(1.00f, 0.97f, 0.92f), 0.14f, 0.08f, 0.22f, "Travertine", 0.5f) },
        { "Coppi",        (new Color(1.00f, 0.96f, 0.92f), 0.16f, 0.10f, 0.20f, "Coppi", 0.9f) },
        { "ShutterGreen", (new Color(0.34f, 0.52f, 0.38f), 0.22f, 0.12f, 0.25f, "Louvre", 0.8f) },
        { "ShutterBrown", (new Color(0.56f, 0.38f, 0.25f), 0.22f, 0.12f, 0.25f, "Louvre", 0.8f) },
        { "Wood",         (new Color(1.00f, 0.95f, 0.90f), 0.18f, 0.10f, 0.18f, "Wood", 0.7f) },
        { "Frame",        (new Color(0.90f, 0.88f, 0.82f), 0.30f, 0.15f, 0.20f, null, 0f) },
        { "Glass",        (new Color(0.12f, 0.15f, 0.19f), 0.85f, 0.90f, 0.35f, null, 0f) },
        { "Interior",     (new Color(0.10f, 0.09f, 0.08f), 0.05f, 0.02f, 0.05f, null, 0f) },
        { "Iron",         (new Color(0.08f, 0.08f, 0.085f), 0.45f, 0.30f, 0.20f, null, 0f) },
        { "Copper",       (new Color(0.55f, 0.37f, 0.23f), 0.50f, 0.35f, 0.25f, null, 0f) },
        { "Terracotta",   (new Color(0.68f, 0.36f, 0.22f), 0.10f, 0.06f, 0.20f, null, 0f) },
        { "Geranium",     (new Color(0.88f, 0.12f, 0.14f), 0.12f, 0.08f, 0.35f, null, 0f) },
        { "Leaf",         (new Color(0.18f, 0.36f, 0.14f), 0.08f, 0.05f, 0.30f, null, 0f) },
        { "AwningRed",    (new Color(0.72f, 0.14f, 0.12f), 0.08f, 0.05f, 0.25f, null, 0f) },
        { "AwningGreen",  (new Color(0.16f, 0.38f, 0.26f), 0.08f, 0.05f, 0.25f, null, 0f) },
        { "AwningCream",  (new Color(0.92f, 0.86f, 0.72f), 0.08f, 0.05f, 0.25f, null, 0f) },
        { "SignGold",     (new Color(0.88f, 0.68f, 0.28f), 0.60f, 0.60f, 0.30f, null, 0f) },
        { "SignDark",     (new Color(0.11f, 0.17f, 0.15f), 0.25f, 0.15f, 0.20f, null, 0f) },
    };

    private static readonly Dictionary<string, Material> _townKitMats = new Dictionary<string, Material>();

    /// <summary>Normal maps must import as NormalMap or CelLit unpacks garbage.</summary>
    private static Texture TownKitNormal(string stem)
    {
        string path = $"{TextureDir}/Fuji_Town_{stem}_Normal.png";
        if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }

    private static Material TownKitMaterial(string slotName)
    {
        // glTF importers can suffix instance names; match the role by exact prefix + known key.
        string role = slotName.StartsWith("Fuji_Town_") ? slotName.Substring("Fuji_Town_".Length) : slotName;
        int sp = role.IndexOfAny(new[] { ' ', '.', '(' });
        if (sp > 0) role = role.Substring(0, sp);
        if (!TownKitRoles.TryGetValue(role, out var r)) role = "Masonry";
        if (_townKitMats.TryGetValue(role, out var cached) && cached != null) return cached;
        r = TownKitRoles[role];
        bool neutral = role == "Masonry" || role == "Travertine" || role == "Frame" || role == "Iron" || role == "Glass";
        // CelMaterial is cached by NAME in MaterialCache - fine, the name is unique per role.
        var mat = CelMaterial($"Fuji_Town_{role}", r.tint, gloss: r.gloss, spec: r.spec, rim: r.rim,
                              texture: r.tex != null ? FujiTexture($"Fuji_Town_{r.tex}_Albedo.png") : null,
                              shade: neutral ? GroundShade : (Color?)null);
        if (r.tex != null && r.nrm > 0f)
        {
            var n = TownKitNormal(r.tex);
            if (n != null) { mat.SetTexture("_NormalMap", n); mat.SetFloat("_NormalStrength", r.nrm); }
        }
        else mat.SetFloat("_NormalStrength", 0f);
        if (r.tex == null) mat.SetTexture("_MainTex", null);
        // Mild wall weathering (moss/grime at the base, sun-bleach) on the big masonry roles only.
        bool weather = role.StartsWith("Render") || role == "Masonry";
        mat.SetFloat("_WeatherAmount", weather ? 0.35f : 0f);
        mat.SetFloat("_Cull", 2f);   // no camera-interior geometry in the kit (portico is open-fronted, not entered)
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        _townKitMats[role] = mat;
        return mat;
    }

    // ------------------------------------------------------------------ flush

    private static float TownKitLodHeight(float size, float dist) =>
        Mathf.Clamp(size / (dist * 1.155f), 0.0005f, 0.99f);   // fov 60: 2 tan 30 = 1.155

    /// <summary>Merges every placement into world-grid chunks with LOD0/1/2. Returns LOD0 tris.</summary>
    private static int FlushHillTownKit(Transform group)
    {
        _townKitMats.Clear();
        if (_townKitPlacements.Count == 0) return 0;
        var root = new GameObject("Fuji Hill Town Kit (E1)").transform;
        root.SetParent(group, false);

        var chunks = new Dictionary<long, List<(TownKitVariant v, Matrix4x4 m)>>();
        foreach (var p in _townKitPlacements)
        {
            Vector3 pos = p.m.GetColumn(3);
            long key = ((long)Mathf.FloorToInt(pos.x / TownKitChunkM) << 32) ^ (uint)Mathf.FloorToInt(pos.z / TownKitChunkM);
            if (!chunks.TryGetValue(key, out var l)) chunks[key] = l = new List<(TownKitVariant, Matrix4x4)>();
            l.Add(p);
        }

        int lod0Tris = 0, renderers = 0;
        foreach (var kv in chunks)
        {
            int kx = (int)(kv.Key >> 32), kz = (int)(kv.Key & 0xffffffff);
            var chunk = new GameObject($"Town Kit {kx},{kz}").transform;
            chunk.SetParent(root, false);
            var lodRenderers = new List<Renderer>[3];
            for (int l = 0; l < 3; l++)
            {
                lodRenderers[l] = new List<Renderer>();
                var lists = new Dictionary<Material, List<CombineInstance>>();
                foreach (var (v, m) in kv.Value)
                    foreach (var part in v.lods[l])
                    {
                        var mat = TownKitMaterial(part.mat);
                        if (!lists.TryGetValue(mat, out var ci)) lists[mat] = ci = new List<CombineInstance>();
                        ci.Add(new CombineInstance { mesh = part.mesh, subMeshIndex = part.sub, transform = m * part.rel });
                    }
                var lt = new GameObject($"LOD{l}").transform;
                lt.SetParent(chunk, false);
                foreach (var ml in lists)
                {
                    var mesh = new Mesh { name = $"Fuji_TownKit_{kx}_{kz}_L{l}_{ml.Key.name}", indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(ml.Value.ToArray(), true, true, false);
                    mesh.RecalculateBounds();
                    if (l == 0) lod0Tris += mesh.triangles.Length / 3;
                    var go = AddMesh(lt, ml.Key.name, mesh, ml.Key, collider: false);
                    var mr = go.GetComponent<MeshRenderer>();
                    // LOD0 + LOD1 cast (the facades' own relief is the point); LOD2 is too far to read.
                    mr.shadowCastingMode = l < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    lodRenderers[l].Add(mr);
                    renderers++;
                }
            }
            var lg = chunk.gameObject.AddComponent<LODGroup>();
            lg.SetLODs(new[]
            {
                new LOD(0.5f, lodRenderers[0].ToArray()), new LOD(0.1f, lodRenderers[1].ToArray()),
                new LOD(0.01f, lodRenderers[2].ToArray()),
            });
            lg.RecalculateBounds();
            float s = lg.size;
            lg.SetLODs(new[]
            {
                new LOD(TownKitLodHeight(s, TownKitLod0M), lodRenderers[0].ToArray()),
                new LOD(TownKitLodHeight(s, TownKitLod1M), lodRenderers[1].ToArray()),
                new LOD(TownKitLodHeight(s, TownKitCullM), lodRenderers[2].ToArray()),
            });
            lg.fadeMode = LODFadeMode.None;
        }
        Debug.Log($"[fuji] E1 hill-town kit: {_townKitPlacements.Count} townhouses in {chunks.Count} chunks, " +
                  $"{renderers} renderers, {lod0Tris} LOD0 tris.");
        return lod0Tris;
    }
}
