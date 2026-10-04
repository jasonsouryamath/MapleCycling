using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The roster's equivalent of <see cref="CoralNpcSelfTest"/>: drives every staged rider's real
/// <see cref="NpcGreeting"/> and checks the behaviour the player will meet on the road.
///
/// The point of interest here is <c>ownPhrases</c>. The shared 100-line pool is a STATIC field,
/// so before that override existed all twelve riders would have drawn from one bag and taken
/// turns finishing each other's sentences. Each rider must speak only their own four lines, no
/// two riders may share a line, and a rider must not repeat a line before exhausting their own
/// set. Coral is in this list because she is now staged by the roster like everyone else; she
/// used to have no ownPhrases at all and fell back to the shared pool.
///
/// Edit mode, not Play mode - entering Play from batchmode triggers a domain reload that
/// destroys the test's state. NpcGreeting exposes an injectable clock for exactly this reason;
/// Time.time does not advance in batch mode.
/// </summary>
public static class SakuraNpcRosterSelfTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    // The twelve originals, then the N1 moving/stopped layer (SakuraNpcRoster.MovingCyclistNames).
    static readonly string[] Names = new[]
    { "Aoi", "Coral", "Haruka", "Shiori", "Mika", "Nao", "Ren", "Daichi", "Sora", "Emi", "Yuki",
      "Takumi" }.Concat(SakuraNpcRoster.MovingCyclistNames).ToArray();

    static readonly List<string> Failures = new List<string>();
    static float clock;

    [MenuItem("MapleRide/NPCs/Run Roster Greeting Self Test")]
    public static void Run()
    {
        Failures.Clear();
        clock = 100f;
        NpcGreeting.Clock = () => clock;
        try
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var player = FindAnyPlayer();
            if (player == null) { Fail("no player transform in the scene for the riders to greet."); Report(); return; }
            Vector3 originalPlayerPos = player.position;

            var everyLine = new Dictionary<string, string>();   // line -> owner, to catch overlap

            foreach (var name in Names)
            {
                var npc = GameObject.Find("Sakura NPC " + name);
                if (npc == null) { Fail($"{name} is not in the scene."); continue; }

                var g = npc.GetComponentInChildren<NpcGreeting>();
                if (g == null) { Fail($"{name} has no NpcGreeting."); continue; }

                CheckOwnPhrases(name, g, everyLine);
                CheckGreetingCycle(name, g, player);
                CheckRouteIsLive(name, npc);
            }

            player.position = originalPlayerPos;
            Report();
        }
        finally
        {
            NpcGreeting.Clock = null;
            // Inspect-only: leaving the scene dirty makes Unity write a crash-recovery backup
            // that the next interactive session restores over the top of the real scene.
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    /// <summary>
    /// The rider's route is SERIALIZED into the scene, so a rebuilt road leaves it describing an
    /// older pass while everything still compiles and every greeting still fires. That drift
    /// shipped: the roster was riding a 400-point route against a 481-point road, up to 236 m
    /// out, which buried riders in the asphalt and stranded others below it - still greeting the
    /// player, because NpcGreeting only tests distance and a view cone.
    ///
    /// This is the guard that turns that silent drift into a loud failure.
    /// </summary>
    static void CheckRouteIsLive(string name, GameObject npc)
    {
        var cyc = npc.GetComponent<NPCCyclist>();
        if (cyc == null) { Fail($"{name} has no NPCCyclist."); return; }

        var baked = cyc.route;
        if (baked == null || baked.Length < 2) { Fail($"{name} has no baked route."); return; }

        var graph = RouteGraph.Load();
        var seg = graph != null ? graph.Segment(cyc.segmentId) : null;
        if (seg == null) { Fail($"{name}: no '{cyc.segmentId}' segment in the route graph."); return; }

        // Rebuild into a throwaway rider so the scene copy is not touched by a read-only test.
        var probeGo = new GameObject("routeProbe");
        try
        {
            var probe = probeGo.AddComponent<NPCCyclist>();
            probe.segmentId = cyc.segmentId;
            probe.reverse = cyc.reverse;
            probe.laneOffset = cyc.laneOffset;
            probe.groundOffset = cyc.groundOffset;
            if (!probe.RebuildFromGraph()) { Fail($"{name}: could not rebuild route from the graph."); return; }

            if (probe.route.Length != baked.Length)
            {
                Fail($"{name}: baked route has {baked.Length} points, the live road has " +
                     $"{probe.route.Length}. Re-run SakuraNpcRoster.AddAllToScene.");
                return;
            }

            float worst = 0f;
            for (int i = 0; i < baked.Length; i++)
                worst = Mathf.Max(worst, Vector3.Distance(baked[i], probe.route[i]));
            if (worst > 0.25f)
                Fail($"{name}: baked route has drifted {worst:F2} m from the live road. " +
                     "Re-run SakuraNpcRoster.AddAllToScene.");

            if (!cyc.routeIncludesOffsets)
                Fail($"{name}: route does not carry the banked lane/ground offsets - the rider " +
                     "will be placed with a horizontal side vector and sink on the superelevated bends.");
        }
        finally { Object.DestroyImmediate(probeGo); }
    }

    static void CheckOwnPhrases(string name, NpcGreeting g, Dictionary<string, string> everyLine)
    {        var own = g.ownPhrases;
        if (own == null || own.Length == 0) { Fail($"{name} has no ownPhrases - would share the global pool."); return; }
        if (own.Distinct().Count() != own.Length) Fail($"{name} has duplicate lines.");
        if (own.Any(string.IsNullOrWhiteSpace)) Fail($"{name} has a blank line.");

        foreach (var line in own)
        {
            if (everyLine.TryGetValue(line, out var owner))
                Fail($"{name} shares the line \"{line}\" with {owner}.");
            else everyLine[line] = name;
        }

        // Draw two full passes: each must return every one of this rider's lines exactly once,
        // and nothing from the shared static pool.
        var next = typeof(NpcGreeting).GetMethod("NextPhrase", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int pass = 0; pass < 2; pass++)
        {
            var drawn = new List<string>();
            for (int i = 0; i < own.Length; i++) drawn.Add((string)next.Invoke(g, null));
            if (drawn.Distinct().Count() != own.Length)
                Fail($"{name} repeated a line before exhausting their own {own.Length}.");
            foreach (var d in drawn)
                if (!own.Contains(d)) Fail($"{name} spoke \"{d}\", which is not one of their lines.");
        }
        Log($"{name}: {own.Length} own lines, two clean draw passes.");
    }

    static void CheckGreetingCycle(string name, NpcGreeting g, Transform player)
    {
        float visible = g.visibleSeconds, rearm = g.rearmSeconds;
        g.visibleSeconds = 1.0f;
        g.rearmSeconds = 0.5f;
        try
        {
            Invoke(g, "Awake");

            Place(player, g, 500f);
            Invoke(g, "Update");

            // Behind: oncoming riders must not greet someone they have already passed.
            Place(player, g, -4f);
            Invoke(g, "Update");
            if (CanSee(g, player)) Fail($"{name} sees a player standing behind them.");

            // Beyond triggerDistance.
            Place(player, g, g.triggerDistance + 5f);
            Invoke(g, "Update");
            if (CanSee(g, player)) Fail($"{name} sees a player beyond triggerDistance.");

            // In view: must speak.
            Place(player, g, 6f);
            Invoke(g, "Update");
            string first = g.phrase;
            if (!CanSee(g, player)) Fail($"{name} cannot see a player 6 m directly in front.");
            if (string.IsNullOrEmpty(first)) Fail($"{name} saw the player but said nothing.");
            if (!string.IsNullOrEmpty(first) && g.ownPhrases != null && !g.ownPhrases.Contains(first))
                Fail($"{name} greeted with \"{first}\", which is not one of their lines.");

            // Bubble expires.
            clock += 1.4f;
            Invoke(g, "Update");

            // Second meeting: fresh line.
            Place(player, g, -4f);
            Invoke(g, "Update");
            clock += 0.6f;
            Place(player, g, 6f);
            Invoke(g, "Update");
            string second = g.phrase;
            if (second == first) Fail($"{name} repeated the same line on consecutive greetings.");

            Log($"{name}: greeted \"{first}\" then \"{second}\".");
        }
        finally
        {
            Invoke(g, "OnDisable");
            g.visibleSeconds = visible;
            g.rearmSeconds = rearm;
        }
    }

    static Transform FindAnyPlayer()
    {
        var npc = GameObject.Find("Sakura NPC Aoi");
        if (npc == null) return null;
        var g = npc.GetComponentInChildren<NpcGreeting>();
        if (g == null) return null;
        var m = typeof(NpcGreeting).GetMethod("FindPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        return (Transform)m.Invoke(g, null);
    }

    static void Place(Transform player, NpcGreeting g, float forwardMetres)
    {
        var t = g.transform;
        var flat = t.forward; flat.y = 0f; flat.Normalize();
        player.position = t.position + flat * forwardMetres;
    }

    static bool CanSee(NpcGreeting g, Transform player) =>
        (bool)typeof(NpcGreeting).GetMethod("CanSee", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(g, new object[] { player });

    static void Invoke(NpcGreeting g, string method) =>
        typeof(NpcGreeting).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(g, null);

    static void Log(string m) => Debug.Log($"[roster-test] {m}");
    static void Fail(string m) { Failures.Add(m); Debug.LogError($"[roster-test] FAIL {m}"); }

    static void Report()
    {
        if (Failures.Count == 0) { Debug.Log("[roster-test] RESULT: all checks passed."); return; }
        Debug.Log($"[roster-test] RESULT: {Failures.Count} failure(s).");
        foreach (var f in Failures) Debug.Log($"[roster-test]   - {f}");
    }
}
