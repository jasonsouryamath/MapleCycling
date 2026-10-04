// NB9 (COORDINATION.md "PRIORITY 1: Nagisa Bay overhaul"): tropical flora + ground.
//
// Fixes the two open issues (flat verge grass, bare slopes):
//   * jungle clumps on every slope near the ride (banana / monstera / fern / pandanus, 3 clump variants,
//     GLBs from tools/blender/build_nagisa_flora2_models.py), fan-palm clusters mixed in;
//   * coconut palms leaning out over the sand, royal palms at the back of the promenade, dune grass along the
//     sand edge, frangipani + hibiscus + bougainvillea garden infill inland of the highway frontage, coral
//     outcrops on rocky shore;
//   * lush ground tufts and creeping ground cover on the flat verge land of the non-coastal sections;
//   * Nagisa_Ground.mat re-tuned (tropical lawn albedo/normal/rough in the grass slot, more colour variation).
// Sway: every card material is MapleRide/HDRP/Foliage (height-weighted wind in the vertex stage; trunks stay planted).
//
// SIDE RULE (HARD): nothing is placed in the highway band (inland 0..46 m on the coastal spans), nothing inside
// CorridorKeepOutM + margin of the ride road, nothing on pads / bridges, and every spot is checked against the
// same occupancy map NB4 uses (props, people, big building footprints), so nothing grows through another package.
// Other packages' files are never edited; objects live in the stage group "NB Flora" (rebuilt by name).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- PROVISIONAL tuning (NB9)
    private const int Nb9JungleCap = 2600, Nb9LeanPalmCap = 170, Nb9RoyalCap = 190, Nb9DuneCap = 900;
    private const int Nb9TuftCap = 1100, Nb9GardenCap = 420, Nb9CoralCap = 40;
    private const float Nb9AlongM = 9f, Nb9AcrossM = 9.5f, Nb9MaxOffM = 420f;
    private const float Nb9InlandBandM = 46f;          // highway band + sound wall + palm buffer (NB3), coastal spans only
    private const float Nb9SeaReserveM = 14f;          // promenade + beach-life starts here (sea side), coastal spans only
    private const float Nb9JungleMinSlopeDeg = 9f, Nb9CoastalMinSlopeDeg = 13f, Nb9MaxSlopeDeg = 46f;
    private const string Nb9GroupName = "NB Flora";

    private static readonly Vector2[] Nb9Spans = { new Vector2(0f, 4720f), new Vector2(12167f, 14991f) };
    private static bool Nb9Coastal(float d) { foreach (var s in Nb9Spans) if (d >= s.x && d <= s.y) return true; return false; }

    // ---------------------------------------------------------------- route hash (fast nearest-within-30 m)
    private static Dictionary<long, List<int>> _nb9Hash;
    private static float[] _nb9Sea;
    private const float Nb9HashCell = 30f;
    private static long Nb9K(int x, int z) => ((long)x << 32) ^ (uint)z;

    private static void Nb9BuildHash()
    {
        _nb9Hash = new Dictionary<long, List<int>>();
        _nb9Sea = new float[_route.Count];
        for (int i = 0; i < _route.Count; i++)
        {
            var p = _route.Position[i];
            long k = Nb9K(Mathf.FloorToInt(p.x / Nb9HashCell), Mathf.FloorToInt(p.z / Nb9HashCell));
            if (!_nb9Hash.TryGetValue(k, out var l)) _nb9Hash[k] = l = new List<int>();
            l.Add(i);
            _nb9Sea[i] = 0f;
        }
    }

    private static float Nb9Sea(int i)
    {
        if (_nb9Sea[i] == 0f) _nb9Sea[i] = SeaSign(i);
        return _nb9Sea[i];
    }

    /// <summary>Distance to the nearest route sample, exact up to ~30 m; 999 when farther than the hash reach.</summary>
    private static float Nb9RouteDist(float x, float z)
    {
        int cx = Mathf.FloorToInt(x / Nb9HashCell), cz = Mathf.FloorToInt(z / Nb9HashCell);
        float best = 999f * 999f;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (_nb9Hash.TryGetValue(Nb9K(cx + dx, cz + dz), out var l))
                    foreach (int i in l)
                    {
                        var p = _route.Position[i];
                        float ex = p.x - x, ez = p.z - z, d = ex * ex + ez * ez;
                        if (d < best) best = d;
                    }
        return Mathf.Sqrt(best);
    }

    private static float Nb9SlopeDeg(float x, float z)
    {
        const float s = 2.5f;
        float gx = (_ground.Height(x + s, z) - _ground.Height(x - s, z)) / (2f * s);
        float gz = (_ground.Height(x, z + s) - _ground.Height(x, z - s)) / (2f * s);
        return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
    }

    // ---------------------------------------------------------------- materials
    private static void Nb9Materials()
    {
        var white = Color.white;
        _nb["NB9_Monstera"] = NbFoliage("NB9_Monstera", "Flora2/NB9_Monstera", white, 0.22f);
        _nb["NB9_Banana"] = NbFoliage("NB9_Banana", "Flora2/NB9_Banana", white, 0.34f);
        _nb["NB9_Fern"] = NbFoliage("NB9_Fern", "Flora2/NB9_Fern", white, 0.20f);
        _nb["NB9_Pandanus"] = NbFoliage("NB9_Pandanus", "Flora2/NB9_Pandanus", white, 0.26f);
        _nb["NB9_Frangipani"] = NbFoliage("NB9_Frangipani", "Flora2/NB9_Frangipani", white, 0.16f);
        _nb["NB9_DuneGrass"] = NbFoliage("NB9_DuneGrass", "Flora2/NB9_DuneGrass", white, 0.30f);
        _nb["NB9_GroundGrass"] = NbFoliage("NB9_GroundGrass", "Flora2/NB9_GroundGrass", white, 0.22f);
        _nb["NB9_GroundLeafy"] = NbFoliage("NB9_GroundLeafy", "Flora2/NB9_GroundLeafy", white, 0.06f);
        _nb["NB9_BananaStem"] = NbCel("NB9_BananaStem", "Flora2/NB9_BananaStem", white, 0.08f, 0.04f, 0.10f, 1.0f);
        _nb["NB9_RoyalTrunk"] = NbCel("NB9_RoyalTrunk", "Flora2/NB9_RoyalTrunk", white, 0.18f, 0.10f, 0.10f, 1.0f);
        _nb["NB9_CoralRock"] = NbCel("NB9_CoralRock", "Flora2/NB9_CoralRock", white, 0.10f, 0.05f, 0.10f, 1.0f);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Nagisa_Ground.mat tuning only: tropical lawn in the grass slot + richer colour variation.</summary>
    private static void Nb9TuneGround()
    {
        var path = $"{Dir}/Materials/Nagisa_Ground.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { Debug.LogWarning("[nb9] Nagisa_Ground.mat missing - ground not tuned."); return; }
        var a = NbTex("Flora2/NB9_TropicalLawn", "Albedo");
        var n = NbTex("Flora2/NB9_TropicalLawn", "Normal");
        var r = NbTex("Flora2/NB9_TropicalLawn", "Rough");
        if (a != null) mat.SetTexture("_GrassTex", a);
        if (n != null) mat.SetTexture("_GrassNormal", n);
        if (r != null) mat.SetTexture("_GrassRough", r);
        mat.SetColor("_GrassColor", new Color(0.36f, 0.48f, 0.20f, 1f));     // richer tropical green (was 0.25/0.37/0.17)
        mat.SetFloat("_GrassScale", 5.0f);
        mat.SetFloat("_MacroVariation", 0.55f);
        mat.SetFloat("_MesoVariation", 0.38f);
        mat.SetFloat("_MesoHue", 0.45f);
        mat.SetFloat("_MossStrength", 0.22f);
        EditorUtility.SetDirty(mat);
        Debug.Log($"[nb9] Nagisa_Ground.mat tuned (lawn albedo {(a != null)}, normal {(n != null)}, rough {(r != null)}).");
    }

    // ---------------------------------------------------------------- occupancy extras
    private static void Nb9SeedBuildings(Nb4Occ occ, Transform root, Transform mine, out int footprints)
    {
        footprints = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (r.transform.IsChildOf(mine)) continue;
            var b = r.bounds;
            float sx = b.size.x, sz = b.size.z;
            if (b.size.y < 2.2f || Mathf.Max(sx, sz) < 7f || Mathf.Max(sx, sz) > 90f) continue;   // buildings, not ribbons / ground
            string n = r.name;
            if (n.Contains("Ground") || n.Contains("Terrain") || n.Contains("Ocean") || n.Contains("Road") || n.Contains("asphalt")) continue;
            footprints++;
            for (float x = b.min.x + 1.5f; x <= b.max.x - 1.5f + 0.01f; x += 4.5f)
                for (float z = b.min.z + 1.5f; z <= b.max.z - 1.5f + 0.01f; z += 4.5f)
                    occ.Add(false, new Vector3(x, b.center.y, z), 3.2f);
        }
    }

    // ---------------------------------------------------------------- stage
    private static int _nb9Count;
    private static readonly Dictionary<string, int> _nb9Stats = new Dictionary<string, int>();

    private static bool Nb9Put(Nb4Occ occ, Transform parent, string stem, Vector3 p, float yaw, float scale, float cull, float r, string stat)
    {
        var go = PlaceWorld(stem, parent, p, yaw, scale, cull);
        if (go == null) return false;
        occ.Add(false, p, r);
        _nb9Count++;
        _nb9Stats[stat] = (_nb9Stats.TryGetValue(stat, out var c) ? c : 0) + 1;
        return true;
    }

    private static Transform Nb9Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    [NagisaStage(96, "Flora")]
    private static void BuildNb9Flora(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();
        Nb9Materials();
        Nb9TuneGround();
        Nb9BuildHash();
        _nb9Count = 0; _nb9Stats.Clear();
        Physics.SyncTransforms();

        var root = group.parent != null && group.parent.parent != null ? group.parent.parent : group.root;
        var occ = Nb4SeedOcc(root, group);
        Nb9SeedBuildings(occ, root, group, out int footprints);
        var rng = new System.Random(9909);

        var jungleT = Nb9Child(group, "Jungle slopes");
        var palmT = Nb9Child(group, "Palms");
        var duneT = Nb9Child(group, "Dune grass");
        var groundT = Nb9Child(group, "Ground cover");
        var gardenT = Nb9Child(group, "Garden infill");
        var coralT = Nb9Child(group, "Coral outcrops");

        // ---- lattice scan along the route: jungle, tufts, garden, coral ------------------------------------
        var jungle = new List<(Vector3 p, float w)>();
        var tufts = new List<Vector3>();
        var garden = new List<Vector3>();
        var coral = new List<Vector3>();
        float lastD = -1e9f;
        int scanned = 0;
        for (int i = 0; i < _route.Count; i++)
        {
            float dist = _route.Distance[i];
            if (dist - lastD < Nb9AlongM) continue;
            lastD = dist;
            if (_route.OnBridge(i)) continue;
            var side = _route.SideFlat(i);
            float sea = Nb9Sea(i);
            bool coastal = Nb9Coastal(dist);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                // off > 0 = seaward on coastal spans (sd = side * sea); elsewhere the sign is just a side
                float inlandSign = coastal ? sea * sgn * -1f : 1f;      // +1 when this sgn side is inland on a coastal span
                for (float off = 11f + (float)rng.NextDouble() * Nb9AcrossM; off < Nb9MaxOffM; off += Nb9AcrossM * (0.8f + (float)rng.NextDouble() * 0.5f))
                {
                    float jx = (float)(rng.NextDouble() - 0.5) * Nb9AlongM * 0.8f;
                    var tan = _route.Tangent[i]; tan.y = 0f; tan.Normalize();
                    var q = _route.Position[i] + side * (sgn * off) + tan * jx;
                    scanned++;
                    if (coastal)
                    {
                        bool inland = inlandSign > 0f;
                        if (inland && off < Nb9InlandBandM) continue;
                        if (!inland && off < Nb9SeaReserveM) continue;
                    }
                    float gh = _ground.Height(q.x, q.z);
                    if (gh < 1.0f) continue;
                    float coastM = _ground.Coast(q.x, q.z);
                    float rd = Nb9RouteDist(q.x, q.z);
                    if (rd < CorridorKeepOutM + 2.5f) continue;
                    if (InAnyPad(q.x, q.z, 6f)) continue;
                    float slope = Nb9SlopeDeg(q.x, q.z);
                    q.y = gh;

                    if (coastM >= 0.8f && coastM <= 9f && slope >= 5f && coral.Count < Nb9CoralCap * 6 && rng.NextDouble() < 0.5)
                    { coral.Add(q); continue; }
                    if (coastM < 18f) continue;
                    float minSlope = coastal ? Nb9CoastalMinSlopeDeg : Nb9JungleMinSlopeDeg;
                    if (slope >= minSlope && slope <= Nb9MaxSlopeDeg)
                    {
                        // density falls with distance from the road (the rider sees the near slopes)
                        float keep = off < 120f ? 0.92f : Mathf.Lerp(0.55f, 0.12f, Mathf.InverseLerp(120f, Nb9MaxOffM, off));
                        if (rng.NextDouble() < keep) jungle.Add((q, off));
                    }
                    else if (slope < 12f)
                    {
                        if (!coastal && off < 26f && rd >= CorridorKeepOutM + 1.5f && rng.NextDouble() < 0.7) tufts.Add(q);
                        else if (coastal && inlandSign > 0f && off > Nb9InlandBandM + 4f && off < 150f && rng.NextDouble() < 0.18) garden.Add(q);
                    }
                }
            }
        }
        Nb9Shuffle(jungle, rng); Nb9Shuffle(tufts, rng); Nb9Shuffle(garden, rng); Nb9Shuffle(coral, rng);

        // ---- jungle clumps -------------------------------------------------------------------------------
        int jn = 0;
        foreach (var (p, w) in jungle)
        {
            if (jn >= Nb9JungleCap) break;
            if (!occ.Free(p, 0f, 2.6f)) continue;
            double h = rng.NextDouble();
            string stem = h < 0.36 ? "Nagisa_NB9_JungleA" : h < 0.70 ? "Nagisa_NB9_JungleB" : h < 0.94 ? "Nagisa_NB9_JungleC" : "Nagisa_NB9_FanCluster";
            if (Nb9Put(occ, jungleT, stem, p, (float)rng.NextDouble() * 360f, 0.85f + (float)rng.NextDouble() * 0.5f,
                       stem.EndsWith("FanCluster") ? 0.004f : 0.006f, 2.6f, "jungle")) jn++;
        }

        // ---- ground tufts / creeping cover (flat verge land) -----------------------------------------------
        int tn = 0;
        foreach (var p in tufts)
        {
            if (tn >= Nb9TuftCap) break;
            if (!occ.Free(p, 0f, 1.2f)) continue;
            string stem = rng.NextDouble() < 0.62 ? "Nagisa_NB9_GroundTuft" : "Nagisa_NB9_GroundLeafy";
            if (Nb9Put(occ, groundT, stem, p, (float)rng.NextDouble() * 360f, 0.9f + (float)rng.NextDouble() * 0.6f, 0.012f, 0.9f, "ground")) tn++;
        }

        // ---- garden infill inland of the highway frontage ---------------------------------------------------
        int gn = 0;
        var gardenStems = new[] { "Nagisa_NB9_Frangipani", "Nagisa_Hibiscus", "Nagisa_Bougainvillea", "Nagisa_NB9_FanCluster", "Nagisa_Hibiscus" };
        foreach (var p in garden)
        {
            if (gn >= Nb9GardenCap) break;
            if (!occ.Free(p, 0f, 4.5f)) continue;
            string stem = gardenStems[rng.Next(gardenStems.Length)];
            if (Nb9Put(occ, gardenT, stem, p, (float)rng.NextDouble() * 360f, 0.9f + (float)rng.NextDouble() * 0.35f, 0.006f, 2.2f, "garden")) gn++;
        }

        // ---- coral outcrops on rocky shore -----------------------------------------------------------------------
        int cn = 0;
        foreach (var p in coral)
        {
            if (cn >= Nb9CoralCap) break;
            if (!occ.Free(p, 0f, 4f)) continue;
            if (Nb9Put(occ, coralT, "Nagisa_NB9_CoralOutcrop", p, (float)rng.NextDouble() * 360f, 0.8f + (float)rng.NextDouble() * 1.1f, 0.004f, 2.5f, "coral")) cn++;
        }

        // ---- beach strip: lean coconuts, royal palms, dune grass (sea side of the coastal spans only) --------------
        Nb9BeachStrip(occ, palmT, duneT, rng);

        var parts = new List<string>();
        foreach (var kv in _nb9Stats) parts.Add($"{kv.Value} {kv.Key}");
        Debug.Log($"[nb9] flora: {_nb9Count} instances ({string.Join(", ", parts)}); scanned {scanned} lattice points, {footprints} building footprints in occupancy; " +
                  "side rule: highway band, corridor, pads, bridges and every existing prop/person kept clear.");
    }

    private static void Nb9Shuffle<T>(List<T> l, System.Random rng)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
    }

    private static void Nb9BeachStrip(Nb4Occ occ, Transform palmT, Transform duneT, System.Random rng)
    {
        int lean = 0, royal = 0, dune = 0;
        float lastLean = -999f, lastRoyal = -999f, lastDune = -999f;
        for (int i = 0; i < _route.Count; i++)
        {
            float dist = _route.Distance[i];
            if (!Nb9Coastal(dist) || _route.OnBridge(i)) continue;
            var sd = _route.SideFlat(i) * Nb9Sea(i);

            // royal palms at the back of the promenade
            if (dist - lastRoyal >= 44f && royal < Nb9RoyalCap)
            {
                var p = _route.Position[i] + sd * 11.9f;
                if (Nb9DryClear(p, 3f) && occ.Free(p, 0f, 2.6f))
                {
                    p.y = _ground.Height(p.x, p.z);
                    if (Nb9Put(occ, palmT, "Nagisa_NB9_RoyalPalm", p, (float)rng.NextDouble() * 360f, 0.92f + (float)rng.NextDouble() * 0.2f, 0.0018f, 1.6f, "royal"))
                    { royal++; lastRoyal = dist; }
                }
            }

            // dune grass along the sand edge
            if (dist - lastDune >= 5f && dune < Nb9DuneCap && rng.NextDouble() < 0.72)
            {
                float off = 13.2f + (float)rng.NextDouble() * 3.5f;
                var p = _route.Position[i] + sd * off + _route.Tangent[i] * ((float)rng.NextDouble() * 4f - 2f);
                if (Nb9DryClear(p, 2f) && occ.Free(p, 0f, 1.1f))
                {
                    p.y = _ground.Height(p.x, p.z);
                    if (Nb9Put(occ, duneT, "Nagisa_NB9_DuneGrass", p, (float)rng.NextDouble() * 360f, 0.85f + (float)rng.NextDouble() * 0.6f, 0.01f, 0.8f, "dune"))
                    { dune++; lastDune = dist; }
                }
            }

            // coconut palms leaning out over the sand: walk seaward to ~7 m inland of the waterline
            if (dist - lastLean >= 27f && lean < Nb9LeanPalmCap)
            {
                for (float off = 14.5f; off < 110f; off += 1.5f)
                {
                    var p = _route.Position[i] + sd * off;
                    float c = _ground.Coast(p.x, p.z);
                    if (c < 0f) break;
                    if (c > 8.5f) continue;
                    if (c < 4.5f) break;
                    float gx = _ground.Coast(p.x + 3f, p.z) - _ground.Coast(p.x - 3f, p.z);
                    float gz = _ground.Coast(p.x, p.z + 3f) - _ground.Coast(p.x, p.z - 3f);
                    var toSea = new Vector3(-gx, 0f, -gz);
                    if (toSea.sqrMagnitude < 1e-4f) toSea = sd;
                    toSea.Normalize();
                    if (!Nb9DryClear(p, 3f) || !occ.Free(p, 0f, 3.0f)) break;
                    p.y = _ground.Height(p.x, p.z);
                    // model leans toward local +x; Unity yaw y maps +x to (cos y, 0, -sin y)
                    float yaw = Mathf.Atan2(-toSea.z, toSea.x) * Mathf.Rad2Deg;
                    string stem = (lean & 1) == 0 ? "Nagisa_NB9_CoconutLeanA" : "Nagisa_NB9_CoconutLeanB";
                    if (Nb9Put(occ, palmT, stem, p, yaw + (float)(rng.NextDouble() - 0.5) * 24f, 0.9f + (float)rng.NextDouble() * 0.25f, 0.0018f, 2.2f, "coconut"))
                    { lean++; lastLean = dist; }
                    break;
                }
            }
        }
    }

    private static bool Nb9DryClear(Vector3 p, float coastMin) =>
        _ground.Coast(p.x, p.z) >= coastMin && _ground.Height(p.x, p.z) > 0.25f && !InAnyPad(p.x, p.z, 3f) &&
        Nb9RouteDist(p.x, p.z) > CorridorKeepOutM + 1.0f;

    // ---------------------------------------------------------------- verification
    /// <summary>Counts per group, side-rule band check on every instance (highway band, corridor), motion proxy: all cards use the sway shader.</summary>
    public static void Nb9SelfTest()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnsureRouteGround();
        Nb9BuildHash();
        var roots = FindRootsByExactName(RootName);
        if (roots.Count == 0) { Debug.LogError("[nb9] SELFTEST FAIL: no Nagisa root"); return; }
        Transform grp = null;
        foreach (var t in roots[0].GetComponentsInChildren<Transform>(true)) if (t.name == "NB Flora") { grp = t; break; }
        if (grp == null) { Debug.LogError("[nb9] SELFTEST FAIL: no 'NB Flora' group"); return; }
        int total = 0, band = 0, corridor = 0, swayMats = 0, nonSway = 0;
        var perGroup = new List<string>();
        foreach (Transform g in grp)
        {
            int n = 0;
            foreach (var lg in g.GetComponentsInChildren<LODGroup>(true))
            {
                n++; total++;
                var p = lg.transform.position;
                float d = _route.PlanDistance(p.x, p.z, out int ni);
                if (d < CorridorKeepOutM + 0.5f) corridor++;
                float dd = _route.Distance[ni];
                if (Nb9Coastal(dd))
                {
                    var sd = _route.SideFlat(ni) * SeaSign(ni);
                    float off = Vector3.Dot(p - _route.Position[ni], sd);
                    if (off < 0f && -off < Nb9InlandBandM - 1f) band++;
                }
            }
            perGroup.Add($"{g.name}={n}");
        }
        foreach (var r in grp.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                if (m.shader != null && m.shader.name == "MapleRide/HDRP/Foliage" && m.HasProperty("_WindStrength") && m.GetFloat("_WindStrength") > 0f) swayMats++;
                else if (m.shader != null && m.shader.name == "MapleRide/HDRP/Foliage") nonSway++;
            }
        bool pass = total > 500 && band == 0 && corridor == 0;
        Debug.Log($"[nb9] SELFTEST {(pass ? "PASS" : "FAIL")}: {total} instances ({string.Join(", ", perGroup)}), in highway band {band}, in corridor {corridor}, " +
                  $"foliage renderers with sway {swayMats} (static ground cover {nonSway}).");
    }

    /// <summary>Verification frames: reference/good_graphics/nagisa_bay/overhaul/nb9_*.png (ride-road jungle slope, promenade palms, ground).</summary>
    public static void Nb9Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnsureRouteGround();
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        try
        {
            var roots = FindRootsByExactName(RootName);
            Transform jung = null, palms = null;
            foreach (var t in roots[0].GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Jungle slopes" && jung == null) jung = t;
                if (t.name == "Palms" && palms == null && t.parent != null && t.parent.name == "NB Flora") palms = t;
            }
            // jungle: the clump nearest the route that is >= 25 m off it, on the climb side of the course
            if (jung != null && jung.childCount > 0)
            {
                Transform best = null; float bestScore = float.MaxValue;
                foreach (Transform c in jung)
                {
                    float d = _route.PlanDistance(c.position.x, c.position.z, out int ni);
                    if (d < 24f || d > 70f) continue;
                    float score = Mathf.Abs(_route.Distance[ni] - 5600f) + d;   // the climb, away from the coast
                    if (score < bestScore) { bestScore = score; best = c; }
                }
                if (best == null) best = jung.GetChild(jung.childCount / 2);
                _route.PlanDistance(best.position.x, best.position.z, out int ni2);
                var t = _route.Tangent[ni2]; t.y = 0f; t.Normalize();
                var eye = _route.Position[ni2] + Vector3.up * 1.7f - t * 18f;
                NagisaBayDiagnostics.Shot(eye, best.position + Vector3.up * 1.5f, 62f, "overhaul/nb9_jungle_slope.png");
            }
            if (palms != null && palms.childCount > 0)
            {
                Transform best = null;
                foreach (Transform c in palms) if (c.name.Contains("CoconutLean")) { best = c; break; }
                if (best != null)
                {
                    float d = _route.PlanDistance(best.position.x, best.position.z, out int ni);
                    var t = _route.Tangent[ni]; t.y = 0f; t.Normalize();
                    var eye = _route.Position[ni] + Vector3.up * 1.7f - t * 22f;
                    NagisaBayDiagnostics.Shot(eye, best.position + Vector3.up * 4f, 60f, "overhaul/nb9_beach_palms.png");
                }
            }
            {
                int i = _route.IndexAt(6900f);
                var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
                var eye = _route.Position[i] + Vector3.up * 1.7f - t * 6f;
                NagisaBayDiagnostics.Shot(eye, _route.Position[i] + t * 40f + Vector3.up * 2.5f, 62f, "overhaul/nb9_verge_ground.png");
            }
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previous))
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
            }
        }
    }
}
