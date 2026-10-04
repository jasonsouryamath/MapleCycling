// Nagisa Bay (B5) - milestone 3: pastel town, beach cafes / surf shops, marina with moored
// yachts, forested tropical hills and the east coast-road dressing.
//
// Near-road trees are LOD-grouped GLB instances; far forest (beyond FarForestM of the road) is
// merged per 512 m chunk from each asset's LOD2 so the whole island can be covered without
// tens of thousands of GameObjects (the Minato 3.7M-object crash lesson).
// Every placement goes through CanPlace (dry land, real Height(), outside the road corridor).
// All numbers are PROVISIONAL (illustrative tuning).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const float NearForestM = 230f;     // instanced trees within this of the road
    private const float NearTreeGridM = 18f;
    private const float FarChunkM = 512f;
    private const float CoastRailOffsetM = 6.8f;
    private const float MarinaPontoonGapM = 34f;
    // berth centre, so the ambient boats (Life.cs) keep their lanes clear of the pontoons and moored yachts
    private static Vector3 _marinaBerths; private static bool _marinaBerthsSet;
    private const float MarinaBoatClearM = 190f;

    static partial void BuildTown(Transform root)
    {
        var group = new GameObject("Nagisa Town").transform;
        group.SetParent(root, false);
        BuildTownGrid(group);           // pass 2: street grid + modular kit (Town2.cs); replaces BuildPastelTown
        BuildBeachShops2(group);        // pass 2 kit beach bars / kiosks / surf shop (Beach2.cs)
        BuildMarina(group);
        BuildForest(group);
        BuildCoastRoadDressing(group);

        long total = 0;
        foreach (var kv in _instStats) total += kv.Value.tris;
        Debug.Log($"[nagisa] town+resort instanced LOD0 tris (all instances, pre-LOD): {total:N0}");
    }

    // ================================================================= pastel town

    private static readonly (string name, Color c)[] Pastels =
    {
        ("Pink", new Color(0.98f, 0.80f, 0.80f)), ("Mint", new Color(0.76f, 0.93f, 0.84f)),
        ("Lemon", new Color(0.99f, 0.93f, 0.70f)), ("Sky", new Color(0.76f, 0.87f, 0.97f)),
        ("Coral", new Color(0.99f, 0.76f, 0.64f)), ("Cream", new Color(0.97f, 0.94f, 0.86f)),
    };

    private static Dictionary<string, Material>[] _pastelRemap;

    private static Dictionary<string, Material> PastelRemap(int k)
    {
        if (_pastelRemap == null || _pastelRemap.Length != Pastels.Length || _pastelRemap[0]["NB_TownWall"] == null)
        {
            _pastelRemap = new Dictionary<string, Material>[Pastels.Length];
            for (int i = 0; i < Pastels.Length; i++)
            {
                var m = NbCel("NB_TownWall_" + Pastels[i].name, "NB_TownWindows", Pastels[i].c, 0.2f, 0.12f);
                _pastelRemap[i] = new Dictionary<string, Material> { ["NB_TownWall"] = m };
            }
        }
        return _pastelRemap[((k % Pastels.Length) + Pastels.Length) % Pastels.Length];
    }

    /// <summary>Yaw (deg) that turns local -z (the shop / balcony face) toward the road.</summary>
    private static float FaceRoadYaw(Vector3 p)
    {
        _route.PlanDistance(p.x, p.z, out int i);
        var d = _route.Position[i] - p; d.y = 0f;
        if (d.sqrMagnitude < 1e-4f) return 0f;
        d.Normalize();
        return Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
    }

    private static void BuildPastelTown(Transform group)
    {
        var townT = new GameObject("Pastel Town").transform;
        townT.SetParent(group, false);
        var kinds = new[] { "Nagisa_MidriseA", "Nagisa_MidriseB", "Nagisa_MidriseC" };
        var halfSize = new[] { 11.5f, 10f, 13.5f };      // footprint radius incl. balconies
        var rng = new System.Random(5601);
        int placed = 0, colour = 0;
        foreach (var pad in new[] { "town_a", "town_b", "town_c" })
        {
            if (!_route.Pads.TryGetValue(pad, out var P)) continue;
            const float step = 30f;
            for (float dx = -P.h.x + 14f; dx <= P.h.x - 14f; dx += step)
                for (float dz = -P.h.y + 14f; dz <= P.h.y - 14f; dz += step)
                {
                    int k = rng.Next(kinds.Length);
                    float x = P.c.x + dx + (float)(rng.NextDouble() * 4 - 2);
                    float z = P.c.z + dz + (float)(rng.NextDouble() * 4 - 2);
                    if (!CanPlace(x, z, halfSize[k], out float y, 6f)) continue;
                    var pos = new Vector3(x, Mathf.Min(y, P.c.y + 0.3f) - 0.15f, z);
                    float yaw = Mathf.Round(FaceRoadYaw(pos) / 90f) * 90f;   // grid-aligned blocks
                    PlaceWorld(kinds[k], townT, pos, yaw, 1f, 0.0015f, PastelRemap(colour++), hero: true);
                    placed++;
                    // courtyard palm + bougainvillea at the corner
                    var cp = pos + Quaternion.Euler(0, yaw, 0) * new Vector3(halfSize[k] + 1.5f, 0, -halfSize[k] + 2f);
                    if (CanPlace(cp.x, cp.z, 1f, out float cy))
                        PlaceWorld(rng.NextDouble() < 0.5 ? "Nagisa_FanPalm" : "Nagisa_CoconutPalm_B", townT,
                                   new Vector3(cp.x, cy - 0.1f, cp.z), (float)rng.NextDouble() * 360f,
                                   0.8f + (float)rng.NextDouble() * 0.3f);
                    var bp = pos + Quaternion.Euler(0, yaw, 0) * new Vector3(-halfSize[k] + 1f, 0, -halfSize[k] - 1.5f);
                    if (CanPlace(bp.x, bp.z, 0.5f, out float by))
                        PlaceWorld("Nagisa_Bougainvillea", townT, new Vector3(bp.x, by - 0.05f, bp.z),
                                   (float)rng.NextDouble() * 360f, 1.1f, SmallPropCull);
                }
        }
        Debug.Log($"[nagisa] town: {placed} pastel mid-rises");
    }

    private static void BuildBeachShops(Transform group)
    {
        var shopT = new GameObject("Beach Shops").transform;
        shopT.SetParent(group, false);
        if (!_route.Pads.TryGetValue("cafes", out var P)) return;
        var stems = new[] { "Nagisa_BeachCafe", "Nagisa_SurfShop", "Nagisa_BeachCafe", "Nagisa_SurfShop" };
        int placed = 0;
        for (int k = 0; k < stems.Length; k++)
        {
            float x = P.c.x - P.h.x + 20f + k * ((2f * P.h.x - 40f) / (stems.Length - 1));
            float z = P.c.z;
            if (!CanPlace(x, z, 9f, out float y, 3f)) continue;
            var pos = new Vector3(x, y - 0.1f, z);
            PlaceWorld(stems[k], shopT, pos, FaceRoadYaw(pos), 1f, 0.0015f, PastelRemap(k + 2), hero: true);
            placed++;
        }
        Debug.Log($"[nagisa] beach shops: {placed}");
    }

    // ================================================================= marina

    private static void BuildMarina(Transform group)
    {
        var marT = new GameObject("Nagisa Marina").transform;
        _marinaBerthsSet = false;
        marT.SetParent(group, false);
        if (!_route.Pads.TryGetValue("marina_quay", out var quay)) return;
        // Walk seaward from the quay to the first water > 2.5 m deep (Height < -2.5).
        Vector3 dir = Vector3.zero; float best = float.MaxValue;
        for (int a = 0; a < 36; a++)
        {
            var d = Quaternion.Euler(0, a * 10f, 0) * Vector3.forward;
            for (float r = 20f; r < 400f; r += 10f)
            {
                var q = quay.c + d * r;
                if (_ground.Height(q.x, q.z) < -2.5f && _ground.Coast(q.x, q.z) < -15f)
                { if (r < best) { best = r; dir = d; } break; }
            }
        }
        if (dir == Vector3.zero) { Debug.LogWarning("[nagisa] marina: no deep water near the quay"); return; }
        var perp = Vector3.Cross(Vector3.up, dir);
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        var rng = new System.Random(5701);
        int pontoons = 0, boats = 0;
        for (int k = -2; k <= 2; k++)
        {
            var c = quay.c + dir * (best + 22f) + perp * (k * MarinaPontoonGapM);
            if (_ground.Height(c.x, c.z) > -1.5f) continue;
            var pc = new Vector3(c.x, 0f, c.z);
            var pont = PlaceWorld("Nagisa_Pontoon", marT, pc, yaw, 1f, 0.0015f, null, hero: true);
            pontoons++;
            if (pont == null) continue;
            // six fingers on the +x side; boats berth between fingers
            for (int f = 0; f < 6; f++)
            {
                if (rng.NextDouble() < 0.08) continue;
                float lz = -20f + 4f + f * 6.4f + 3.2f;
                bool sail = rng.NextDouble() < 0.45;
                var local = new Vector3(1.5f + 6.2f, 0f, lz);
                var w = pont.transform.TransformPoint(local);
                if (_ground.Height(w.x, w.z) > -1.2f) continue;
                float byaw = yaw + 90f + (rng.NextDouble() < 0.5 ? 0f : 180f);
                PlaceWorld(sail ? "Nagisa_Sailboat" : "Nagisa_MotorYacht", marT, new Vector3(w.x, 0.02f, w.z),
                           byaw + (float)(rng.NextDouble() * 3 - 1.5), sail ? 0.95f : 0.78f, 0.0015f, null, hero: true);
                boats++;
            }
        }
        if (pontoons > 0) { BuildMarinaPier(marT, quay.c, dir, perp, best); _marinaBerths = quay.c + dir * (best + 22f); _marinaBerthsSet = true; }
        // pass 2: the marina clubhouse / restaurant on the quay, beside the pier root, terrace (front, local -z) to the water
        {
            var f = FootOf("Nagisa_B_MarinaClub");
            foreach (float side in new[] { 1f, -1f })
            {
                var cpos = quay.c + perp * (side * (f.hx + 6f)) - dir * 2f;
                if (!CanPlace(cpos.x, cpos.z, f.hz, out float cy, 1f)) continue;
                float cyaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;   // local -z -> dir (the water)
                var club = PlaceWorld("Nagisa_B_MarinaClub", marT, new Vector3(cpos.x, cy - 0.1f, cpos.z), cyaw, 1f, 0.0015f, NbColourway(0), hero: true);
                if (club == null) continue;
                var front = club.transform.rotation * Vector3.back;
                for (int k = 0; k < 6; k++)
                    _pSpots.Add(PS(club.transform.position + front * (f.hz + 1.5f + (k % 2) * 1.2f) + perp * ((k - 2.5f) * 1.6f), -front,
                                   "MarinaDiner", (k & 1) == 0 ? MinatoCrowdActor.MotionKind.Sit : MinatoCrowdActor.MotionKind.Idle));
                Debug.Log($"[nagisa] marina clubhouse placed ({(side > 0 ? "+" : "-")}perp side).");
                break;
            }
        }
        // a few larger yachts at anchor further out
        for (int k = 0; k < 4; k++)
        {
            var c = quay.c + dir * (best + 110f + k * 35f) + perp * ((k - 1.5f) * 60f);
            if (_ground.Height(c.x, c.z) > -3f) continue;
            PlaceWorld("Nagisa_MotorYacht", marT, new Vector3(c.x, 0.02f, c.z), yaw + k * 47f, 1.15f,
                       0.0015f, null, hero: true);
            boats++;
        }
        Debug.Log($"[nagisa] marina: {pontoons} pontoons, {boats} boats (dir {yaw:0} deg, {best:0} m out)");
    }

    /// <summary>
    /// The quay sits behind a ~100 m shallow reef, so the berths float out at the drop-off; a teak
    /// main pier on piles runs from the quay's shore out to a cross walkway that ties every pontoon
    /// spine together (the spines' near ends sit at <paramref name="best"/> + 2 m). Deck top 0.55 m,
    /// piles to -3 m. One merged mesh, converged by name like everything else under the root.
    /// </summary>
    private static void BuildMarinaPier(Transform marT, Vector3 quay, Vector3 dir, Vector3 perp, float best)
    {
        float shore = 0f;
        for (float r = 0f; r < best; r += 2f)
        {
            var q = quay + dir * r;
            if (_ground.Height(q.x, q.z) < 0.05f) { shore = r; break; }
        }
        float s0 = Mathf.Max(0f, shore - 10f), s1 = best + 2.5f;
        while (s0 < shore + 5f && _route.PlanDistance((quay + dir * s0).x, (quay + dir * s0).z, out _) < CorridorKeepOutM + 1f) s0 += 1f;
        const float deckTop = 0.55f, deckT = 0.28f, pierW = 3.2f;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        var up = Vector3.up;
        // main pier (landward end rests on the ground if the quay edge is higher than the deck)
        var mid = quay + dir * ((s0 + s1) * 0.5f);
        float yTop = Mathf.Max(deckTop, _ground.Height((quay + dir * s0).x, (quay + dir * s0).z) + 0.05f);
        Box(v, uv, tri, new Vector3(mid.x, yTop - deckT * 0.5f, mid.z), perp, dir, pierW, deckT, s1 - s0);
        // cross walkway along the pontoon heads
        var head = quay + dir * s1;
        float span = 2f * 2f * MarinaPontoonGapM + 4f;
        Box(v, uv, tri, new Vector3(head.x, deckTop - deckT * 0.5f, head.z), perp, dir, span, deckT, 2.6f);
        // piles every 6 m under both, and rails as low kerbs on the main pier
        for (float s = s0 + 3f; s < s1; s += 6f)
            foreach (float sd in new[] { -1f, 1f })
            {
                var p = quay + dir * s + perp * (sd * (pierW * 0.5f - 0.2f));
                if (_ground.Height(p.x, p.z) > deckTop - 0.3f) continue;
                Box(v, uv, tri, new Vector3(p.x, (deckTop - deckT - 3f) * 0.5f, p.z), perp, dir, 0.28f, 3f + deckTop - deckT, 0.28f);
            }
        for (float o = -span * 0.5f + 2f; o < span * 0.5f; o += 6f)
        {
            var p = head + perp * o - dir * 1.1f;
            Box(v, uv, tri, new Vector3(p.x, (deckTop - deckT - 3f) * 0.5f, p.z), perp, dir, 0.28f, 3f + deckTop - deckT, 0.28f);
        }
        foreach (float sd in new[] { -1f, 1f })
        {
            var r = quay + dir * ((s0 + s1) * 0.5f) + perp * (sd * (pierW * 0.5f - 0.08f));
            Box(v, uv, tri, new Vector3(r.x, yTop + 0.09f, r.z), perp, dir, 0.16f, 0.18f, s1 - s0);
        }
        var mesh = Finish("Nagisa_MarinaPier", v, uv, tri);
        var go = AddMesh(marT, "Nagisa Marina Pier", mesh, _nb["NB_PontoonWood"], true);
        Debug.Log($"[nagisa] marina pier: {s1 - s0:0} m main pier + {span:0} m head walkway, {tri.Count / 3:N0} tris (shore at {shore:0} m).");
    }

    // ================================================================= forest

    private class FarBatch
    {
        public readonly Dictionary<Material, List<CombineInstance>> ByMat =
            new Dictionary<Material, List<CombineInstance>>();
    }

    private static void BuildForest(Transform group)
    {
        var forT = new GameObject("Tropical Forest").transform;
        forT.SetParent(group, false);
        var farT = new GameObject("Far Forest (merged)").transform;
        farT.SetParent(forT, false);
        var rng = new System.Random(5801);

        var g = _ground;
        float x0 = g.X0, z0 = g.Z0, x1 = g.X0 + g.Nx * g.Cell, z1 = g.Z0 + g.Nz * g.Cell;
        int near = 0, far = 0;
        var batches = new Dictionary<(int, int), FarBatch>();
        var lod2 = new Dictionary<string, (Mesh mesh, Material[] mats)>();

        for (float x = x0; x < x1; x += NearTreeGridM)
            for (float z = z0; z < z1; z += NearTreeGridM)
            {
                float jx = x + (float)(rng.NextDouble() - 0.5) * NearTreeGridM * 0.8f;
                float jz = z + (float)(rng.NextDouble() - 0.5) * NearTreeGridM * 0.8f;
                int cls = g.ClassAt(jx, jz);
                if (cls != 3 && cls != 2) continue;
                float road = _route.PlanDistance(jx, jz, out _);
                bool isNear = road < NearForestM;
                double fill = cls == 3 ? (isNear ? 0.80 : 0.50) : (isNear ? 0.08 : 0.03);
                if (rng.NextDouble() > fill) continue;
                if (InAnyPad(jx, jz, 12f)) continue;
                if (InTown(jx, jz) || InTown(jx + 7f, jz) || InTown(jx - 7f, jz) || InTown(jx, jz + 7f) || InTown(jx, jz - 7f)) continue;
                if (!CanPlace(jx, jz, 6.5f, out float y, 40f)) continue;
                if (y < 6f && cls != 3) continue;

                double pick = rng.NextDouble();
                string stem = cls == 2 ? (pick < 0.6 ? "Nagisa_CoconutPalm_A" : "Nagisa_FanPalm")
                    : pick < 0.45 ? "Nagisa_CanopyTree_A" : pick < 0.8 ? "Nagisa_CanopyTree_B"
                    : pick < 0.9 ? "Nagisa_FanPalm" : "Nagisa_CoconutPalm_A";
                float s = 0.8f + (float)rng.NextDouble() * 0.45f;
                float yaw = (float)rng.NextDouble() * 360f;
                var pos = new Vector3(jx, y - 0.15f, jz);
                if (isNear)
                {
                    PlaceWorld(stem, forT, pos, yaw, s);
                    near++;
                }
                else
                {
                    if (!lod2.TryGetValue(stem, out var lm)) lod2[stem] = lm = Lod2Of(stem);
                    if (lm.mesh == null) continue;
                    var key = (Mathf.FloorToInt(jx / FarChunkM), Mathf.FloorToInt(jz / FarChunkM));
                    if (!batches.TryGetValue(key, out var b)) batches[key] = b = new FarBatch();
                    var mtx = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one * s);
                    for (int sm = 0; sm < lm.mesh.subMeshCount && sm < lm.mats.Length; sm++)
                    {
                        if (!b.ByMat.TryGetValue(lm.mats[sm], out var list)) b.ByMat[lm.mats[sm]] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = lm.mesh, subMeshIndex = sm, transform = mtx });
                    }
                    far++;
                }
            }

        long farTris = 0;
        foreach (var kv in batches)
        {
            foreach (var mk in kv.Value.ByMat)
            {
                var mesh = new Mesh { name = $"Nagisa_FarForest_{kv.Key.Item1}_{kv.Key.Item2}_{mk.Key.name}" };
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(mk.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                farTris += mesh.triangles.LongLength / 3;
                var go = AddMesh(farT, mesh.name, mesh, mk.Key, false);
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;   // far field: no shadows (>150 m)
            }
        }
        Debug.Log($"[nagisa] forest: {near} instanced near-road trees, {far} merged far trees " +
                  $"({batches.Count} chunks, {farTris:N0} tris)");
    }

    /// <summary>The LOD2 mesh of a GLB and its remapped materials, for far-field merging.</summary>
    private static (Mesh mesh, Material[] mats) Lod2Of(string stem)
    {
        var src = Glb(stem);
        if (src == null) return (null, null);
        foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.name.EndsWith("_LOD2")) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            var mats = mr.sharedMaterials;
            var outM = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string slot = mats[i] != null ? SlotName(mats[i].name) : null;
                outM[i] = slot != null && _nb.TryGetValue(slot + "_Far", out var fm) ? fm
                        : slot != null && _nb.TryGetValue(slot, out var m) ? m : mats[i];
            }
            // the GLB child may carry its own local transform; bake it
            var local = mf.transform.localToWorldMatrix * src.transform.worldToLocalMatrix;
            if (local.isIdentity) return (mf.sharedMesh, outM);
            var ci = new CombineInstance[mf.sharedMesh.subMeshCount];
            for (int s = 0; s < ci.Length; s++) ci[s] = new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = local };
            var m2 = new Mesh { name = mf.sharedMesh.name + "_baked" };
            m2.CombineMeshes(ci, false, true);
            return (m2, outM);
        }
        Debug.LogWarning($"[nagisa] {stem}: no _LOD2 node for far-field merge");
        return (null, null);
    }

    // ================================================================= coast road

    private static void BuildCoastRoadDressing(Transform group)
    {
        var coastT = new GameObject("Coast Road Dressing").transform;
        coastT.SetParent(group, false);
        int i0 = _route.IndexAt(_route.CoastStartM), i1 = _route.IndexAt(_route.BridgeStartM);
        float lastRail = -999f, lastPalm = -999f;
        int rails = 0, palms = 0;
        var rng = new System.Random(5901);
        for (int i = i0; i < i1; i++)
        {
            float d = _route.Distance[i];
            var p = _route.Position[i];
            var sd = _route.SideFlat(i) * SeaSign(i);
            // guard rail only where the seaward verge actually drops away toward the sea
            if (d - lastRail >= 4f)
            {
                var q = p + sd * CoastRailOffsetM;
                float gy = _ground.Height(q.x, q.z);
                var far = p + sd * (CoastRailOffsetM + 12f);
                bool drop = _ground.Height(far.x, far.z) < p.y - 3f && _ground.Coast(q.x, q.z) > 1f;
                if (drop && gy > p.y - 1.5f)
                {
                    lastRail = d;
                    var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
                    float yaw = Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg - 90f;
                    PlaceWorld("Nagisa_Railing", coastT, new Vector3(q.x, gy - 0.05f, q.z), yaw, 1f, SmallPropCull);
                    rails++;
                }
            }
            if (d - lastPalm >= 22f)
            {
                lastPalm = d;
                var pp = p - sd * (CorridorKeepOutM + 3f + (float)rng.NextDouble() * 6f);
                if (CanPlace(pp.x, pp.z, 1f, out float py) && !InAnyPad(pp.x, pp.z, 3f))
                {
                    PlaceWorld(rng.NextDouble() < 0.6 ? "Nagisa_CoconutPalm_B" : "Nagisa_CoconutPalm_A", coastT,
                               new Vector3(pp.x, py - 0.1f, pp.z), (float)rng.NextDouble() * 360f,
                               0.85f + (float)rng.NextDouble() * 0.3f);
                    palms++;
                }
                if (i % 2 == 0)
                {
                    var hp = p - sd * (CorridorKeepOutM + 1.2f);
                    if (CanPlace(hp.x, hp.z, 0.3f, out float hy))
                        PlaceWorld("Nagisa_Hibiscus", coastT, new Vector3(hp.x, hy - 0.05f, hp.z),
                                   (float)rng.NextDouble() * 360f, 1f, SmallPropCull);
                }
            }
        }
        Debug.Log($"[nagisa] coast road: {rails} rail sections, {palms} palms");
    }
}
