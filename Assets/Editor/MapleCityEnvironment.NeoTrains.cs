using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MAPLE CITY - BULLET-TRAIN NETWORK (user, 2026-10-04: "put in a lot of bullet trains, eg kinda like the ones you see suspended
/// in chongching"). Built per district on top of the vaporwave layer, same helpers and materials:
///   * route lines  - two more stacked lines over the boulevard (a suspended line at 27 m, a straddle-beam line at 39 m, opposite
///                    direction to the existing 15 m maglev), carried on gantry portals at the pavement edges;
///   * sweeper loops - three free stadium-shaped loops (two long straights joined by wide arcs) at 44-72 m, on slim piers, crossing
///                    the district at different angles; one of them threads straight through a tower (a Chongqing-style pass-through
///                    gate with a neon-framed opening and station signs);
///   * trains       - 2-3 per line, 5 cars each, bullet nose on the head and tail cars, 4 colourways, 28-52 m/s. Straddle trains ride
///                    ON the beam with skirts; suspended trains hang UNDER it from hangers. All driven by MapleCityMaglevTrain.
/// </summary>
public static partial class MapleCityEnvironment
{
    private const float TrainStepM = 4.0f;

    private sealed class TrainTemplates { public Transform Mid, Head, Tail; }

    private static List<Vector3> NeoResampleClosed(List<Vector3> pts, float step)
    {
        // pts: open polyline describing a closed loop (no repeated end point). Returns uniformly spaced points, first point repeated at the end.
        int n = pts.Count;
        var cum = new float[n + 1];
        for (int i = 1; i <= n; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i % n]);
        float total = cum[n];
        int m = Mathf.Max(16, Mathf.RoundToInt(total / step));
        var o = new List<Vector3>(m + 1);
        int seg = 0;
        for (int k = 0; k < m; k++)
        {
            float d = total * k / m;
            while (seg < n - 1 && cum[seg + 1] <= d) seg++;
            float t = Mathf.Clamp01((d - cum[seg]) / Mathf.Max(0.001f, cum[seg + 1] - cum[seg]));
            o.Add(Vector3.Lerp(pts[seg], pts[(seg + 1) % n], t));
        }
        o.Add(o[0]);
        return o;
    }

    private static List<Vector3> NeoStadium(Vector3 centre, float angleDeg, float radius, float straight, float y0, float undulation, float phase)
    {
        var pts = new List<Vector3>();
        float perim = 2f * straight + 2f * Mathf.PI * radius;
        float ca = Mathf.Cos(angleDeg * Mathf.Deg2Rad), sa = Mathf.Sin(angleDeg * Mathf.Deg2Rad);
        for (float s = 0f; s < perim; s += 3f)
        {
            float x, z;
            if (s < straight) { x = -straight * 0.5f + s; z = -radius; }
            else if (s < straight + Mathf.PI * radius) { float f = -Mathf.PI * 0.5f + (s - straight) / radius; x = straight * 0.5f + radius * Mathf.Cos(f); z = radius * Mathf.Sin(f); }
            else if (s < 2f * straight + Mathf.PI * radius) { x = straight * 0.5f - (s - straight - Mathf.PI * radius); z = radius; }
            else { float f = Mathf.PI * 0.5f + (s - 2f * straight - Mathf.PI * radius) / radius; x = -straight * 0.5f + radius * Mathf.Cos(f); z = radius * Mathf.Sin(f); }
            float y = y0 + undulation * Mathf.Sin(s / perim * Mathf.PI * 4f + phase);
            pts.Add(new Vector3(centre.x + x * ca - z * sa, y, centre.z + x * sa + z * ca));
        }
        return pts;
    }

    private static int NeoNearestIndex(CityRoute route, Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < route.Count; i++)
        {
            float dx = route.Position[i].x - p.x, dz = route.Position[i].z - p.z;
            float d = dx * dx + dz * dz; if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private static float NeoMinRouteDistance(CityRoute route, Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < route.Count; i += 2)
        {
            float dx = route.Position[i].x - p.x, dz = route.Position[i].z - p.z;
            float d = dx * dx + dz * dz; if (d < best) best = d;
        }
        return Mathf.Sqrt(best);
    }

    private static readonly Color[] TrainBodies =
    {
        new Color(0.90f, 0.88f, 0.97f), new Color(0.82f, 0.92f, 0.96f), new Color(0.80f, 0.80f, 0.86f), new Color(0.20f, 0.16f, 0.34f),
    };

    private static Material TrainStripeMat(int palette) => palette switch
    {
        0 => NeoTube("MapleNeo_TubePink", new Color(2.3f, 0.30f, 1.15f, 1f)),
        1 => NeoTube("MapleNeo_TubeCyan", new Color(0.30f, 1.80f, 2.30f, 1f)),
        2 => NeoTube("MapleNeo_TubeAmber", new Color(2.3f, 1.35f, 0.35f, 1f)),
        _ => NeoTube("MapleNeo_TubeViolet", new Color(1.1f, 0.55f, 2.3f, 1f)),
    };

    /// <summary>One car design in three flavours (middle, head with a nose toward +Z, tail with a nose toward -Z).</summary>
    private static TrainTemplates BuildTrainTemplates(Transform holder, string style, bool hang)
    {
        var tpl = new TrainTemplates();
        for (int variant = 0; variant < 3; variant++)          // 0 mid, 1 head, 2 tail
        {
            var car = new GameObject("Car " + (variant == 0 ? "Mid" : variant == 1 ? "Head" : "Tail")).transform;
            car.SetParent(holder, false);
            var body = new NeoBag(); var windows = new NeoBag(); var stripe = new NeoBag();
            Vector3 fwd = Vector3.forward;
            Box(body.V, body.UV, body.T, null, Vector3.zero, Vector3.right * 1.45f, fwd * 4.7f, 2.7f, default);
            Box(body.V, body.UV, body.T, null, Vector3.up * 2.7f, Vector3.right * 1.1f, fwd * 4.3f, 0.35f, default);
            if (hang) Box(body.V, body.UV, body.T, null, new Vector3(0f, 3.05f, 0f), Vector3.right * 0.18f, fwd * 3.2f, 0.65f, default);
            else
            {
                Box(body.V, body.UV, body.T, null, new Vector3(1.12f, -0.9f, 0f), Vector3.right * 0.06f, fwd * 4.6f, 0.9f, default);
                Box(body.V, body.UV, body.T, null, new Vector3(-1.12f, -0.9f, 0f), Vector3.right * 0.06f, fwd * 4.6f, 0.9f, default);
            }
            float zWin = variant == 0 ? 4.0f : 3.6f;
            for (int sg = -1; sg <= 1; sg += 2)
            {
                Box(windows.V, windows.UV, windows.T, null, new Vector3(1.47f * sg, 1.3f, 0f), fwd * zWin, Vector3.right * 0.02f, 0.85f, default, centredY: true);
                Box(stripe.V, stripe.UV, stripe.T, null, new Vector3(1.47f * sg, 0.55f, 0f), fwd * 4.7f, Vector3.right * 0.02f, 0.12f, default, centredY: true);
            }
            if (variant != 0)
            {
                float dir = variant == 1 ? 1f : -1f;
                float[] hw = { 1.30f, 1.05f, 0.70f }, hh = { 2.45f, 2.00f, 1.45f }, by = { 0.10f, 0.30f, 0.55f };
                for (int j = 0; j < 3; j++)
                    Box(body.V, body.UV, body.T, null, new Vector3(0f, by[j], dir * (5.2f + j)), Vector3.right * hw[j], fwd * 0.5f, hh[j], default);
                Box(windows.V, windows.UV, windows.T, null, new Vector3(0f, 1.35f, dir * 5.72f), Vector3.right * 0.85f, fwd * 0.02f, 0.7f, default, centredY: true);
                Box(stripe.V, stripe.UV, stripe.T, null, new Vector3(0f, 0.7f, dir * 7.72f), Vector3.right * 0.5f, fwd * 0.02f, 0.14f, default, centredY: true);
            }
            string tag = style + " " + car.name;
            NeoFlush(car, "Car Body", body, CelMaterial("MapleCity_TrainBody_0", TrainBodies[0], gloss: 0.5f, spec: 0.45f, rim: 0.6f), shadows: true);
            NeoFlush(car, "Car Windows", windows, NeoTube("MapleNeo_TrainWindow", new Color(0.7f, 1.6f, 2.2f, 1f)));
            NeoFlush(car, "Car Stripe", stripe, TrainStripeMat(0));
            // NeoFlush names meshes by its name argument; rename the three generated meshes so styles/variants never share an asset
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) mf.sharedMesh.name = NeoMeshName(tag + " " + mf.gameObject.name);
            if (variant == 0) tpl.Mid = car; else if (variant == 1) tpl.Head = car; else tpl.Tail = car;
        }
        return tpl;
    }

    private static void SpawnTrain(Transform parent, string name, TrainTemplates tpl, List<Vector3> path, float yOffset, int cars, int palette, float speed, float startOffset)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        var list = new List<Transform>();
        for (int k = 0; k < cars; k++)
        {
            var src = k == 0 ? tpl.Head : k == cars - 1 ? tpl.Tail : tpl.Mid;
            var c = UnityEngine.Object.Instantiate(src.gameObject, root);
            c.name = "Car " + k;
            foreach (var r in c.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.gameObject.name == "Car Body") r.sharedMaterial = CelMaterial("MapleCity_TrainBody_" + palette, TrainBodies[palette], gloss: 0.5f, spec: 0.45f, rim: 0.6f);
                else if (r.gameObject.name == "Car Stripe") r.sharedMaterial = TrainStripeMat(palette);
            }
            list.Add(c.transform);
        }
        var pa = new Vector3[path.Count]; var cum = new float[path.Count]; float acc = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            pa[i] = path[i] + Vector3.up * yOffset;
            if (i > 0) acc += Vector3.Distance(pa[i - 1], pa[i]);
            cum[i] = acc;
        }
        var t = root.gameObject.AddComponent<MapleCityMaglevTrain>();
        t.path = pa; t.cumulative = cum; t.cars = list.ToArray(); t.speed = speed; t.carSpacing = 10.2f;
        t.startOffset = startOffset;
        t.Apply(0f);
    }

    /// <summary>Beam + glow strips along a closed, uniformly resampled path. hang: box girder carrying the train from above; else a slab under the train.</summary>
    private static void BuildBeam(NeoBag beam, NeoBag glow, List<Vector3> path, bool hang)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Vector3 a = path[i], b = path[i + 1];
            Vector3 along = b - a; float len = along.magnitude; if (len < 0.2f) continue; along /= len;
            Vector3 flat = new Vector3(along.x, 0f, along.z); if (flat.sqrMagnitude < 1e-4f) continue;
            Vector3 side = Vector3.Cross(Vector3.up, flat.normalized);
            Vector3 mid = (a + b) * 0.5f;
            float halfW = hang ? 0.9f : 1.15f, h = hang ? 1.2f : 1.5f;
            Vector3 baseC = hang ? mid - Vector3.up * 0.6f : mid - Vector3.up * 1.5f;
            Box(beam.V, beam.UV, beam.T, null, baseC, side * halfW, along * (len * 0.5f + 0.3f), h, default);
            if ((i & 3) == 0)
            {
                Vector3 gC = (hang ? mid - Vector3.up * 0.55f : mid - Vector3.up * 1.2f);
                Box(glow.V, glow.UV, glow.T, null, gC + side * (halfW + 0.03f), side * 0.05f, along * (len * 0.5f + 1.2f), 0.1f, default);
                Box(glow.V, glow.UV, glow.T, null, gC - side * (halfW + 0.03f), side * 0.05f, along * (len * 0.5f + 1.2f), 0.1f, default);
            }
        }
    }

    private static void BuildNeoTrainLines(Transform group, CityRoute route, string tag)
    {
        var holder = new GameObject("Train Templates").transform;
        holder.SetParent(group, false);
        var straddle = BuildTrainTemplates(holder, "Straddle", hang: false);
        var hung = BuildTrainTemplates(holder, "Hung", hang: true);

        var rng = new System.Random(tag == "East" ? 9101 : 9102);
        var pier = new NeoBag(); var gantry = new NeoBag(); var tower = new NeoBag(); var frame = new NeoBag(); var signs = new NeoBag();
        var matBeam = CelMaterial("MapleCity_TrainBeam", new Color(0.36f, 0.37f, 0.46f), gloss: 0.18f, spec: 0.15f, rim: 0.45f);
        int trains = 0, lineNo = 0;
        float baseY = BasinY - 2f;
        Vector3 centroid = Vector3.zero; for (int i = 0; i < route.Count; i++) centroid += route.Position[i]; centroid /= route.Count;

        // ---------------- route-following lines: suspended @27 m (reverse), straddle @39 m
        for (int pass = 0; pass < 2; pass++)
        {
            bool hang = pass == 0;
            float lift = hang ? 27f : 39f;
            int n = route.Count;
            var raw = new List<Vector3>(n);
            for (int i = n - 1; i >= 0; i--)                      // reversed direction vs the 15 m maglev
            {
                float ys = 0f; int cnt = 0;
                for (int k = -10; k <= 10; k++) { ys += route.Position[((i + k) % n + n) % n].y; cnt++; }
                var p = route.Position[i]; p.y = ys / cnt + lift;
                raw.Add(p);
            }
            if (raw[0].y != raw[raw.Count - 1].y) { }              // closed route: ends meet already
            var path = NeoResampleClosed(raw, TrainStepM);
            var beam = new NeoBag(); var glow = new NeoBag();
            BuildBeam(beam, glow, path, hang);
            string nm = (hang ? "Hung " : "Straddle ") + "Route Line";
            NeoFlush(group, nm + " Beam", beam, matBeam, shadows: true);
            NeoFlush(group, nm + " Glow", glow, TrainStripeMat(hang ? 1 : 0));
            // gantry portals every ~48 m
            for (int k = 0; k < path.Count - 1; k += 12)
            {
                Vector3 a = path[k], b = path[k + 1];
                Vector3 dir = new Vector3(b.x - a.x, 0f, b.z - a.z); if (dir.sqrMagnitude < 1e-4f) continue; dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                int ri = NeoNearestIndex(route, a);
                float frac = route.Frac(ri);
                float lat = CarriagewayHalfWidth(frac) + PavementWidthM + 1.25f;
                float deck = a.y - (hang ? 0.6f : 1.5f);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 foot = new Vector3(a.x, baseY, a.z) + side * (sg * lat);
                    Box(gantry.V, gantry.UV, gantry.T, null, foot, dir * 0.5f, side * 0.5f, (deck + 0.2f) - baseY, default);
                }
                Box(gantry.V, gantry.UV, gantry.T, null, new Vector3(a.x, deck - 0.7f, a.z), dir * 0.5f, side * lat, 0.8f, default);
            }
            int perLine = hang ? 3 : 2;
            for (int k = 0; k < perLine; k++)
            {
                trains++;
                SpawnTrain(group, (hang ? "Hung Train " : "Straddle Train ") + k, hang ? hung : straddle, path, hang ? -3.8f : 0f,
                           5, rng.Next(4), hang ? 30f + k * 3f : 40f + k * 4f, path.Count * TrainStepM * (k + 0.37f) / perLine);
            }
            lineNo++;
        }

        // ---------------- free sweeper loops
        float[] radius = { 78f, 62f, 95f };
        float[] straight = { 760f, 640f, 880f };
        float[] angle = { 18f, 76f, 132f };
        float[] height = { 56f, 46f, 70f };
        for (int L = 0; L < 3; L++)
        {
            bool hang = L != 1;
            Vector3 c = centroid + new Vector3((float)(rng.NextDouble() - 0.5) * 300f, 0f, (float)(rng.NextDouble() - 0.5) * 300f);
            var raw = NeoStadium(c, angle[L] + (tag == "East" ? 23f : 0f), radius[L], straight[L], baseY + height[L] + 30f, 5f, L * 1.7f);
            var path = NeoResampleClosed(raw, TrainStepM);
            var beam = new NeoBag(); var glow = new NeoBag();
            BuildBeam(beam, glow, path, hang);
            string nm = "Sweeper " + L;
            NeoFlush(group, nm + " Beam", beam, matBeam, shadows: true);
            NeoFlush(group, nm + " Glow", glow, TrainStripeMat((L + 1) % 4));

            // piers every ~64 m, skipped where they would stand on the road
            for (int k = 6; k < path.Count - 1; k += 16)
            {
                Vector3 a = path[k], b = path[k + 1];
                if (NeoMinRouteDistance(route, a) < 14f) continue;
                Vector3 dir = new Vector3(b.x - a.x, 0f, b.z - a.z); if (dir.sqrMagnitude < 1e-4f) continue; dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                float topY = a.y - (hang ? 0.6f : 1.5f);
                if (hang)
                {
                    Vector3 foot = new Vector3(a.x, baseY, a.z) + side * 3.4f;
                    Box(pier.V, pier.UV, pier.T, null, foot, dir * 0.8f, side * 0.8f, (a.y + 0.3f) - baseY, default);
                    Box(pier.V, pier.UV, pier.T, null, new Vector3(a.x, a.y + 0.2f, a.z) + side * 1.7f, dir * 0.6f, side * 1.9f, 0.8f, default);
                }
                else
                {
                    Vector3 foot = new Vector3(a.x, baseY, a.z);
                    Box(pier.V, pier.UV, pier.T, null, foot, dir * 0.9f, side * 0.9f, topY - baseY, default);
                    Box(pier.V, pier.UV, pier.T, null, new Vector3(a.x, topY - 0.8f, a.z), dir * 0.7f, side * 2.4f, 0.8f, default);
                }
            }

            // pass-through tower on one straight, as far from the road as possible
            if (L == 0 || L == 2)
            {
                int bestK = -1; float bestD = 0f;
                int total = path.Count - 1;
                for (int k = total / 16; k < total / 2 - total / 16; k += 6)       // first straight/arc zone, scanned coarsely
                {
                    Vector3 a = path[k], b = path[k + 1];
                    Vector3 dd = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
                    // want a straight section: direction unchanged over 40 m
                    Vector3 b2 = path[Mathf.Min(k + 10, total)]; Vector3 d2 = new Vector3(b2.x - a.x, 0f, b2.z - a.z).normalized;
                    if (Vector3.Dot(dd, d2) < 0.9995f) continue;
                    float d = NeoMinRouteDistance(route, a);
                    if (d > bestD) { bestD = d; bestK = k; }
                }
                if (bestK >= 0 && bestD > 40f)
                {
                    Vector3 a = path[bestK], b = path[bestK + 1];
                    Vector3 t = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
                    Vector3 lat = Vector3.Cross(Vector3.up, t);
                    float lo = a.y - (hang ? 5.2f : 2.4f), hi = a.y + (hang ? 2.4f : 5.2f), top = a.y + 24f;
                    float openHalf = 4.2f, towerHalf = 12f, towerLen = 14f;
                    Vector3 foot = new Vector3(a.x, baseY, a.z);
                    for (int sg = -1; sg <= 1; sg += 2)
                        Box(tower.V, tower.UV, tower.T, null, foot + lat * (sg * (openHalf + towerHalf) * 0.5f), lat * ((towerHalf - openHalf) * 0.5f), t * towerLen, top - baseY, default);
                    Box(tower.V, tower.UV, tower.T, null, new Vector3(a.x, lo, a.z) - Vector3.up * (lo - baseY), lat * openHalf, t * towerLen, lo - baseY, default);
                    Box(tower.V, tower.UV, tower.T, null, new Vector3(a.x, hi, a.z), lat * openHalf, t * towerLen, top - hi, default);
                    // neon frame on both faces of the opening
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        Vector3 fc = new Vector3(a.x, 0f, a.z) + t * (sg * (towerLen + 0.1f));
                        Vector3 p0 = fc + lat * -openHalf + Vector3.up * lo, p1 = fc + lat * openHalf + Vector3.up * lo;
                        Vector3 p2 = fc + lat * openHalf + Vector3.up * hi, p3 = fc + lat * -openHalf + Vector3.up * hi;
                        NeoTube(frame, p0, p1, 0.1f); NeoTube(frame, p1, p2, 0.1f); NeoTube(frame, p2, p3, 0.1f); NeoTube(frame, p3, p0, 0.1f);
                        // station sign above the opening, readable from the approach
                        Vector3 sc = new Vector3(a.x, 0f, a.z) + t * (sg * (towerLen + 0.25f)) + Vector3.up * (hi + (top - hi) * 0.5f);
                        NeoQuad(signs.V, signs.UV, signs.UV2, signs.T, sc, lat * 6f, Vector3.up * 6f, NeoTile(rng.Next(32)), flip: sg > 0);
                    }
                    // side faces: big atlas boards
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        Vector3 sc = new Vector3(a.x, 0f, a.z) + lat * (sg * (towerHalf + 0.15f)) + Vector3.up * (top - 9f);
                        NeoQuad(signs.V, signs.UV, signs.UV2, signs.T, sc, t * 7f, Vector3.up * 7f, NeoTile(rng.Next(32)), flip: sg > 0);
                    }
                }
            }

            int per = 3;
            for (int k = 0; k < per; k++)
            {
                trains++;
                SpawnTrain(group, "Sweeper " + L + " Train " + k, hang ? hung : straddle, path, hang ? -3.8f : 0f,
                           5, rng.Next(4), 34f + L * 6f + k * 4f, path.Count * TrainStepM * (k + 0.2f * L) / per);
            }
            lineNo++;
        }

        NeoFlush(group, "Train Piers", pier, matBeam, shadows: true);
        NeoFlush(group, "Train Gantries", gantry, matBeam, shadows: true);
        NeoFlush(group, "Train Gate Towers", tower, CelMaterial("MapleCity_TrainTower", new Color(0.14f, 0.14f, 0.24f), gloss: 0.2f, spec: 0.2f, rim: 0.5f), shadows: true);
        NeoFlush(group, "Train Gate Frames", frame, TrainStripeMat(0));
        NeoFlush(group, "Train Gate Signs", signs, NeoSignMat());
        UnityEngine.Object.DestroyImmediate(holder.gameObject);
        Debug.Log($"[maple-neo] {tag}: {lineNo} extra train lines, {trains} trains.");
    }
}
