using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// PERMANENT validation gate for B5 NAGISA BAY (copilot, 2026-09-27; modelled on
/// MinatoCoastValidation). Renders answer "does it look right"; this answers "does it hold up",
/// and every check reports a measured value against a named tolerance.
///
///   1. ROUTE     - samples monotonic, 12-18 km, a real climb (sustained grade) and the KOM.
///   2. SURFACE   - downward raycast every 2 m along the centreline: the first hit must be the
///                  Nagisa road collider at the published height (no holes, nothing on the road).
///   3. CORRIDOR  - no renderer under the root intrudes into the ride lane + shoulder between
///                  0.1 m and 2.6 m above the road (props, palms, people, boats).
///   4. MATERIALS - no null / error-shader materials; only the shared MapleRide HDRP shaders
///                  (+ HDRP/Lit for glass and pool water).
///   5. ASSETS    - >= 8 distinct Blender GLBs placed from Assets/Environment/NagisaBay/Models,
///                  every instanced GLB has a 3-level LODGroup with a cull distance.
///   6. LIFE      - people on foot: count, nobody within the road corridor, civilian looks, not
///                  helmeted; riders: the full roster staged; boats: every path point in open
///                  water > BoatRoadClear from the road.
///   7. REGISTRY  - RegionCatalog row unlocked + BuiltCourseId/EnvironmentRoot, the baked graph
///                  publishes nagisa_bay_loop in region nagisa_bay with checkpoints.
///
///   run_steps.ps1 "NagisaBayValidation.Run|copilot_b5_val1.log|1"
/// All tolerances are PROVISIONAL.
/// </summary>
public static class NagisaBayValidation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    private const float MinCourseM = 12000f, MaxCourseM = 18000f;
    /// <summary>"A real climb": some 1 km window must average at least this grade (%).</summary>
    private const float ClimbGradePct = 6f;
    private const float SurfaceStrideM = 2f;
    /// <summary>First hit must lie within this of the published centreline height (m).</summary>
    private const float SurfaceTolM = 0.20f;
    private const float LaneHalfM = NagisaBayEnvironment.RoadHalfWidth + 0.6f;
    private const float EnvelopeLowM = 0.10f, EnvelopeHighM = 2.6f;
    private const float PersonKeepOutM = 6.5f;
    private const float BoatRoadClearM = 60f;
    private const int MinGlbs = 8, MinPeople = 20, ExpectedRiders = 13, MinCheckpoints = 4;

    private static readonly string[] AllowedShaders =
    {
        "MapleRide/HDRP/CelLit", "MapleRide/HDRP/Terrain", "MapleRide/HDRP/Road", "MapleRide/HDRP/Ocean",
        "MapleRide/HDRP/Shallows", "MapleRide/HDRP/Foliage", "HDRP/Lit",
    };
    // Generated ground-hugging surfaces that ARE the road / lie flush with the ground.
    private static readonly string[] SurfacePrefixes =
    {
        "Road", "Terrain_Chunk", "Shallows", "Ocean", "Viaduct", "Nagisa_FarForest", "Promenade Paving",
        "Hotel Driveway", "Nagisa Marina Pier", "Beach", "Sand",
        "Town Ground", "Boardwalk",          // pass 2: flush street / sidewalk / kerb / lot surfaces
    };

    [MenuItem("MapleRide/Diagnostics/Validate Nagisa Bay")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // The root must be visible for GameObject.Find and physics; restore afterwards.
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = NagisaBayEnvironment.RegionId;
            regions.ApplyEnvironmentVisibility();
        }
        var log = new StringBuilder();
        log.AppendLine("=== NAGISA BAY VALIDATION ===");
        bool ok = true;
        try
        {
            var root = GameObject.Find(NagisaBayEnvironment.RootName);
            if (root == null)
            {
                log.AppendLine($"[root] '{NagisaBayEnvironment.RootName}': [FAIL] not in scene - run NagisaBayEnvironment.Apply");
                ok = false;
            }
            else
            {
                Physics.SyncTransforms();
                var route = NagisaBayEnvironment.NagisaRoute.Load();
                var ground = NagisaBayEnvironment.NagisaGround.Load();
                ok &= CheckRoute(route, log);
                ok &= CheckSurface(route, log);
                ok &= CheckCorridor(root, route, log);
                ok &= CheckMaterials(root, log);
                ok &= CheckAssets(root, log);
                ok &= CheckLife(root, route, ground, log);
            }
            ok &= CheckRegistry(log);
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previous))
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
            }
        }
        log.AppendLine(ok ? "[nagisa-val] RESULT: PASS" : "[nagisa-val] RESULT: FAIL - see [FAIL] items above");
        foreach (var line in log.ToString().Split('\n'))
            if (line.Trim().Length > 0) Debug.Log("[nagisa-val] " + line.TrimEnd());
        if (!ok) Debug.LogError("[nagisa-val] validation FAILED");
    }

    private static string Mark(bool ok) => ok ? "[ok]" : "[FAIL]";

    // ------------------------------------------------------------------ 1. route
    private static bool CheckRoute(NagisaBayEnvironment.NagisaRoute r, StringBuilder log)
    {
        int nonMono = 0;
        for (int i = 1; i < r.Count; i++) if (r.Distance[i] <= r.Distance[i - 1]) nonMono++;
        bool lenOk = r.Length >= MinCourseM && r.Length <= MaxCourseM && nonMono == 0;
        log.AppendLine($"[route] {r.Count:N0} samples, {r.Length:N0} m, non-monotonic {nonMono}: {Mark(lenOk)} (want {MinCourseM:N0}-{MaxCourseM:N0} m)");

        float bestGrade = 0f, bestAt = 0f;
        for (int i = 0; i < r.Count; i += 4)
        {
            int j = r.IndexAt(r.Distance[i] + 1000f);
            if (j <= i || r.Distance[j] - r.Distance[i] < 900f) break;
            float g = (r.Position[j].y - r.Position[i].y) / (r.Distance[j] - r.Distance[i]) * 100f;
            if (g > bestGrade) { bestGrade = g; bestAt = r.Distance[i]; }
        }
        int kom = r.IndexAt(r.KomM);
        bool komOk = r.KomM > 0f && Mathf.Abs(r.Position[kom].y - r.MaxY) < 25f;
        bool climbOk = bestGrade >= ClimbGradePct;
        log.AppendLine($"[route] steepest 1 km: {bestGrade:0.0}% from {bestAt:N0} m: {Mark(climbOk)} (want >= {ClimbGradePct}%)");
        log.AppendLine($"[route] KOM at {r.KomM:N0} m, y {r.Position[kom].y:0.0} (route max {r.MaxY:0.0}): {Mark(komOk)}");
        return lenOk && climbOk && komOk;
    }

    // ------------------------------------------------------------------ 2. surface
    private static bool CheckSurface(NagisaBayEnvironment.NagisaRoute r, StringBuilder log)
    {
        int holes = 0, blocked = 0, off = 0, n = 0;
        string firstBad = null;
        for (float m = 1f; m < r.Length - 1f; m += SurfaceStrideM)
        {
            int i = r.IndexAt(m);
            var p = r.Position[i];
            n++;
            var hits = Physics.RaycastAll(p + Vector3.up * 4f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) { holes++; firstBad = firstBad ?? $"hole at {m:0} m"; continue; }
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var h = hits[0];
            if (!h.collider.name.StartsWith("Road"))
            {
                // terrain poking up through the road is a hole too; anything else is a blocker
                blocked++; firstBad = firstBad ?? $"'{h.collider.name}' on the road at {m:0} m";
                continue;
            }
            if (Mathf.Abs(h.point.y - p.y) > SurfaceTolM) { off++; firstBad = firstBad ?? $"road {h.point.y - p.y:+0.00;-0.00} m off at {m:0} m"; }
        }
        bool ok = holes == 0 && blocked == 0 && off == 0;
        log.AppendLine($"[surface] {n:N0} rays every {SurfaceStrideM} m: holes {holes}, first-hit not road {blocked}, " +
                       $"off-height {off} (tol {SurfaceTolM} m): {Mark(ok)}{(firstBad != null ? " first: " + firstBad : "")}");
        return ok;
    }

    // ------------------------------------------------------------------ 3. corridor
    private static bool CheckCorridor(GameObject root, NagisaBayEnvironment.NagisaRoute r, StringBuilder log)
    {
        int checkedN = 0, bad = 0;
        var worst = new List<string>();
        foreach (var mr in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!mr.enabled) continue;
            string n = mr.name;
            if (SurfacePrefixes.Any(s => n.StartsWith(s)) || (mr.transform.parent != null &&
                SurfacePrefixes.Any(s => mr.transform.parent.name.StartsWith(s)))) continue;
            var b = mr.bounds;
            if (b.size.sqrMagnitude < 1e-6f) continue;
            checkedN++;
            r.PlanDistance(b.center.x, b.center.z, out int near);
            float reach = Mathf.Max(b.extents.x, b.extents.z);
            if (new Vector2(b.center.x - r.Position[near].x, b.center.z - r.Position[near].z).magnitude - reach > LaneHalfM + 2f) continue;
            int lo = Mathf.Max(0, near - 60), hi = Mathf.Min(r.Count - 1, near + 60);
            bool aabbHit = false; int hitI = 0; float hitLat = 0f;
            for (int i = lo; i <= hi && !aabbHit; i++)
            {
                var c = r.Position[i];
                var q = b.ClosestPoint(new Vector3(c.x, b.center.y, c.z));
                // lateral distance of the closest point from the centreline, in the road's own frame
                var side = r.SideFlat(i);
                var d = q - c;
                float lat = Mathf.Abs(Vector3.Dot(d, side));
                float along = Mathf.Abs(Vector3.Dot(d, new Vector3(r.Tangent[i].x, 0f, r.Tangent[i].z).normalized));
                if (lat > LaneHalfM || along > 2.5f) continue;
                if (b.max.y < c.y + EnvelopeLowM || b.min.y > c.y + EnvelopeHighM) continue;
                aabbHit = true; hitI = i; hitLat = lat;
            }
            if (!aabbHit) continue;
            // The AABB of a palm or canopy tree spans trunk-to-crown, so it overlaps the lane box even
            // when only the high crown overhangs. Confirm with the real vertices where there is a mesh.
            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                if (!VerticesInLane(mf, r, lo, hi, out hitI, out hitLat)) continue;
            }
            bad++;
            if (worst.Count < 8) worst.Add($"'{Path(mr.transform)}' at {r.Distance[hitI]:0} m (lat {hitLat:0.0} m)");
        }
        bool ok = bad == 0;
        log.AppendLine($"[corridor] {checkedN:N0} renderers vs the {LaneHalfM:0.0} m lane, {EnvelopeLowM}-{EnvelopeHighM} m above the road: " +
                       $"{bad} intrusions: {Mark(ok)}");
        foreach (var w in worst) log.AppendLine("[corridor]    " + w);
        return ok;
    }

    private static readonly Dictionary<Mesh, Vector3[]> _verts = new Dictionary<Mesh, Vector3[]>();

    /// <summary>True if any real vertex of the mesh lies inside the lane box (lateral &lt; LaneHalf,
    /// within 2.5 m along a sample, EnvelopeLow..High above that sample).</summary>
    private static bool VerticesInLane(MeshFilter mf, NagisaBayEnvironment.NagisaRoute r, int lo, int hi,
                                       out int hitI, out float hitLat)
    {
        hitI = lo; hitLat = 0f;
        if (!_verts.TryGetValue(mf.sharedMesh, out var vs))
        {
            // AcquireReadOnlyMeshData also reads non-readable imported (GLB) meshes in the Editor.
            using (var data = Mesh.AcquireReadOnlyMeshData(mf.sharedMesh))
            {
                var arr = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                data[0].GetVertices(arr);
                vs = arr.ToArray();
                arr.Dispose();
            }
            _verts[mf.sharedMesh] = vs;
        }
        var m = mf.transform.localToWorldMatrix;
        for (int k = 0; k < vs.Length; k++)
        {
            var w = m.MultiplyPoint3x4(vs[k]);
            for (int i = lo; i <= hi; i++)
            {
                var c = r.Position[i];
                float dx = w.x - c.x, dz = w.z - c.z;
                if (dx * dx + dz * dz > 25f) continue;
                var d = new Vector3(dx, 0f, dz);
                float lat = Mathf.Abs(Vector3.Dot(d, r.SideFlat(i)));
                if (lat > LaneHalfM) continue;
                float dy = w.y - c.y;
                if (dy < EnvelopeLowM || dy > EnvelopeHighM) continue;
                hitI = i; hitLat = lat;
                return true;
            }
        }
        return false;
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        for (int k = 0; t != null && k < 4; k++, t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }

    // ------------------------------------------------------------------ 4. materials
    private static bool CheckMaterials(GameObject root, StringBuilder log)
    {
        int nullMats = 0, errorShader = 0, foreign = 0, total = 0;
        var foreignNames = new HashSet<string>();
        var seen = new HashSet<Material>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is SkinnedMeshRenderer || r.GetComponentInParent<MinatoCrowdActor>() != null) continue; // donor people: shared NPC shaders
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { nullMats++; continue; }
                if (!seen.Add(m)) continue;
                total++;
                var sh = m.shader;
                if (sh == null || sh.name == "Hidden/InternalErrorShader" || !sh.isSupported) { errorShader++; foreignNames.Add(m.name + " (error)"); continue; }
                if (!AllowedShaders.Contains(sh.name)) { foreign++; foreignNames.Add($"{m.name} ({sh.name})"); }
            }
        }
        bool ok = nullMats == 0 && errorShader == 0 && foreign == 0;
        log.AppendLine($"[materials] {total} distinct: null slots {nullMats}, error/unsupported {errorShader}, non-shared shaders {foreign}: {Mark(ok)}" +
                       (foreignNames.Count > 0 ? " -> " + string.Join(", ", foreignNames.Take(8)) : ""));
        return ok;
    }

    // ------------------------------------------------------------------ 5. assets
    private static bool CheckAssets(GameObject root, StringBuilder log)
    {
        var stems = new Dictionary<string, int>();
        int lodGroups = 0, badLod = 0;
        foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
        {
            if (g.GetComponentInParent<MinatoCrowdActor>() != null || g.GetComponentInChildren<MinatoCrowdActor>() != null)
                continue;   // Maple City donor people keep their own (2-level) crowd LODs
            lodGroups++;
            var lods = g.GetLODs();
            if (lods.Length < 3 || lods[lods.Length - 1].screenRelativeTransitionHeight <= 0f) badLod++;
        }
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (!path.StartsWith(NagisaBayEnvironment.ModelDir) || !path.EndsWith(".glb")) continue;
            string stem = System.IO.Path.GetFileNameWithoutExtension(path);
            stems[stem] = stems.TryGetValue(stem, out int c) ? c + 1 : 1;
        }
        bool ok = stems.Count >= MinGlbs && badLod == 0 && lodGroups > 0;
        log.AppendLine($"[assets] {stems.Count} distinct Blender GLBs placed (want >= {MinGlbs}), {lodGroups:N0} LODGroups, " +
                       $"{badLod} without 3 levels + cull: {Mark(ok)}");
        log.AppendLine("[assets]    " + string.Join(", ", stems.OrderBy(k => k.Key).Select(k => $"{k.Key} x{k.Value}")));
        return ok;
    }

    // ------------------------------------------------------------------ 6. life
    private static bool CheckLife(GameObject root, NagisaBayEnvironment.NagisaRoute r,
                                  NagisaBayEnvironment.NagisaGround g, StringBuilder log)
    {
        bool ok = true;
        var people = root.GetComponentsInChildren<MinatoCrowdActor>(true);
        int onRoad = 0, pathOnRoad = 0, notCivil = 0, nb = 0;
        string firstBad = null;
        foreach (var p in people)
        {
            float d = r.PlanDistance(p.transform.position.x, p.transform.position.z, out _);
            if (d < PersonKeepOutM) { onRoad++; firstBad = firstBad ?? $"{p.name} {d:0.0} m from the centreline"; }
            if (p.motion == MinatoCrowdActor.MotionKind.Walk)
                for (int k = 0; k <= 8; k++)
                {
                    var q = Vector3.Lerp(p.pathStart, p.pathEnd, k / 8f);
                    if (r.PlanDistance(q.x, q.z, out _) < PersonKeepOutM) { pathOnRoad++; firstBad = firstBad ?? $"{p.name} walk path crosses the road"; break; }
                }
            var look = p.GetComponent<MapleCityLook>();
            if (look == null) look = p.GetComponentInParent<MapleCityLook>();
            if (look == null || string.IsNullOrEmpty(look.look) || !look.civilianAtlas) notCivil++;
            else if (look.look.StartsWith("NB")) nb++;
        }
        bool pOk = people.Length >= MinPeople && onRoad == 0 && pathOnRoad == 0 && notCivil == 0;
        log.AppendLine($"[life] {people.Length} people on foot (want >= {MinPeople}), NB resort looks {nb}, not civilian {notCivil}, " +
                       $"on road {onRoad}, walk paths over road {pathOnRoad}: {Mark(pOk)}{(firstBad != null ? " first: " + firstBad : "")}");
        ok &= pOk;

        var ridersRoot = GameObject.Find("Nagisa NPCs");
        int riders = 0;
        if (ridersRoot != null)
            foreach (var c in ridersRoot.GetComponentsInChildren<NPCCyclist>(true))
                if (c.name.StartsWith("Nagisa NPC ")) riders++;
        bool rOk = riders == ExpectedRiders;
        log.AppendLine($"[life] riders under 'Nagisa NPCs': {riders} (want {ExpectedRiders}): {Mark(rOk)}");
        ok &= rOk;

        var boats = root.GetComponentsInChildren<AmbientPathMover>(true);
        int nBoats = boats.Length, badPts = 0, pts = 0;
        float maxCull = 0f;
        foreach (var b in boats)
        {
            maxCull = Mathf.Max(maxCull, b.cullDistance);
            foreach (var p in b.path)
            {
                pts++;
                if (g.Height(p.x, p.z) > -2f || r.PlanDistance(p.x, p.z, out _) < BoatRoadClearM) badPts++;
            }
        }
        bool bOk = nBoats >= 1 && badPts == 0 && maxCull <= 1500f && maxCull > 0f;
        log.AppendLine($"[life] ambient boats (W0 AmbientPathMover) {nBoats}, {pts} baked path points, {badPts} on land / < {BoatRoadClearM} m from the road, " +
                       $"cull distance {maxCull:0} m: {Mark(bOk)}");
        return ok && bOk;
    }

    // ------------------------------------------------------------------ 7. registry
    private static bool CheckRegistry(StringBuilder log)
    {
        bool ok = true;
        var region = RegionCatalog.Find(NagisaBayEnvironment.RegionId);
        bool catOk = region != null && region.Unlocked && region.BuiltCourseId == NagisaBayEnvironment.CourseId &&
                     region.EnvironmentRoot == NagisaBayEnvironment.RootName;
        log.AppendLine($"[registry] RegionCatalog '{NagisaBayEnvironment.RegionId}': unlocked {region?.Unlocked}, course {region?.BuiltCourseId}, " +
                       $"root {region?.EnvironmentRoot}, pin {(region != null ? RegionCatalog.PinFor(region).ToString("F3") : "-")}: {Mark(catOk)}");
        ok &= catOk;

        var graph = AssetDatabase.LoadAssetAtPath<RouteGraph>(RouteGraph.AssetPath);
        var def = graph != null ? graph.Course(NagisaBayEnvironment.CourseId) : null;
        var built = def != null ? RouteCourse.Build(graph, def) : null;
        bool courseOk = def != null && def.regionId == NagisaBayEnvironment.RegionId && built != null &&
                        built.Length >= MinCourseM && built.Length <= MaxCourseM;
        int cps = built != null ? built.Checkpoints.Length : 0;
        bool cpOk = cps >= MinCheckpoints;
        log.AppendLine($"[registry] graph course '{NagisaBayEnvironment.CourseId}': region {def?.regionId}, " +
                       $"{(built != null ? built.Length : 0f):N0} m runtime: {Mark(courseOk)}");
        log.AppendLine($"[registry] checkpoints {cps} (want >= {MinCheckpoints}): {Mark(cpOk)} " +
                       (built != null ? string.Join(", ", built.Checkpoints.Select(c => c.Name)) : ""));
        ok &= courseOk && cpOk;
        if (built != null)
        {
            float maxG = 0f;
            for (float m = 0f; m < built.Length; m += 50f) maxG = Mathf.Max(maxG, built.GradeAt(m, 50f));
            log.AppendLine($"[registry] HUD elevation profile source: {built.ElevationAt(0f):0}..{built.ElevationAt(r_KomOf(built)):0} m, steepest 50 m grade {maxG * (maxG < 1f ? 100f : 1f):0.0}%");
        }
        return ok;
    }

    private static float r_KomOf(RouteCourse c)
    {
        float best = 0f, bm = 0f;
        for (float m = 0f; m < c.Length; m += 25f) { float y = c.ElevationAt(m); if (y > best) { best = y; bm = m; } }
        return bm;
    }
}
