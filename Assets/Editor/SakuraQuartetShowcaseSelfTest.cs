using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The showcase group's equivalent of <see cref="CoralNpcSelfTest"/> / <see
/// cref="SakuraNpcRosterSelfTest"/>: checks the things that have actually gone wrong on this
/// project's riders before, measured rather than assumed.
///
/// Every geometric check is expressed RELATIVE TO CORAL, the one rider in this scene whose fit
/// is proven by eye: she is staged by the roster from the same numbers, so her hand-to-hood gap,
/// her foot-to-pedal gap and her wheel-to-transform drop are what "correct" looks like at this
/// bike scale. An absolute threshold invented here would just be another green number.
///
/// This is an INSPECT-ONLY pass: it solves the rigs, measures, and discards the scene, so it
/// never leaves a crash-recovery backup for the next interactive session to restore.
///
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -quit -executeMethod SakuraQuartetShowcaseSelfTest.Run
///
/// Menu: MapleRide/NPCs/Run Sakura Quartet Showcase Self Test
/// </summary>
public static class SakuraQuartetShowcaseSelfTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string CoralName = "Sakura NPC Coral";
    const string PlayerName = "Kuro on Sakura Pass";

    /// <summary>PROVISIONAL: how much worse than Coral a contact gap may be, in metres.</summary>
    const float ContactSlackM = 0.05f;

    /// <summary>PROVISIONAL: how far a rider's wheel-to-root drop may differ from Coral's.</summary>
    const float RideHeightSlackM = 0.06f;

    /// <summary>PROVISIONAL: minimum world separation between any two showcase riders.</summary>
    const float MinSeparationM = 12f;

    static readonly List<string> Failures = new List<string>();

    [MenuItem("MapleRide/NPCs/Run Sakura Quartet Showcase Self Test", priority = 43)]
    public static void Run()
    {
        Failures.Clear();
        try
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var group = SakuraQuartetShowcase.Group();
            if (group == null)
            {
                Fail($"no '{SakuraQuartetShowcase.GroupName}' group in the scene - run " +
                     "SakuraQuartetShowcase.Stage.");
                Report();
                return;
            }

            var reference = Measure(GameObject.Find(CoralName));
            if (reference == null)
            {
                Fail($"'{CoralName}' is not in the scene, so there is no proven rider to " +
                     "measure the showcase against.");
                Report();
                return;
            }
            Log($"reference (Coral): grip {reference.Grip * 1000f:F1} mm, " +
                $"pedal {reference.Pedal * 1000f:F1} mm, seated {reference.SeatedHeight:F3} m, " +
                $"wheel drop {reference.WheelDrop:F3} m" +
                (reference.Missing.Count > 0
                    ? $" [UNRESOLVED SOCKETS: {string.Join(", ", reference.Missing)}]" : ""));

            var protectedMats = ProtectedMaterials();
            var placed = new List<(string name, Vector3 pos)>();
            var everyLine = new Dictionary<string, string>();

            foreach (var name in SakuraQuartetShowcase.Names)
            {
                string objName = SakuraQuartetShowcase.NamePrefix + name;
                var matches = new List<Transform>();
                foreach (Transform c in group) if (c.name == objName) matches.Add(c);

                if (matches.Count == 0) { Fail($"{name}: '{objName}' is not in the group."); continue; }
                if (matches.Count > 1)
                    Fail($"{name}: {matches.Count} objects named '{objName}' - staging is not " +
                         "idempotent.");

                var npc = matches[0].gameObject;
                if (!npc.activeInHierarchy) Fail($"{name}: staged but not active in the scene.");

                CheckHierarchy(name, npc);
                CheckRouteIsLive(name, npc);
                CheckGreeting(name, npc, everyLine);
                CheckMaterials(name, npc, protectedMats);

                var m = Measure(npc);
                if (m == null) { Fail($"{name}: no CoralBikeRig to solve."); continue; }
                if (m.Missing.Count > 0)
                    Fail($"{name}: rig did not resolve {string.Join(", ", m.Missing)} - the " +
                         "contact gaps below are measured against nothing.");
                Log($"{name}: grip {m.Grip * 1000f:F1} mm, pedal {m.Pedal * 1000f:F1} mm, " +
                    $"seated {m.SeatedHeight:F3} m, wheel drop {m.WheelDrop:F3} m, " +
                    $"pos {npc.transform.position:F2}");

                if (m.Grip > reference.Grip + ContactSlackM)
                    Fail($"{name}: hands are {m.Grip * 1000f:F0} mm off the brake hoods " +
                         $"(Coral {reference.Grip * 1000f:F0} mm).");
                if (m.Pedal > reference.Pedal + ContactSlackM)
                    Fail($"{name}: feet are {m.Pedal * 1000f:F0} mm off the pedals " +
                         $"(Coral {reference.Pedal * 1000f:F0} mm).");
                if (Mathf.Abs(m.WheelDrop - reference.WheelDrop) > RideHeightSlackM)
                    Fail($"{name}: wheel sits {m.WheelDrop:F3} m below the route point, Coral " +
                         $"{reference.WheelDrop:F3} m - the bike is floating or buried.");

                float target = m.TargetHeight;
                if (target > 0f && Mathf.Abs(m.SeatedHeight - target) > target * 0.10f)
                    Fail($"{name}: seated height {m.SeatedHeight:F3} m is more than 10% from the " +
                         $"staged target {target:F3} m.");

                placed.Add((name, npc.transform.position));
            }

            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                {
                    float d = Vector3.Distance(placed[i].pos, placed[j].pos);
                    if (d < MinSeparationM)
                        Fail($"{placed[i].name} and {placed[j].name} are only {d:F1} m apart " +
                             $"(minimum {MinSeparationM:F0} m) - they will overlap on screen.");
                }

            Report();
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    // ---------------------------------------------------------------- checks

    static void CheckHierarchy(string name, GameObject npc)
    {
        var rider = npc.transform.Find(name + "Rider");
        if (rider == null) { Fail($"{name}: no '{name}Rider' child."); return; }

        // Exact child name "Bike" - KuroBikeRig.Setup has no global fallback by design.
        var bike = rider.Find("Bike");
        if (bike == null)
        {
            Fail($"{name}: no child named exactly 'Bike' - the rig cannot resolve a drivetrain.");
            return;
        }
        if (bike.Find("BikeMesh") == null)
            Fail($"{name}: 'Bike' has no 'BikeMesh' child.");
        if (rider.Find(name + "ArmatureAndMesh") == null)
            Fail($"{name}: no '{name}ArmatureAndMesh' body under the rider.");
        if (rider.GetComponent<CoralBikeRig>() == null)
            Fail($"{name}: no CoralBikeRig on the rider node.");

        var smile = npc.GetComponentsInChildren<Transform>(true)
                       .FirstOrDefault(t => t.name == "SmileDecal");
        if (smile == null) Fail($"{name}: no SmileDecal under the rig.");
        else
        {
            var r = smile.GetComponent<Renderer>();
            if (r != null && r.enabled) Fail($"{name}: SmileDecal is on in the saved scene.");
        }
    }

    /// <summary>
    /// The rider's route is SERIALIZED, so a rebuilt road leaves it riding an older pass while
    /// everything still compiles. Re-deriving and comparing turns that silent drift into a loud
    /// failure. Inspect-only: the scene is discarded at the end of the run.
    /// </summary>
    static void CheckRouteIsLive(string name, GameObject npc)
    {
        var cyc = npc.GetComponent<NPCCyclist>();
        if (cyc == null) { Fail($"{name}: no NPCCyclist."); return; }
        var cached = cyc.route;
        if (cached == null || cached.Length < 2) { Fail($"{name}: no baked route."); return; }

        var before = (Vector3[])cached.Clone();
        if (!cyc.RebuildFromGraph()) { Fail($"{name}: route graph segment 'pass' is missing."); return; }
        if (cyc.route.Length != before.Length)
        {
            Fail($"{name}: cached route has {before.Length} points, the live graph gives " +
                 $"{cyc.route.Length} - re-run the showcase staging pass.");
            return;
        }
        float worst = 0f;
        for (int i = 0; i < before.Length; i++) worst = Mathf.Max(worst, Vector3.Distance(before[i], cyc.route[i]));
        if (worst > 0.25f)
            Fail($"{name}: cached route drifts up to {worst:F2} m from the live road.");
    }

    static void CheckGreeting(string name, GameObject npc, Dictionary<string, string> everyLine)
    {
        var g = npc.GetComponent<NpcGreeting>();
        if (g == null) { Fail($"{name}: no NpcGreeting."); return; }
        if (g.ownPhrases == null || g.ownPhrases.Length < 2)
            Fail($"{name}: fewer than two own phrases - she would draw from the shared pool.");
        else
        {
            foreach (var line in g.ownPhrases)
            {
                if (string.IsNullOrWhiteSpace(line)) { Fail($"{name}: blank greeting line."); continue; }
                if (everyLine.TryGetValue(line, out var owner) && owner != name)
                    Fail($"{name} and {owner} share the greeting line \"{line}\".");
                else everyLine[line] = name;
            }
        }
        if (g.riderName != name) Fail($"{name}: riderName is '{g.riderName}'.");
        if (!g.useFaceCard) Fail($"{name}: face card disabled.");
        if (g.portrait == null) Fail($"{name}: no portrait - the card falls back to a silhouette.");
    }

    /// <summary>
    /// The livery must be this rider's OWN cloned assets, living in the showcase material folder.
    /// Her BODY atlas and the bicycle's UNPAINTED parts, by contrast, must still be the materials
    /// that came out of a GLB: spokes, chain and tyres stay metal and rubber on anyone's bicycle,
    /// so sharing those imported materials with the player and Coral is correct and is how the
    /// whole roster is built. What would be a defect is a GENERATED material - one this or
    /// another pass wrote to disk - turning up on the player's or Coral's bike, because that
    /// means a recolour leaked out of this rider and repainted somebody else.
    /// </summary>
    static void CheckMaterials(string name, GameObject npc, HashSet<Material> protectedMats)
    {
        var rider = npc.transform.Find(name + "Rider");
        var bike = rider != null ? rider.Find("Bike") : null;
        if (bike != null)
        {
            var seen = new HashSet<Material>();
            foreach (var r in bike.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) seen.Add(m);

            int owned = 0, imported = 0;
            foreach (var m in seen)
            {
                var path = AssetDatabase.GetAssetPath(m);
                bool isImported = !string.IsNullOrEmpty(path) && path.EndsWith(".glb");
                if (path.StartsWith(SakuraQuartetShowcase.MaterialDir)) owned++;
                else if (isImported) imported++;

                if (protectedMats.Contains(m) && !isImported)
                    Fail($"{name}: generated bike material '{m.name}' ({path}) is also on the " +
                         "player's or Coral's bike - a recolour has leaked.");
            }
            if (owned == 0)
                Fail($"{name}: no cloned livery material under {SakuraQuartetShowcase.MaterialDir} " +
                     "- the bike is still wearing the shared GLB paint.");
            else Log($"{name}: {owned} cloned livery material(s), {imported} shared imported " +
                     $"(spokes/chain/tyres), {seen.Count} on the bike.");
        }

        var body = rider != null ? rider.Find(name + "ArmatureAndMesh") : null;
        if (body == null) return;
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                var path = AssetDatabase.GetAssetPath(m);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".glb"))
                    Fail($"{name}: body material '{m.name}' is a generated asset ({path}); her " +
                         "kit must stay in her own imported atlas.");
            }
    }

    static HashSet<Material> ProtectedMaterials()
    {
        var set = new HashSet<Material>();
        foreach (var rootName in new[] { PlayerName, CoralName })
        {
            var go = GameObject.Find(rootName);
            if (go == null) { Log($"note: '{rootName}' not found for the material guard."); continue; }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) set.Add(m);
        }
        return set;
    }

    // ---------------------------------------------------------------- measurement

    sealed class Fit
    {
        public float Grip, Pedal, SeatedHeight, WheelDrop, TargetHeight;
        /// <summary>Sockets the rig failed to resolve. A gap of "0.0 mm" measured against a
        /// NULL socket is the exact kind of green number this project has shipped defects
        /// behind, so it is reported rather than silently scored as a perfect grip.</summary>
        public List<string> Missing = new List<string>();
    }

    /// <summary>
    /// Solves the rider's rig once and MEASURES the posed result - baked skinned vertices, never
    /// Renderer.bounds, which on an imported SkinnedMeshRenderer is the import-time estimate.
    /// </summary>
    static Fit Measure(GameObject npc)
    {
        if (npc == null) return null;
        var rig = npc.GetComponentInChildren<CoralBikeRig>(true);
        if (rig == null) return null;
        rig.ForceSolveOnce();

        var fit = new Fit();
        foreach (var (socket, t) in new (string, Transform)[]
                 { ("WristL", rig.WristL), ("WristR", rig.WristR), ("HoodL", rig.HoodL),
                   ("HoodR", rig.HoodR), ("FootL", rig.FootL), ("FootR", rig.FootR),
                   ("PedalL", rig.PedalL), ("PedalR", rig.PedalR) })
            if (t == null) fit.Missing.Add(socket);

        fit.Grip = Mathf.Max(Gap(rig.WristL, rig.HoodL), Gap(rig.WristR, rig.HoodR));
        fit.Pedal = Mathf.Max(
            rig.FootL != null ? Vector3.Distance(rig.FootL.position, rig.FootTargetL) : 0f,
            rig.FootR != null ? Vector3.Distance(rig.FootR.position, rig.FootTargetR) : 0f);

        float top = float.NegativeInfinity;
        foreach (var smr in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            foreach (var v in baked.vertices) top = Mathf.Max(top, smr.transform.TransformPoint(v).y);
            Object.DestroyImmediate(baked);
        }
        fit.SeatedHeight = float.IsNegativeInfinity(top) ? 0f : top - npc.transform.position.y;

        float bottom = float.PositiveInfinity;
        var rider = rig.transform;
        var bike = rider.Find("Bike");
        if (bike != null)
            foreach (var r in bike.GetComponentsInChildren<MeshRenderer>(true))
                bottom = Mathf.Min(bottom, r.bounds.min.y);
        fit.WheelDrop = float.IsPositiveInfinity(bottom) ? 0f : npc.transform.position.y - bottom;

        // What the staging pass aimed at, recovered from the rig scale it wrote.
        fit.TargetHeight = rider.localScale.y * 1.185f;
        return fit;
    }

    static float Gap(Transform wrist, Transform hood) =>
        wrist != null && hood != null ? Vector3.Distance(wrist.position, hood.position) : 0f;

    // ---------------------------------------------------------------- reporting

    static void Log(string m) => Debug.Log($"[sakq-test] {m}");
    static void Fail(string m) { Failures.Add(m); Debug.LogError($"[sakq-test] FAIL {m}"); }

    static void Report()
    {
        if (Failures.Count == 0) { Debug.Log("[sakq-test] RESULT: all checks passed."); return; }
        Debug.Log($"[sakq-test] RESULT: {Failures.Count} failure(s).");
        foreach (var f in Failures) Debug.Log($"[sakq-test]   - {f}");
    }
}
