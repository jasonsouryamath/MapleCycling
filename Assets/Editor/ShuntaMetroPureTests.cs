using UnityEditor;
using UnityEngine;

/// <summary>
/// Pure-logic checks for Shunta Metro: course JSON schema invariants, the elevation interpolation and
/// zone lookup boundaries (synthetic data), plus ShuntaZoneModifiers continuity. No scene, no route build.
/// Run: menu MapleRide/Shunta Metro/Pure Tests, or run_steps "ShuntaMetroPureTests.Run|claude_shunta_pure.log|1".
/// </summary>
public static class ShuntaMetroPureTests
{
    static int _fails;

    static bool Check(bool ok, string what)
    {
        Debug.Log($"[shunta-pure] {(ok ? "PASS" : "FAIL")} {what}");
        if (!ok) _fails++;
        return ok;
    }

    static bool Near(float a, float b, float eps = 1e-3f) => Mathf.Abs(a - b) <= eps;

    [MenuItem("MapleRide/Shunta Metro/Pure Tests")]
    public static void Run()
    {
        _fails = 0;
        Schema();
        Elevation();
        ZoneLookup();
        Modifiers();
        Debug.Log(_fails == 0 ? "[shunta-pure] ALL PASS" : $"[shunta-pure] {_fails} FAILED");
        if (Application.isBatchMode) EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- JSON schema invariants

    static void Schema()
    {
        var c = ShuntaCourseData.Load();
        if (!Check(c != null, "course json loads")) return;

        Check(c.zones != null && c.zones.Length == 12, $"12 zones ({(c.zones == null ? -1 : c.zones.Length)})");
        if (c.zones == null || c.zones.Length == 0) return;

        Check(Near(c.zones[0].startKm, 0f), "first zone starts at 0 km");
        Check(Near(c.zones[c.zones.Length - 1].endKm, c.distanceKm, 0.02f), $"last zone ends at distanceKm {c.distanceKm}");
        for (int i = 0; i < c.zones.Length; i++)
        {
            var z = c.zones[i];
            Check(z.index == i + 1, $"zone[{i}] index == {i + 1} (got {z.index})");
            Check(z.endKm > z.startKm, $"zone {z.index} has positive length");
            Check(ColorUtility.TryParseHtmlString(z.color, out _), $"zone {z.index} colour parses ({z.color})");
            Check(z.grip > 0f && z.grip <= 1f, $"zone {z.index} grip in (0,1] ({z.grip})");
            Check(z.draftMultiplier > 0f, $"zone {z.index} draftMultiplier > 0 ({z.draftMultiplier})");
            if (i > 0) Check(Near(z.startKm, c.zones[i - 1].endKm), $"zone {z.index} contiguous with zone {z.index - 1}");
        }

        var e = c.elevation;
        Check(e != null && e.Length >= 2, $"elevation has keypoints ({(e == null ? -1 : e.Length)})");
        if (e != null && e.Length >= 2)
        {
            bool mono = true;
            for (int i = 1; i < e.Length; i++)
                if (!(e[i].km > e[i - 1].km)) { mono = false; Debug.Log($"[shunta-pure] non-monotone km at elevation[{i}]"); }
            Check(mono, "elevation km strictly increasing");
            Check(e[e.Length - 1].km >= c.distanceKm - 0.02f, $"elevation covers distanceKm (last km {e[e.Length - 1].km})");
        }
        Check(Mathf.Abs(c.ComputeGain() - c.targetGainM) <= 3f, $"gain {c.ComputeGain():F1} m within 3 m of target {c.targetGainM}");

        Check(c.waypoints != null && c.waypoints.Length >= 2, $"waypoints >= 2 ({(c.waypoints == null ? -1 : c.waypoints.Length)})");
        if (c.waypoints != null)
        {
            bool inRange = true, nonDecreasing = true;
            for (int i = 0; i < c.waypoints.Length; i++)
            {
                if (c.waypoints[i].zone < 1 || c.waypoints[i].zone > c.zones.Length) inRange = false;
                if (i > 0 && c.waypoints[i].zone < c.waypoints[i - 1].zone) nonDecreasing = false;
            }
            Check(inRange, "every waypoint zone in 1..zones");
            Check(nonDecreasing, "waypoint zones non-decreasing");
        }

        if (c.climbs != null)
            foreach (var cl in c.climbs)
                Check(cl.endKm > cl.startKm && cl.zone >= 1 && cl.zone <= c.zones.Length, $"climb '{cl.name}' sane");
        if (c.landmarks != null)
            foreach (var l in c.landmarks)
                Check(l.km >= 0f && l.km <= c.distanceKm + 0.02f, $"landmark '{l.name}' km within course");
    }

    // ---------------------------------------------------------------- elevation interpolation (synthetic)

    static ShuntaCourseData Synthetic()
    {
        return new ShuntaCourseData
        {
            distanceKm = 3f,
            elevation = new[]
            {
                new ShuntaElevationKey { km = 0f, m = 10f },
                new ShuntaElevationKey { km = 1f, m = 110f },
                new ShuntaElevationKey { km = 2f, m = 60f },
                new ShuntaElevationKey { km = 3f, m = 90f },
            },
            zones = new[]
            {
                new ShuntaZone { index = 1, id = "a", startKm = 0f, endKm = 1f },
                new ShuntaZone { index = 2, id = "b", startKm = 1f, endKm = 2f },
                new ShuntaZone { index = 3, id = "c", startKm = 2f, endKm = 3f },
            }
        };
    }

    static void Elevation()
    {
        var c = Synthetic();
        Check(Near(c.ElevationAtKm(0f), 10f), "elev at first key");
        Check(Near(c.ElevationAtKm(1f), 110f), "elev at interior key");
        Check(Near(c.ElevationAtKm(3f), 90f), "elev at last key");
        Check(Near(c.ElevationAtKm(0.5f), 81.875f), "PCHIP rising midpoint respects endpoint slope (81.875)");
        Check(Near(c.ElevationAtKm(1.5f), 85f), "midpoint of descent (85)");
        Check(Near(c.ElevationAtKm(-5f), 10f), "clamps below first key");
        Check(Near(c.ElevationAtKm(99f), 90f), "clamps above last key");
        Check(c.ElevationAtKm(0.25f) < c.ElevationAtKm(0.5f) && c.ElevationAtKm(0.5f) < c.ElevationAtKm(0.75f), "monotone within a rising span");
        Check(c.ElevationAtKm(0.1f) > 10f + 100f * 0.1f, "PCHIP endpoint continues the rising slope");
        Check(Near(c.ComputeGain(), 130f), $"gain = 100 + 30 = 130 ({c.ComputeGain():F2})");

        var empty = new ShuntaCourseData();
        Check(Near(empty.ElevationAtKm(1f), 0f), "no keys -> 0 m");
        Check(Near(empty.ComputeGain(), 0f), "no keys -> gain 0");
        Check(Near(empty.MaxGradePercent(), 0f), "no keys -> grade 0");
        var one = new ShuntaCourseData { elevation = new[] { new ShuntaElevationKey { km = 0f, m = 5f } } };
        Check(Near(one.ElevationAtKm(2f), 5f), "single key is constant");

        // One-sided PCHIP endpoint slope is 175 m/km; a 100 m window averages ~17.1%.
        float g = c.MaxGradePercent();
        Check(g > 17f && g < 17.5f, $"max grade of synthetic course ~17.1 % ({g:F2})");
    }

    // ---------------------------------------------------------------- zone lookup boundaries

    static void ZoneLookup()
    {
        var c = Synthetic();
        Check(c.ZoneAtKm(0f).index == 1, "km 0 -> zone 1");
        Check(c.ZoneAtKm(-1f).index == 1, "negative km clamps to zone 1");
        Check(c.ZoneAtKm(0.999f).index == 1, "just below boundary -> zone 1");
        Check(c.ZoneAtKm(1f).index == 1, "exactly on boundary belongs to the earlier zone (km <= endKm)");
        Check(c.ZoneAtKm(1.0001f).index == 2, "just past boundary -> zone 2");
        Check(c.ZoneAtKm(2f).index == 2, "second boundary -> zone 2");
        Check(c.ZoneAtKm(3f).index == 3, "last km -> zone 3");
        Check(c.ZoneAtKm(50f).index == 3, "beyond the end clamps to last zone");
        Check(new ShuntaCourseData().ZoneAtKm(1f) == null, "no zones -> null");

        var real = ShuntaCourseData.Load();
        if (real != null && real.zones.Length == 12)
            foreach (var z in real.zones)
            {
                float mid = (z.startKm + z.endKm) * 0.5f;
                Check(real.ZoneAtKm(mid).index == z.index, $"real zone {z.index} midpoint resolves to itself");
                Check(real.ZoneAtKm(z.endKm).index == z.index, $"real zone {z.index} endKm resolves to itself");
            }
    }

    // ---------------------------------------------------------------- zone modifiers

    static void Modifiers()
    {
        var c = Synthetic();
        c.zones[0].grip = 1f; c.zones[0].draftMultiplier = 1f;
        c.zones[1].grip = 0.8f; c.zones[1].draftMultiplier = 1.35f;
        c.zones[2].grip = 0f; c.zones[2].draftMultiplier = 0f;   // invalid -> treated as 1
        var m = new ShuntaZoneModifiers(c, 300f);
        Check(m.ZoneCount == 3, "modifiers zone count");
        Check(Near(m.Sample(0.5f).gripMultiplier, 1f), "zone 1 interior grip");
        Check(Near(m.Sample(1.5f).gripMultiplier, 0.8f), "zone 2 interior grip");
        Check(Near(m.Sample(1.5f).draftMultiplier, 1.35f), "zone 2 interior draft");
        Check(Near(m.Sample(2.5f).gripMultiplier, 1f) && Near(m.Sample(2.5f).draftMultiplier, 1f), "non-positive grip/draft default to 1");
        Check(Near(m.Sample(1f).gripMultiplier, 0.9f, 0.01f), "boundary value is the midpoint of the blend (0.9)");
        Check(Near(m.Sample(0.8f).gripMultiplier, 1f), "blend ends exactly BlendMetres/2 before the boundary");
        Check(Near(m.Sample(1.2f).gripMultiplier, 0.8f), "blend ends exactly BlendMetres/2 after the boundary");

        float prev = m.Sample(0.5f).gripMultiplier; bool cont = true;
        for (float km = 0.5f; km <= 2.5f; km += 0.001f)
        {
            float g = m.Sample(km).gripMultiplier;
            if (Mathf.Abs(g - prev) > 0.02f) { cont = false; Debug.Log($"[shunta-pure] grip jump at km {km:F3}: {prev:F3} -> {g:F3}"); }
            prev = g;
        }
        Check(cont, "grip is continuous across zone boundaries (<= 0.02 per metre)");
        Check(new ShuntaZoneModifiers(null).Sample(5f).gripMultiplier == 1f, "null course -> neutral");
        Check(Near(ShuntaZoneModifiers.PackFromDraft(1f), 0.5f), "pack tightness neutral at draft 1");
        Check(ShuntaZoneModifiers.PackFromDraft(10f) <= 1f && ShuntaZoneModifiers.PackFromDraft(-10f) >= 0f, "pack tightness clamped to 0..1");
    }
}
