using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renderless verification for the shared riding line, the swerve-to-pass rule, and the Shiosai
/// greeting face card.
///
/// WHY RENDERLESS. The project is currently on a render pipeline these regions' custom shaders
/// do not compile under, so every capture comes back magenta. A magenta frame can still prove
/// GEOMETRY (where things are) but it cannot prove appearance - and most of this work is
/// placement and state, not appearance. So the parts that can be proven by arithmetic are proven
/// by arithmetic, and the parts that genuinely need eyes are listed as render-gated rather than
/// quietly claimed.
///
/// The centrepiece is <see cref="SimulateMeeting"/> / <see cref="SimulateOvertake"/>: rather than
/// assert that the avoidance CODE was called, they integrate the actual rule forward in time and
/// measure the closest the two riders ever got. That is the only form of this test that could
/// have caught the sign error it is really guarding against - an avoidance that steers riders
/// INTO each other reports just as much "activity" as one that steers them apart.
/// </summary>
public static class TrafficSelfTest
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>Clearance two riders must keep, metres. Two chibi riders are ~0.5 m wide.</summary>
    private const float RequiredClearanceM = 0.75f;

    private static int _pass, _fail;

    private static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Debug.Log($"[traffic-test] PASS  {what}"); }
        else { _fail++; Debug.LogError($"[traffic-test] FAIL  {what}"); }
    }

    [MenuItem("MapleRide/Tests/Traffic - Shared Line, Swerve and Face Cards", priority = 90)]
    public static void Run()
    {
        _pass = _fail = 0;
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        RuleTests();
        MeetingTests();
        SharedLineTests();
        ShiosaiFaceCardTests();
        SakuraRosterTests();

        Debug.Log($"[traffic-test] ===== {_pass} passed, {_fail} failed =====");
        if (_fail > 0) Debug.LogError("[traffic-test] TRAFFIC SELF-TEST FAILED");
    }

    // ---------------------------------------------------------------- the rule in isolation

    private static void RuleTests()
    {
        // Head-on, 20 m apart, both on the line -> step to my own LEFT (negative).
        float meet = TrafficLine.DesiredShift(20f, 0f, true, 7f, 7f, false);
        Check(meet < -0.1f, $"head-on rider steps to its own left (shift {meet:F2} m)");

        // Same direction, someone slower 8 m in front -> step to my own RIGHT (positive).
        float pass = TrafficLine.DesiredShift(8f, 0f, false, 8f, 5f, false);
        Check(pass > 0.1f, $"overtaking rider steps to its own right (shift {pass:F2} m)");

        // Same direction, someone FASTER 8 m in front -> no shift; you cannot pass them.
        float noPass = TrafficLine.DesiredShift(8f, 0f, false, 5f, 8f, false);
        Check(Mathf.Approximately(noPass, 0f), "no step-out when the rider ahead is faster");

        // Being overtaken: a faster rider BEHIND me. I hold my line - the overtaker moves.
        float held = TrafficLine.DesiredShift(-8f, 0f, false, 5f, 8f, false);
        Check(Mathf.Approximately(held, 0f), "rider being passed holds the line (etiquette)");

        // An oncoming rider already behind me is gone; no phantom swerve at 15 m/s of closure.
        float gone = TrafficLine.DesiredShift(-6f, 0f, true, 7f, 7f, false);
        Check(Mathf.Approximately(gone, 0f), "no swerve for an oncoming rider already passed");

        // Somebody far off to the side is not a conflict at all.
        float wide = TrafficLine.DesiredShift(12f, 3.0f, true, 7f, 7f, false);
        Check(Mathf.Approximately(wide, 0f), "a rider 3 m to the side triggers no swerve");

        // Hysteresis: once out, the step-out is HELD past the point of contact rather than
        // dropped the instant the front wheels cross. Without this a pass ends in a twitch.
        float holdOut = TrafficLine.DesiredShift(-3f, 0f, false, 8f, 5f, true);
        Check(holdOut > 0.1f, $"step-out is held until properly clear (shift {holdOut:F2} m)");
    }

    // ---------------------------------------------------------------- integrated encounters

    /// <summary>
    /// Runs two riders at each other on the shared line and returns the closest they ever came,
    /// laterally, while they were within a bike length of each other longitudinally.
    ///
    /// Both riders run the rule, which is the important half: in a meeting BOTH step left, so
    /// the clearance is the sum of two shifts and neither has to move very far.
    /// </summary>
    private static float SimulateMeeting(float paceA, float paceB, out float shiftA, out float shiftB)
    {
        const float dt = 1f / 60f;
        float a = 0f, b = 120f;          // arc positions; A rides +, B rides -
        float latA = 0f, latB = 0f;      // metres, each on its OWN right
        bool outA = false, outB = false;
        float worst = float.MaxValue, peakA = 0f, peakB = 0f;

        for (int step = 0; step < 60 * 30; step++)
        {
            float along = b - a;                       // +ve: B is in front of A
            // In B's own frame everything flips sign: B is travelling the other way.
            float wantA = TrafficLine.DesiredShift(along, -(latB + latA), true, paceA, paceB, outA);
            float wantB = TrafficLine.DesiredShift(along, -(latA + latB), true, paceB, paceA, outB);
            outA = Mathf.Abs(wantA) > 0.01f;
            outB = Mathf.Abs(wantB) > 0.01f;
            latA = TrafficLine.Approach(latA, wantA, dt);
            latB = TrafficLine.Approach(latB, wantB, dt);
            peakA = Mathf.Max(peakA, Mathf.Abs(latA));
            peakB = Mathf.Max(peakB, Mathf.Abs(latB));

            a += paceA * dt; b -= paceB * dt;

            // Both step to their OWN left, and their own lefts point opposite ways in the world,
            // so the world-space gap between them is |latA| + |latB|.
            if (Mathf.Abs(b - a) < 2.0f)
                worst = Mathf.Min(worst, Mathf.Abs(latA) + Mathf.Abs(latB));
        }
        // Report the PEAK step-out, not the final one. Reporting the final value alongside a
        // healthy clearance reads as a contradiction ("cleared 1.30 m, stepped 0.00 m") when it
        // is actually the happy ending: by the time the loop stops, both riders have tucked back
        // onto the shared line.
        shiftA = peakA; shiftB = peakB;
        return worst == float.MaxValue ? -1f : worst;
    }

    /// <summary>Faster rider catches a slower one from behind; returns the closest lateral gap.</summary>
    private static float SimulateOvertake(float fast, float slow, out float maxShift)
    {
        const float dt = 1f / 60f;
        float a = 0f, b = 40f;           // A (fast) behind B (slow), both riding +
        float latA = 0f;
        bool outA = false;
        float worst = float.MaxValue;
        maxShift = 0f;

        for (int step = 0; step < 60 * 60; step++)
        {
            float along = b - a;
            float wantA = TrafficLine.DesiredShift(along, -latA, false, fast, slow, outA);
            outA = Mathf.Abs(wantA) > 0.01f;
            latA = TrafficLine.Approach(latA, wantA, dt);
            maxShift = Mathf.Max(maxShift, Mathf.Abs(latA));

            a += fast * dt; b += slow * dt;

            // B holds the line, so the gap is just how far A has moved across.
            if (Mathf.Abs(b - a) < 2.0f) worst = Mathf.Min(worst, Mathf.Abs(latA));
        }
        return worst == float.MaxValue ? -1f : worst;
    }

    private static void MeetingTests()
    {
        WriteTraces();

        float meetGap = SimulateMeeting(7.2f, 6.8f, out float sa, out float sb);
        Check(meetGap > RequiredClearanceM,
              $"head-on meeting clears {meetGap:F2} m (need > {RequiredClearanceM:F2}) " +
              $"[peak step-out A {sa:F2} m, B {sb:F2} m; both back on the line at the end]");

        float passGap = SimulateOvertake(8.4f, 5.2f, out float ms);
        Check(passGap > RequiredClearanceM,
              $"overtake clears {passGap:F2} m (need > {RequiredClearanceM:F2}) " +
              $"[overtaker stepped out {ms:F2} m]");

        // A pass must actually END - a rider that never tucks back in has not passed anybody,
        // it has changed lanes. Run well past the encounter and check it came home.
        const float dt = 1f / 60f;
        float lat = 0f; bool o = false;
        for (int i = 0; i < 60 * 40; i++)
        {
            float along = -60f;   // long since past
            float w = TrafficLine.DesiredShift(along, -lat, false, 8.4f, 5.2f, o);
            o = Mathf.Abs(w) > 0.01f;
            lat = TrafficLine.Approach(lat, w, dt);
        }
        Check(Mathf.Abs(lat) < 0.02f, $"rider returns to the shared line after a pass ({lat:F3} m)");
    }

    // ---------------------------------------------------------------- one line, not two

    /// <summary>
    /// Dumps the two encounters, and the staged roster's lateral offsets, as CSV.
    ///
    /// This is the "positional capture" for this work. A normal render cannot serve as one right
    /// now - the project is on a pipeline these shaders do not compile under, so an overhead shot
    /// of the riders would be magenta riders on a magenta road, and any lane structure in it
    /// would be invisible. The numbers are the same numbers the game uses, produced by the same
    /// C# rule, so plotting them is evidence about the shipped behaviour rather than about a
    /// reimplementation of it.
    /// </summary>
    private static void WriteTraces()
    {
        const float dt = 1f / 60f;
        var dir = "C:/Temp/mapleride_traffic";
        System.IO.Directory.CreateDirectory(dir);

        // --- head-on meeting -----------------------------------------------------------
        {
            var sb = new System.Text.StringBuilder("t,gap_along,latA,latB,world_gap\n");
            float a = 0f, b = 120f, latA = 0f, latB = 0f;
            bool oA = false, oB = false;
            for (int i = 0; i < 60 * 14; i++)
            {
                float along = b - a;
                float wA = TrafficLine.DesiredShift(along, -(latB + latA), true, 7.2f, 6.8f, oA);
                float wB = TrafficLine.DesiredShift(along, -(latA + latB), true, 6.8f, 7.2f, oB);
                oA = Mathf.Abs(wA) > 0.01f; oB = Mathf.Abs(wB) > 0.01f;
                latA = TrafficLine.Approach(latA, wA, dt);
                latB = TrafficLine.Approach(latB, wB, dt);
                sb.AppendLine($"{i * dt:F3},{along:F2},{latA:F4},{latB:F4}," +
                              $"{Mathf.Abs(latA) + Mathf.Abs(latB):F4}");
                a += 7.2f * dt; b -= 6.8f * dt;
            }
            System.IO.File.WriteAllText($"{dir}/meeting.csv", sb.ToString());
        }

        // --- overtake ------------------------------------------------------------------
        {
            var sb = new System.Text.StringBuilder("t,gap_along,latA,world_gap\n");
            float a = 0f, b = 40f, latA = 0f;
            bool oA = false;
            for (int i = 0; i < 60 * 30; i++)
            {
                float along = b - a;
                float wA = TrafficLine.DesiredShift(along, -latA, false, 8.4f, 5.2f, oA);
                oA = Mathf.Abs(wA) > 0.01f;
                latA = TrafficLine.Approach(latA, wA, dt);
                sb.AppendLine($"{i * dt:F3},{along:F2},{latA:F4},{Mathf.Abs(latA):F4}");
                a += 8.4f * dt; b += 5.2f * dt;
            }
            System.IO.File.WriteAllText($"{dir}/overtake.csv", sb.ToString());
        }

        // --- the staged roster's actual lateral offsets ---------------------------------
        {
            var sb = new System.Text.StringBuilder("name,segment,reverse,road_side_offset_m,speed_mps\n");
            foreach (var r in Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None))
            {
                if (r.segmentId != "pass") continue;
                float roadSide = (r.reverse ? -1f : 1f) * r.laneOffset;
                sb.AppendLine($"{r.name},{r.segmentId},{r.reverse},{roadSide:F4},{r.speed:F3}");
            }
            System.IO.File.WriteAllText($"{dir}/sakura_lanes.csv", sb.ToString());
        }

        Debug.Log($"[traffic-test] traces written to {dir}");
    }

    private static void SharedLineTests()
    {
        var riders = Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                          FindObjectsSortMode.None)
                           .Where(r => r.segmentId == "pass").ToArray();
        if (riders.Length == 0) { Check(false, "Sakura roster riders found in the scene"); return; }

        // NPCCyclist bakes the lane as side * (dir * laneOffset), so the ROAD-side offset a
        // rider actually sits on is dir * laneOffset. Two lanes show up here as two clusters of
        // opposite sign; one shared line shows up as one tight cluster.
        var roadSide = riders.Select(r => (r.reverse ? -1f : 1f) * r.laneOffset).ToArray();
        float lo = roadSide.Min(), hi = roadSide.Max();
        float spread = hi - lo;

        Check(spread <= TrafficLine.LineJitterM * 2f + 0.01f,
              $"all {riders.Length} Sakura riders share ONE line " +
              $"(road-side offsets {lo:F2} .. {hi:F2} m, spread {spread:F2} m)");

        Check(roadSide.All(v => Mathf.Sign(v) == Mathf.Sign(TrafficLine.SharedLineM)),
              "no rider is left on the far side of the road (no second lane)");

        bool anyOncoming = riders.Any(r => r.reverse);
        bool anySameWay = riders.Any(r => !r.reverse);
        Check(anyOncoming && anySameWay,
              $"Sakura has BOTH oncoming and same-direction traffic " +
              $"({riders.Count(r => r.reverse)} oncoming, {riders.Count(r => !r.reverse)} same-way)");

        // The player must be on that same line, or "shared" means nothing.
        var follower = Object.FindFirstObjectByType<RouteFollower>();
        if (follower != null)
        {
            Check(Mathf.Abs(follower.laneOffset - TrafficLine.SharedLineM) < 0.01f,
                  $"player rides the shared line too (RouteFollower.laneOffset {follower.laneOffset:F2})");
            Check(follower.laneChangeEnabled,
                  "player's lateral steer is enabled, so they can pass same-direction riders");
        }
        else Check(false, "player RouteFollower present");
    }

    // ---------------------------------------------------------------- Shiosai face cards

    private static void ShiosaiFaceCardTests()
    {
        var env = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                                                      FindObjectsSortMode.None)
                        .FirstOrDefault(t => t.name == "Shiosai Traffic");
        if (env == null) { Check(false, "Shiosai traffic root present in the scene"); return; }

        var greets = env.GetComponentsInChildren<NpcGreeting>(true);
        Check(greets.Length > 0, $"Shiosai riders carry NpcGreeting ({greets.Length} found)");

        int noCard = greets.Count(g => !g.useFaceCard);
        Check(noCard == 0, $"every Shiosai rider has useFaceCard set ({noCard} without)");

        int noName = greets.Count(g => string.IsNullOrEmpty(g.riderName));
        Check(noName == 0, $"every Shiosai rider has a riderName for the card ({noName} without)");

        int noLines = greets.Count(g => g.ownPhrases == null || g.ownPhrases.Length == 0);
        Check(noLines == 0, $"every Shiosai rider has coastal ownPhrases ({noLines} without)");

        // Every coastal livery must ship a real portrait. A silhouette fallback keeps the card
        // alive for development, but is a defect for the route's authored cyclist encounters.
        var named = greets.Select(g => g.riderName).Distinct().OrderBy(n => n).ToArray();
        var have = named.Where(n => AssetDatabase.LoadAssetAtPath<Texture2D>(
                       $"Assets/Resources/{NpcGreeting.PortraitResourceDir}{n}.png") != null).ToArray();
        Debug.Log($"[traffic-test] coastal liveries {named.Length}: " +
                  $"{have.Length} with a baked portrait ({string.Join(", ", have)}), " +
                  $"{named.Length - have.Length} on the silhouette fallback " +
                  $"({string.Join(", ", named.Except(have))}).");
        Check(have.Length == named.Length,
              $"every coastal livery has a baked portrait ({named.Length - have.Length} missing)");

        // The card host is created on demand, so it cannot be "missing" - but prove it, because
        // a null here would mean no card ever appears no matter how well the riders are wired.
        Check(NpcGreetingCard.Instance != null,
              "NpcGreetingCard singleton resolves in the coast context");
    }

    // ---------------------------------------------------------------- other rosters unharmed

    private static void SakuraRosterTests()
    {
        // The other four regions wired their face cards during their own builds. A regression
        // here would be silent - the card simply stops appearing - so it is worth one sweep.
        foreach (var seg in new[] { "pass", "maplecity", "azora", "taka" })
        {
            var rs = Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                          FindObjectsSortMode.None)
                           .Where(r => r.segmentId == seg).ToArray();
            if (rs.Length == 0) continue;
            var gs = rs.Select(r => r.GetComponent<NpcGreeting>()).Where(g => g != null).ToArray();
            int bad = gs.Count(g => !g.useFaceCard || string.IsNullOrEmpty(g.riderName));
            Check(bad == 0, $"'{seg}' roster face cards intact ({gs.Length} riders, {bad} unwired)");
        }
    }
}
