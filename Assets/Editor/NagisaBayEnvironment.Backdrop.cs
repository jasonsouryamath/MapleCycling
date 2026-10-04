using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NB8A - background density beyond ~150 m of the ride road: hillside villas and terraced condos
/// (infinity pools, lit NB_Interior windows) on open hill ground. The old open-sea skyline headlands were
/// removed: they read as buildings floating on the horizon (no land out there), so the sea horizon stays clean. Models: tools/blender/build_nagisa_backdrop_*.py.
/// Stage hook gives a fresh "NB Backdrop" group each run, so re-runs converge by exact name.
/// Never touches the route, road, colliders or CorridorKeepOutM (all sites are >= BdMinRouteM away).
/// </summary>
public static partial class NagisaBayEnvironment
{
    // ---- PROVISIONAL art tunables (illustrative, not final) --------------------------------
    private const float BdMinRouteM = 150f;     // backdrop starts beyond this plan distance from the road
    private const float BdMaxRouteM = 900f;     // and stops here (beyond: fog does the work)
    private const float BdScanStepM = 24f;      // candidate grid step
    private const float BdMinCoastM = 30f;      // keep off beaches / cliffs
    private const float BdMinHeightM = 6f;
    private const float BdMaxReliefM = 6f;      // max ground relief under a lot (Blender RETAIN_D = 7 m)
    private const float BdVillaSpacingM = 34f;
    private const float BdCondoSpacingM = 72f;
    private const float BdOccupiedM = 30f;      // clearance from any existing LODGroup building
    private const int BdMaxVillas = 170;
    private const int BdMaxCondos = 26;
    private const float BdCondoMinHeightM = 25f; // terraced condos only where the hill is real

    private static readonly string[] BdVillaStems = { "Nagisa_BD_VillaCliff", "Nagisa_BD_VillaTile", "Nagisa_BD_VillaStack" };
    private static readonly string[] BdCondoStems = { "Nagisa_BD_TerraceCondoA", "Nagisa_BD_TerraceCondoB" };

    [NagisaStage(96, "Backdrop")]
    private static void BuildBackdropStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();   // standalone MR_NB_STAGES run: reload the shared NB_* assets
        if (_nb.Count == 0)
        {
            Debug.LogError("[nb8a] Run NagisaBayEnvironment.Apply before Backdrop.");
            return;
        }
        var villasT = BdChild(group, "Hill villas");
        var condosT = BdChild(group, "Terraced condos");

        var occupied = new List<Vector3>();
        foreach (var lg in group.root.GetComponentsInChildren<LODGroup>(true))
            if (!lg.transform.IsChildOf(group)) occupied.Add(lg.transform.position);
        int preexisting = occupied.Count;

        // candidate sites, highest ground first (hill tops read best from the road and sea)
        var cands = new List<(Vector3 p, Vector2 down, int cls)>();
        float x0 = -3600f, x1 = 4400f, z0 = -35200f, z1 = -27800f;
        for (float z = z0; z < z1; z += BdScanStepM)
        for (float x = x0; x < x1; x += BdScanStepM)
        {
            if (_ground.Coast(x, z) < BdMinCoastM) continue;
            float h = _ground.Height(x, z);
            if (h < BdMinHeightM) continue;
            int cls = _ground.ClassAt(x, z);
            if (cls != 2 && cls != 3) continue;
            float d = _route.PlanDistance(x, z, out _);
            if (d < BdMinRouteM || d > BdMaxRouteM) continue;
            if (InAnyPad(x, z, 40f)) continue;
            float gx = _ground.Height(x + 8f, z) - _ground.Height(x - 8f, z);
            float gz = _ground.Height(x, z + 8f) - _ground.Height(x, z - 8f);
            var down = new Vector2(-gx, -gz);
            if (down.sqrMagnitude < 1e-4f) down = new Vector2(0f, -1f);
            cands.Add((new Vector3(x, h, z), down.normalized, cls));
        }
        // height-biased shuffle: hills still win, but lots spread over every slope instead of one summit cluster
        var rng = new System.Random(8801);
        var key = new Dictionary<int, double>();
        for (int i = 0; i < cands.Count; i++) key[i] = rng.NextDouble() * (0.5 + cands[i].p.y / 240.0);
        var order = new List<int>(key.Keys);
        order.Sort((a, b) => key[b].CompareTo(key[a]));
        var sorted = new List<(Vector3 p, Vector2 down, int cls)>(cands.Count);
        foreach (int i in order) sorted.Add(cands[i]);
        cands = sorted;

        var placed = new List<Vector3>();
        int condos = 0, villas = 0, rejects = 0;
        // condos first (need more room), grass preferred, then forest clearings
        for (int pass = 0; pass < 2; pass++)
        foreach (var c in cands)
        {
            bool condo = pass == 0;
            if (condo && (condos >= BdMaxCondos || c.p.y < BdCondoMinHeightM || c.cls != 2)) continue;
            if (!condo && villas >= BdMaxVillas) break;
            float spacing = condo ? BdCondoSpacingM : BdVillaSpacingM;
            if (BdNear(placed, c.p, spacing) || BdNear(occupied, c.p, BdOccupiedM)) continue;
            string stem = condo ? BdCondoStems[rng.Next(BdCondoStems.Length)] : BdVillaStems[rng.Next(BdVillaStems.Length)];
            float hx = condo ? 30f : 14f, hzFront = condo ? 30f : 17f, hzBack = condo ? 26f : 9f;
            // front (local -z) faces downhill -> world forward of the model = -down... yaw so local -z -> down
            float yaw = Mathf.Atan2(-c.down.x, -c.down.y) * Mathf.Rad2Deg;
            if (!BdSeat(c.p, yaw, hx, hzFront, hzBack, out float y)) { rejects++; continue; }
            var remap = _nbColourRemaps[rng.Next(_nbColourRemaps.Length)];
            var go = PlaceWorld(stem, condo ? condosT : villasT, new Vector3(c.p.x, y, c.p.z), yaw, 1f, 0.004f, remap);
            if (go == null) continue;
            go.name = (condo ? "BD Condo " + condos.ToString("00") : "BD Villa " + villas.ToString("00"));
            placed.Add(c.p);
            if (condo) condos++; else villas++;
        }

        Debug.Log($"[nb8a] Backdrop: candidates={cands.Count} preexistingLOD={preexisting} villas={villas} " +
                  $"condos={condos} seatRejects={rejects}");
    }

    /// <summary>Ground relief under the rotated lot; seat on the highest point (retaining plinth covers the rest).</summary>
    private static bool BdSeat(Vector3 p, float yaw, float hx, float hzFront, float hzBack, out float y)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i <= 4; i++)
        for (int j = 0; j <= 4; j++)
        {
            var l = new Vector3(-hx + 2f * hx * i / 4f, 0f, -hzFront + (hzFront + hzBack) * j / 4f);
            var w = p + rot * l;
            if (_ground.Coast(w.x, w.z) < 8f) { y = 0f; return false; }
            if (_route.PlanDistance(w.x, w.z, out _) < BdMinRouteM - 40f) { y = 0f; return false; }
            float h = _ground.Height(w.x, w.z);
            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
        }
        // back half may bury into the slope; the front must not hang more than the retaining depth
        y = hi - 0.6f;
        return hi - lo <= BdMaxReliefM;
    }

    private static bool BdNear(List<Vector3> pts, Vector3 p, float r)
    {
        float r2 = r * r;
        foreach (var q in pts)
        {
            float dx = p.x - q.x, dz = p.z - q.z;
            if (dx * dx + dz * dz < r2) return true;
        }
        return false;
    }

    private static Transform BdChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t == null) { t = new GameObject(name).transform; t.SetParent(parent, false); }
        return t;
    }
}
