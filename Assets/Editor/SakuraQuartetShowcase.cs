using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the four concept-sheet riders - Shiori, Akihiro, Shinobu and Akane - TOGETHER on
/// Sakura Pass, close to the player's start line, so their visual fidelity can be judged in the
/// actual map rather than in four different regions one at a time.
///
/// WHY THIS IS A SEPARATE PASS AND NOT FOUR MORE ENTRIES IN <see cref="SakuraNpcRoster"/>.
///
/// The quartet already lives in four different rosters (Shiori in Sakura, Akihiro in Maple City,
/// Shinobu in Azora, Akane in Taka) and in the Shiosai coast traffic pool. Every one of those
/// rosters writes its generated materials into the SAME folder, Assets/Kuro/NPC/Materials, and
/// every one of them starts with a PurgeOldMaterials that deletes any material in that folder
/// whose name begins with one of ITS OWN rider names. So adding "Akihiro" to the Sakura roster
/// would make the next Sakura staging run DELETE Akihiro_Colnago_Racing_Red.mat out from under
/// the Maple City rider who is already pointing at it - a staged rider in another region would
/// silently lose his livery, and nothing would error.
///
/// This pass therefore:
///   * owns its own material folder, <see cref="MaterialDir"/>, and only ever purges inside it;
///   * prefixes every generated material with "Showcase_", which is not a rider name in ANY
///     roster, so no other roster's purge can match it either;
///   * uses its own object-name prefix, <see cref="NamePrefix"/>, so SakuraNpcRoster's own
///     exact-name prune, its pace pass and its self test all leave these riders alone - and
///     Coral, the player and the shared bike GLB are never written to;
///   * never calls ApplyKit: all four carry their kit painted into their OWN atlas (see
///     <c>SakuraNpcRoster.Npc.Rig</c>), so their body materials are left exactly as imported.
///     Only the bicycle is repainted, into per-rider CLONES.
///
/// Idempotent: the showcase group is pruned by exact name and rebuilt, and the generated
/// materials are purged from this folder first, so re-running converges instead of leaking.
///
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -quit -executeMethod SakuraQuartetShowcase.Stage
///
/// Menu: MapleRide/NPCs/Stage Sakura Quartet Showcase
/// </summary>
public static class SakuraQuartetShowcase
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;

    /// <summary>This pass's OWN material folder - see the class remarks.</summary>
    public const string MaterialDir = "Assets/Kuro/NPC/Materials/SakuraShowcase";

    /// <summary>Prefix on every generated material, so no roster purge anywhere can match it.</summary>
    const string MaterialPrefix = "Showcase";

    /// <summary>Exact object-name prefix. Pruning and the self test both match on
    /// <c>NamePrefix + rider name</c>; a Contains-style match would grab the roster's own
    /// "Sakura NPC Shiori" as well.</summary>
    public const string NamePrefix = "Sakura Showcase NPC ";

    /// <summary>Hierarchy parent, a child of the existing "NPCs" root so the riders are picked
    /// up by the roster's TrafficAvoidance (which collects GetComponentsInChildren).</summary>
    public const string GroupName = "Sakura Quartet Showcase";

    const string RosterRootName = "NPCs";

    // ---- Coral's verified fit numbers, reused verbatim from SakuraNpcRoster ----------------
    // These are NOT free parameters. The hand grip was baked against THIS frame's brake hoods at
    // THIS bike scale, and the lean angles are the ones that put the hands on the hoods.

    const float BikeScale = NpcCanonicalConformance.BikeScale;
    const float CoralSeatedHeight = 1.185f;
    const float GroundOffset = 0.065f;

    /// <summary>
    /// PROVISIONAL: where the four are parked along the pass, in metres from the player's start
    /// line, and how far apart.
    ///
    /// Chosen so the player meets all four within the first ~150 m of the ride - the brief is
    /// "easy to find shortly after entering the map" - while still being far enough apart
    /// (<see cref="SpacingM"/>) that no two riders share a frame closely enough to clip, and
    /// that each can be photographed unobstructed.
    /// </summary>
    public const float FirstMeetM = 55f;
    public const float SpacingM = 25f;

    /// <summary>
    /// PROVISIONAL: the showcase riders travel the PLAYER'S way.
    ///
    /// An oncoming rider is in shot for a couple of seconds and then gone behind the player,
    /// which is the wrong behaviour for a rider you are meant to go and look at. Same-way riders
    /// sit in front of the player for as long as they like, are caught and overtaken, and - per
    /// NpcGreeting's forward view cone - greet the moment the player gets past them, so nothing
    /// about the normal behaviour is lost.
    /// </summary>
    const bool SameWay = true;

    sealed class Rider
    {
        public string Name;
        public string Rig;
        public float TargetHeight;   // metres, seated, after the rig scale
        public float Speed;          // m/s, authored; scaled by SakuraNpcRoster.PaceScale
        public string Frame, Accent; // bike livery hexes
        public string[] Lines;
    }

    /// <summary>
    /// The four, with the TargetHeight, livery hexes and greeting lines they carry in their home
    /// rosters, copied verbatim so a rider looks and sounds like herself here. Only the placement
    /// (distance, direction, pace) is showcase-specific.
    ///
    /// PROVISIONAL: the authored speeds are deliberately at the slow end of the roster's range so
    /// the player catches all four early in the ride.
    /// </summary>
    static readonly Rider[] Quartet =
    {
        // Home roster: SakuraNpcRoster (she is the one of the four already native to this pass).
        new Rider { Name = "Shiori", Rig = "Assets/Kuro/NPC/KuroNPC_Shiori_Rigged.glb",
                    TargetHeight = 1.39f, Speed = 3.9f, Frame = "12575C", Accent = "EE6A5E",
                    Lines = new[] { "Freckles and sunburn, that's my spring!",
                                    "This cutting is my favourite bit of road.",
                                    "Teal and coral - you can't miss me, right?",
                                    "Save something for the hairpins." } },

        // Home roster: MapleCityNpcRoster.
        new Rider { Name = "Akihiro", Rig = "Assets/Kuro/NPC/KuroNPC_Akihiro_Rigged.glb",
                    TargetHeight = 1.41f, Speed = 3.7f, Frame = "3A3C40", Accent = "F07A22",
                    Lines = new[] { "Graphite and burnt orange - hard to lose me out here.",
                                    "Quiet road for once. I'll take it.",
                                    "Hold the inside line, I'll come round you.",
                                    "City's better from a saddle. So is this pass." } },

        // Home roster: AzoraNpcRoster.
        new Rider { Name = "Shinobu", Rig = "Assets/Kuro/NPC/KuroNPC_Shinobu_Rigged.glb",
                    TargetHeight = 1.37f, Speed = 4.0f, Frame = "222A45", Accent = "9A8FC4",
                    Lines = new[] { "Thin air suits me. Always has.",
                                    "Navy and violet - I picked it for the dusk.",
                                    "Keep your shoulders loose on the ramps.",
                                    "I'll be over the top before the cloud is." } },

        // Home roster: TakaNpcRoster.
        new Rider { Name = "Akane", Rig = "Assets/Kuro/NPC/KuroNPC_Akane_Rigged.glb",
                    TargetHeight = 1.40f, Speed = 3.8f, Frame = "7A1220", Accent = "D3B36A",
                    Lines = new[] { "Burgundy and gold. Club colours, since you ask.",
                                    "Tie the hair up before the bends - trust me.",
                                    "I descend this better than I climb it.",
                                    "Blossom all the way down. Not a bad office." } },
    };

    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    public static IEnumerable<string> Names => Quartet.Select(q => q.Name);

    // ------------------------------------------------------------------ staging

    [MenuItem("MapleRide/NPCs/Stage Sakura Quartet Showcase", priority = 42)]
    public static void Stage()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var roster = GameObject.Find(RosterRootName);
        if (roster == null) roster = new GameObject(RosterRootName);

        // Exact-name prune of the whole group, then rebuild: idempotent by construction.
        var existing = roster.transform.Find(GroupName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var group = new GameObject(GroupName);
        group.transform.SetParent(roster.transform, false);

        EnsureMaterialFolder();
        PurgeShowcaseMaterials();

        int staged = 0;
        for (int i = 0; i < Quartet.Length; i++)
        {
            float metres = FirstMeetM + i * SpacingM;
            var go = StageOne(Quartet[i], group.transform, metres);
            if (go == null) continue;
            staged++;
            Debug.Log($"[sakq] {Quartet[i].Name}: {metres:F0} m up the pass, " +
                      $"scale {RigScaleFor(Quartet[i]):F3}, " +
                      $"{SakuraNpcRoster.StagedSpeed(Quartet[i].Speed):F2} m/s " +
                      $"({SakuraNpcRoster.StagedSpeed(Quartet[i].Speed) * 3.6f:F1} kph), " +
                      $"{(SameWay ? "same-way" : "oncoming")}, pos {go.transform.position:F2}");
        }
        AssetDatabase.SaveAssets();

        // Re-collect the swerve-to-pass director so the showcase riders take part in normal
        // traffic behaviour instead of being ridden through. GetComponent-or-add, exactly as
        // SakuraNpcRoster does - this is the same component on the same root.
        var avoid = roster.GetComponent<TrafficAvoidance>();
        if (avoid == null) avoid = roster.AddComponent<TrafficAvoidance>();
        avoid.riders = roster.GetComponentsInChildren<NPCCyclist>(true);
        EditorUtility.SetDirty(avoid);
        Debug.Log($"[sakq] traffic avoidance re-wired to {avoid.riders.Length} rider(s).");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[sakq] staged {staged}/{Quartet.Length} showcase rider(s) under " +
                  $"'{RosterRootName}/{GroupName}'.");
    }

    static float RigScaleFor(Rider def) => NpcCanonicalConformance.RigScaleForAnyProductionNpc();

    static GameObject StageOne(Rider def, Transform parent, float metres)
    {
        var rigAsset = LoadPrefab(def.Rig);
        var bikeAsset = LoadPrefab(BikePath);
        if (rigAsset == null) { Debug.LogWarning($"[sakq] missing rig {def.Rig}"); return null; }
        if (bikeAsset == null) { Debug.LogWarning($"[sakq] missing bike {BikePath}"); return null; }

        float rigScale = RigScaleFor(def);

        var npc = new GameObject(NamePrefix + def.Name);
        npc.transform.SetParent(parent, false);

        var rider = new GameObject(def.Name + "Rider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * rigScale;

        // MUST be a child named exactly "Bike" - KuroBikeRig.Setup resolves the drivetrain by
        // that exact local name and has no global fallback.
        var bikeAnchor = new GameObject("Bike");
        bikeAnchor.transform.SetParent(rider.transform, false);
        bikeAnchor.transform.localScale = Vector3.one * BikeScale;
        var bikeModel = Instantiate(bikeAsset, bikeAnchor.transform);
        bikeModel.name = "BikeMesh";
        RepaintBike(def, bikeModel);

        var body = Instantiate(rigAsset, rider.transform);
        body.name = def.Name + "ArmatureAndMesh";
        // NO kit swap: her kit is painted into her own atlas. Touching those materials would
        // repaint the copy of her staged in her home region, because they are shared by
        // reference with the imported GLB.

        var rig = rider.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        rig.previewCadenceRpm = 55f;
        NpcCanonicalConformance.Configure(rig);

        var cyclist = npc.AddComponent<NPCCyclist>();
        cyclist.speed = SakuraNpcRoster.StagedSpeed(def.Speed);
        cyclist.groundOffset = GroundOffset;
        cyclist.reverse = !SameWay;
        // Same shared-line maths as the roster: pre-multiplying by dir cancels NPCCyclist's own
        // dir term so every rider, whichever way they travel, lands on the same road-side line.
        float jitter = ((def.Name.GetHashCode() & 0xFFFF) / 65535f - 0.5f) * 2f
                       * TrafficLine.LineJitterM;
        cyclist.laneOffset = (TrafficLine.SharedLineM + jitter) * (cyclist.reverse ? -1f : 1f);
        cyclist.segmentId = "pass";
        if (!cyclist.RebuildFromGraph())
            Debug.LogWarning($"[sakq] {def.Name}: no route graph - rider has no line to ride.");

        int steps = Mathf.RoundToInt(metres / NPCCyclist.Spacing);
        cyclist.progress = cyclist.reverse
            ? Mathf.Clamp(cyclist.route.Length - 1 - steps, 0, cyclist.route.Length - 1)
            : Mathf.Clamp(steps, 0, cyclist.route.Length - 1);
        cyclist.ApplyPose();
        EditorUtility.SetDirty(cyclist);

        var greeting = npc.AddComponent<NpcGreeting>();
        greeting.triggerDistance = 13f;
        greeting.viewAngle = 150f;
        greeting.visibleSeconds = 3.5f;
        greeting.rearmSeconds = 6f;
        greeting.bubbleOffset = new Vector3(0f, NpcCanonicalConformance.BubbleHeight, 0f);
        greeting.smileRendererName = "SmileDecal";
        greeting.ownPhrases = def.Lines;
        greeting.useFaceCard = true;
        // The rider's REAL name, not the object name: NpcGreeting.ResolveIdentity would derive
        // "Showcase NPC Shiori" from the object and miss her baked portrait.
        greeting.riderName = def.Name;
        greeting.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(
            $"Assets/Resources/{NpcGreeting.PortraitResourceDir}{def.Name}.png");
        if (greeting.portrait == null)
            Debug.LogWarning($"[sakq] {def.Name}: no baked portrait - card falls back to the " +
                             "silhouette.");
        EditorUtility.SetDirty(greeting);

        SetSmileNeutral(npc, def.Name);

        // Seat the rider in the editor, or the saved scene opens with a body standing through
        // its own bicycle. ForceSolveOnce runs Setup itself if the rig is not resolved yet.
        rig.ForceSolveOnce();
        SakuraNpcRoster.FreezeSkinnedBounds(npc);
        return npc;
    }

    // ------------------------------------------------------------------ materials

    static void EnsureMaterialFolder()
    {
        if (AssetDatabase.IsValidFolder(MaterialDir)) return;
        if (!AssetDatabase.IsValidFolder("Assets/Kuro/NPC/Materials"))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");
        AssetDatabase.CreateFolder("Assets/Kuro/NPC/Materials", "SakuraShowcase");
    }

    /// <summary>
    /// Deletes only the materials THIS pass generated, and only from its own folder. SaveMaterial
    /// uses GenerateUniqueAssetPath, so without this a second run would write
    /// "Showcase_Akane_Colnago_Racing_Red 1.mat" and the folder would grow without bound.
    /// </summary>
    static void PurgeShowcaseMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (file.StartsWith(MaterialPrefix + "_")) AssetDatabase.DeleteAsset(path);
        }
    }

    /// <summary>
    /// Recolours THIS rider's bicycle into per-rider material CLONES. The shared
    /// kuro_bike_colnago.glb materials are never written to - doing so would repaint the
    /// player's bike, Coral's and every other rider's in the scene.
    /// </summary>
    static void RepaintBike(Rider def, GameObject bike)
    {
        var frame = Hex(def.Frame);
        var accent = Hex(def.Accent);
        var clones = new Dictionary<string, Material>();

        foreach (var renderer in bike.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string src = mats[i].name.Replace(" (Instance)", "");
                Color? tint = FrameMaterials.Contains(src) ? frame
                            : AccentMaterials.Contains(src) ? accent
                            : (Color?)null;
                if (tint == null) continue;   // spokes, chain and tyres stay metal and rubber

                if (!clones.TryGetValue(src, out var clone))
                {
                    clone = new Material(mats[i]) { name = $"{MaterialPrefix}_{def.Name}_{src}" };
                    SetColor(clone, tint.Value);
                    AssetDatabase.CreateAsset(
                        clone, AssetDatabase.GenerateUniqueAssetPath($"{MaterialDir}/{clone.name}.mat"));
                    clones[src] = clone;
                }
                mats[i] = clone;
                touched = true;
            }
            if (touched)
            {
                renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    static void SetColor(Material m, Color c)
    {
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, p == "baseColorFactor" ? c.linear : c);
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    static void SetSmileNeutral(GameObject npc, string name)
    {
        var smile = FindChild(npc.transform, "SmileDecal");
        if (smile == null)
        {
            Debug.LogWarning($"[sakq] {name}: no 'SmileDecal' under the rig - she will greet " +
                             "but will not smile.");
            return;
        }
        var renderer = smile.GetComponent<Renderer>();
        if (renderer != null) { renderer.enabled = false; EditorUtility.SetDirty(renderer); }
    }

    // ------------------------------------------------------------------ lookup helpers

    public static Transform Group()
    {
        var roster = GameObject.Find(RosterRootName);
        return roster != null ? roster.transform.Find(GroupName) : null;
    }

    public static GameObject Find(string riderName)
    {
        var group = Group();
        if (group == null) return null;
        var t = group.Find(NamePrefix + riderName);
        return t != null ? t.gameObject : null;
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject LoadPrefab(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path) ??
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().FirstOrDefault();

    static GameObject Instantiate(GameObject prefab, Transform parent)
    {
        var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (go == null) go = Object.Instantiate(prefab);
        if (PrefabUtility.IsPartOfPrefabInstance(go))
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }
}
