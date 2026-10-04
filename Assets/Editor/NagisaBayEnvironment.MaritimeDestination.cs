// Nagisa Bay destination pass: premium marina / working-harbour contrast, authored life,
// climb and descent dressing, and a dedicated near-shore coral scan.
//
// This is intentionally an additive stage. It reuses the established Nagisa GLB kit and W0
// AmbientPathMover contract, and owns only the rebuilt "NB MaritimeDestination" child.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class NagisaBayEnvironment
{
    private const float MaritimeCull = 0.0025f;
    private const float MaritimeRoadClear = 10.5f;
    private const float MaritimeBoatRoadClear = 70f;

    [NagisaStage(88, "MaritimeDestination")]
    private static void BuildMaritimeDestination(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0)
        {
            PrepareNagisaTextures();
            BuildNbMaterials();
        }
        var marina = new GameObject("Premium Marina + Working Harbour").transform;
        marina.SetParent(group, false);
        BuildMarinaContrast(marina);
        BuildMarinaTraffic(marina);
        BuildPurposefulLife(marina);
        BuildClimbDescentDressing(group);
        BuildShorelineCoral(group);
    }

    private static void BuildMarinaContrast(Transform parent)
    {
        // The existing marina is deliberately split into two readable precincts: a bright,
        // serviced yacht club on the resort side and a compact, darker service quay beyond it.
        int i = _route.IndexAt(300f);
        var road = _route.Position[i];
        var sea = _route.SideFlat(i) * SeaSign(i);
        var tang = new Vector3(_route.Tangent[i].x, 0f, _route.Tangent[i].z).normalized;
        float yaw = Mathf.Atan2(tang.x, tang.z) * Mathf.Rad2Deg;

        Vector3 club = road + sea * 34f;
        club.y = SeaLevelY + 0.05f;
        PlaceWorld("Nagisa_B_MarinaClub", parent, club, yaw, 1f, MaritimeCull, NbColourway(1), hero: true);
        PlaceWorld("Nagisa_Pontoon", parent, club + sea * 22f + tang * 7f, yaw, 0.9f, MaritimeCull);
        PlaceWorld("Nagisa_S_BikeRack", parent, road + sea * 15f + tang * 13f, yaw + 90f, 1f, MaritimeCull);

        // Working harbour: lower, practical silhouettes and a service-side rhythm, kept
        // visually separate from the resort club so the contrast reads at a glance.
        // Claude G (2026-10-01): the service quay used to sit INLAND (road - sea*30) inside the NB3 highway band and forced the
        // highway into a tunnel at m 318-330. The working harbour is now the real one in Marina2 (fish sheds, mole, boats),
        // so this placeholder is disabled.
        Vector3 service = road - sea * 30f + tang * 18f;
        if (false && CanPlace(service.x, service.z, 5f, out float sy, 1.5f))
        {
            service.y = sy;
            PlaceWorld("Nagisa_B_SurfShop", parent, service, yaw + 180f, 0.82f, MaritimeCull,
                       NbColourway(5), hero: false);
            PlaceWorld("Nagisa_S_BikeRack", parent, service - tang * 9f + sea * 5f, yaw, 0.85f, MaritimeCull);
            PlaceWorld("Nagisa_S_StreetLight", parent, service - tang * 13f - sea * 4f, yaw + 180f, 0.9f, MaritimeCull);
        }

        int lamps = 0;
        for (int k = -2; k <= 2; k++)
        {
            Vector3 p = club + tang * (k * 18f) - sea * 8f;
            if (!CanPlace(p.x, p.z, 1.8f, out float y, 1.2f)) continue;
            p.y = y;
            if (PlaceWorld("Nagisa_S_StreetLight", parent, p, yaw, 0.95f, MaritimeCull) != null) lamps++;
        }
        Debug.Log($"[nagisa-maritime] marina contrast: premium club + service quay, {lamps} promenade lights.");
    }

    private static void BuildMarinaTraffic(Transform parent)
    {
        // Small, slow loops read as harbour manoeuvres rather than a second open-sea race lane.
        int i = _route.IndexAt(420f);
        var road = _route.Position[i];
        var sea = _route.SideFlat(i) * SeaSign(i);
        var tangent = new Vector3(_route.Tangent[i].x, 0f, _route.Tangent[i].z).normalized;
        var cross = Vector3.Cross(Vector3.up, tangent);
        var centre = road + sea * 118f;
        centre.y = SeaLevelY + 0.3f;

        AddMarinaMover(parent, "Nagisa_MarinaTender", "Nagisa_WaterTaxi", centre,
                       tangent, cross, 38f, 20f, 4.5f, 0.18f, 8101);
        AddMarinaMover(parent, "Nagisa_WorkingHarbourLaunch", "Nagisa_Sailboat", centre + cross * 36f,
                       tangent, cross, 52f, 15f, 3.1f, 0.52f, 8102);
    }

    private static void AddMarinaMover(Transform parent, string name, string stem, Vector3 centre,
                                       Vector3 tangent, Vector3 cross, float rx, float rz,
                                       float speed, float phase, int seed)
    {
        var points = new List<Vector3>(32);
        for (int k = 0; k < 32; k++)
        {
            float a = k / 32f * Mathf.PI * 2f;
            points.Add(centre + tangent * (Mathf.Cos(a) * rx) + cross * (Mathf.Sin(a) * rz));
        }
        for (int k = 0; k < points.Count; k++)
        {
            var p = points[k];
            if (_ground.Height(p.x, p.z) > -2f || _route.PlanDistance(p.x, p.z, out _) < MaritimeBoatRoadClear)
            {
                Debug.LogWarning($"[nagisa-maritime] dropped {name}: loop entered land/road at {p:F1}.");
                return;
            }
        }

        var holder = AmbientMoverStaging.Converge(parent, name);
        var boat = PlaceWorld(stem, holder.transform, points[0], 0f, 0.92f, MaritimeCull);
        if (boat == null) return;
        var mover = holder.AddComponent<AmbientPathMover>();
        mover.mode = AmbientPathMover.PathMode.Loop;
        mover.path = AmbientMoverStaging.BakePolyline(points);
        mover.cars = new[] { boat.transform };
        mover.carOffsets = new[] { 0f };
        mover.carYaw = new[] { 0f };
        mover.speed = speed;
        mover.accel = 1.2f;
        mover.bankFactor = 0.16f;
        mover.bobHeave = 0.08f;
        mover.bobTilt = 0.65f;
        mover.bobFrequency = 0.32f;
        mover.cullDistance = AmbientCull.DefaultCullDistance;
        mover.seed = seed;
        mover.Bake();
        mover.startDistance = phase * mover.TotalLength;
        mover.SetHead(mover.startDistance);
        mover.Place();
        EditorUtility.SetDirty(mover);
        Debug.Log($"[nagisa-maritime] moving boat {name}: {mover.path.Length} points, {mover.TotalLength:0} m loop, {speed:0.0} m/s.");
    }

    private static void BuildPurposefulLife(Transform parent)
    {
        int i0 = _route.IndexAt(120f), i1 = _route.IndexAt(840f), placed = 0;
        for (int i = i0; i <= i1; i += 18)
        {
            var p = _route.Position[i];
            var sea = _route.SideFlat(i) * SeaSign(i);
            var t = new Vector3(_route.Tangent[i].x, 0f, _route.Tangent[i].z).normalized;
            // Boardwalk-side activity points: racks face the quay; lamps face the ride line.
            var rack = p + sea * 14f + t * 4f;
            if (CanPlace(rack.x, rack.z, 1.2f, out float y, 1.2f))
            {
                rack.y = y;
                if (PlaceWorld("Nagisa_S_BikeRack", parent, rack, Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg + 90f,
                               0.7f, MaritimeCull) != null) placed++;
            }
            if (i % 36 == 0)
            {
                var bench = p + sea * 16f - t * 3f;
                if (CanPlace(bench.x, bench.z, 1.4f, out float by, 1.2f))
                {
                    bench.y = by;
                    if (PlaceWorld("Nagisa_Bench", parent, bench, Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg,
                                   0.9f, MaritimeCull) != null) placed++;
                }
            }
        }
        Debug.Log($"[nagisa-maritime] purposeful marina life: {placed} bike-parking / rest anchors staged.");
    }

    private static void BuildClimbDescentDressing(Transform group)
    {
        var dressing = new GameObject("Climb and Descent Story Dressing").transform;
        dressing.SetParent(group, false);
        int count = 0;
        float[] marks = { 4720f, 5350f, 6250f, 7350f, 8250f, 11620f, 12450f, 13400f, 14600f };
        foreach (float d in marks)
        {
            int i = _route.IndexAt(d);
            var p = _route.Position[i];
            var side = _route.SideFlat(i) * (d < _route.RidgeEndM ? -1f : 1f);
            var tang = new Vector3(_route.Tangent[i].x, 0f, _route.Tangent[i].z).normalized;
            var q = p + side * (CorridorKeepOutM + 3.5f);
            if (!CanPlace(q.x, q.z, 1.5f, out float y, 2f)) continue;
            q.y = y;
            string stem = d < _route.RidgeEndM ? "Nagisa_S_StreetLight" : "Nagisa_Bench";
            if (PlaceWorld(stem, dressing, q, Mathf.Atan2(tang.x, tang.z) * Mathf.Rad2Deg + 180f,
                           d < _route.RidgeEndM ? 0.8f : 0.75f, MaritimeCull) != null) count++;
        }
        Debug.Log($"[nagisa-maritime] climb/descent dressing: {count}/{marks.Length} authored effort/release markers.");
    }

    private static void BuildShorelineCoral(Transform group)
    {
        var coralRoot = new GameObject("Dedicated Shoreline Coral").transform;
        coralRoot.SetParent(group, false);
        int placed = 0;
        float[][] spans = { new[] { 0f, 4720f }, new[] { 12167f, 14991f } };
        for (int s = 0; s < spans.Length; s++)
        {
            for (float d = spans[s][0] + 24f; d < spans[s][1] - 24f && placed < 48; d += 74f)
            {
                int i = _route.IndexAt(d);
                var p = _route.Position[i];
                var sea = _route.SideFlat(i) * SeaSign(i);
                for (float off = 2.5f; off <= 11f && placed < 48; off += 2.1f)
                {
                    var q = p + sea * off;
                    float coast = _ground.Coast(q.x, q.z);
                    float h = _ground.Height(q.x, q.z);
                    if (coast < 0.8f || coast > 9f || h < 0.15f) continue;
                    if (!CanPlace(q.x, q.z, 1.8f, out float y, 0.2f)) continue;
                    q.y = y;
                    if (PlaceWorld("Nagisa_NB9_CoralOutcrop", coralRoot, q, (d * 13f) % 360f,
                                   0.65f + (d % 3f) * 0.12f, 0.004f) != null) { placed++; break; }
                }
            }
        }
        Debug.Log($"[nagisa-maritime] shoreline coral fix: {placed} dedicated outcrops across coastal scans (target 24+).");
    }
}
