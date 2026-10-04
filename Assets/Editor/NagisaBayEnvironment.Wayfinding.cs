// NB-QOL: Nagisa Bay wayfinding + ride-corridor snag audit (COORDINATION.md, NB-QOL log line).
//
// Player-facing quality of life on the 16.2 km nagisa_bay_loop, which had NO roadside wayfinding:
//   1. a welcome board at the start and a name board ~30 m before each of the 9 route checkpoints
//      ("Next checkpoint x.x km"), so the HUD's checkpoint names exist in the world too;
//   2. km plates every 1 km with "x.x km to finish";
//   3. a Palm Ridge climb board at the foot (length / avg / max grade, from the route) and
//      KOM 1 km / 500 m / 200 m + FINISH 1 km / 500 m / 200 m countdown boards;
//   4. chevron boards on the outside of the tight hill bends (the fast Ridge Loop descent);
//   5. a read-only ride-corridor audit that lists colliders / static renderers intruding over
//      the carriageway (snags + visual clipping) so their owners can fix them.
// Everything sits on the verge band (+4.1..+7.4 m), sea side on the coastal sections (the side rule:
// never in the -8..-42 m highway band), has NO colliders, avoids existing verge props, and is
// batched into a few meshes per 2 km chunk with one atlas material. Text/numbers come from
// tools/textures/make_nagisa_wayfinding.py, which reads NagisaRoute.json (single source).
// Stage: [NagisaStage(95, "Wayfinding")] -> "Nagisa Bay Environment/NB Overhaul/NB Wayfinding".
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class NagisaBayEnvironment
{
    private const string WayDir = NagisaTex + "/Wayfinding";
    private const string WayAtlasPng = WayDir + "/NB_Wayfinding_Atlas.png";
    private const string WayAtlasJson = WayDir + "/NB_Wayfinding_Atlas.json";

    // ---- tunables (all PROVISIONAL / illustrative; tune by render)
    /// <summary>Clear gap between the shoulder edge (4.1 m) and the nearest board edge.</summary>
    private const float WayEdgeGapM = 0.45f;
    /// <summary>Boards turn this far from square-on toward the road so an approaching rider reads them.</summary>
    private const float WayToeInDeg = 14f;
    private const float WayBigW = 2.4f, WayBigH = 1.2f, WayBigBottomM = 1.7f;     // welcome / checkpoint / climb
    private const float WayCountW = 1.6f, WayCountH = 0.8f, WayCountBottomM = 1.5f; // KOM / finish countdown
    private const float WayKmW = 0.9f, WayKmH = 0.45f, WayKmBottomM = 1.15f;       // km plates
    private const float WayChevW = 1.1f, WayChevH = 0.55f, WayChevBottomM = 0.9f;  // bend chevrons
    /// <summary>Board lead: name boards stand this far before the checkpoint mark.</summary>
    private const float WayCheckpointLeadM = 30f;
    /// <summary>Skip a km plate that would stand this close to another board.</summary>
    private const float WayKmMinGapM = 120f;
    /// <summary>A bend gets chevrons when it turns faster than this (deg per metre ~ radius 125 m, fast descent bends).</summary>
    private const float WayChevTurnDegPerM = 0.45f;
    private const float WayChevSpacingM = 30f;
    private const int WayChevMax = 80;
    /// <summary>Reject a spot whose ground is this far above / below the road (cut bank / drop-off).</summary>
    private const float WayMaxBankUpM = 2.2f, WayMaxDropM = 3.0f;
    private const float WayChunkM = 2000f;

    [System.Serializable] private class WayAtlasDto { public int cols; public int rows; public string[] names; }

    private sealed class WayBatch
    {
        public readonly List<Vector3> fv = new List<Vector3>(), pv = new List<Vector3>();
        public readonly List<Vector2> fuv = new List<Vector2>(), puv = new List<Vector2>();
        public readonly List<int> ft = new List<int>(), pt = new List<int>();
    }

    [NagisaStage(95, "Wayfinding")]
    private static void BuildWayfindingStage(Transform group)
    {
        var r = _route;
        if (r == null || _ground == null) { Debug.LogError("[nagisa-qol] route/ground not loaded"); return; }
        if (!File.Exists(WayAtlasJson) || !File.Exists(WayAtlasPng))
        {
            Debug.LogError($"[nagisa-qol] missing {WayAtlasPng}: run python tools/textures/make_nagisa_wayfinding.py");
            return;
        }
        var atlas = JsonUtility.FromJson<WayAtlasDto>(File.ReadAllText(WayAtlasJson));
        var cell = new Dictionary<string, int>();
        for (int i = 0; i < atlas.names.Length; i++) cell[atlas.names[i]] = i;

        ConfigureWayAtlasImport();
        var faceMat = Cel("Nagisa_NBW_SignFace", Color.white, gloss: 0.35f, spec: 0.1f, rim: 0.05f,
                          texture: AssetDatabase.LoadAssetAtPath<Texture2D>(WayAtlasPng));
        faceMat.SetFloat("_ShadowAmbient", 0.8f);   // keep type readable in the verge-palm shade
        var postMat = Cel("Nagisa_NBW_Post", Srgb(0x9A, 0xA2, 0xA6), gloss: 0.5f, spec: 0.35f);

        var blockers = WayBlockers(group);
        var placed = new List<Vector3>();
        var chunks = new Dictionary<int, WayBatch>();
        int ok = 0, skipped = 0;
        var skipLog = new StringBuilder();

        bool Place(string name, float atM, float w, float h, float bottom, int sideOverride = 0)
        {
            if (!cell.TryGetValue(name, out int ci)) { Debug.LogWarning($"[nagisa-qol] no atlas cell '{name}'"); return false; }
            float[] tries = { 0f, 4f, -4f, 8f, -8f, 12f, -12f, 18f, -18f };
            foreach (float dm in tries)
            {
                float m = Mathf.Clamp(atM + dm, 5f, r.Length - 5f);
                if (TryWayFoot(m, w, sideOverride, blockers, placed, out var foot, out var n, out var rt, out var side, out bool bridge))
                {
                    int k = Mathf.FloorToInt(m / WayChunkM);
                    if (!chunks.TryGetValue(k, out var b)) chunks[k] = b = new WayBatch();
                    AddWayBoard(b, foot, n, rt, w, h, bottom, ci, atlas.cols, atlas.rows, bridge, r.SideFlat(r.IndexAt(m)) * side);
                    placed.Add(foot);
                    ok++;
                    return true;
                }
            }
            skipped++;
            skipLog.Append($" {name}@{atM:F0}");
            return false;
        }

        // 1. welcome + checkpoint boards
        var boards = new List<float>();
        Place("welcome", 25f, WayBigW, WayBigH, WayBigBottomM); boards.Add(25f);
        var cps = WayCheckpointDistances();
        for (int i = 0; i < cps.Count; i++)
        {
            float at = i == 0 ? 70f : Mathf.Max(70f, cps[i] - WayCheckpointLeadM);
            Place($"cp_{i}", at, WayBigW, WayBigH, WayBigBottomM); boards.Add(at);
        }
        // 3. climb + countdowns
        float climbAt = Mathf.Max(80f, r.ClimbStartM - 120f);
        Place("climb_info", climbAt, WayBigW, WayBigH, WayBigBottomM); boards.Add(climbAt);
        foreach (int n in new[] { 1000, 500, 200 })
        {
            Place($"kom_{n}", r.KomM - n, WayCountW, WayCountH, WayCountBottomM); boards.Add(r.KomM - n);
            Place($"fin_{n}", r.Length - n, WayCountW, WayCountH, WayCountBottomM); boards.Add(r.Length - n);
        }
        // 2. km plates
        int kmSkippedNear = 0;
        for (int k = 1; k * 1000f < r.Length - 300f; k++)
        {
            float at = k * 1000f;
            if (boards.Any(b => Mathf.Abs(b - at) < WayKmMinGapM)) { kmSkippedNear++; continue; }
            Place($"km_{k}", at, WayKmW, WayKmH, WayKmBottomM);
        }
        // 4. chevrons on the tight hill bends (the coast has a gentle line + the highway on the land side)
        int chev = 0; float lastChev = -1e9f;
        for (int i = 3; i < r.Count - 3 && chev < WayChevMax; i++)
        {
            float d = r.Distance[i];
            if (d < r.ClimbStartM || d > r.CoastStartM) continue;
            if (d - lastChev < WayChevSpacingM) continue;
            int a = Mathf.Max(0, r.IndexAt(d - 12f)), c = Mathf.Min(r.Count - 1, r.IndexAt(d + 12f));
            float span = r.Distance[c] - r.Distance[a];
            if (span < 1f) continue;
            float turn = Vector3.SignedAngle(r.SideFlat(a), r.SideFlat(c), Vector3.up);
            if (Mathf.Abs(turn) / span < WayChevTurnDegPerM) continue;
            int outer = turn > 0f ? -1 : 1;   // right-hander -> board on the left (outside)
            if (Place(turn > 0f ? "chev_R" : "chev_L", d, WayChevW, WayChevH, WayChevBottomM, outer)) chev++;
            lastChev = d;
        }

        foreach (var kv in chunks.OrderBy(x => x.Key))
        {
            var b = kv.Value;
            var face = AddMesh(group, $"NBW Faces {kv.Key}", Finish($"Nagisa_NBW_Faces_{kv.Key}", b.fv, b.fuv, b.ft), faceMat, collider: false);
            face.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AddMesh(group, $"NBW Posts {kv.Key}", Finish($"Nagisa_NBW_Posts_{kv.Key}", b.pv, b.puv, b.pt), postMat, collider: false);
        }
        foreach (Transform t in group) GameObjectUtility.SetStaticEditorFlags(t.gameObject,
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        Debug.Log($"[nagisa-qol] wayfinding: {ok} boards placed ({chev} chevrons), {skipped} skipped{(skipped > 0 ? ":" + skipLog : "")}; " +
                  $"{kmSkippedNear} km plates merged into nearby boards; {blockers.Count} verge blockers avoided-checked.");
        AuditRideCorridorInternal(group.root, 40);
    }

    private static List<float> WayCheckpointDistances()
    {
        var list = new List<float>();
        try
        {
            var j = File.ReadAllText(RoutePathForWay());
            // minimal parse: "distance": value inside "checkpoints"
            int at = j.IndexOf("\"checkpoints\"", System.StringComparison.Ordinal);
            if (at >= 0)
            {
                var tail = j.Substring(at);
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(tail, "\"distance\"\\s*:\\s*([-0-9.eE]+)"))
                    list.Add(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[nagisa-qol] checkpoint parse failed: {e.Message}"); }
        return list;
    }

    private static string RoutePathForWay() => Dir + "/NagisaRoute.json";

    private static void ConfigureWayAtlasImport()
    {
        var imp = AssetImporter.GetAtPath(WayAtlasPng) as TextureImporter;
        if (imp == null) { AssetDatabase.ImportAsset(WayAtlasPng); imp = AssetImporter.GetAtPath(WayAtlasPng) as TextureImporter; }
        if (imp == null) return;
        bool dirty = imp.maxTextureSize != 4096 || imp.anisoLevel != 8 || imp.wrapMode != TextureWrapMode.Clamp || !imp.sRGBTexture;
        if (!dirty) return;
        imp.maxTextureSize = 4096; imp.anisoLevel = 8; imp.wrapMode = TextureWrapMode.Clamp; imp.sRGBTexture = true;
        imp.mipmapEnabled = true; imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
    }

    /// <summary>XZ footprints of the existing small verge props near the road (benches, lamps, palms,
    /// racks) so boards never stand inside them. Big combined meshes (&gt;25 m) are ground/roads/batches.</summary>
    private static List<Bounds> WayBlockers(Transform myGroup)
    {
        var list = new List<Bounds>();
        var root = myGroup.root;
        foreach (var rd in root.GetComponentsInChildren<Renderer>(true))
        {
            if (rd.transform.IsChildOf(myGroup)) continue;
            var b = rd.bounds;
            if (b.size.x > 25f || b.size.z > 25f || b.size.y < 0.3f) continue;
            float pd = _route.PlanDistance(b.center.x, b.center.z, out _);
            if (pd > 12f) continue;
            list.Add(b);
        }
        return list;
    }

    private static bool TryWayFoot(float m, float w, int sideOverride, List<Bounds> blockers, List<Vector3> placed,
                                   out Vector3 foot, out Vector3 n, out Vector3 rt, out int side, out bool bridge)
    {
        var r = _route;
        int i = r.IndexAt(m);
        var t = r.Tangent[i]; t.y = 0f; t.Normalize();
        var s = r.SideFlat(i);
        side = sideOverride != 0 ? sideOverride : (int)SeaSign(i);
        bridge = r.OnBridge(i);
        float toe = WayToeInDeg * Mathf.Deg2Rad;
        // face normal points back at the approaching rider, toed in toward the road
        n = (-t * Mathf.Cos(toe) - s * side * Mathf.Sin(toe)).normalized;
        rt = Vector3.Cross(Vector3.up, -n).normalized;
        float lateralHalf = Mathf.Abs(Vector3.Dot(rt, s)) * w * 0.5f;
        float edge = RoadHalfWidth + ShoulderWidth + WayEdgeGapM;
        float lat = bridge ? edge + 0.35f : edge + lateralHalf;
        var p = r.Position[i];
        foot = p + s * side * lat;
        float roadY = p.y;
        if (bridge) foot.y = RoadY(i, lat) + 0.15f;
        else
        {
            if (_ground.Coast(foot.x, foot.z) < 0.5f) return false;
            float gy = _ground.Height(foot.x, foot.z);
            if (gy < 0.25f || gy > roadY + WayMaxBankUpM || gy < roadY - WayMaxDropM) return false;
            foot.y = gy;
        }
        // stay clear of other boards and verge props
        foreach (var q in placed)
            if ((new Vector2(q.x - foot.x, q.z - foot.z)).sqrMagnitude < 9f) return false;
        var probe = new Bounds(foot + Vector3.up * 1.5f, new Vector3(w + 0.6f, 3f, w + 0.6f));
        foreach (var b in blockers)
            if (b.Intersects(probe)) return false;
        return true;
    }

    private static void AddWayBoard(WayBatch b, Vector3 foot, Vector3 n, Vector3 rt, float w, float h, float bottom,
                                    int ci, int cols, int rows, bool bridge, Vector3 seaDir)
    {
        var up = Vector3.up;
        // on the viaduct the single post stands on the walkway and the board overhangs the parapet
        var c = foot + up * (bottom + h * 0.5f);
        if (bridge) c += seaDir * (w * 0.5f - 0.3f);
        // face (u,v cell; atlas row 0 = top of the PNG)
        int cx = ci % cols, cy = ci / cols;
        float u0 = cx / (float)cols, u1 = (cx + 1) / (float)cols;
        float v1 = 1f - cy / (float)rows, v0 = 1f - (cy + 1) / (float)rows;
        float inset = 0.5f / 4096f; u0 += inset; u1 -= inset; v0 += inset; v1 -= inset;
        var fc = c + n * 0.028f;
        var bl = fc - rt * (w * 0.5f) - up * (h * 0.5f);
        var br = fc + rt * (w * 0.5f) - up * (h * 0.5f);
        var tl = fc - rt * (w * 0.5f) + up * (h * 0.5f);
        var tr = fc + rt * (w * 0.5f) + up * (h * 0.5f);
        WayQuad(b.fv, b.fuv, b.ft, bl, tl, tr, br, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), n);
        // backing panel (frame) and posts
        WayBox(b.pv, b.puv, b.pt, c, rt * (w * 0.5f + 0.04f), up * (h * 0.5f + 0.04f), n * 0.025f);
        float postTop = bottom + h - 0.05f, postBase = -0.35f;
        float postH = (postTop - postBase) * 0.5f;
        var postC = foot + up * (postBase + postH) - n * 0.07f;
        var pr = rt * 0.045f; var pn = n * 0.045f; var ph = up * postH;
        if (w >= 1.5f && !bridge)
        {
            WayBox(b.pv, b.puv, b.pt, postC - rt * (w * 0.33f), pr, ph, pn);
            WayBox(b.pv, b.puv, b.pt, postC + rt * (w * 0.33f), pr, ph, pn);
        }
        else WayBox(b.pv, b.puv, b.pt, postC, pr * 1.3f, ph, pn * 1.3f);
    }

    private static void WayQuad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 outward)
    {
        int o = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
        // Unity front faces: Cross(b-a, c-a) points toward the viewer
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f) { t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3); }
        else { t.Add(o); t.Add(o + 2); t.Add(o + 1); t.Add(o); t.Add(o + 3); t.Add(o + 2); }
    }

    /// <summary>Oriented box from a centre and three half-axis vectors.</summary>
    private static void WayBox(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az)
    {
        var z = Vector2.zero; var one = Vector2.one; var u10 = new Vector2(1, 0); var u01 = new Vector2(0, 1);
        void F(Vector3 n, Vector3 a1, Vector3 a2) =>
            WayQuad(v, uv, t, c + n - a1 - a2, c + n - a1 + a2, c + n + a1 + a2, c + n + a1 - a2, z, u01, one, u10, n);
        F(ax, ay, az); F(-ax, ay, az); F(ay, ax, az); F(-ay, ax, az); F(az, ax, ay); F(-az, ax, ay);
    }

    // =================================================================== ride-corridor snag audit

    /// <summary>Read-only: lists colliders and static renderers whose footprint reaches over the
    /// carriageway (|lateral| &lt; 3.5 m) between road level +0.15 m and +2.2 m, i.e. things a rider
    /// would ride through or snag on. Never modifies the scene. Menu + batch entry point.</summary>
    [MenuItem("MapleRide/Environment/Nagisa Bay Ride-Corridor Audit")]
    public static void AuditRideCorridor()
    {
        if (Application.isBatchMode && EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (_route == null) { _route = NagisaRoute.Load(); }
        var roots = FindRootsByExactName(RootName);
        if (roots.Count == 0) { Debug.LogError($"[nagisa-qol] no '{RootName}' root"); return; }
        AuditRideCorridorInternal(roots[0].transform, 80);
    }

    private static void AuditRideCorridorInternal(Transform root, int maxLines)
    {
        var r = _route;
        var hits = new List<(float d, float lat, string what, string path)>();
        int scanned = 0;
        bool Intrudes(Bounds b, out float d, out float lat)
        {
            d = 0f; lat = 0f;
            if (b.size.x > 40f || b.size.z > 40f) return false;   // ground / road / bridge / batched sheets
            float pd = r.PlanDistance(b.center.x, b.center.z, out int ni);
            float minHalf = Mathf.Min(b.extents.x, b.extents.z);
            if (pd - minHalf > RoadHalfWidth - 0.1f) return false;
            float ry = r.Position[ni].y;
            if (b.max.y < ry + 0.15f || b.min.y > ry + 2.2f) return false;
            d = r.Distance[ni];
            var s = r.SideFlat(ni);
            lat = Vector3.Dot(b.center - r.Position[ni], s);
            return true;
        }
        bool Excluded(Transform t)
        {
            // the ride surface itself, and legitimate movers on the road (NB5 riders, the player)
            for (var p = t; p != null; p = p.parent)
            {
                string n = p.name;
                if (n == "Road" || n.StartsWith("Terrain") || n.Contains("Rider") || n.Contains("Cyclist") ||
                    n.Contains("Kuro") || n.Contains("Player")) return true;
                if (p.GetComponent("AmbientPathMover") != null) return true;
            }
            return false;
        }
        foreach (var col in root.GetComponentsInChildren<Collider>(false))
        {
            scanned++;
            if (Excluded(col.transform)) continue;
            if (Intrudes(col.bounds, out float d, out float lat))
                hits.Add((d, lat, "collider", PathOf(col.transform)));
        }
        foreach (var rd in root.GetComponentsInChildren<MeshRenderer>(false))
        {
            scanned++;
            if (rd.name.EndsWith("_LOD1") || rd.name.EndsWith("_LOD2") || rd.name.EndsWith("_LOD3")) continue;
            if (Excluded(rd.transform)) continue;
            if (!Intrudes(rd.bounds, out _, out _)) continue;
            // AABB says "maybe": confirm with real vertices (overhanging canopies / adjacent buildings are fine)
            var mf = rd.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) { hits.Add((0f, 0f, "renderer?", PathOf(rd.transform))); continue; }
            var verts = mesh.vertices;
            var l2w = rd.transform.localToWorldMatrix;
            int step = Mathf.Max(1, verts.Length / 2000);
            for (int k = 0; k < verts.Length; k += step)
            {
                var w = l2w.MultiplyPoint3x4(verts[k]);
                float pd = r.PlanDistance(w.x, w.z, out int ni);
                if (pd > RoadHalfWidth - 0.1f) continue;
                float ry = r.Position[ni].y;
                if (w.y < ry + 0.15f || w.y > ry + 2.2f) continue;
                hits.Add((r.Distance[ni], Vector3.Dot(w - r.Position[ni], r.SideFlat(ni)), "renderer", PathOf(rd.transform)));
                break;
            }
        }
        hits.Sort((a, b) => a.d.CompareTo(b.d));
        int cols = hits.Count(h => h.what == "collider");
        Debug.Log($"[nagisa-qol] corridor audit: scanned {scanned}, {hits.Count} intrusions over the carriageway " +
                  $"({cols} colliders, {hits.Count - cols} static renderers).");
        foreach (var h in hits.Take(maxLines))
            Debug.Log($"[nagisa-qol]   {h.d,7:F0} m  lat {h.lat,5:F1}  {h.what,-8} {h.path}");
    }

    private static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null && p.parent != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
