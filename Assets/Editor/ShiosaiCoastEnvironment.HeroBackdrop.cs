using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HERO BACKDROP (claude-cowork, 2026-09-25). Places the high-poly background landforms built
/// by tools/blender/build_shiosai_backdrop_hero.py:
///
///   Headlands  inland side, 450-1000 m out, one every ~2.6-3.6 km, cliff face turned to the road
///   Islets     sea side, 320-1500 m offshore, one every ~1.8-3.0 km
///   Massifs    inland side, 2.3-4.6 km out, one every ~6-8 km, crest facing the coast
///
/// WHY. QA of the chapter captures: the far ranges are smooth procedural ridges, the land past
/// the headland is empty, and the horizon has no hero shapes. These are real landforms
/// (strata-banded cliffs, gullies, ridged crests, skerries) at 14k-40k tris each, with an
/// LOD1 at ~25 % used beyond ~20 % screen height and never culled (they ARE the horizon).
///
/// Everything is deterministic (own rng), idempotent (one exact-named group under the region
/// root, which Apply rebuilds from scratch), colliderless, and steers clear of: the route
/// (per-footprint clearance), the wrong side of a route that doubles back (IsInland), and any
/// stack / island / lighthouse / harbour already staged by BuildLandmarks.
/// Missing GLBs are skipped with one warning naming the script to run.
/// </summary>
public static partial class ShiosaiCoastEnvironment
{
    private const string HeroBackdropGroup = "Coast Hero Backdrop";

    // name, footprint width (x), depth (z), kind. Must match shiosai_backdrop_terrain.ASSETS.
    private static readonly (string asset, float w, float d, int kind)[] HeroAssets =
    {
        ("Shiosai_Hero_Headland_A", 700f, 420f, 0), ("Shiosai_Hero_Headland_B", 620f, 420f, 0),
        ("Shiosai_Hero_Headland_C", 820f, 460f, 0),
        ("Shiosai_Hero_Islet_A", 260f, 200f, 1), ("Shiosai_Hero_Islet_B", 200f, 170f, 1),
        ("Shiosai_Hero_Islet_C", 320f, 220f, 1),
        ("Shiosai_Hero_Massif_A", 3200f, 1600f, 2), ("Shiosai_Hero_Massif_B", 2800f, 1600f, 2),
    };

    // PROVISIONAL tuning, per kind: stride min/max along the route, offset min/max from the
    // centreline, clearance every footprint sample must keep from ANY part of the route.
    private static readonly (float strideLo, float strideHi, float offLo, float offHi, float clear)[] HeroRules =
    {
        (2600f, 3600f, 450f, 1000f, 140f),     // headland
        (1800f, 3000f, 320f, 1500f, 160f),     // islet
        (6000f, 8000f, 2300f, 4600f, 1400f),   // massif
    };

    private static void BuildHeroBackdrop(Transform root, CoastRoute route)
    {
        var group = new GameObject(HeroBackdropGroup).transform;
        group.SetParent(root, false);

        // Footprints already taken by authored landmarks (sea stacks, finale islands, the
        // lighthouse promontory, the harbour) - collected by name from what Apply staged.
        var taken = new List<(Vector2 c, float r)>();
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = mr.gameObject.name.ToLowerInvariant();
            if (!(n.Contains("stack") || n.Contains("island") || n.Contains("islet") ||
                  n.Contains("lighthouse") || n.Contains("harbour"))) continue;
            var b = mr.bounds;
            taken.Add((new Vector2(b.center.x, b.center.z), Mathf.Max(b.extents.x, b.extents.z) + 40f));
        }

        var rng = new System.Random(ScatterSeed + 1201);
        int placed = 0, missing = 0;
        for (int kind = 0; kind < 3; kind++)
        {
            var rule = HeroRules[kind];
            var pool = new List<(string asset, float w, float d, int kind)>();
            foreach (var a in HeroAssets) if (a.kind == kind) pool.Add(a);

            for (float d = Lerp(rule.strideLo * 0.3f, rule.strideLo * 0.8f, rng);
                 d < route.Length; d += Lerp(rule.strideLo, rule.strideHi, rng))
            {
                var pick = pool[rng.Next(pool.Count)];
                int i = route.IndexAt(d);
                var p = route.Position[i];
                var s = route.SideFlat(i);                        // +s = inland
                float side = kind == 1 ? -1f : 1f;               // islets go seaward
                float scale = Lerp(0.85f, 1.15f, rng);
                float yawJitter = Lerp(-18f, 18f, rng);

                bool ok = false;
                Vector3 centre = Vector3.zero, away = Vector3.zero;
                // try a few offsets, nearest first, so a tight fold of the route pushes the
                // landform out instead of deleting it
                for (int attempt = 0; attempt < 4 && !ok; attempt++)
                {
                    float off = Mathf.Lerp(rule.offLo, rule.offHi, (attempt + (float)rng.NextDouble()) / 4f);
                    away = new Vector3(s.x * side, 0f, s.z * side).normalized;
                    centre = new Vector3(p.x + away.x * off, SeaLevelY, p.z + away.z * off);
                    ok = HeroFootprintClear(route, centre, away, pick.w * scale, pick.d * scale,
                                            rule.clear, kind == 1, taken);
                }
                if (!ok) continue;

                // local -Z faces the road: local +Z points AWAY from it
                float yaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg + yawJitter;
                var go = PlaceHero(pick.asset, $"Hero {pick.asset} {placed:00}", group,
                                   centre + Vector3.up * (kind == 2 ? -20f : -2f), yaw, scale, kind == 2);
                if (go == null) { missing++; continue; }
                taken.Add((new Vector2(centre.x, centre.z), 0.5f * Mathf.Max(pick.w, pick.d) * scale));
                placed++;
            }
        }
        Debug.Log($"[shiosai] hero backdrop: {placed} landforms placed" +
                  (missing > 0 ? $", {missing} skipped (missing GLB - run blender -b --factory-startup -P tools/blender/build_shiosai_backdrop_hero.py)" : "") + ".");
    }

    private static float Lerp(float a, float b, System.Random rng) => Mathf.Lerp(a, b, (float)rng.NextDouble());

    /// <summary>Samples the rotated footprint (centre, corners, edge midpoints).</summary>
    private static bool HeroFootprintClear(CoastRoute route, Vector3 c, Vector3 fwd, float w, float d,
                                           float clear, bool seaward, List<(Vector2 c, float r)> taken)
    {
        var right = new Vector3(fwd.z, 0f, -fwd.x);
        for (int ix = -1; ix <= 1; ix++)
        for (int iz = -1; iz <= 1; iz++)
        {
            var q = c + right * (ix * w * 0.5f) + fwd * (iz * d * 0.5f);
            if (RouteClearance(route, q.x, q.z, clear) < clear) return false;
            if (IsInland(route, q.x, q.z) == seaward) return false;
        }
        float rad = 0.5f * Mathf.Max(w, d);
        foreach (var t in taken)
            if ((t.c - new Vector2(c.x, c.z)).sqrMagnitude < (t.r + rad) * (t.r + rad)) return false;
        return true;
    }

    /// <summary>LOD0 + LOD1 under one LODGroup; LOD1 is never culled (it is the horizon).</summary>
    private static GameObject PlaceHero(string asset, string displayName, Transform parent,
                                        Vector3 pos, float yaw, float scale, bool farOnly)
    {
        var holder = new GameObject(displayName);
        holder.transform.SetParent(parent, false);
        holder.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        holder.transform.localScale = Vector3.one * scale;

        var lod0 = Place(asset, "LOD0", holder.transform, pos, yaw, 1f);
        if (lod0 == null) { Object.DestroyImmediate(holder); return null; }
        lod0.transform.localPosition = Vector3.zero;
        lod0.transform.localRotation = Quaternion.identity;
        lod0.transform.localScale = Vector3.one;

        GameObject lod1 = null;
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetDir}/{asset}_LOD1.glb") != null)
        {
            lod1 = Place(asset + "_LOD1", "LOD1", holder.transform, pos, yaw, 1f);
            lod1.transform.localPosition = Vector3.zero;
            lod1.transform.localRotation = Quaternion.identity;
            lod1.transform.localScale = Vector3.one;
        }

        foreach (var t in holder.GetComponentsInChildren<Transform>(true))
            t.gameObject.isStatic = true;
        foreach (var r in holder.GetComponentsInChildren<MeshRenderer>(true))
            r.shadowCastingMode = farOnly ? UnityEngine.Rendering.ShadowCastingMode.Off
                                          : UnityEngine.Rendering.ShadowCastingMode.On;

        if (lod1 != null)
        {
            var group = holder.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(farOnly ? 0.35f : 0.20f, lod0.GetComponentsInChildren<Renderer>(true)),
                new LOD(0f, lod1.GetComponentsInChildren<Renderer>(true)),   // never culled
            });
            group.RecalculateBounds();
        }
        return holder;
    }
}
