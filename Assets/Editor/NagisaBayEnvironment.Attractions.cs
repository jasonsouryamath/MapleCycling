// Nagisa Bay overhaul - NB8B animated attractions (COORDINATION.md NB8, sub-task 8B) - copilot CLI.
//
// Stage 97 "Attractions" (NB0 hook): builds under "NB Overhaul/NB Attractions"
//   * Shiokaze Pier (fictional): an elevated timber pier on piles running seaward from route
//     ~4,130 m, a wide pier head with a 44 m Ferris wheel (AmbientRotor on the hub; cabins on
//     pins kept upright by NagisaAttractionUpright + a gentle AmbientSway) and a small roller
//     coaster (baked closed Catmull-Rom track mesh + supports; 5-car train on an AmbientPathMover
//     in Loop mode).
//   * Palm Ridge Gondola (fictional): valley station (-600,-33450) -> summit station (-350,-31400),
//     towers placed on the upper hull of the terrain + clearance, sagging cable, 24 cabins on one
//     AmbientPathMover Loop (up one line, round the bullwheels, down the other), bullwheels on
//     AmbientRotors.
// Models: tools/blender/build_nagisa_attractions_models.py -> Models/Nagisa_NB8B_*.glb (LOD0-2).
// The stage group is rebuilt by exact name every run, so re-runs converge (no duplicates).
// No colliders; nothing enters the ride corridor (logged). All tuning below is PROVISIONAL.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- tunables (PROVISIONAL)
    private const float Nb8bPierRouteM = 4130f;       // route metre the pier leaves from
    private const float Nb8bPierStartM = 45f;         // seaward offset of the deck start (ramp foot is 20 m nearer)
    private const float Nb8bDeckY = 5.2f;             // deck-top height above sea level
    private const int Nb8bWalkBays = 10;              // 20 m bays of the 18 m walkway
    private const int Nb8bHeadRows = 5;               // pier head: 4 x 5 bays (72 x 100 m)
    private const float Nb8bWheelDegPerSec = 3f;      // ~2 min per revolution
    private const float Nb8bWheelR = 22f, Nb8bWheelHubY = 26f; // contract with the Blender kit
    private const int Nb8bWheelPins = 24;
    private const float Nb8bCoasterSpeed = 11f;       // m/s
    private const int Nb8bCoasterCars = 5;
    private const float Nb8bCarPitch = 2.8f;

    private static readonly Vector2 Nb8bGondolaLow = new Vector2(-600f, -33450f);
    private static readonly Vector2 Nb8bGondolaTop = new Vector2(-350f, -31400f);
    private const float Nb8bStationCableY = 6.3f, Nb8bCableGauge = 3.2f, Nb8bBullwheelZ = -8f, Nb8bStationExitZ = 14f;
    private const float Nb8bTowerHeadCableY = 1.2f;
    private const float Nb8bCableClear = 21f;         // cable above ground at hull points
    private const float Nb8bMaxSpan = 380f;           // add towers on longer spans
    private const float Nb8bSagFrac = 0.012f;         // mid-span sag / span
    private const float Nb8bGondolaSpeed = 5f;        // m/s
    private const int Nb8bGondolaCabins = 24;

    private static readonly Dictionary<string, Material> _nb8b = new Dictionary<string, Material>();

    // ================================================================= stage
    [NagisaStage(97, "Attractions")]
    private static void BuildAttractions(Transform group)
    {
        if (_route == null) _route = NagisaRoute.Load();
        if (_ground == null) _ground = NagisaGround.Load();
        Nb8bMaterials();

        var pier = BuildShiokazePier(group);
        var gondola = BuildPalmRidgeGondola(group);
        Debug.Log($"[nagisa-nb8b] attractions staged: pier={(pier != null)}, gondola={(gondola != null)}");
    }

    private static void Nb8bMaterials()
    {
        _nb8b.Clear();
        Color c(float r, float g, float b) => new Color(r, g, b, 1f);
        _nb8b["NB8B_Wood"] = NbCel("NB8B_Wood", "NB_PontoonWood", c(0.92f, 0.86f, 0.78f), 0.18f, 0.10f);
        _nb8b["NB8B_TimberDark"] = NbCel("NB8B_TimberDark", "NB_Teak", c(0.52f, 0.42f, 0.34f), 0.14f, 0.08f);
        _nb8b["NB8B_Concrete"] = NbCel("NB8B_Concrete", "NB_Stone", c(0.80f, 0.79f, 0.76f), 0.10f, 0.06f);
        _nb8b["NB8B_Roof"] = NbCel("NB8B_Roof", "NB_MetalRoof", c(0.36f, 0.66f, 0.70f), 0.45f, 0.35f);
        _nb8b["NB8B_PaintWhite"] = Cel("Nagisa_NB8B_PaintWhite", c(0.94f, 0.94f, 0.91f), 0.50f, 0.35f, 0.22f);
        _nb8b["NB8B_Steel"] = Cel("Nagisa_NB8B_Steel", c(0.46f, 0.48f, 0.52f), 0.60f, 0.50f, 0.18f);
        _nb8b["NB8B_Track"] = Cel("Nagisa_NB8B_Track", c(0.86f, 0.20f, 0.16f), 0.55f, 0.45f, 0.2f);
        _nb8b["NB8B_Cable"] = Cel("Nagisa_NB8B_Cable", c(0.12f, 0.12f, 0.13f), 0.5f, 0.4f, 0.1f);
        _nb8b["NB8B_SignBoard"] = Cel("Nagisa_NB8B_SignBoard", c(0.10f, 0.22f, 0.38f), 0.35f, 0.2f);
        _nb8b["NB8B_CabinPaint"] = Cel("Nagisa_NB8B_Cabin0", c(0.95f, 0.36f, 0.42f), 0.55f, 0.4f, 0.2f);
        _nb8b["NB8B_CoasterPaint"] = Cel("Nagisa_NB8B_CoasterPaint", c(0.98f, 0.72f, 0.10f), 0.6f, 0.45f, 0.2f);
        _nb8b["NB8B_Seat"] = Cel("Nagisa_NB8B_Seat", c(0.18f, 0.28f, 0.55f), 0.3f, 0.15f);
        _nb8b["NB8B_Skin"] = Cel("Nagisa_NB8B_Skin", c(0.98f, 0.83f, 0.72f), 0.2f, 0.1f, 0.2f);
        _nb8b["NB8B_Shirt"] = Cel("Nagisa_NB8B_Shirt", c(0.30f, 0.70f, 0.85f), 0.2f, 0.1f);
        _nb8b["NB8B_Hair"] = Cel("Nagisa_NB8B_Hair", c(0.22f, 0.15f, 0.12f), 0.4f, 0.3f);
        _nb8b["NB8B_Rubber"] = Cel("Nagisa_NB8B_Rubber", c(0.08f, 0.08f, 0.09f), 0.2f, 0.1f);
        _nb8b["NB8B_Glass"] = NbLit("NB8B_Glass", new Color(0.55f, 0.72f, 0.80f, 0.35f), 0.92f, 0f, true);
        _nb8b["NB8B_Bulb"] = NbGlow("NB8B_Bulb", c(1f, 0.86f, 0.56f));
        _nb8b["NB8B_SignGlow"] = NbGlow("NB8B_SignGlow", c(1f, 0.42f, 0.36f));
    }

    private static readonly Color[] Nb8bCabinColours =
    {
        new Color(0.95f, 0.36f, 0.42f), new Color(0.30f, 0.72f, 0.86f), new Color(0.98f, 0.78f, 0.22f),
        new Color(0.44f, 0.80f, 0.52f), new Color(0.72f, 0.52f, 0.90f), new Color(0.98f, 0.58f, 0.30f),
    };

    private static Dictionary<string, Material> Nb8bCabinRemap(int k)
    {
        var d = new Dictionary<string, Material>(_nb8b);
        int i = ((k % Nb8bCabinColours.Length) + Nb8bCabinColours.Length) % Nb8bCabinColours.Length;
        d["NB8B_CabinPaint"] = Cel("Nagisa_NB8B_Cabin" + i, Nb8bCabinColours[i], 0.55f, 0.4f, 0.2f);
        return d;
    }

    private static GameObject Nb8bPlace(string stem, Transform parent, Vector3 local, Quaternion rot,
                                        bool hero = false, Dictionary<string, Material> remap = null)
    {
        var go = Place("Nagisa_NB8B_" + stem, parent, local, 0f, 1f, hero, 0.0015f, remap ?? _nb8b);
        if (go != null) go.transform.localRotation = rot;
        return go;
    }

    private static Transform Nb8bNode(Transform parent, string name, Vector3 local, Quaternion rot)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = local;
        t.localRotation = rot;
        return t;
    }

    // ================================================================= Shiokaze Pier
    private static Transform BuildShiokazePier(Transform group)
    {
        int i = _route.IndexAt(Nb8bPierRouteM);
        var p = _route.Position[i];
        var sd = _route.SideFlat(i);
        var probe = p + sd * 200f;
        if (_ground.Coast(probe.x, probe.z) > 0f) sd = -sd;           // + inland: flip to seaward
        var origin = p + sd * Nb8bPierStartM;
        origin.y = Nb8bDeckY;
        float yaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;

        var pier = Nb8bNode(group, "NB8B Shiokaze Pier", Vector3.zero, Quaternion.identity);
        pier.position = origin;
        pier.rotation = Quaternion.Euler(0f, yaw, 0f);
        var I = Quaternion.identity;
        var r90 = Quaternion.Euler(0f, 90f, 0f);
        var r180 = Quaternion.Euler(0f, 180f, 0f);

        // --- landward ramp + entrance arch (from the sand up to the deck)
        var foot = pier.TransformPoint(0f, 0f, -20f);
        float footY = Mathf.Max(_ground.Height(foot.x, foot.z), 0.3f);
        float drop = Nb8bDeckY - footY;
        var tilt = Quaternion.Euler(-Mathf.Atan2(drop, 20f) * Mathf.Rad2Deg, 0f, 0f);
        var deckT = Nb8bNode(pier, "NB8B Pier Deck", Vector3.zero, I);
        Nb8bPlace("PierDeck", deckT, new Vector3(0f, -drop, -20f), tilt);
        Nb8bPlace("PierRail", deckT, new Vector3(-9f, -drop, -20f), tilt);
        Nb8bPlace("PierRail", deckT, new Vector3(9f, -drop, -20f), tilt * r180 * Quaternion.identity).transform.localPosition = new Vector3(9f, 0f, 0f);
        Nb8bPlace("PierArch", pier, new Vector3(0f, -drop, -20f), I, true);

        // --- walkway
        for (int k = 0; k < Nb8bWalkBays; k++)
        {
            float z = 20f * k;
            Nb8bPlace("PierDeck", deckT, new Vector3(0f, 0f, z), I);
            Nb8bPlace("PierRail", deckT, new Vector3(-9f, 0f, z), I);
            Nb8bPlace("PierRail", deckT, new Vector3(9f, 0f, z + 20f), r180);
        }
        // --- pier head 72 x 100 m
        float z0 = 20f * Nb8bWalkBays, z1 = z0 + 20f * Nb8bHeadRows;
        foreach (float x in new[] { -27f, -9f, 9f, 27f })
            for (int k = 0; k < Nb8bHeadRows; k++)
                Nb8bPlace("PierDeck", deckT, new Vector3(x, 0f, z0 + 20f * k), I);
        for (int k = 0; k < Nb8bHeadRows; k++)
        {
            Nb8bPlace("PierRail", deckT, new Vector3(-36f, 0f, z0 + 20f * k), I);
            Nb8bPlace("PierRail", deckT, new Vector3(36f, 0f, z0 + 20f * k + 20f), r180);
        }
        foreach (float x in new[] { -36f, -16f, 4f, 16f })
            Nb8bPlace("PierRail", deckT, new Vector3(x, 0f, z1), r90);
        foreach (float x in new[] { -36f, -29f, 9f, 16f })
            Nb8bPlace("PierRail", deckT, new Vector3(x, 0f, z0), r90);

        BuildNb8bWheel(pier, new Vector3(-16f, 0f, z0 + 50f));
        BuildNb8bCoaster(pier);

        // keep-out report (landward ramp foot is the closest point to the ride road)
        float dFoot = _route.PlanDistance(foot.x, foot.z, out _);
        Debug.Log($"[nagisa-nb8b] Shiokaze Pier at route {Nb8bPierRouteM:0} m, yaw {yaw:0.0}, ramp foot {dFoot:0.0} m " +
                  $"from the centreline (keep-out {CorridorKeepOutM:0.0} m), ramp drop {drop:0.00} m, head centre {pier.TransformPoint(0f, 0f, (z0 + z1) * 0.5f)}");
        if (dFoot < CorridorKeepOutM + 4f) Debug.LogError("[nagisa-nb8b] pier ramp too close to the ride road");
        return pier;
    }

    private static void BuildNb8bWheel(Transform pier, Vector3 local)
    {
        var wheel = Nb8bNode(pier, "NB8B Ferris Wheel", local, Quaternion.identity);
        Nb8bPlace("WheelFrame", wheel, Vector3.zero, Quaternion.identity, true);
        var hub = Nb8bNode(wheel, "NB8B Wheel Hub", new Vector3(0f, Nb8bWheelHubY, 0f), Quaternion.identity);
        Nb8bPlace("WheelRim", hub, Vector3.zero, Quaternion.identity, true);
        var pins = new Transform[Nb8bWheelPins];
        var cabins = new Transform[Nb8bWheelPins];
        for (int k = 0; k < Nb8bWheelPins; k++)
        {
            float a = k * Mathf.PI * 2f / Nb8bWheelPins;
            pins[k] = Nb8bNode(hub, $"NB8B Wheel Pin {k:00}", new Vector3(Nb8bWheelR * Mathf.Cos(a), Nb8bWheelR * Mathf.Sin(a), 0f), Quaternion.identity);
            var cab = Nb8bPlace("WheelCabin", pins[k], Vector3.zero, Quaternion.identity, false, Nb8bCabinRemap(k));
            cabins[k] = cab.transform;
        }
        var upright = Nb8bNode(wheel, "NB8B Wheel Upright", Vector3.zero, Quaternion.identity).gameObject.AddComponent<NagisaAttractionUpright>();
        upright.targets = pins;
        upright.yawReference = wheel;
        upright.Sync();
        AmbientMoverStaging.StageRotor(wheel, "NB8B Wheel Rotor", new[] { hub }, Vector3.forward, Nb8bWheelDegPerSec, 0f, 0f, 97);
        AmbientMoverStaging.StageSway(wheel, "NB8B Wheel Cabin Sway", cabins, Vector3.forward, 2.5f, 0.32f, AmbientSway.SwayMode.Pendulum, default, 971);
        Debug.Log($"[nagisa-nb8b] Ferris wheel: {Nb8bWheelPins} cabins, R {Nb8bWheelR} m, hub {Nb8bWheelHubY} m, {Nb8bWheelDegPerSec} deg/s");
    }

    // ---------------------------------------------------------------- coaster
    private static readonly Vector3[] Nb8bCoasterCtrl =
    {
        new Vector3(8f, 1.2f, 212f), new Vector3(8f, 1.2f, 224f), new Vector3(8f, 1.6f, 234f),
        new Vector3(8f, 6f, 246f), new Vector3(8f, 16f, 262f), new Vector3(10f, 18.6f, 271f),
        new Vector3(15f, 19f, 278f), new Vector3(22f, 16.5f, 284f), new Vector3(28f, 8f, 287f),
        new Vector3(32.5f, 3.2f, 278f), new Vector3(33f, 4.5f, 264f), new Vector3(31f, 11f, 253f),
        new Vector3(31.5f, 5f, 241f), new Vector3(32.5f, 2.8f, 228f), new Vector3(29f, 8.5f, 215f),
        new Vector3(22f, 6f, 207f), new Vector3(14f, 2.2f, 205.5f),
    };

    private static void BuildNb8bCoaster(Transform pier)
    {
        var coaster = Nb8bNode(pier, "NB8B Coaster", Vector3.zero, Quaternion.identity);
        var local = AmbientMoverStaging.ResamplePolyline(AmbientMoverStaging.BakeCatmullRom(Nb8bCoasterCtrl, 14, true), 0.5f, true);
        int n = local.Length;
        var tr = new MB8(); var sup = new MB8(); var st = new MB8();
        float along = 0f;
        for (int j = 0; j < n; j++)
        {
            var a = local[j]; var b = local[(j + 1) % n];
            var t = (b - a).normalized;
            var s = Vector3.Cross(Vector3.up, t).normalized;
            var u = Vector3.Cross(t, s).normalized;
            var ta = (local[(j + 1) % n] - local[(j + n - 1) % n]).normalized;
            var sa = Vector3.Cross(Vector3.up, ta).normalized; var ua = Vector3.Cross(ta, sa).normalized;
            var tb = (local[(j + 2) % n] - local[j]).normalized;
            var sb = Vector3.Cross(Vector3.up, tb).normalized; var ub = Vector3.Cross(tb, sb).normalized;
            float L = Vector3.Distance(a, b);
            foreach (float x in new[] { -0.55f, 0.55f })
                tr.TubeSeg(a + sa * x - ua * 0.12f, b + sb * x - ub * 0.12f, sa, ua, sb, ub, 0.09f, 6, along, L);
            tr.TubeSeg(a - ua * 0.62f, b - ub * 0.62f, sa, ua, sb, ub, 0.2f, 6, along, L);   // spine
            if (j % 3 == 0) tr.Box(a - u * 0.35f, s, u, t, new Vector3(0.62f, 0.08f, 0.09f));  // tie
            if (j % 3 == 0) foreach (float x in new[] { -0.3f, 0.3f }) tr.Box(a + s * x - u * 0.4f, s, u, t, new Vector3(0.05f, 0.22f, 0.05f));
            if (j % 14 == 0 && a.y > 2.2f)
            {
                var top = a - u * 0.8f;
                sup.Tube(new Vector3(top.x, 0f, top.z), top, 0.2f, 8);
                sup.Box(new Vector3(top.x, 0.1f, top.z), Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.45f, 0.1f, 0.45f));
                if (a.y > 9f)
                    foreach (float x in new[] { -1f, 1f })
                        sup.Tube(new Vector3(top.x, 0f, top.z) + s * (2.2f * x), top - u * 2.5f, 0.1f, 6);
            }
            along += L;
        }
        // station: platform + canopy over the straight (x=8, z 212..232)
        st.Box(new Vector3(11.2f, 0.45f, 222f), Vector3.right, Vector3.up, Vector3.forward, new Vector3(1.6f, 0.45f, 11f));
        foreach (float x in new[] { 5.2f, 12.4f })
            foreach (float z in new[] { 212f, 222f, 232f })
                st.Tube(new Vector3(x, 0f, z), new Vector3(x, 4.2f, z), 0.12f, 8);
        var roofMb = new MB8();
        roofMb.Box(new Vector3(8.8f, 4.35f, 222f), Vector3.right, Vector3.up, Vector3.forward, new Vector3(4.6f, 0.18f, 12f));
        AddMesh(coaster, "NB8B Coaster Track", tr.Mesh("NB8B_CoasterTrack"), _nb8b["NB8B_Track"], false);
        AddMesh(coaster, "NB8B Coaster Supports", sup.Mesh("NB8B_CoasterSupports"), _nb8b["NB8B_PaintWhite"], false);
        AddMesh(coaster, "NB8B Coaster Station", st.Mesh("NB8B_CoasterStation"), _nb8b["NB8B_PaintWhite"], false);
        AddMesh(coaster, "NB8B Coaster Canopy", roofMb.Mesh("NB8B_CoasterCanopy"), _nb8b["NB8B_Roof"], false);

        // train
        var world = new Vector3[n];
        for (int j = 0; j < n; j++) world[j] = pier.TransformPoint(local[j]);
        var trainT = Nb8bNode(coaster, "NB8B Coaster Train", Vector3.zero, Quaternion.identity);
        var cars = new Transform[Nb8bCoasterCars];
        var offs = new float[Nb8bCoasterCars];
        for (int k = 0; k < Nb8bCoasterCars; k++)
        {
            var car = Nb8bNode(trainT, $"NB8B Coaster Car {k}", Vector3.zero, Quaternion.identity);
            Nb8bPlace("CoasterCar", car, Vector3.zero, Quaternion.identity);
            cars[k] = car; offs[k] = k * Nb8bCarPitch;
        }
        var mover = AmbientMoverStaging.StagePathMover(coaster, "NB8B Coaster Mover", world, cars, offs, null,
            Nb8bCoasterSpeed, 3f, AmbientPathMover.PathMode.Loop, 0f, 30f, 0f, 0f, 0f, 972);
        mover.transform.position = pier.TransformPoint(20f, 8f, 250f);
        Debug.Log($"[nagisa-nb8b] coaster: track {along:0} m ({n} samples), {Nb8bCoasterCars} cars @ {Nb8bCoasterSpeed} m/s, " +
                  $"height {Nb8bMinY(local):0.0}..{Nb8bMaxY(local):0.0} m above deck");
    }

    private static float Nb8bMinY(Vector3[] p) { float m = float.MaxValue; foreach (var v in p) m = Mathf.Min(m, v.y); return m; }
    private static float Nb8bMaxY(Vector3[] p) { float m = float.MinValue; foreach (var v in p) m = Mathf.Max(m, v.y); return m; }

    // ================================================================= Palm Ridge Gondola
    private static Transform BuildPalmRidgeGondola(Transform group)
    {
        var g = Nb8bNode(group, "NB8B Palm Ridge Gondola", Vector3.zero, Quaternion.identity);
        var lo = new Vector3(Nb8bGondolaLow.x, _ground.Height(Nb8bGondolaLow.x, Nb8bGondolaLow.y), Nb8bGondolaLow.y);
        var hi = new Vector3(Nb8bGondolaTop.x, _ground.Height(Nb8bGondolaTop.x, Nb8bGondolaTop.y), Nb8bGondolaTop.y);
        var dir = new Vector3(hi.x - lo.x, 0f, hi.z - lo.z).normalized;
        var side = Vector3.Cross(Vector3.up, dir).normalized;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        var stLo = Nb8bNode(g, "NB8B Valley Station", Vector3.zero, Quaternion.identity);
        stLo.position = lo; stLo.rotation = Quaternion.Euler(0f, yaw, 0f);
        var stHi = Nb8bNode(g, "NB8B Summit Station", Vector3.zero, Quaternion.identity);
        stHi.position = hi; stHi.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
        Nb8bPlace("GondolaStationLow", stLo, Vector3.zero, Quaternion.identity, true);
        Nb8bPlace("GondolaStationTop", stHi, Vector3.zero, Quaternion.identity, true);
        var bwLo = Nb8bPlace("Bullwheel", stLo, new Vector3(0f, Nb8bStationCableY, Nb8bBullwheelZ), Quaternion.identity).transform;
        var bwHi = Nb8bPlace("Bullwheel", stHi, new Vector3(0f, Nb8bStationCableY, Nb8bBullwheelZ), Quaternion.identity).transform;
        float bwDeg = -Nb8bGondolaSpeed / Nb8bCableGauge * Mathf.Rad2Deg;
        AmbientMoverStaging.StageRotor(stLo, "NB8B Valley Bullwheel Rotor", new[] { bwLo }, Vector3.up, bwDeg, 0f, 0f, 973);
        AmbientMoverStaging.StageRotor(stHi, "NB8B Summit Bullwheel Rotor", new[] { bwHi }, Vector3.up, bwDeg, 0f, 0f, 974);

        // --- line profile: exits of both stations, towers on the upper hull of ground + clearance
        var e0 = stLo.TransformPoint(0f, Nb8bStationCableY, Nb8bStationExitZ);
        var e1 = stHi.TransformPoint(0f, Nb8bStationCableY, Nb8bStationExitZ);
        float span = new Vector2(e1.x - e0.x, e1.z - e0.z).magnitude;
        var pts = new List<Vector2> { new Vector2(0f, e0.y) };
        for (float s = 30f; s < span - 30f; s += 10f)
        {
            var q = Vector3.Lerp(e0, e1, s / span);
            float gy = Mathf.Max(_ground.Height(q.x + side.x * 3.2f, q.z + side.z * 3.2f), _ground.Height(q.x - side.x * 3.2f, q.z - side.z * 3.2f));
            pts.Add(new Vector2(s, Mathf.Max(gy, 0f) + Nb8bCableClear));
        }
        pts.Add(new Vector2(span, e1.y));
        var hull = new List<Vector2>();                      // upper hull (monotone chain)
        foreach (var q in pts)
        {
            while (hull.Count >= 2)
            {
                var o = hull[hull.Count - 2]; var a = hull[hull.Count - 1];
                if ((a.x - o.x) * (q.y - o.y) - (a.y - o.y) * (q.x - o.x) >= 0f) hull.RemoveAt(hull.Count - 1); else break;
            }
            hull.Add(q);
        }
        var sup = new List<Vector2> { hull[0] };             // split long spans
        for (int k = 1; k < hull.Count; k++)
        {
            var a = hull[k - 1]; var b = hull[k];
            int parts = Mathf.CeilToInt((b.x - a.x) / Nb8bMaxSpan);
            for (int m = 1; m < parts; m++) sup.Add(Vector2.Lerp(a, b, m / (float)parts));
            sup.Add(b);
        }
        // merge supports closer than 60 m (keep the higher)
        for (int k = sup.Count - 2; k >= 1; k--)
            if (sup[k + 1].x - sup[k].x < 60f && k + 1 < sup.Count - 1) { if (sup[k + 1].y > sup[k].y) sup[k] = sup[k + 1]; sup.RemoveAt(k + 1); }

        Vector3 LinePoint(float s, float lateral)
        {
            int k = 1; while (k < sup.Count - 1 && sup[k].x < s) k++;
            var a = sup[k - 1]; var b = sup[k];
            float u = Mathf.Clamp01((s - a.x) / Mathf.Max(b.x - a.x, 1e-3f));
            float y = Mathf.Lerp(a.y, b.y, u) - 4f * Nb8bSagFrac * (b.x - a.x) * u * (1f - u);
            var q = Vector3.Lerp(e0, e1, s / span);
            return new Vector3(q.x, y, q.z) + side * lateral;
        }

        // towers
        var towers = Nb8bNode(g, "NB8B Gondola Towers", Vector3.zero, Quaternion.identity);
        float minTower = float.MaxValue, maxTower = 0f;
        for (int k = 1; k < sup.Count - 1; k++)
        {
            var q = Vector3.Lerp(e0, e1, sup[k].x / span);
            float gy = _ground.Height(q.x, q.z);
            float headY = sup[k].y - Nb8bTowerHeadCableY;
            float h = headY - gy;
            minTower = Mathf.Min(minTower, h); maxTower = Mathf.Max(maxTower, h);
            var tw = Nb8bNode(towers, $"NB8B Tower {k:00}", Vector3.zero, Quaternion.identity);
            tw.position = new Vector3(q.x, gy, q.z); tw.rotation = Quaternion.Euler(0f, yaw, 0f);
            Nb8bPlace("TowerFooting", tw, Vector3.zero, Quaternion.identity);
            var mast = Nb8bPlace("TowerMast", tw, Vector3.zero, Quaternion.identity, true);
            mast.transform.localScale = new Vector3(1f, h, 1f);
            var lg = mast.GetComponent<LODGroup>(); if (lg != null) lg.RecalculateBounds();
            Nb8bPlace("TowerHead", tw, new Vector3(0f, h, 0f), Quaternion.identity, true);
        }

        // --- closed cabin path: valley bullwheel -> up line (+side) -> summit bullwheel -> down line
        var path = new List<Vector3>();
        var cLo = stLo.TransformPoint(0f, Nb8bStationCableY, Nb8bBullwheelZ);
        var cHi = stHi.TransformPoint(0f, Nb8bStationCableY, Nb8bBullwheelZ);
        const int arc = 12;
        for (int k = 0; k <= arc; k++)
        {
            float th = Mathf.PI * k / arc;
            path.Add(cLo + side * (-Nb8bCableGauge * Mathf.Cos(th)) - dir * (Nb8bCableGauge * Mathf.Sin(th)));
        }
        path.Add(e0 + side * Nb8bCableGauge);
        for (float s = 10f; s < span; s += 10f) path.Add(LinePoint(s, Nb8bCableGauge));
        path.Add(e1 + side * Nb8bCableGauge);
        for (int k = 0; k <= arc; k++)
        {
            float th = Mathf.PI * k / arc;
            path.Add(cHi + side * (Nb8bCableGauge * Mathf.Cos(th)) + dir * (Nb8bCableGauge * Mathf.Sin(th)));
        }
        path.Add(e1 - side * Nb8bCableGauge);
        for (float s = span - 10f; s > 0f; s -= 10f) path.Add(LinePoint(s, -Nb8bCableGauge));
        path.Add(e0 - side * Nb8bCableGauge);
        var P = path.ToArray();

        // cable mesh (world path -> group-local; the gondola group sits at the origin)
        var cable = new MB8();
        float along = 0f;
        for (int j = 0; j < P.Length; j++)
        {
            var a = g.InverseTransformPoint(P[j]); var b = g.InverseTransformPoint(P[(j + 1) % P.Length]);
            float L = Vector3.Distance(a, b);
            var t = (b - a).normalized; var s2 = Vector3.Cross(Vector3.up, t).normalized; var u2 = Vector3.Cross(t, s2);
            cable.TubeSeg(a + Vector3.up * 0.08f, b + Vector3.up * 0.08f, s2, u2, s2, u2, 0.075f, 6, along, L);
            along += L;
        }
        AddMesh(g, "NB8B Gondola Cable", cable.Mesh("NB8B_GondolaCable"), _nb8b["NB8B_Cable"], false);

        // cabins
        var cabinsT = Nb8bNode(g, "NB8B Gondola Cabins", Vector3.zero, Quaternion.identity);
        float loop = AmbientMoverStaging.GetPolylineLength(P, true);
        var cars = new Transform[Nb8bGondolaCabins];
        var grips = new Transform[Nb8bGondolaCabins];
        var bodies = new Transform[Nb8bGondolaCabins];
        var offs = new float[Nb8bGondolaCabins];
        for (int k = 0; k < Nb8bGondolaCabins; k++)
        {
            cars[k] = Nb8bNode(cabinsT, $"NB8B Gondola Car {k:00}", Vector3.zero, Quaternion.identity);
            grips[k] = Nb8bNode(cars[k], "Grip", Vector3.zero, Quaternion.identity);
            bodies[k] = Nb8bPlace("GondolaCabin", grips[k], Vector3.zero, Quaternion.identity, false, Nb8bCabinRemap(k)).transform;
            offs[k] = loop * k / Nb8bGondolaCabins;
        }
        var mover = AmbientMoverStaging.StagePathMover(g, "NB8B Gondola Mover", P, cars, offs, null,
            Nb8bGondolaSpeed, 1f, AmbientPathMover.PathMode.Loop, 0f, loop * 0.37f, 0f, 0f, 0f, 975);
        mover.transform.position = Vector3.Lerp(lo, hi, 0.5f);
        var up = Nb8bNode(g, "NB8B Gondola Upright", Vector3.Lerp(lo, hi, 0.5f), Quaternion.identity).gameObject.AddComponent<NagisaAttractionUpright>();
        up.targets = grips;
        up.Sync();
        var sway = AmbientMoverStaging.StageSway(g, "NB8B Gondola Cabin Sway", bodies, Vector3.right, 2f, 0.28f, AmbientSway.SwayMode.Pendulum, default, 976);
        sway.transform.position = Vector3.Lerp(lo, hi, 0.5f);

        // clearance report: cabin floor vs ground along both lines
        float minClear = float.MaxValue; Vector3 worst = Vector3.zero;
        for (int j = arc + 2; j < P.Length; j++)
        {
            float gy = _ground.Height(P[j].x, P[j].z);
            float c = P[j].y - 4.65f - gy;
            if (Vector3.Distance(P[j], lo) < 30f || Vector3.Distance(P[j], hi) < 30f) continue;
            if (c < minClear) { minClear = c; worst = P[j]; }
        }
        float dLo = _route.PlanDistance(lo.x, lo.z, out _), dHi = _route.PlanDistance(hi.x, hi.z, out _);
        float dLine = float.MaxValue;
        for (int j = 0; j < P.Length; j += 3) dLine = Mathf.Min(dLine, _route.PlanDistance(P[j].x, P[j].z, out _));
        Debug.Log($"[nagisa-nb8b] Palm Ridge Gondola: span {span:0} m, rise {e1.y - e0.y:0.0} m, {sup.Count - 2} towers " +
                  $"({minTower:0.0}..{maxTower:0.0} m), loop {loop:0} m, {Nb8bGondolaCabins} cabins @ {Nb8bGondolaSpeed} m/s, " +
                  $"min cabin-floor clearance {minClear:0.0} m at {worst}, route distance valley {dLo:0} / summit {dHi:0} / line min {dLine:0} m");
        if (minClear < 3f) Debug.LogError("[nagisa-nb8b] gondola cabin clearance below 3 m");
        if (dLine < CorridorKeepOutM + 10f) Debug.LogError("[nagisa-nb8b] gondola line inside the ride corridor");
        return g;
    }

    // ================================================================= tiny mesh builder
    private sealed class MB8
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> U = new List<Vector2>();
        public readonly List<int> T = new List<int>();

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { (a, d) = (d, a); (b, c) = (c, b); (ua, ud) = (ud, ua); (ub, uc) = (uc, ub); }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            U.Add(ua); U.Add(ub); U.Add(uc); U.Add(ud);
            T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
        }

        public void TubeSeg(Vector3 a, Vector3 b, Vector3 sa, Vector3 ua, Vector3 sb, Vector3 ub, float r, int seg, float v0, float len)
        {
            for (int k = 0; k < seg; k++)
            {
                float t0 = k * Mathf.PI * 2f / seg, t1 = (k + 1) * Mathf.PI * 2f / seg;
                var oa0 = (sa * Mathf.Cos(t0) + ua * Mathf.Sin(t0)) * r; var oa1 = (sa * Mathf.Cos(t1) + ua * Mathf.Sin(t1)) * r;
                var ob0 = (sb * Mathf.Cos(t0) + ub * Mathf.Sin(t0)) * r; var ob1 = (sb * Mathf.Cos(t1) + ub * Mathf.Sin(t1)) * r;
                Quad(a + oa0, b + ob0, b + ob1, a + oa1, (oa0 + oa1 + ob0 + ob1),
                     new Vector2(k / (float)seg, v0), new Vector2(k / (float)seg, v0 + len),
                     new Vector2((k + 1) / (float)seg, v0 + len), new Vector2((k + 1) / (float)seg, v0));
            }
        }

        public void Tube(Vector3 a, Vector3 b, float r, int seg)
        {
            var t = (b - a).normalized;
            var s = Vector3.Cross(Mathf.Abs(t.y) > 0.9f ? Vector3.forward : Vector3.up, t).normalized;
            var u = Vector3.Cross(t, s).normalized;
            TubeSeg(a, b, s, u, s, u, r, seg, 0f, Vector3.Distance(a, b));
        }

        public void Box(Vector3 c, Vector3 x, Vector3 y, Vector3 z, Vector3 half)
        {
            x = x.normalized * half.x; y = y.normalized * half.y; z = z.normalized * half.z;
            Vector3[] n = { x, -x, y, -y, z, -z };
            foreach (var f in n)
            {
                Vector3 p, q;
                if (f == x || f == -x) { p = y; q = z; } else if (f == y || f == -y) { p = x; q = z; } else { p = x; q = y; }
                var o = c + f;
                Quad(o - p - q, o + p - q, o + p + q, o - p + q, f, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
        }

        public Mesh Mesh(string name) => Finish(name, V, U, T);
    }

    // ================================================================= capture (render + motion)
    /// <summary>Frames: overhaul/nb8b_pier_A/B.png (B = 3 s later), overhaul/nb8b_gondola_A.png, and a
    /// numeric motion check of the wheel cabins, the coaster train and the gondola cabins.
    /// run_steps.ps1 "NagisaBayEnvironment.Nb8bCapture|copilot_nb8b_cap.log|1"</summary>
    public static void Nb8bCapture()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != "Assets/Scenes/SakuraPass.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        Directory.CreateDirectory(Path.Combine(NagisaBayDiagnostics.OutDir, "overhaul"));
        GameObject Find(string n)
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == n) return t.gameObject;
            return null;
        }
        var pier = Find("NB8B Shiokaze Pier");
        var gond = Find("NB8B Palm Ridge Gondola");
        var stage = pier != null ? pier.transform.parent : null;
        if (pier == null || gond == null) { Debug.LogError("[nagisa-nb8b] capture: attractions not staged"); return; }

        var pt = pier.transform;
        var eyeP = pt.TransformPoint(-95f, 14f, 150f);
        var tgtP = pt.TransformPoint(-2f, 16f, 252f);
        var lo = gond.transform.Find("NB8B Valley Station").position;
        var hi = gond.transform.Find("NB8B Summit Station").position;
        var dir = (hi - lo); dir.y = 0f; dir.Normalize();
        var side = Vector3.Cross(Vector3.up, dir);
        var eyeG = lo - dir * 70f + side * 55f + Vector3.up * 30f;
        var tgtG = Vector3.Lerp(lo, hi, 0.22f) + Vector3.up * 20f;

        var wheelCab = Find("NB8B Wheel Pin 00").transform.GetChild(0);
        var car0 = Find("NB8B Coaster Car 0").transform;
        var gcab = Find("NB8B Gondola Car 00").transform;
        var p0 = new[] { wheelCab.position, car0.position, gcab.position };
        var r0 = wheelCab.rotation;

        NagisaBayDiagnostics.Shot(eyeP, tgtP, 50f, "overhaul/nb8b_pier_A.png");
        NagisaBayDiagnostics.Shot(eyeG, tgtG, 55f, "overhaul/nb8b_gondola_A.png");

        var root = stage;   // NB Attractions
        AmbientCull.TargetPositionOverride = () => eyeP;   // cull distance lifted below (capture only, scene not saved)
        try
        {
            var rotors = root.GetComponentsInChildren<AmbientRotor>(true);
            var movers = root.GetComponentsInChildren<AmbientPathMover>(true);
            var sways = root.GetComponentsInChildren<AmbientSway>(true);
            var ups = root.GetComponentsInChildren<NagisaAttractionUpright>(true);
            foreach (var r in rotors) r.cullDistance = 1e6f;
            foreach (var m in movers) m.cullDistance = 1e6f;
            foreach (var s in sways) s.cullDistance = 1e6f;
            // warm-up: movers start from rest (accel ramp), so measure cruise motion only
            for (int k = 0; k < 20; k++)
            {
                foreach (var m in movers) { m.ForceCullCheck(m.transform.position); m.Step(0.1f); }
                foreach (var u in ups) u.Sync();
            }
            p0[1] = car0.position; p0[2] = gcab.position;
            for (int k = 0; k < 30; k++)
            {
                foreach (var r in rotors) { r.ForceCullCheck(r.transform.position); r.Step(0.1f); }
                foreach (var m in movers) { m.ForceCullCheck(m.transform.position); m.Step(0.1f); }
                foreach (var s in sways) { s.ForceCullCheck(s.transform.position); s.Step(0.1f); }
                foreach (var u in ups) u.Sync();
            }
            NagisaBayDiagnostics.Shot(eyeP, tgtP, 50f, "overhaul/nb8b_pier_B.png");
            float dw = Vector3.Distance(p0[0], wheelCab.position), dc = Vector3.Distance(p0[1], car0.position), dg = Vector3.Distance(p0[2], gcab.position);
            float tilt = Vector3.Angle(wheelCab.up, Vector3.up);
            Debug.Log($"[nagisa-nb8b] motion over 3 s: wheel cabin {dw:0.00} m (tilt from vertical {tilt:0.0} deg), coaster car {dc:0.0} m, gondola cabin {dg:0.0} m; " +
                      $"{rotors.Length} rotors, {movers.Length} movers, {sways.Length} sways, {ups.Length} upright drivers");
            bool pass = dw > 1f && dc > 5f && dg > 5f && tilt < 6f;
            Debug.Log(pass ? "[nagisa-nb8b] MOTION PASS" : "[nagisa-nb8b] MOTION FAIL");
        }
        finally { AmbientCull.TargetPositionOverride = null; }
    }
}
