using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 2026-09-26 "has nothing in it" pass. Fills the 24 km climb with real authored props instead
/// of the cone placeholders: clumped pines (the shared SakuraPass pine GLBs, read-only), heather
/// and gorse shrubs, grass tufts on the verge, rock outcrops, post-and-rail fences where the
/// dry-stone walls stop, farmhouses and huts, a roadside shrine, a rest stop and km posts.
///
/// Everything is MERGED per 600 m route tile and per material, so a tile is a handful of
/// renderers however dense it is (same idea as the tuft/forest tiles in the main file).
/// Shared GLBs are only read; every material is Azora-owned under MaterialDir.
/// </summary>
public static partial class AzoraHighlandsEnvironment
{
    private const string MinatoGlbDir = "Assets/Environment/MinatoCoast/BlenderAssets";
    private const string SakuraGlbDir = "Assets/Environment/SakuraPass/BlenderAssets";
    private const float DressTileM = 600f;
    private const float CorridorKeepOutM = RoadHalfWidth + ShoulderWidth + 0.6f; // 4.3 m

    // ------------------------------------------------------------------ source parts

    private struct GlbPart { public Mesh mesh; public int sub; public Matrix4x4 rel; public string mat; }

    private class GlbSource
    {
        public string name;
        public List<GlbPart> parts = new List<GlbPart>();
        public Bounds bounds;
        public bool ok;
    }

    private static GlbSource LoadGlb(string path)
    {
        var src = new GlbSource { name = path };
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) { Debug.LogWarning($"[azora] dressing: missing {path}"); return src; }
        var inv = go.transform.worldToLocalMatrix;
        bool first = true;
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
                src.parts.Add(new GlbPart { mesh = mesh, sub = s, rel = rel, mat = mname });
            }
            var b = mesh.bounds;
            // Transform the 8 corners into the root frame.
            for (int c = 0; c < 8; c++)
            {
                var p = rel.MultiplyPoint3x4(new Vector3(
                    (c & 1) == 0 ? b.min.x : b.max.x,
                    (c & 2) == 0 ? b.min.y : b.max.y,
                    (c & 4) == 0 ? b.min.z : b.max.z));
                if (first) { src.bounds = new Bounds(p, Vector3.zero); first = false; }
                else src.bounds.Encapsulate(p);
            }
        }
        src.ok = src.parts.Count > 0;
        return src;
    }

    private static GlbSource Glb(string dir, string name) => LoadGlb($"{dir}/{name}.glb");

    /// <summary>Per-tile accumulator: one CombineInstance list per Azora material.</summary>
    private class TileBatch
    {
        public readonly Dictionary<Material, List<CombineInstance>> lists =
            new Dictionary<Material, List<CombineInstance>>();
        public readonly Dictionary<Material, long> verts = new Dictionary<Material, long>();

        public void Add(GlbSource src, Matrix4x4 place, System.Func<string, Material> role)
        {
            if (src == null || !src.ok) return;
            foreach (var part in src.parts)
            {
                var mat = role(part.mat);
                if (mat == null) continue;
                if (!lists.TryGetValue(mat, out var l)) { l = new List<CombineInstance>(); lists[mat] = l; verts[mat] = 0; }
                l.Add(new CombineInstance { mesh = part.mesh, subMeshIndex = part.sub, transform = place * part.rel });
                verts[mat] += part.mesh.vertexCount;
            }
        }

        public int Flush(Transform group, string label, int tile)
        {
            int n = 0;
            foreach (var kv in lists)
            {
                if (kv.Value.Count == 0) continue;
                var mesh = new Mesh { name = $"Azora_{label}_{kv.Key.name}_{tile}" };
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(kv.Value.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                var go = AddMesh(group, $"{label} {kv.Key.name} {tile}", mesh, kv.Key, collider: false);
                n++;
            }
            lists.Clear(); verts.Clear();
            return n;
        }
    }

    private static Matrix4x4 PlaceOnGround(GlbSource src, Vector3 foot, float yawDeg, float targetHeight,
                                           float squash = 1f, float sink = 0.15f)
    {
        float h = Mathf.Max(0.01f, src.bounds.size.y);
        float s = targetHeight / h;
        var scale = new Vector3(s, s * squash, s);
        // Base of the model sits at foot, sunk slightly so no model floats on a slope.
        var lift = new Vector3(0f, -src.bounds.min.y * s * squash - sink, 0f);
        return Matrix4x4.TRS(foot + lift, Quaternion.Euler(0f, yawDeg, 0f), scale);
    }

    // ------------------------------------------------------------------ materials

    private static Material _needleA, _needleB, _needleC, _bark, _heather, _gorse, _shrubGreen,
                            _grassBlade, _rock, _houseWall, _houseRoof, _houseTrim, _houseGlass,
                            _houseBase, _wood, _signWhite, _signRed, _stone, _lanternGlow;

    private static void DressingMaterials()
    {
        _needleA = CelMaterial("Azora_Dress_NeedleA", new Color(0.16f, 0.33f, 0.22f), gloss: 0.06f, spec: 0.04f, rim: 0.12f);
        _needleB = CelMaterial("Azora_Dress_NeedleB", new Color(0.22f, 0.38f, 0.20f), gloss: 0.06f, spec: 0.04f, rim: 0.14f);
        _needleC = CelMaterial("Azora_Dress_NeedleC", new Color(0.13f, 0.27f, 0.23f), gloss: 0.06f, spec: 0.04f, rim: 0.10f);
        var barkTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoastTextureDir}/Shiosai_Bark_Albedo.png");
        _bark = CelMaterial("Azora_Dress_Bark", barkTex != null ? new Color(0.78f, 0.70f, 0.62f) : new Color(0.33f, 0.24f, 0.18f),
                            gloss: 0.05f, spec: 0.03f, rim: 0.10f, texture: barkTex, shade: GroundShade);
        _heather = CelMaterial("Azora_Dress_Heather", new Color(0.50f, 0.30f, 0.48f), gloss: 0.05f, spec: 0.03f, rim: 0.18f);
        _gorse = CelMaterial("Azora_Dress_Gorse", new Color(0.40f, 0.44f, 0.14f), gloss: 0.05f, spec: 0.03f, rim: 0.20f);
        _shrubGreen = CelMaterial("Azora_Dress_Shrub", new Color(0.25f, 0.42f, 0.19f), gloss: 0.05f, spec: 0.03f, rim: 0.16f);
        _grassBlade = CelMaterial("Azora_Dress_Grass", new Color(0.42f, 0.55f, 0.22f), gloss: 0.04f, spec: 0.02f, rim: 0.20f);
        var rockTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoastTextureDir}/Shiosai_Rock_Albedo.png");
        _rock = CelMaterial("Azora_Dress_Rock", new Color(0.70f, 0.69f, 0.64f), gloss: 0.08f, spec: 0.05f, rim: 0.20f,
                            texture: rockTex, shade: GroundShade);
        _houseWall = CelMaterial("Azora_Dress_HouseWall", new Color(0.86f, 0.82f, 0.72f), gloss: 0.08f, spec: 0.05f, rim: 0.2f, shade: GroundShade);
        _houseRoof = CelMaterial("Azora_Dress_HouseRoof", new Color(0.26f, 0.29f, 0.33f), gloss: 0.18f, spec: 0.10f, rim: 0.15f, shade: GroundShade);
        _houseTrim = CelMaterial("Azora_Dress_HouseTrim", new Color(0.36f, 0.25f, 0.17f), gloss: 0.06f, spec: 0.04f, rim: 0.12f, shade: GroundShade);
        _houseGlass = CelMaterial("Azora_Dress_HouseGlass", new Color(0.20f, 0.26f, 0.32f), gloss: 0.6f, spec: 0.4f, rim: 0.1f, shade: GroundShade);
        _houseBase = CelMaterial("Azora_Dress_HouseBase", new Color(0.52f, 0.52f, 0.50f), gloss: 0.06f, spec: 0.04f, rim: 0.1f, shade: GroundShade);
        _wood = CelMaterial("Azora_Dress_Wood", new Color(0.45f, 0.34f, 0.24f), gloss: 0.05f, spec: 0.03f, rim: 0.12f, shade: GroundShade);
        _signWhite = CelMaterial("Azora_Dress_SignWhite", new Color(0.92f, 0.92f, 0.88f), gloss: 0.2f, spec: 0.1f, rim: 0.1f, shade: GroundShade);
        _signRed = CelMaterial("Azora_Dress_Vermilion", new Color(0.78f, 0.20f, 0.12f), gloss: 0.15f, spec: 0.08f, rim: 0.15f);
        _stone = CelMaterial("Azora_Dress_Stone", new Color(0.62f, 0.61f, 0.57f), gloss: 0.06f, spec: 0.04f, rim: 0.12f,
                             texture: rockTex, shade: GroundShade);
        _lanternGlow = CelMaterial("Azora_Dress_LanternGlow", new Color(1.0f, 0.86f, 0.55f), gloss: 0.1f, spec: 0.05f, rim: 0.4f);
    }

    private static Material TreeRole(string m, Material needle)
    {
        string k = m.ToLowerInvariant();
        return (k.Contains("bark") || k.Contains("trunk")) ? _bark : needle;
    }

    private static Material HouseRole(string m)
    {
        string k = m.ToLowerInvariant();
        if (k.Contains("roof")) return _houseRoof;
        if (k.Contains("trim") || k.Contains("wood")) return _houseTrim;
        if (k.Contains("glass")) return _houseGlass;
        if (k.Contains("foundation") || k.Contains("concrete")) return _houseBase;
        return _houseWall;
    }

    private static Material StoneRole(string m)
    {
        string k = m.ToLowerInvariant();
        return k.Contains("glow") ? _lanternGlow : _stone;
    }

    private static Material SignRole(string m)
    {
        string k = m.ToLowerInvariant();
        if (k.Contains("post") || k.Contains("timber") || k.Contains("plinth")) return _wood;
        if (k.Contains("black") || k.Contains("ink")) return _houseRoof;
        if (k.Contains("yellow")) return _signRed;
        return _signWhite;
    }

    // ------------------------------------------------------------------ hashing

    private static float H01(float a, float b)
    {
        float x = Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f;
        x = Mathf.Abs(x) % 1f;
        return float.IsNaN(x) ? 0.5f : Mathf.Clamp(x, 0f, 0.9999f);
    }

    // ------------------------------------------------------------------ entry

    /// <summary>Replaces BuildCloudForest's cone trees and fills the moorland.</summary>
    private static void BuildHighlandDressing(Transform root, AzoraRoute route)
    {
        DressingMaterials();
        _snowCulled = 0; _snowSunk = 0;
        var group = new GameObject("Azora Highland Dressing").transform;
        group.SetParent(root, false);

        var pines = new[] { Glb(MinatoGlbDir, "Minato_SakuraPass_Pine_A"), Glb(MinatoGlbDir, "Minato_SakuraPass_Pine_B") };
        // claude-azora2: procedural 3D shrub clumps replace the Minato low-shrub CARD GLBs.
        var shrubs = ProcShrubs();
        var grass = Glb(SakuraGlbDir, "SakuraPass_Grass_Tuft");
        var rocks = new[] { Glb(SakuraGlbDir, "SakuraPass_Rock_Cluster_A"), Glb(SakuraGlbDir, "SakuraPass_Rock_Cluster_B") };
        var houses = new[] { Glb(SakuraGlbDir.Replace("SakuraPass", "ShiosaiCoast"), "Shiosai_House_A"),
                             Glb(SakuraGlbDir.Replace("SakuraPass", "ShiosaiCoast"), "Shiosai_House_B"),
                             Glb(SakuraGlbDir.Replace("SakuraPass", "ShiosaiCoast"), "Shiosai_House_C") };
        var lantern = Glb(SakuraGlbDir, "SakuraPass_Stone_Lantern");
        var chevron = Glb(SakuraGlbDir, "SakuraPass_Chevron_Sign");
        var summitSign = Glb(SakuraGlbDir, "SakuraPass_Summit_Sign");

        var needles = new[] { _needleA, _needleB, _needleC };
        System.Func<string, Material> shrubRoleH = _ => _heather;
        System.Func<string, Material> shrubRoleG = _ => _gorse;
        System.Func<string, Material> shrubRoleS = _ => _shrubGreen;
        System.Func<string, Material> grassRole = _ => _grassBlade;
        System.Func<string, Material> rockRole = _ => _rock;

        var batch = new TileBatch();
        var procV = new List<Vector3>(); var procUv = new List<Vector2>(); var procTri = new List<int>(); var procCol = new List<Color>();
        var redV = new List<Vector3>(); var redUv = new List<Vector2>(); var redTri = new List<int>(); var redCol = new List<Color>();
        int tile = 0, trees = 0, shrubN = 0, grassN = 0, rockN = 0, renderers = 0;
        float nextTile = DressTileM;

        void FlushProc()
        {
            if (procTri.Count > 0)
            {
                var m = Finish($"Azora_DressWood_{tile}", procV.ToArray(), procUv.ToArray(), procTri);
                AddMesh(group, $"Fences+Posts {tile}", m, _wood, collider: false); renderers++;
            }
            if (redTri.Count > 0)
            {
                var m = Finish($"Azora_DressRed_{tile}", redV.ToArray(), redUv.ToArray(), redTri);
                AddMesh(group, $"Shrine+Signs {tile}", m, _signRed, collider: false); renderers++;
            }
            procV.Clear(); procUv.Clear(); procTri.Clear(); procCol.Clear();
            redV.Clear(); redUv.Clear(); redTri.Clear(); redCol.Clear();
        }

        float treeTop = TreeLineY + 400f;
        for (float d = 12f; d < route.Length - 12f; d += 3f)
        {
            if (d >= nextTile)
            {
                renderers += batch.Flush(group, "Dress", tile);
                FlushProc();
                tile++; nextTile += DressTileM;
            }
            int i = route.IndexAt(d);
            float y = route.Position[i].y;
            float frac = route.Frac(i);
            float lush = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1250f, treeTop + 150f, y));
            lush = Mathf.Max(lush, 0.25f);

            // ---- grass tufts: 2 per 3 m on the verge band, both sides.
            for (int k = 0; k < 2; k++)
            {
                float r = H01(d, k + 1.3f);
                float off = (r > 0.5f ? 1f : -1f) * Mathf.Lerp(CorridorKeepOutM + 0.2f, 16f, H01(d, k + 7.7f) * H01(d, k + 3.1f));
                DFoot(route, i, off, out var f);
                if (Wet(f)) continue;
                float gh = Mathf.Lerp(0.35f, 0.8f, H01(d, k + 9.1f));
                // WP-G: only a sparse ~12% of tufts poke through deep snow (dried grass on the snowfield).
                if (!AboveSnow(f, gh, out float gSink, H01(d, k + 88.1f), 0.12f)) continue;
                batch.Add(grass, PlaceOnGround(grass, f, r * 720f, gh, 1f, 0.05f + gSink), grassRole);
                grassN++;
            }

            // ---- shrubs every 6 m: heather low/mid, gorse, green shrub.
            if (Mathf.Repeat(d, 6f) < 3f)
            {
                for (int k = 0; k < 2; k++)
                {
                    float r = H01(d + 0.5f, k + 21.3f);
                    float off = (r > 0.5f ? 1f : -1f) * Mathf.Lerp(CorridorKeepOutM + 3f, 55f, Mathf.Pow(H01(d, k + 31.7f), 1.6f));
                    DFoot(route, i, off, out var f);
                    if (Wet(f)) continue;
                    var src = shrubs[(int)(H01(d, k + 4.4f) * shrubs.Length) % shrubs.Length];
                    var role = ShrubRole(H01(d, k + 5.5f), H01(d, k + 8.8f));
                    float hgt = Mathf.Lerp(0.45f, 1.0f, H01(d, k + 6.6f)) * Mathf.Lerp(0.8f, 1.1f, lush);
                    if (!AboveSnow(f, hgt, out float snowSink)) continue;   // WP-G: buried under the pack
                    batch.Add(src, PlaceOnGround(src, f, r * 900f, hgt, 1f, 0.08f + snowSink), role);
                    shrubN++;
                }
            }

            // ---- pine clumps: a clump centre every ~45 m per side.
            if (y < treeTop && !InVillage(d) && Mathf.Repeat(d, 30f) < 3f)
            {
                float dens = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TreeLineY - 200f, treeTop, y));
                for (int side = -1; side <= 1; side += 2)
                {
                    if (H01(d, side * 3.3f) > dens * 0.95f + 0.05f) continue;
                    float centreOff = side * Mathf.Lerp(11f, 95f, Mathf.Pow(Mathf.Abs(H01(d, side * 5.1f)), 1.3f));
                    float along = (H01(d, side * 8.2f) - 0.5f) * 20f;
                    int n = 3 + (int)(H01(d, side * 9.9f) * Mathf.Lerp(2f, 8f, dens));
                    for (int t = 0; t < n; t++)
                    {
                        float ang = H01(d + t, side * 11.1f) * Mathf.PI * 2f;
                        float rad = Mathf.Sqrt(H01(d + t, side * 12.7f)) * Mathf.Lerp(5f, 14f, dens);
                        float off = centreOff + Mathf.Cos(ang) * rad;
                        if (Mathf.Abs(off) < 9f) off = side * (9f + H01(d + t, 2.2f) * 4f);
                        int j = route.IndexAt(Mathf.Clamp(d + along + Mathf.Sin(ang) * rad, 12f, route.Length - 12f));
                        DFoot(route, j, off, out var f);
                        if (Wet(f, 2f)) continue;   // crown radius
                        var src = pines[(int)(H01(d + t, 13.3f) * 2f) & 1];
                        var needle = needles[(int)(H01(d + t, 14.4f) * 3f) % 3];
                        float hgt = Mathf.Lerp(6f, 17f, H01(d + t, 15.5f)) * Mathf.Lerp(0.55f, 1f, dens);
                        batch.Add(src, PlaceOnGround(src, f, H01(d + t, 16.6f) * 360f, hgt, Mathf.Lerp(0.9f, 1.15f, H01(d + t, 17.7f)), 0.3f),
                                  m => TreeRole(m, needle));
                        trees++;
                        // A shrub skirt at the clump edge.
                        if (t % 2 == 0)
                        {
                            var sf = f + new Vector3(Mathf.Cos(ang + 1f), 0f, Mathf.Sin(ang + 1f)) * 3f;
                            sf.y = GH(sf.x, sf.z);
                            if (Wet(sf) || !AboveSnow(sf, 1.1f, out float skirtSink)) continue;
                            var ss = shrubs[t % shrubs.Length];
                            batch.Add(ss, PlaceOnGround(ss, sf, ang * 57f, 1.1f, 1f, 0.08f + skirtSink), ShrubRole(H01(d + t, 18.8f), 1f));
                        }
                    }
                }
            }

            // ---- rock outcrops every ~33 m: a group of 1-4 clusters.
            if (!InVillage(d) && Mathf.Repeat(d, 33f) < 3f)
            {
                float side = H01(d, 41.1f) > 0.5f ? 1f : -1f;
                float centreOff = side * Mathf.Lerp(7f, 120f, Mathf.Pow(H01(d, 42.2f), 1.5f));
                int n = 1 + (int)(H01(d, 43.3f) * 4f);
                float rockScale = Mathf.Lerp(1f, 1.8f, 1f - lush); // bigger up high
                for (int t = 0; t < n; t++)
                {
                    float off = centreOff + (H01(d + t, 44.4f) - 0.5f) * 8f;
                    if (Mathf.Abs(off) < 6.5f) off = side * 6.5f;
                    int j = route.IndexAt(Mathf.Clamp(d + (H01(d + t, 45.5f) - 0.5f) * 10f, 12f, route.Length - 12f));
                    DFoot(route, j, off, out var f);
                    var src = rocks[t & 1];
                    float hgt = Mathf.Lerp(0.7f, 2.6f, H01(d + t, 46.6f)) * rockScale * (t == 0 ? 1.4f : 1f);
                    if (Wet(f, hgt)) continue;
                    batch.Add(src, PlaceOnGround(src, f, H01(d + t, 47.7f) * 360f, hgt, 1f, hgt * 0.2f), rockRole);
                    rockN++;
                }
            }

            // ---- post-and-rail fence where the dry-stone walls are absent (below the col).
            // WP-G: not on the cliff road (RidgeFromM-RidgeToM) - it has a guardrail on the drop side and
            // the rock cut on the other, and a farm fence between the road and a cliff read as a mistake.
            bool cliffRoad = d >= RidgeFromM && d < RidgeToM;
            if (frac < 0.82f && !InVillage(d) && !cliffRoad && WallPresence(frac, route.Distance[i]) <= 0f && Mathf.Repeat(d, 3f) < 3f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    if (Mathf.PerlinNoise(d * 0.004f + side * 5f, 3.3f) < 0.42f) continue; // gaps and gates
                    float off = side * (RoadHalfWidth + ShoulderWidth + 1.6f);
                    DFoot(route, i, off, out var f);
                    int j2 = route.IndexAt(Mathf.Min(d + 3f, route.Length - 12f));
                    DFoot(route, j2, off, out var f2);
                    if (Wet(f) || Wet(f2)) continue;
                    var fw = f2 - f; fw.y = 0f; if (fw.sqrMagnitude < 0.01f) continue;
                    var dir = fw.normalized; var rt = new Vector3(dir.z, 0f, -dir.x);
                    BoxO(procV, procUv, procTri, procCol, f - Vector3.up * 0.2f, rt * 0.06f, dir * 0.06f, 1.35f);
                    // two rails spanning to the next post
                    var mid = (f + f2) * 0.5f;
                    float len = (f2 - f).magnitude * 0.5f;
                    var along3 = (f2 - f).normalized;
                    var rt3 = Vector3.Cross(Vector3.up, along3).normalized;
                    BoxO(procV, procUv, procTri, procCol, mid + Vector3.up * 0.55f, rt3 * 0.035f, along3 * len, 0.09f, true);
                    BoxO(procV, procUv, procTri, procCol, mid + Vector3.up * 1.0f, rt3 * 0.035f, along3 * len, 0.09f, true);
                }
            }

            // ---- km posts every 1000 m (white post with red band), right side.
            if (Mathf.Repeat(d, 1000f) < 3f && d > 100f)
            {
                DFoot(route, i, RoadHalfWidth + ShoulderWidth + 0.9f, out var f);
                var t = route.SideFlat(i); var dir = new Vector3(-t.z, 0f, t.x);
                if (!Wet(f))
                BoxO(procV, procUv, procTri, procCol, f - Vector3.up * 0.2f, t * 0.09f, dir * 0.09f, 1.5f);
                if (!Wet(f))
                BoxO(redV, redUv, redTri, redCol, f + Vector3.up * 1.05f, t * 0.1f, dir * 0.1f, 0.25f);
            }

            // ---- chevron signs on bends.
            if (Mathf.Repeat(d, 60f) < 3f && i > 2 && i < route.Count - 3 && chevron.ok)
            {
                var a = route.SideFlat(Mathf.Max(0, i - 3)); var b = route.SideFlat(Mathf.Min(route.Count - 1, i + 3));
                float turn = Vector3.SignedAngle(a, b, Vector3.up);
                float span = route.Distance[Mathf.Min(route.Count - 1, i + 3)] - route.Distance[Mathf.Max(0, i - 3)];
                if (span > 0.1f && Mathf.Abs(turn) / span > 0.35f)
                {
                    float outer = turn > 0f ? -1f : 1f;
                    DFoot(route, i, outer * (RoadHalfWidth + ShoulderWidth + 1.2f), out var f);
                    if (Wet(f)) continue;
                    var s = route.SideFlat(i);
                    float yaw = Mathf.Atan2(-s.x * outer, -s.z * outer) * Mathf.Rad2Deg;
                    batch.Add(chevron, PlaceOnGround(chevron, f, yaw, 2.1f, 1f, 0.1f), SignRole);
                }
            }
        }
        renderers += batch.Flush(group, "Dress", tile);
        FlushProc();

        // ---- set pieces: farmsteads, huts, shrine, rest stop.
        var setGroup = new GameObject("Azora Farmsteads + Shrine").transform;
        setGroup.SetParent(group, false);
        var set = new TileBatch();
        var sv = new List<Vector3>(); var suv = new List<Vector2>(); var stri = new List<int>(); var scol = new List<Color>();
        var wv = new List<Vector3>(); var wuv = new List<Vector2>(); var wtri = new List<int>(); var wcol = new List<Color>();
        float[] farmFracs = { 0.03f, 0.09f, 0.16f, 0.22f, 0.30f, 0.37f, 0.45f, 0.60f, 0.70f, 0.93f };
        int farms = 0;
        foreach (float ff in farmFracs)
        {
            float d = route.Length * ff;
            int i = route.IndexAt(d);
            float side = H01(ff, 1.1f) > 0.5f ? 1f : -1f;
            float off = side * Mathf.Lerp(22f, 45f, H01(ff, 2.2f));
            DFoot(route, i, off, out var f);
            if (Wet(f, 18f)) { farms++; continue; }   // house + shed + windbreak footprint
            var s = route.SideFlat(i);
            float yaw = Mathf.Atan2(-s.x * side, -s.z * side) * Mathf.Rad2Deg; // face the road
            var src = houses[farms % 3];
            set.Add(src, PlaceOnGround(src, f, yaw, Mathf.Lerp(5.5f, 7.5f, H01(ff, 3.3f)), 1f, 0.3f), HouseRole);
            // a barn/shed box beside it
            var tdir = new Vector3(-s.z, 0f, s.x);
            var shed = f + tdir * 12f + new Vector3(s.x, 0f, s.z) * side * 4f;
            shed.y = GH(shed.x, shed.z) - 0.2f;
            BoxO(wv, wuv, wtri, wcol, shed, new Vector3(s.x, 0, s.z) * 2.2f, tdir * 3.2f, 3.0f);
            // stacked hay/wood pile + a pine windbreak behind the house
            for (int t = 0; t < 5; t++)
            {
                var tp = f + new Vector3(s.x, 0f, s.z) * side * (14f + t % 2 * 3f) + tdir * (t - 2) * 5f;
                tp.y = GH(tp.x, tp.z);
                if (Wet(tp)) continue;
                var pz = pines[t & 1];
                set.Add(pz, PlaceOnGround(pz, tp, t * 71f, 11f + t, 1f, 0.3f), m => TreeRole(m, _needleC));
            }
            // a short farm lane of fence posts from the house to the road
            for (float a = 6f; a < Mathf.Abs(off) - 3f; a += 3f)
            {
                DFoot(route, i, side * a, out var fp);
                fp += tdir * 3.5f; fp.y = GH(fp.x, fp.z);
                BoxO(wv, wuv, wtri, wcol, fp - Vector3.up * 0.2f, tdir * 0.06f, new Vector3(s.x, 0, s.z) * 0.06f, 1.3f);
            }
            farms++;
        }

        // Shrine at the switchbacks checkpoint and a rest stop at the false flat.
        foreach (var (at, isShrine) in new[] { (CpSwitchbacks - 120f, true), (CpFalseFlat + 150f, false), (CpCol - 200f, true) })
        {
            if (at <= 0f || at >= route.Length) continue;
            int i = route.IndexAt(at);
            float side = isShrine ? 1f : -1f;
            var s = route.SideFlat(i);
            var sideV = new Vector3(s.x, 0f, s.z) * side;
            var tdir = new Vector3(-s.z, 0f, s.x);
            DFoot(route, i, side * 11f, out var f);
            if (isShrine)
            {
                // Torii: two vermilion pillars + kasagi + nuki, facing the road.
                for (int p = -1; p <= 1; p += 2)
                {
                    var pp = f + tdir * p * 1.6f; pp.y = GH(pp.x, pp.z) - 0.2f;
                    BoxO(redV, redUv, redTri, redCol, pp, tdir * 0.16f, sideV * 0.16f, 3.6f);
                }
                var top = f; top.y = GH(f.x, f.z);
                BoxO(redV, redUv, redTri, redCol, top + Vector3.up * 3.55f, tdir * 2.4f, sideV * 0.2f, 0.22f);
                BoxO(redV, redUv, redTri, redCol, top + Vector3.up * 2.9f, tdir * 1.9f, sideV * 0.1f, 0.14f);
                // Stone lanterns flanking the approach, and a small hokora behind.
                for (int p = -1; p <= 1; p += 2)
                {
                    var lp = f - sideV * 2.5f + tdir * p * 2.6f; lp.y = GH(lp.x, lp.z);
                    set.Add(lantern, PlaceOnGround(lantern, lp, 0f, 1.8f, 1f, 0.05f), StoneRole);
                }
                var hk = f + sideV * 5f; hk.y = GH(hk.x, hk.z) - 0.2f;
                BoxO(wv, wuv, wtri, wcol, hk, tdir * 1.1f, sideV * 0.9f, 1.8f);
                BoxO(sv, suv, stri, scol, hk + Vector3.up * 1.8f, tdir * 1.5f, sideV * 1.3f, 0.25f);
                for (int t = 0; t < 7; t++)
                {
                    var tp = f + sideV * (9f + t % 3 * 3f) + tdir * (t - 3) * 4f; tp.y = GH(tp.x, tp.z);
                    if (Wet(tp)) continue;
                    set.Add(pines[t & 1], PlaceOnGround(pines[t & 1], tp, t * 53f, 13f + t, 1f, 0.3f), m => TreeRole(m, _needleA));
                }
            }
            else
            {
                // Rest stop: gravel pad, two benches, a sign board, a small shelter.
                var pad = f; pad.y = GH(f.x, f.z) - 0.25f;
                BoxO(sv, suv, stri, scol, pad, tdir * 9f, sideV * 5f, 0.3f);
                for (int p = -1; p <= 1; p += 2)
                {
                    var bp = f + tdir * p * 4f + sideV * 1.5f; bp.y = GH(bp.x, bp.z);
                    BoxO(wv, wuv, wtri, wcol, bp + Vector3.up * 0.42f, tdir * 0.9f, sideV * 0.22f, 0.07f);
                    BoxO(wv, wuv, wtri, wcol, bp, tdir * 0.08f, sideV * 0.18f, 0.42f);
                }
                var sh = f + sideV * 4.5f; sh.y = GH(sh.x, sh.z) - 0.1f;
                for (int p = -1; p <= 1; p += 2)
                    BoxO(wv, wuv, wtri, wcol, sh + tdir * p * 2.2f, tdir * 0.12f, sideV * 0.12f, 2.6f);
                BoxO(wv, wuv, wtri, wcol, sh - sideV * 1.4f + tdir * 0f, tdir * 2.4f, sideV * 0.1f, 2.4f);
                BoxO(sv, suv, stri, scol, sh + Vector3.up * 2.6f, tdir * 2.8f, sideV * 1.8f, 0.2f);
                if (summitSign.ok)
                {
                    var sp = f - sideV * 3.5f + tdir * 6f; sp.y = GH(sp.x, sp.z);
                    set.Add(summitSign, PlaceOnGround(summitSign, sp, Mathf.Atan2(-sideV.x, -sideV.z) * Mathf.Rad2Deg, 2.4f, 1f, 0.05f), SignRole);
                }
            }
        }

        renderers += set.Flush(setGroup, "Set", 0);
        if (wtri.Count > 0) { AddMesh(setGroup, "Set Timber", Finish("Azora_Set_Timber", wv.ToArray(), wuv.ToArray(), wtri), _houseTrim, collider: false); renderers++; }
        if (stri.Count > 0) { AddMesh(setGroup, "Set Slate", Finish("Azora_Set_Slate", sv.ToArray(), suv.ToArray(), stri), _houseRoof, collider: false); renderers++; }
        if (redTri.Count > 0) { AddMesh(setGroup, "Set Vermilion", Finish("Azora_Set_Vermilion", redV.ToArray(), redUv.ToArray(), redTri), _signRed, collider: false); renderers++; }

        BuildAlpineUpper(group, route, rocks, grass);

        Debug.Log($"[azora] dressing: {trees} pines, {shrubN} shrubs, {grassN} grass tufts, {rockN} rocks, " +
                  $"{farms} farmsteads, {tile + 1} tiles, {renderers} renderers; " +
                  $"snowpack (dressing+alpine): {_snowCulled} low plants buried, {_snowSunk} seated in the pack.");
    }
}

public static partial class AzoraHighlandsEnvironment
{
    private static int _dfootWarn;

    /// <summary>Foot() with a guard: bad offsets/indices are logged (first 5) and clamped, never thrown.</summary>
    private static void DFoot(AzoraRoute r, int i, float offset, out Vector3 foot)
    {
        i = Mathf.Clamp(i, 0, r.Count - 1);
        if (float.IsNaN(offset) || float.IsInfinity(offset))
        {
            if (_dfootWarn++ < 5) Debug.LogWarning($"[azora] dressing: bad offset {offset} at sample {i}");
            offset = 10f;
        }
        try { Foot(r, i, offset, out foot, out _); }
        catch (System.Exception e)
        {
            var p = r.Position[i]; var s = r.SideFlat(i);
            if (_dfootWarn++ < 5) Debug.LogWarning($"[azora] dressing: Foot threw at sample {i} off {offset} p {p}: {e.GetType().Name}");
            foot = p + s * offset;
        }
        if (float.IsNaN(foot.y)) foot.y = r.Position[i].y;
    }
}

public static partial class AzoraHighlandsEnvironment
{
    private static float GH(float x, float z)
    {
        if (float.IsNaN(x) || float.IsNaN(z)) { if (_dfootWarn++ < 5) Debug.LogWarning("[azora] dressing: NaN ground query"); return 0f; }
        try { return Height(x, z); }
        catch (System.Exception) { if (_dfootWarn++ < 5) Debug.LogWarning($"[azora] dressing: Height threw at {x},{z}"); return 0f; }
    }
}
