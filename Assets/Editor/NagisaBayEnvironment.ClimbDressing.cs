// NAGISA BAY destination brief sections 9 + 11 - COASTAL CLIMB and RESORT DESCENT dressing (worker G).
// Route topology, road mesh, checkpoints and physics are NOT touched: everything here is placed AROUND the road, never
// inside CorridorKeepOutM except flat lay-by paving, and it is collider-free (except lay-by decks for people).
//   * stone retaining walls on the cut side and masonry parapets on the fill side (offset ~5 m, beyond the shoulder)
//   * Japanese black pines (clumps both sides) + stone lanterns / gateposts at lookouts and resort lots
//   * roadside LOOKOUTS: cantilevered stone lay-by + parapet with a gap, benches, bike racks, pines, a lantern, photo spots
//     (placed where the bay opens up, so the climb reveals it a little more at each one)
//   * hillside villas and boutique resort lots on the downhill side (front faces the view) with gateposts + lanterns
//   * descent: two rock-shed "tunnel" galleries (portals, roof, grass cover mound, ceiling light strips) where the road
//     already runs between rising ground, and tree windows toward the bay (Flora removed from a wedge) for glimpses
// Stage "ClimbDressing" (order 99) -> "NB Overhaul/NB ClimbDressing". Markers "CL_Spot_*" feed WorldLife2.
// Models: tools/blender/build_nagisa_climb_kit.py (Nagisa_CL_*). ALL numbers are PROVISIONAL art tuning.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const float ClClimbM0 = 4780f, ClClimbM1 = 8230f, ClDescM0 = 8290f, ClDescM1 = 11600f;
    private const float ClStepM = 6f;
    private const float ClWallOffM = 5.2f;       // fill-side parapet offset
    private const float ClCutWallOffM = 12.9f;    // cut-side retaining wall at the toe of the batter (bench half width 12.6 m)

    private sealed class ClSt
    {
        public float m; public Vector3 p, t, left; public float y;
        public float cutL, cutR, dropL, dropR;      // terrain rise / fall next to the road, each side
    }

    private static List<ClSt> ClBuildStations(float m0, float m1)
    {
        var l = new List<ClSt>();
        for (float m = m0; m <= m1; m += ClStepM)
        {
            int i = _route.IndexAt(m);
            var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
            var s = new ClSt { m = _route.Distance[i], p = _route.Position[i], t = t, left = _route.SideFlat(i), y = _route.Position[i].y };
            foreach (float sg in new[] { 1f, -1f })
            {
                float rise = -99f, fall = 0f;
                foreach (float o in new[] { 14f, 16f, 18f, 20f })   // the road bench is flat to 12.6 m, so the cut batter starts beyond it
                {
                    var q = s.p + s.left * (sg * o);
                    rise = Mathf.Max(rise, _ground.Height(q.x, q.z) - s.y);
                }
                foreach (float o in new[] { 8f, 12f, 16f })
                {
                    var q = s.p + s.left * (sg * o);
                    fall = Mathf.Max(fall, s.y - _ground.Height(q.x, q.z));
                }
                if (sg > 0f) { s.cutL = rise; s.dropL = fall; } else { s.cutR = rise; s.dropR = fall; }
            }
            l.Add(s);
        }
        return l;
    }

    private static Dictionary<string, Vector3> _clSize = new Dictionary<string, Vector3>();

    /// <summary>Half-extents (x, height, z) of a GLB's LOD0, measured on a temporary instance.</summary>
    private static Vector3 ClMeasure(string stem)
    {
        if (_clSize.TryGetValue(stem, out var v)) return v;
        v = new Vector3(6f, 8f, 6f);
        var src = Glb(stem);
        if (src != null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            var b = new Bounds(go.transform.position, Vector3.zero); bool any = false;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (LodLevel(r.transform, go.transform.parent) != 0) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any) v = new Vector3(b.extents.x, b.size.y, b.extents.z);
            Object.DestroyImmediate(go);
        }
        _clSize[stem] = v;
        return v;
    }

    private static M2Occ ClSeedOcc(Transform root, Transform mine, float m0, float m1, bool skipFlora = false)
    {
        var occ = new M2Occ();
        float minx = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxz = float.MinValue;
        for (float m = m0; m <= m1; m += 40f)
        {
            var p = _route.Position[_route.IndexAt(m)];
            minx = Mathf.Min(minx, p.x); maxx = Mathf.Max(maxx, p.x); minz = Mathf.Min(minz, p.z); maxz = Mathf.Max(maxz, p.z);
        }
        minx -= 200f; minz -= 200f; maxx += 200f; maxz += 200f;
        int seen = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.transform.IsChildOf(mine)) continue;
            var b = r.bounds;
            if (b.max.x < minx || b.min.x > maxx || b.max.z < minz || b.min.z > maxz) continue;
            if (b.size.x > 45f || b.size.z > 45f) continue;
            if (b.size.y < 0.25f && (b.size.x > 8f || b.size.z > 8f)) continue;
            if (b.size.y < 1.1f && Mathf.Max(b.size.x, b.size.z) < 3f) continue;           // grass clumps / tufts / small litter
            if (skipFlora && InFloraGroup(r.transform)) continue;
            occ.Add(b.min.x, b.min.z, b.max.x, b.max.z);
            seen++;
        }
        Debug.Log($"[g-climb] occupancy{(skipFlora ? " (no flora)" : "")}: {seen} renderers indexed.");
        return occ;
    }

    private static bool InFloraGroup(Transform t)
    {
        for (var c = t; c != null; c = c.parent) if (c.name == "NB Flora") return true;
        return false;
    }

    /// <summary>Removes NB Flora trees whose position lies inside the rectangle (the lay-by / lookout footprint).</summary>
    private static int ClPruneFlora(Transform floraT, float minx, float minz, float maxx, float maxz)
    {
        if (floraT == null) return 0;
        int n = 0;
        var kill = new List<GameObject>();
        foreach (var lg in floraT.GetComponentsInChildren<LODGroup>(true))
        {
            var p = lg.transform.position;
            if (p.x > minx && p.x < maxx && p.z > minz && p.z < maxz) kill.Add(lg.gameObject);
        }
        foreach (var g in kill) if (g != null) { Object.DestroyImmediate(g); n++; }
        return n;
    }

    private static Material _clNeedle;

    [NagisaStage(99, "ClimbDressing")]
    private static void BuildClimbDressingStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0 || _nbColourRemaps[0] == null) { PrepareNagisaTextures(); BuildNbMaterials(); }
        if (_nb.Count == 0) { Debug.LogError("[g-climb] Run NagisaBayEnvironment.Apply first."); return; }
        if (!_nb.ContainsKey("NB9_CoralRock")) Nb9Materials();   // standalone stage run: Flora slots (coral, frangipani) for the shared GLBs
        _clNeedle = Cel("Nagisa_CL_Needle", Srgb(30, 74, 44), 0.06f, 0.03f, 0.1f, null, null, 0f, 0f);
        _nb["NB_CL_Needle"] = _clNeedle;
        var rng = new System.Random(5151);
        var root = group.root;
        var occ = ClSeedOcc(root, group, ClClimbM0, ClDescM1);
        var occLook = ClSeedOcc(root, group, ClClimbM0, ClDescM1, true);
        _clFloraT = group.parent != null ? group.parent.Find("NB Flora") : null;
        var wallsT = M2Child(group, "Retaining walls and parapets");
        var lookT = M2Child(group, "Lookouts");
        var treeT = M2Child(group, "Black pines");
        var lotT = M2Child(group, "Villas and boutique resorts");
        var tunT = M2Child(group, "Descent galleries");
        var spotsT = M2Child(group, "Spots");

        var climb = ClBuildStations(ClClimbM0, ClClimbM1);
        var desc = ClBuildStations(ClDescM0, ClDescM1);
        var all = new List<ClSt>(climb); all.AddRange(desc);

        var looks = ClLookouts(lookT, spotsT, all, occLook, occ, rng);                       // choose + build lookouts first (walls leave gaps)
        var plantsT = M2Child(group, "Wall planting");
        int walls = ClWalls(wallsT, all, looks, plantsT, rng);
        int pines = ClPines(treeT, all, looks, occ, rng);
        int lots = ClLots(lotT, all, occ, rng);
        int tun = ClGalleries(tunT, desc, rng);
        int cleared = ClBayWindows(group, all, looks);
        Debug.Log($"[g-climb] done: {walls} wall/parapet segments, {looks.Count} lookouts, {pines} pines, {lots} villa/resort lots, {tun} galleries, {cleared} trees cleared for bay windows.");
    }

    // ---------------------------------------------------------------- walls
    private static bool ClInGap(List<(float m, float side)> looks, float m, float sg)
    {
        foreach (var l in looks) if (Mathf.Abs(m - l.m) < 17f && l.side * sg > 0f) return true;
        return false;
    }

    private static int ClWalls(Transform parent, List<ClSt> st, List<(float m, float side)> looks, Transform plants, System.Random rng)
    {
        var wall = new HwBuf(); var cap = new HwBuf();
        int n = 0, chunk = 0; float chunkStart = st.Count > 0 ? st[0].m : 0f;
        var h = new float[st.Count, 2];
        // smoothed wall tops: average 3 neighbours so the masonry steps stay gentle
        for (int k = 0; k < st.Count; k++)
            for (int s = 0; s < 2; s++)
            {
                float sum = 0f; int c = 0;
                for (int j = -1; j <= 1; j++)
                {
                    var q = st[Mathf.Clamp(k + j, 0, st.Count - 1)];
                    float cut = s == 0 ? q.cutL : q.cutR;
                    sum += cut; c++;
                }
                h[k, s] = sum / c;
            }
        for (int k = 0; k < st.Count - 1; k++)
        {
            var a = st[k]; var b = st[k + 1];
            if (b.m - a.m > 12f) continue;
            for (int s = 0; s < 2; s++)
            {
                float sg = s == 0 ? 1f : -1f;
                if (ClInGap(looks, a.m, sg)) continue;
                float cut = h[k, s], drop = s == 0 ? a.dropL : a.dropR;
                var mid = (a.p + b.p) * 0.5f; var tan = (b.p - a.p); tan.y = 0f; float len = tan.magnitude; if (len < 0.5f) continue; tan /= len;
                var side = Vector3.Cross(Vector3.up, tan) * sg;           // outward from the road on this side
                float yMid = (a.y + b.y) * 0.5f;
                if (cut >= 1.2f)
                {
                    // retaining wall holding the cut: top follows the terrain a little below its crest, 1.2 .. 3.0 m
                    float top = Mathf.Clamp(cut * 0.8f + 0.2f, 1.2f, 3.0f);
                    var c = mid + side * ClCutWallOffM; c.y = yMid + top * 0.5f - 0.25f;
                    wall.Box(c, side, tan, 0.7f, top + 0.5f, len + 0.25f);
                    cap.Box(new Vector3(c.x, yMid + top + 0.02f, c.z), side, tan, 0.95f, 0.14f, len + 0.25f);
                    n++;
                    // tropical / flowering planting spilling over the wall crest
                    if (k % 4 == 0 && rng.NextDouble() < 0.75)
                    {
                        string plant = k % 12 == 0 ? "Nagisa_NB9_Frangipani" : k % 8 == 0 ? "Nagisa_Hibiscus" : "Nagisa_Bougainvillea";
                        PlaceWorld(plant, plants, new Vector3(c.x + side.x * 0.9f, yMid + top, c.z + side.z * 0.9f), (float)rng.NextDouble() * 360f, 1.0f + (float)rng.NextDouble() * 0.5f, 0.004f);
                    }
                }
                else if (drop >= 2.2f)
                {
                    // fill-side masonry parapet 0.95 m above the road, carried down to the slope
                    var c = mid + side * (ClWallOffM - 0.1f);
                    float gy = _ground.Height(c.x, c.z);
                    float bottom = Mathf.Max(gy - 0.2f, yMid - 3.5f);
                    float top = yMid + 0.95f;
                    var cc = new Vector3(c.x, (top + bottom) * 0.5f, c.z);
                    wall.Box(cc, side, tan, 0.6f, top - bottom, len + 0.25f);
                    cap.Box(new Vector3(c.x, top + 0.02f, c.z), side, tan, 0.85f, 0.13f, len + 0.25f);
                    n++;
                    if (k % 6 == 0 && rng.NextDouble() < 0.6)
                        PlaceWorld("Nagisa_Hibiscus", plants, new Vector3(c.x + side.x * 1.1f, Mathf.Max(gy, bottom + 0.3f), c.z + side.z * 1.1f), (float)rng.NextDouble() * 360f, 0.9f + (float)rng.NextDouble() * 0.4f, 0.004f);
                }
            }
            if (b.m - chunkStart > 150f)
            {
                M2Flush(wall, parent, $"Nagisa_CL_Wall_{chunk}", _nb["NB_Stone"], false);
                M2Flush(cap, parent, $"Nagisa_CL_WallCap_{chunk}", _nb["NB_Concrete"], false, false);
                wall = new HwBuf(); cap = new HwBuf(); chunk++; chunkStart = b.m;
            }
        }
        M2Flush(wall, parent, $"Nagisa_CL_Wall_{chunk}", _nb["NB_Stone"], false);
        M2Flush(cap, parent, $"Nagisa_CL_WallCap_{chunk}", _nb["NB_Concrete"], false, false);
        return n;
    }

    // ---------------------------------------------------------------- lookouts
    private static Transform _clFloraT;

    private static List<(float m, float side)> ClLookouts(Transform parent, Transform spots, List<ClSt> st, M2Occ occLook, M2Occ occ, System.Random rng)
    {
        var chosen = new List<(float m, float side)>();
        // candidates: a clear fall on one side (the view side), road roughly along the slope, not already close to another
        var cands = new List<(float score, ClSt s, float sg)>();
        foreach (var s in st)
        {
            foreach (float sg in new[] { 1f, -1f })
            {
                float drop = sg > 0f ? s.dropL : s.dropR, cut = sg > 0f ? s.cutL : s.cutR;
                if (drop < 3.5f || cut > 0.8f) continue;
                // openness toward the sea from the platform (eye 2 m): max elevation angle of the terrain along the seaward line
                var q0 = s.p + s.left * (sg * 12f);
                var sea = ClSeaward(q0);
                if (sea == Vector3.zero) continue;
                float worst = -1f;
                for (float d = 25f; d < 700f; d += 25f)
                {
                    var q = q0 + sea * d;
                    worst = Mathf.Max(worst, (_ground.Height(q.x, q.z) - (s.y + 2f)) / d);
                }
                if (worst > 0.01f) continue;
                float align = Mathf.Abs(Vector3.Dot(sea, s.left * sg));      // 1 = the view is straight off the platform
                cands.Add((align * 2f + Mathf.Min(drop, 20f) * 0.05f + (float)rng.NextDouble() * 0.4f, s, sg));
            }
        }
        cands.Sort((a, b) => b.score.CompareTo(a.score));
        int built = 0;
        foreach (var c in cands)
        {
            bool climb = c.s.m < ClClimbM1 + 10f;
            int have = 0; foreach (var l in chosen) if ((l.m < ClClimbM1 + 10f) == climb) have++;
            if (have >= (climb ? 6 : 4)) continue;
            bool near = false; foreach (var l in chosen) if (Mathf.Abs(l.m - c.s.m) < 330f) { near = true; break; }
            if (near) continue;
            if (ClBuildLookout(parent, spots, st, c.s, c.sg, occLook, rng, built, occ)) { chosen.Add((c.s.m, c.sg)); built++; }
        }
        Debug.Log($"[g-climb] lookouts: {built} built of {cands.Count} candidates ({chosen.Count} gaps in the parapets).");
        return chosen;
    }

    private static Vector3 ClSeaward(Vector3 p)
    {
        // direction of decreasing coast distance (toward the sea) from the distance field
        float e = 12f;
        var g = new Vector3(_ground.Coast(p.x + e, p.z) - _ground.Coast(p.x - e, p.z), 0f, _ground.Coast(p.x, p.z + e) - _ground.Coast(p.x, p.z - e));
        if (g.sqrMagnitude < 1e-4f) return Vector3.zero;
        return -g.normalized;
    }

    private static bool ClBuildLookout(Transform parent, Transform spots, List<ClSt> st, ClSt s, float sg, M2Occ occ, System.Random rng, int idx, M2Occ occAll)
    {
        var tan = s.t; var side = s.left * sg;
        const float off0 = 6.6f, off1 = 15.5f, halfLen = 13f;
        // footprint test: nothing already staged there
        var c0 = s.p + side * ((off0 + off1) * 0.5f);
        float minx = c0.x - halfLen - 2f, maxx = c0.x + halfLen + 2f, minz = c0.z - halfLen - 2f, maxz = c0.z + halfLen + 2f;
        if (occ.Hit(minx, minz, maxx, maxz)) return false;
        float y = s.y - 0.1f;
        ClPruneFlora(_clFloraT, minx, minz, maxx, maxz);
        var deck = new HwBuf(); var skirt = new HwBuf(); var rail = new HwBuf();
        int segs = 6;
        for (int k = 0; k < segs; k++)
        {
            float ta = -halfLen + 2f * halfLen * k / segs, tb = -halfLen + 2f * halfLen * (k + 1) / segs;
            var a0 = s.p + tan * ta + side * off0; var a1 = s.p + tan * ta + side * off1;
            var b0 = s.p + tan * tb + side * off0; var b1 = s.p + tan * tb + side * off1;
            a0.y = a1.y = b0.y = b1.y = y;
            deck.Quad(a0, a1, b1, b0, Vector3.up, ta / 4f, tb / 4f, 0f, 2.2f);
            // outer skirt down to the slope (stone retaining face)
            float ga = _ground.Height(a1.x, a1.z), gb = _ground.Height(b1.x, b1.z);
            var sa = new Vector3(a1.x, Mathf.Min(ga, y - 0.6f) - 0.3f, a1.z); var sb = new Vector3(b1.x, Mathf.Min(gb, y - 0.6f) - 0.3f, b1.z);
            skirt.Quad(sa, a1, b1, sb, side, ta / 3f, tb / 3f, 0f, (y - sa.y) / 3f);
            // parapet along the outer edge with a 4 m viewing gap in the middle
            if (Mathf.Abs((ta + tb) * 0.5f) > 2.2f)
            {
                var pc = s.p + tan * ((ta + tb) * 0.5f) + side * (off1 - 0.3f); pc.y = y + 0.5f;
                rail.Box(pc, side, tan, 0.55f, 1.0f, (tb - ta) + 0.1f);
            }
        }
        M2Flush(deck, parent, $"Nagisa_CL_LookoutDeck_{idx}", _nb["NB_DeckStone"], true, false);
        M2Flush(skirt, parent, $"Nagisa_CL_LookoutSkirt_{idx}", _nb["NB_Stone"], false);
        M2Flush(rail, parent, $"Nagisa_CL_LookoutParapet_{idx}", _nb["NB_Stone"], false);
        // furniture: two benches facing the view, bike racks, lanterns, pines
        float yaw = Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg;      // local +z toward the view
        foreach (float t in new[] { -6f, 6f })
        {
            var bp = s.p + tan * t + side * 12.6f; bp.y = y;
            PlaceWorld("Nagisa_Bench", parent, bp, yaw + 180f, 1f, 0.004f);     // bench front is local -z? keep the existing convention: face the view
            M2SpotAt(spots, "CL_Spot_Sit", bp, side);
        }
        for (int k = 0; k < 2; k++)
        {
            var rp = s.p + tan * (-9f - k * 1.8f) + side * 9.2f; rp.y = y;
            PlaceWorld("Nagisa_S_BikeRack", parent, rp, Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg + 90f, 1f, 0.004f);
        }
        M2SpotAt(spots, "CL_Spot_Photo", s.p + tan * 2.5f + side * 13.8f + Vector3.up * (y - s.p.y), side);
        M2SpotAt(spots, "CL_Spot_Photo", s.p + tan * 3.8f + side * 13.8f + Vector3.up * (y - s.p.y), side);
        foreach (float t in new[] { -halfLen + 1.5f, halfLen - 1.5f })
        {
            var lp = s.p + tan * t + side * 14.4f; lp.y = y;
            PlaceWorld("Nagisa_CL_Lantern", parent, lp, yaw, 1f, 0.004f);
        }
        var pp = s.p + tan * (halfLen + 3f) + side * 13f; pp.y = _ground.Height(pp.x, pp.z);
        PlaceWorld("Nagisa_CL_BlackPineSmall", parent, pp, (float)rng.NextDouble() * 360f, 1.2f, 0.004f);
        occ.Add(minx, minz, maxx, maxz);
        occAll.Add(minx, minz, maxx, maxz);
        return true;
    }

    private static void M2SpotAt(Transform spots, string name, Vector3 p, Vector3 face)
    {
        var t = new GameObject(name).transform;
        t.SetParent(spots, false);
        face.y = 0f;
        t.SetPositionAndRotation(p, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face.normalized, Vector3.up) : Quaternion.identity);
    }

    // ---------------------------------------------------------------- black pines
    private static int ClPines(Transform parent, List<ClSt> st, List<(float m, float side)> looks, M2Occ occ, System.Random rng)
    {
        int n = 0; float last = -999f;
        if (!GlbExists("Nagisa_CL_BlackPine")) return 0;
        for (int k = 0; k < st.Count; k += 3)
        {
            var s = st[k];
            if (s.m - last < 16f) continue;
            foreach (float sg in new[] { 1f, -1f })
            {
                if (rng.NextDouble() > 0.55) continue;
                int clump = 1 + rng.Next(3);
                float baseOff = 13f + (float)rng.NextDouble() * 30f;
                for (int c = 0; c < clump; c++)
                {
                    float off = baseOff + (float)(rng.NextDouble() - 0.5) * 8f;
                    var q = s.p + s.left * (sg * off) + s.t * ((float)(rng.NextDouble() - 0.5) * 12f);
                    if (!CanPlace(q.x, q.z, 3f, out float y, 6f)) continue;
                    if (ClInGap(looks, s.m, sg) && off < 30f) continue;
                    if (M2Taken(occ, q, 2.4f)) continue;
                    // pines stand on the slope: skip the ones that would hang over a drop (relief under the crown)
                    float lo = y, hi = y;
                    foreach (var d in new[] { new Vector2(2f, 0f), new Vector2(-2f, 0f), new Vector2(0f, 2f), new Vector2(0f, -2f) })
                    { float h = _ground.Height(q.x + d.x, q.z + d.y); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
                    if (hi - lo > 2.4f) continue;
                    var go = PlaceWorld("Nagisa_CL_BlackPine", parent, new Vector3(q.x, y - 0.12f, q.z), (float)rng.NextDouble() * 360f, 0.8f + (float)rng.NextDouble() * 0.6f, 0.003f);
                    if (go == null) continue;
                    occ.Add(q.x - 2.4f, q.z - 2.4f, q.x + 2.4f, q.z + 2.4f);
                    n++;
                }
            }
            last = s.m;
        }
        return n;
    }

    // ---------------------------------------------------------------- villas + boutique resort lots (downhill side)
    private static int ClLots(Transform parent, List<ClSt> st, M2Occ occ, System.Random rng)
    {
        int villas = 0, resorts = 0;
        var placed = new List<Vector3>(); var placedResort = new List<Vector3>();
        var villaStems = new[] { "Nagisa_BD_VillaCliff", "Nagisa_BD_VillaTile", "Nagisa_BD_VillaStack" };
        var resortStems = new[] { "Nagisa_B_Boutique1", "Nagisa_B_Boutique2", "Nagisa_BoutiqueResort" };
        var order = new List<int>();
        for (int i = 0; i < st.Count; i += 5) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        foreach (int k in order)
        {
            if (villas >= 26 && resorts >= 7) break;
            var s = st[k];
            bool climb = s.m < ClClimbM1 + 10f;
            foreach (float sg in new[] { 1f, -1f })
            {
                float drop = sg > 0f ? s.dropL : s.dropR;
                if (drop < 1.5f) continue;                                   // lots sit on the downhill side, below / beside the road
                for (float off = 30f; off <= 95f; off += 13f)
                {
                    var q = s.p + s.left * (sg * off);
                    var down = ClDownhill(q);
                    if (down == Vector3.zero) continue;
                    bool resort = rng.NextDouble() < 0.22 && resorts < 7;
                    string stem = resort ? resortStems[rng.Next(resortStems.Length)] : villaStems[rng.Next(villaStems.Length)];
                    var sz = ClMeasure(stem);
                    float hx = Mathf.Max(8f, sz.x), hz = Mathf.Max(8f, sz.z);
                    float yaw = M2FrontYawFromDir(down);
                    float spacing = resort ? 170f : 85f;
                    var list = resort ? placedResort : placed;
                    bool close = false; foreach (var o in list) if ((o - q).sqrMagnitude < spacing * spacing) { close = true; break; }
                    if (close) continue;
                    if (!ClSeat(q, yaw, hx + 1f, hz + 1f, hz * 0.5f, 5.2f, 24f, occ, out float y)) continue;
                    var go = PlaceWorld(stem, parent, new Vector3(q.x, y - 0.5f, q.z), yaw, 1f, 0.004f, NbColourway(rng.Next(8)), hero: resort);
                    if (go == null) continue;
                    go.name = (resort ? "CL Resort " : "CL Villa ") + (resort ? resorts : villas).ToString("00");
                    M2Reserve(occ, q, yaw, hx, hz);
                    list.Add(q);
                    // gate: two posts + lanterns toward the road, and a pine
                    var toRoad = (s.p - q); toRoad.y = 0f; toRoad.Normalize();
                    var perp = Vector3.Cross(Vector3.up, toRoad);
                    var gp = q + toRoad * (hz + 4f);
                    foreach (float side2 in new[] { -2.2f, 2.2f })
                    {
                        var pp = gp + perp * side2; pp.y = _ground.Height(pp.x, pp.z);
                        if (_route.PlanDistance(pp.x, pp.z, out _) > CorridorKeepOutM + 1f) PlaceWorld("Nagisa_CL_Gatepost", parent, pp, 0f, 1f, 0.004f);
                    }
                    var tp = gp + perp * 4.5f; tp.y = _ground.Height(tp.x, tp.z);
                    if (_route.PlanDistance(tp.x, tp.z, out _) > CorridorKeepOutM + 2f) PlaceWorld("Nagisa_CL_BlackPineSmall", parent, tp, (float)rng.NextDouble() * 360f, 1.1f, 0.004f);
                    if (resort) resorts++; else villas++;
                    break;
                }
            }
        }
        Debug.Log($"[g-climb] lots: {villas} villas + {resorts} boutique resorts on the downhill sides.");
        return villas + resorts;
    }

    private static float M2FrontYawFromDir(Vector3 dir) => Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;

    private static Vector3 ClDownhill(Vector3 p)
    {
        float e = 8f;
        var g = new Vector3(_ground.Height(p.x + e, p.z) - _ground.Height(p.x - e, p.z), 0f, _ground.Height(p.x, p.z + e) - _ground.Height(p.x, p.z - e));
        if (g.sqrMagnitude < 1e-4f) return Vector3.zero;
        return -g.normalized;
    }

    private static bool ClSeat(Vector3 c, float yaw, float hx, float hz, float hzBack, float maxRelief, float maxSlopeDeg, M2Occ occ, out float y)
    {
        y = 0f;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float lo = float.MaxValue, hi = float.MinValue;
        float minx = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxz = float.MinValue;
        for (int i = 0; i <= 4; i++)
            for (int j = 0; j <= 4; j++)
            {
                var w = c + rot * new Vector3(-hx + 2f * hx * i / 4f, 0f, -hz + (hz + hzBack) * j / 4f);
                if (_ground.Coast(w.x, w.z) < 10f) return false;
                if (_route.PlanDistance(w.x, w.z, out _) < CorridorKeepOutM + 14f) return false;
                float h = _ground.Height(w.x, w.z);
                lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                if (i == 0 || i == 4) { minx = Mathf.Min(minx, w.x); maxx = Mathf.Max(maxx, w.x); minz = Mathf.Min(minz, w.z); maxz = Mathf.Max(maxz, w.z); }
            }
        if (hi - lo > maxRelief) return false;
        if (occ.Hit(minx, minz, maxx, maxz)) return false;
        y = hi;
        return true;
    }

    // ---------------------------------------------------------------- descent galleries ("tunnels")
    private static int ClGalleries(Transform parent, List<ClSt> desc, System.Random rng)
    {
        // choose spans where the road already runs between rising ground: the highest min(cutL, cutR), at least ~2 m on both sides
        var order = new List<(float score, int k)>();
        for (int k = 4; k < desc.Count - 12; k += 2)
        {
            float score = 99f;
            for (int j = 0; j <= 8; j++) score = Mathf.Min(score, Mathf.Min(desc[k + j].cutL, desc[k + j].cutR));
            order.Add((score, k));
        }
        order.Sort((a, b) => b.score.CompareTo(a.score));
        int built = 0; var used = new List<float>();
        foreach (var (score, k) in order)
        {
            if (built >= 2) break;
            if (score < 2.0f) break;
            float m = desc[k].m;
            bool far = true; foreach (float u in used) if (Mathf.Abs(u - m) < 900f) far = false;
            if (!far) continue;
            ClGallery(parent, desc, k, k + 8, built);
            used.Add(m); built++;
        }
        if (built == 0) Debug.Log("[g-climb] no cut-between-banks span >= 2 m on the descent: no galleries built.");
        return built;
    }

    private static void ClGallery(Transform parent, List<ClSt> st, int k0, int k1, int idx)
    {
        var shell = new HwBuf(); var dark = new HwBuf(); var glow = new HwBuf(); var mound = new HwBuf();
        const float inner = 5.4f, roofH = 5.5f, wallT = 0.9f;
        for (int k = k0; k < k1; k++)
        {
            var a = st[k]; var b = st[k + 1];
            var tan = (b.p - a.p); tan.y = 0f; float len = tan.magnitude; tan /= len;
            var side = Vector3.Cross(Vector3.up, tan);
            var mid = (a.p + b.p) * 0.5f; float y = (a.y + b.y) * 0.5f;
            foreach (float sg in new[] { 1f, -1f })
            {
                // outer wall (concrete) + dark inner lining facing the road
                var c = mid + side * (sg * (inner + wallT * 0.5f)); c.y = y + (roofH + 0.4f) * 0.5f - 0.3f;
                shell.Box(c, side, tan, wallT, roofH + 0.4f, len + 0.2f);
                var lin = mid + side * (sg * (inner - 0.04f)); lin.y = y + roofH * 0.5f;
                dark.Quad(new Vector3(lin.x - tan.x * len * 0.5f, y - 0.0f, lin.z - tan.z * len * 0.5f), new Vector3(lin.x - tan.x * len * 0.5f, y + roofH, lin.z - tan.z * len * 0.5f),
                          new Vector3(lin.x + tan.x * len * 0.5f, y + roofH, lin.z + tan.z * len * 0.5f), new Vector3(lin.x + tan.x * len * 0.5f, y, lin.z + tan.z * len * 0.5f), -side * sg, 0f, 1f, 0f, 1f);
            }
            var rc = mid; rc.y = y + roofH + 0.3f;
            shell.Box(rc, side, tan, 2f * (inner + wallT), 0.7f, len + 0.2f);
            // ceiling light strip
            var gc = mid; gc.y = y + roofH - 0.02f;
            glow.Box(gc, side, tan, 0.5f, 0.06f, len * 0.6f);
            // grass cover mound over the roof (pitched, down to the slope either side)
            var ml = mid + side * (-(inner + 15f)); var mr2 = mid + side * (inner + 15f);
            ml.y = Mathf.Max(_ground.Height(ml.x, ml.z), y - 0.2f); mr2.y = Mathf.Max(_ground.Height(mr2.x, mr2.z), y - 0.2f);
            var ridge = mid; ridge.y = y + roofH + 3.2f;
            var la = ml - tan * (len * 0.5f); var lb = ml + tan * (len * 0.5f);
            var ra = mr2 - tan * (len * 0.5f); var rb = mr2 + tan * (len * 0.5f);
            var ca = ridge - tan * (len * 0.5f); var cb = ridge + tan * (len * 0.5f);
            mound.Quad(la, ca, cb, lb, Vector3.up, 0f, len / 4f, 0f, 5f);
            mound.Quad(ca, ra, rb, cb, Vector3.up, 0f, len / 4f, 0f, 5f);
        }
        // portal frames at both ends (heavier concrete, lintel)
        foreach (int kk in new[] { k0, k1 })
        {
            var s = st[kk]; var tan = s.t; var side = s.left;
            var pc = s.p; pc.y = s.y;
            foreach (float sg in new[] { 1f, -1f })
            {
                var c = pc + side * (sg * (inner + 0.9f)); c.y = s.y + (roofH + 1.2f) * 0.5f;
                shell.Box(c, side, tan, 1.8f, roofH + 1.2f, 1.2f);
            }
            var lc = pc; lc.y = s.y + roofH + 0.85f;
            shell.Box(lc, side, tan, 2f * (inner + 1.8f), 1.1f, 1.2f);
        }
        var t0 = M2Child(parent, $"Gallery {idx} (m {st[k0].m:0}-{st[k1].m:0})");
        M2Flush(shell, t0, $"Nagisa_CL_GalleryShell_{idx}", _nb["NB_Concrete"], false);
        M2Flush(dark, t0, $"Nagisa_CL_GalleryLining_{idx}", Cel("Nagisa_CL_TunnelLining", Srgb(52, 54, 58), 0.1f, 0.05f, 0f, null, null, 0f, 0f), false, false);
        M2Flush(glow, t0, $"Nagisa_CL_GalleryLights_{idx}", NbGlow("CL_TunnelLamp", new Color(1f, 0.92f, 0.74f, 1f)), false, false);
        M2Flush(mound, t0, $"Nagisa_CL_GalleryMound_{idx}", _nb.TryGetValue("NB_Lawn", out var gc2) && gc2 != null ? gc2 : _nb["NB_Concrete"], false, false);
        Debug.Log($"[g-climb] gallery {idx}: m {st[k0].m:0}-{st[k1].m:0}, {(st[k1].m - st[k0].m):0} m, inner half width {inner:0.0} m (road + shoulder half width 4.1 m).");
    }

    // ---------------------------------------------------------------- tree windows toward the bay
    private static int ClBayWindows(Transform group, List<ClSt> st, List<(float m, float side)> looks)
    {
        // at each lookout (and a few hand-picked stations) remove NB Flora trees from a seaward wedge so the bay shows through
        var floraT = group.parent != null ? group.parent.Find("NB Flora") : null;
        if (floraT == null) { Debug.Log("[g-climb] no NB Flora group: no tree windows."); return 0; }
        var centres = new List<(Vector3 p, Vector3 dir)>();
        foreach (var l in looks)
        {
            var s = st.Find(x => Mathf.Abs(x.m - l.m) < 4f);
            if (s == null) continue;
            var q = s.p + s.left * (l.side * 14f);
            var sea = ClSeaward(q);
            if (sea != Vector3.zero) centres.Add((q, sea));
        }
        int removed = 0;
        var trees = new List<Transform>();
        foreach (Transform tr in floraT.GetComponentsInChildren<Transform>(true))
            if (tr.GetComponent<LODGroup>() != null) trees.Add(tr);
        foreach (var t in trees)
        {
            if (t == null) continue;
            var p = t.position;
            foreach (var (c, dir) in centres)
            {
                var d = p - c; d.y = 0f;
                float along = Vector3.Dot(d, dir);
                if (along < 6f || along > 220f) continue;
                float lateral = Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, dir)));
                if (lateral > 8f + along * 0.28f) continue;
                if (p.y > c.y + 6f) continue;                       // only trees below eye level block the view down to the bay
                Object.DestroyImmediate(t.gameObject);
                removed++;
                break;
            }
        }
        return removed;
    }
}
