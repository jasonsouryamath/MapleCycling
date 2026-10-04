using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drives <see cref="NpcGreeting"/> for real and checks that the behaviour the player will
/// actually experience holds: Coral greets when the player enters her view cone, smiles for
/// exactly as long as the bubble is up, goes neutral afterwards, does not greet someone
/// standing behind her, and never repeats a phrase within a pass of the pool.
///
/// This runs in edit mode rather than Play mode on purpose. Entering Play mode from batchmode
/// triggers a domain reload that destroys the test's own state, which needs an EditorPrefs
/// resume dance to survive. NpcGreeting instead exposes an injectable clock, so its Update can
/// be ticked against virtual time and observed directly - the same code, deterministically,
/// with far less rig.
/// </summary>
public static class CoralNpcSelfTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    static readonly List<string> Failures = new List<string>();

    /// <summary>Virtual seconds, fed to NpcGreeting in place of Time.time.</summary>
    static float clock;

    [MenuItem("MapleRide/Coral/Run Greeting Self Test")]
    public static void Run()
    {
        Failures.Clear();
        clock = 100f;
        NpcGreeting.Clock = () => clock;
        try
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var npc = GameObject.Find(CoralNpcSetup.NpcName);
            if (npc == null) { Fail("Coral NPC is not in the scene."); Report(); return; }

            var greeting = npc.GetComponentInChildren<NpcGreeting>();
            if (greeting == null) { Fail("Coral has no NpcGreeting component."); Report(); return; }

            TestPhrasePool(greeting);
            TestGreetingCycle(greeting);

            Report();
        }
        finally
        {
            NpcGreeting.Clock = null;
            // This pass only inspects; it must not leave the scene dirty, or Unity writes a
            // crash-recovery backup that the next interactive session restores over the top.
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    // ---------------------------------------------------------------- phrase pool

    static void TestPhrasePool(NpcGreeting greeting)
    {
        var phrases = (string[])Field("Phrases").GetValue(greeting);
        Log($"pool holds {phrases.Length} phrases, {phrases.Distinct().Count()} distinct");

        if (phrases.Length != 100) Fail($"expected 100 phrases, found {phrases.Length}.");
        if (phrases.Distinct().Count() != phrases.Length) Fail("phrase pool contains duplicates.");
        if (phrases.Any(string.IsNullOrWhiteSpace)) Fail("phrase pool contains a blank entry.");

        // Draw two full passes and confirm the shuffled bag really is a bag: each pass must
        // contain every phrase exactly once, which is the property that stops the player
        // hearing the same line twice in a row.
        var next = typeof(NpcGreeting).GetMethod("NextPhrase", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int pass = 0; pass < 2; pass++)
        {
            var drawn = new List<string>();
            for (int i = 0; i < phrases.Length; i++) drawn.Add((string)next.Invoke(greeting, null));
            int distinct = drawn.Distinct().Count();
            Log($"draw pass {pass + 1}: {drawn.Count} draws, {distinct} distinct");
            if (distinct != phrases.Length) Fail($"pass {pass + 1} repeated a phrase before exhausting the pool.");
        }
    }

    // ---------------------------------------------------------------- greeting cycle

    static void TestGreetingCycle(NpcGreeting greeting)
    {
        // Shorten the window so the test does not have to sit through a full 3.5 s bubble.
        float originalVisible = greeting.visibleSeconds;
        float originalRearm = greeting.rearmSeconds;
        greeting.visibleSeconds = 1.0f;
        greeting.rearmSeconds = 0.5f;

        // Drive the real player object rather than a stand-in. An earlier version of this test
        // spawned its own "Kuro on Sakura Pass" decoy, but the scene already contains one, so
        // NpcGreeting.FindPlayer resolved to the genuine rider parked elsewhere on the pass and
        // every greeting assertion failed against a player Coral could not actually see.
        Transform player = FindPlayer(greeting);
        if (player == null)
        {
            Fail("no player transform found in the scene for Coral to greet.");
            greeting.visibleSeconds = originalVisible;
            greeting.rearmSeconds = originalRearm;
            return;
        }
        Log($"driving real player object '{player.name}'");
        Vector3 originalPlayerPos = player.position;

        try
        {
            var smile = FindSmile(greeting);
            if (smile == null) Fail("SmileDecal renderer not found under Coral.");

            Awake(greeting);
            // Park the player far away first so the NPC starts from "has not seen anyone".
            Place(player, greeting, 500f);
            Tick(greeting);
            if (smile != null && smile.enabled) Fail("smile is on before any greeting.");
            Log($"start: smile={(smile != null && smile.enabled)} phrase='{greeting.phrase}'");

            // 1. Player behind her must NOT be greeted, even though they are well in range.
            Place(player, greeting, -4f);
            Tick(greeting);
            bool seenBehind = CanSee(greeting, player);
            Log($"player 4 m BEHIND: canSee={seenBehind}");
            if (seenBehind) Fail("Coral 'sees' a player standing behind her.");
            if (smile != null && smile.enabled) Fail("Coral smiles at a player behind her.");

            // 2. Player out of range in front must not be greeted either.
            Place(player, greeting, greeting.triggerDistance + 5f);
            Tick(greeting);
            bool seenFar = CanSee(greeting, player);
            Log($"player {greeting.triggerDistance + 5f:0.0} m in front (out of range): canSee={seenFar}");
            if (seenFar) Fail("Coral sees a player beyond triggerDistance.");

            // 3. Player rides into view: she should greet and smile in the same frame.
            Place(player, greeting, 6f);
            Tick(greeting);
            bool seenFront = CanSee(greeting, player);
            string spoken = greeting.phrase;
            bool smiling = smile != null && smile.enabled;
            Log($"player 6 m IN FRONT: canSee={seenFront} phrase='{spoken}' smile={smiling}");
            if (!seenFront) Fail("Coral does not see a player 6 m directly in front of her.");
            if (string.IsNullOrEmpty(spoken)) Fail("Coral saw the player but said nothing.");
            if (!smiling) Fail("Coral greeted the player but did not smile.");

            // 4. She must keep smiling for the whole bubble, then drop back to neutral.
            Advance(0.4f); Tick(greeting);
            bool midSmile = smile != null && smile.enabled;
            Log($"mid-bubble (~0.4 s in): smile={midSmile}");
            if (!midSmile) Fail("smile switched off while the bubble was still up.");

            Advance(0.9f); Tick(greeting);
            bool lateSmile = smile != null && smile.enabled;
            Log($"after bubble expired (~1.3 s in): smile={lateSmile}");
            if (lateSmile) Fail("Coral kept smiling after the greeting ended.");

            // 5. Player leaves and returns: she should greet again with a fresh line.
            Place(player, greeting, -4f);
            Tick(greeting);
            Advance(0.6f);
            Place(player, greeting, 6f);
            Tick(greeting);
            string second = greeting.phrase;
            bool smileAgain = smile != null && smile.enabled;
            Log($"second meeting: phrase='{second}' smile={smileAgain}");
            if (!smileAgain) Fail("Coral did not smile on the second meeting.");
            if (second == spoken) Fail("Coral repeated the same phrase on consecutive greetings.");

            // 6. Disabling her must not leave a frozen grin on her face.
            Disable(greeting);
            bool smileAfterDisable = smile != null && smile.enabled;
            Log($"after OnDisable: smile={smileAfterDisable}");
            if (smileAfterDisable) Fail("smile stayed on after the component was disabled.");
        }
        finally
        {
            player.position = originalPlayerPos;
            greeting.visibleSeconds = originalVisible;
            greeting.rearmSeconds = originalRearm;
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Puts the player <paramref name="forwardMetres"/> along Coral's forward axis
    /// (negative puts them behind her), at her own height.</summary>
    static void Place(Transform player, NpcGreeting greeting, float forwardMetres)
    {
        Transform t = greeting.transform;
        Vector3 flat = t.forward; flat.y = 0f; flat.Normalize();
        player.position = t.position + flat * forwardMetres;
    }

    static Transform FindPlayer(NpcGreeting g)
    {
        var m = typeof(NpcGreeting).GetMethod("FindPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        return (Transform)m.Invoke(g, null);
    }

    static bool CanSee(NpcGreeting g, Transform player)
    {
        var m = typeof(NpcGreeting).GetMethod("CanSee", BindingFlags.Instance | BindingFlags.NonPublic);
        return (bool)m.Invoke(g, new object[] { player });
    }

    static Renderer FindSmile(NpcGreeting g)
    {
        var m = typeof(NpcGreeting).GetMethod("FindSmile", BindingFlags.Instance | BindingFlags.NonPublic);
        return (Renderer)m.Invoke(g, null);
    }

    static FieldInfo Field(string name) =>
        typeof(NpcGreeting).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic |
                                           BindingFlags.Static | BindingFlags.Public);

    static void Awake(NpcGreeting g) => Invoke(g, "Awake");
    static void Tick(NpcGreeting g) => Invoke(g, "Update");
    static void Disable(NpcGreeting g) => Invoke(g, "OnDisable");

    static void Invoke(NpcGreeting g, string method) =>
        typeof(NpcGreeting).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(g, null);

    /// <summary>Moves the virtual clock forward.</summary>
    static void Advance(float seconds) => clock += seconds;

    static void Log(string message) => Debug.Log($"[coral-test] {message}");
    static void Fail(string message) { Failures.Add(message); Debug.LogError($"[coral-test] FAIL {message}"); }

    static void Report()
    {
        if (Failures.Count == 0) { Debug.Log("[coral-test] RESULT: all checks passed."); return; }
        Debug.Log($"[coral-test] RESULT: {Failures.Count} failure(s).");
        foreach (var f in Failures) Debug.Log($"[coral-test]   - {f}");
    }
}
