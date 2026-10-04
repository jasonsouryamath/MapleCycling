using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the SHIOSAI COAST ambient traffic pool - thirty riders parked under the coast's
/// environment root and driven at runtime by <see cref="ShiosaiTrafficDirector"/>.
///
/// This is deliberately NOT a second copy of <see cref="SakuraNpcRoster"/>'s design. Sakura's
/// eleven riders are a fixed cast pinned to fixed metre marks on the oncoming lane; Shiosai's
/// thirty are an anonymous pool with no fixed position at all - the director decides where,
/// when and which way each one rides, every ride. What IS copied from the roster, verbatim, is
/// the part that was expensive to get right and is not zone-specific:
///
///   * Coral's full-resolution sculpt for every rider (the decimated KuroNPC_&lt;name&gt;.glb
///     variants collapse into "crumpled foil" - see SakuraNpcRoster's class remarks),
///   * her verified bike scale (0.9), wheel radius, lean angles, ground offset and seated
///     height, so a Shiosai rider is exactly the same physical size as a Sakura rider and the
///     player,
///   * per-rider RECOLOURED KIT TEXTURES rather than material tints, because her whole body is
///     one atlas on one material slot and a tint would recolour her face with her jersey,
///   * cloned bike materials, never the shared imported ones.
///
/// The livery table is Shiosai's own (assets/3d/kuro/shiosai_palette_variants.py): a coastal
/// palette of sea blues, aqua, sail white, sand and sun-bleached coral, so the two zones' riders
/// never read as the same club. Twelve liveries are assigned round-robin across thirty pool
/// slots, each slot pairing a livery with its own bike paint and body height - thirty riders
/// that read apart, at 40% of the texture cost of thirty unique atlases.
///
/// Riders live UNDER the "Shiosai Coast Environment" root, so <see cref="RegionDirector"/>'s
/// one-shared-scene visibility model hides them the instant the player travels to Sakura Pass,
/// with no extra region bookkeeping. <see cref="ShiosaiCoastEnvironment.Apply"/> rebuilds that
/// root from scratch, so it calls this pass at the end of every coast build.
///
/// Menu: MapleRide/NPCs/Stage Shiosai Coast Traffic
/// </summary>
public static class ShiosaiNpcTraffic
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string EnvironmentRootName = "Shiosai Coast Environment";
    const string TrafficRootName = "Shiosai Traffic";

    /// <summary>The one surviving full-resolution cyclist sculpt - shared with Sakura's roster.</summary>
    const string RigPath = "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb";

    /// <summary>Coral's Colnago. Her hand grip was baked against THIS frame at BikeScale.</summary>
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;

    /// <summary>Shiosai's OWN material folder, so Sakura's roster purge can never touch it.</summary>
    const string MaterialDir = "Assets/Kuro/NPC/Materials/Shiosai";
    const string KitDir = "Assets/Kuro/NPC/Textures";

    // ---- Coral's verified numbers, reused unchanged (see SakuraNpcRoster / CoralNpcSetup) ----
    const float BikeScale = NpcCanonicalConformance.BikeScale;
    const float CoralSeatedHeight = 1.185f;

    /// <summary>PROVISIONAL: how many riders' worth of traffic the coast sustains.</summary>
    public const int PoolSize = 30;

    sealed class Livery
    {
        public string Name;
        public string Frame, Accent;   // bike paint hexes
        /// <summary>
        /// Optional path to this livery's OWN skinned GLB, instead of Coral's sculpt.
        ///
        /// The other twelve liveries are Coral's body with a hue-rotated copy of her atlas, so
        /// thirty coast riders share one silhouette, one face and one hair mass. The quartet
        /// built by design_assets/3d/kuro/q4_build_quartet.py keeps Coral's dense sculpt, her
        /// 24-joint skin, her baked hand grip and her SmileDecal - so the bike fit and the
        /// greeting are unchanged - but cuts and reshapes the hair and repaints the atlas per
        /// texel in 3D, so the difference is geometry and printed kit rather than a hue offset.
        ///
        /// A livery with a Rig set carries its kit IN THE ATLAS, so <see cref="ApplyKit"/> must
        /// leave it alone; swapping in a CoralKit_*.png would paint Coral's jersey back on.
        /// </summary>
        public string Rig;
        public string[] Lines;
    }

    /// <summary>
    /// Twelve coastal liveries. Kit colours live in shiosai_palette_variants.py keyed by these
    /// same names; the bike paint below is matched to each kit. Greetings are coast-specific -
    /// a Sakura line about blossom and the summit would give the zone away instantly.
    /// </summary>
    static readonly Livery[] Liveries =
    {
        new Livery { Name = "Nami", Frame = "C43D8E", Accent = "2FB6B0",
                     Lines = new[] { "Morning! Breeze is kind today.",
                                     "Watch the sand on the next bend.",
                                     "Tide's right out - look at that shelf." } },
        new Livery { Name = "Isuzu", Frame = "17A5B5", Accent = "FFF2D0",
                     Lines = new[] { "Lovely day on the coast road!",
                                     "Nice steady rhythm you've got.",
                                     "Headland's worth the climb, promise." } },
        new Livery { Name = "Kaito", Frame = "20306E", Accent = "F0872A",
                     Lines = new[] { "Left side, coming past!",
                                     "Long way to the light today?",
                                     "Keep it smooth over the crest." } },
        new Livery { Name = "Shion", Frame = "E8EEF4", Accent = "5A6E80",
                     Lines = new[] { "Out for a long one today.",
                                     "Careful, gusts round the bluff.",
                                     "Sea's like glass this morning." } },
        new Livery { Name = "Suzu", Frame = "3FCBA8", Accent = "FFE28A",
                     Lines = new[] { "Hey! Great pace.",
                                     "Hydrangeas are out in the cove.",
                                     "Coffee at the harbour after?" } },
        new Livery { Name = "Rei", Frame = "E4553C", Accent = "1F7FA8",
                     Lines = new[] { "Just spinning the legs out.",
                                     "Tailwind heading north today.",
                                     "Mind the grate before the tunnel mouth." } },
        new Livery { Name = "Hayato", Frame = "F2A115", Accent = "2C5FA8",
                     Lines = new[] { "Beautiful morning for it!",
                                     "Easy does it through the cove.",
                                     "Been riding this road for years." } },
        new Livery { Name = "Kanon", Frame = "E3C98A", Accent = "1FA3B8",
                     Lines = new[] { "Zone two all the way for me today.",
                                     "Good line through there!",
                                     "Lighthouse is about two k on." } },
        new Livery { Name = "Tsubasa", Frame = "1E7A4E", Accent = "F5B93C",
                     Lines = new[] { "Nice bike! Ride safe.",
                                     "Pines give some shade up ahead.",
                                     "Steady wins on this stretch." } },
        new Livery { Name = "Riku", Frame = "7FBF34", Accent = "E85C9A",
                     Lines = new[] { "Room for two, come on through!",
                                     "Road's quiet this early.",
                                     "Salt spray on the guardrail - careful." } },
        new Livery { Name = "Mio", Frame = "9B4FD1", Accent = "35C8C0",
                     Lines = new[] { "Hi there! Enjoy the breeze.",
                                     "The overlook is my favourite spot.",
                                     "Don't chase, it's a long road." } },
        new Livery { Name = "Toma", Frame = "3A4148", Accent = "8FD3F0",
                     Lines = new[] { "Cracking day to be out.",
                                     "Watch the crosswind past the point.",
                                     "See you on the way back down!" } },

        // THE FOUR CONCEPT-SHEET RIDERS, appended rather than substituted so every coastal
        // livery above still appears in the pool. With sixteen liveries over thirty slots each
        // of these is staged once, which is the density they were meant for: they are the riders
        // you remember seeing, not the crowd. See Livery.Rig for why ApplyKit skips them.
        new Livery { Name = "Shiori", Frame = "12575C", Accent = "EE6A5E",
                     Rig = "Assets/Kuro/NPC/KuroNPC_Shiori_Rigged.glb",
                     Lines = new[] { "Salt air beats blossom, I've decided.",
                                     "Teal and coral - I match the sea today.",
                                     "Careful, the spray makes that corner slick." } },
        new Livery { Name = "Akihiro", Frame = "3A3C40", Accent = "F07A22",
                     Rig = "Assets/Kuro/NPC/KuroNPC_Akihiro_Rigged.glb",
                     Lines = new[] { "Came out of the city for this road.",
                                     "Flat and fast - finally, my kind of day.",
                                     "Coming past on your left, all yours after." } },
        new Livery { Name = "Shinobu", Frame = "222A45", Accent = "9A8FC4",
                     Rig = "Assets/Kuro/NPC/KuroNPC_Shinobu_Rigged.glb",
                     Lines = new[] { "Sea looks like ink from up on the bluff.",
                                     "Ride to the light and turn around - that's the plan.",
                                     "Quiet road. Best kind." } },
        new Livery { Name = "Akane", Frame = "7A1220", Accent = "D3B36A",
                     Rig = "Assets/Kuro/NPC/KuroNPC_Akane_Rigged.glb",
                     Lines = new[] { "Club run, and I'm off the front again.",
                                     "Ponytail's a sail in this crosswind.",
                                     "Harbour café at the end - come along." } },
    };

    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    /// <summary>
    /// PROVISIONAL seated heights, in metres, cycled across the pool so thirty riders are not
    /// thirty identically-sized people. Same band as Sakura's roster (1.31 - 1.48 m seated).
    /// </summary>
    static readonly float[] HeightCycle = { 1.42f, 1.33f, 1.47f, 1.37f, 1.31f, 1.44f, 1.39f, 1.35f };

    [MenuItem("MapleRide/NPCs/Stage Shiosai Coast Traffic", priority = 31)]
    public static void StageFromMenu()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var env = FindRootByExactName(EnvironmentRootName);
        if (env == null)
        {
            Debug.LogWarning($"[shiosai-traffic] no '{EnvironmentRootName}' root in the scene - " +
                             "run MapleRide/Environment/Build Shiosai Coast Mock first.");
            return;
        }

        Stage(env.transform);

        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[shiosai-traffic] saved '{active.path}'.");
    }

    /// <summary>
    /// THE DOCUMENTED RE-BAKE HOOK for the twelve coastal face cards.
    ///
    /// Why this is a separate menu item rather than part of staging: NpcPortraitBake renders a
    /// real headshot of each rider, and it SKIPS any rider that is not activeInHierarchy -
    /// deleting that rider's stale portrait on the way past. Shiosai's traffic is a recycled
    /// pool whose thirty riders are parked (inactive) in the saved scene, so a plain
    /// MapleRide/NPCs/Bake Greeting Portraits run does not merely miss them, it would delete
    /// any coastal portrait that did exist. This wakes the pool first and parks it again after.
    ///
    /// WHEN TO RUN IT: once the project is back on a render pipeline these shaders compile
    /// under. Baking while the project is on HDRP with Built-in custom shaders produces
    /// perfectly-framed MAGENTA faces, which is strictly worse than the silhouette fallback the
    /// card already gives, because a magenta PNG on disk looks like a successful bake.
    ///
    /// It is safe to run repeatedly; it converges.
    /// </summary>
    [MenuItem("MapleRide/NPCs/Bake Shiosai Portraits", priority = 32)]
    public static void BakeCoastPortraits()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var env = FindRootByExactName(EnvironmentRootName);
        var traffic = env != null ? env.transform.Find(TrafficRootName) : null;
        if (traffic == null)
        {
            Debug.LogWarning("[shiosai-traffic] no staged traffic to bake portraits from - run " +
                             "MapleRide/NPCs/Stage Shiosai Coast Traffic first.");
            return;
        }

        // The shared scene normally keeps Shiosai hidden while Sakura is the active region.
        // Waking only the pool children is not enough: NpcPortraitBake checks
        // activeInHierarchy, so every coastal rider still looked inactive and its portrait was
        // deleted instead of baked. Temporarily wake the owning environment root as well, then
        // restore its exact prior state in the finally block.
        bool envWasActive = env.gameObject.activeSelf;
        if (!envWasActive) env.gameObject.SetActive(true);
        bool trafficWasActive = traffic.gameObject.activeSelf;
        if (!trafficWasActive) traffic.gameObject.SetActive(true);

        // Wake every pool slot. Portraits are keyed by riderName, so duplicate slots simply
        // overwrite the same PNG; waking the complete pool is safer than relying on hierarchy
        // order to choose one active representative per livery.
        var woken = new List<GameObject>();
        foreach (Transform child in traffic)
        {
            var g = child.GetComponent<NpcGreeting>();
            if (g == null || string.IsNullOrEmpty(g.riderName)) continue;
            if (child.gameObject.activeSelf) continue;
            child.gameObject.SetActive(true);
            woken.Add(child.gameObject);
        }
        Debug.Log($"[shiosai-traffic] woke {woken.Count} riders for the portrait bake.");

        try { NpcPortraitBake.BakeAll(); }
        finally
        {
            // Park them again no matter what. A pool rider left active in the saved scene rides
            // the coast road as a ghost the director does not own.
            foreach (var go in woken) if (go != null) go.SetActive(false);
            if (!trafficWasActive && traffic != null) traffic.gameObject.SetActive(false);
            if (!envWasActive && env != null) env.gameObject.SetActive(false);
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[shiosai-traffic] re-parked {woken.Count} riders and saved '{active.path}'.");
        }

        // Re-run staging so the freshly baked textures are assigned to greeting.portrait rather
        // than only being found at runtime through Resources.
        StageFromMenu();
    }

    /// <summary>
    /// Builds the pool under <paramref name="environmentRoot"/>. Idempotent: the traffic root is
    /// matched by EXACT name, every match is destroyed first, and this pass's own materials are
    /// purged, so repeated coast builds converge instead of accumulating.
    /// </summary>
    public static void Stage(Transform environmentRoot)
    {
        if (environmentRoot == null) return;

        EnsureFolder(MaterialDir);
        PurgeOldMaterials();

        // EXACT-name prune of every match. A Contains()-style match has previously leaked a
        // second sun in this project; with thirty riders a leak is thirty skinned bodies.
        for (int i = environmentRoot.childCount - 1; i >= 0; i--)
            if (environmentRoot.GetChild(i).name == TrafficRootName)
                Object.DestroyImmediate(environmentRoot.GetChild(i).gameObject);

        var rigAsset = LoadPrefab(RigPath);
        var bikeAsset = LoadPrefab(BikePath);
        if (rigAsset == null) { Debug.LogWarning($"[shiosai-traffic] missing rig {RigPath}"); return; }
        if (bikeAsset == null) { Debug.LogWarning($"[shiosai-traffic] missing bike {BikePath}"); return; }

        var trafficRoot = new GameObject(TrafficRootName);
        trafficRoot.transform.SetParent(environmentRoot, false);

        // QA #2 fix: shared across the whole pool so any rider whose SmileDecal (or a livery
        // that skips the kit clone) still points at the raw, un-cloned glTF sub-asset converges
        // on ONE persisted CelLit clone, not thirty. See NpcCelLitConversion's header.
        var celLitCache = new Dictionary<Material, Material>();

        var pool = new Transform[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var livery = Liveries[i % Liveries.Length];
            float height = HeightCycle[i % HeightCycle.Length];
            pool[i] = BuildRider(trafficRoot.transform, i, livery, height, rigAsset, bikeAsset, celLitCache);
            Debug.Log($"[shiosai-traffic] rider {i:00} {livery.Name} staged " +
                      $"(seated {height:0.00} m, scale {height / CoralSeatedHeight:0.000}).");
        }

        var director = trafficRoot.AddComponent<ShiosaiTrafficDirector>();
        director.segmentId = "shiosai";
        director.pool = pool;
        // Pushed onto the SERIALIZED component, not left to the code defaults: this director has
        // been in the scene since before the drafting work, so an existing scene value silently
        // wins over any default a later edit introduces (which is exactly how the first pass
        // ended up still running a 1.55 m following gap and a 0.42 m lane jitter). Staging is
        // the authority for these. ALL PROVISIONAL.
        director.laneOffsetM = TrafficLine.SharedLineM;
        director.laneJitterM = TrafficLine.LineJitterM;
        director.sameDirectionShare = 0.5f;
        director.draftRangeM = 11f;
        director.draftIdealGapM = 3.0f;
        director.draftLateralM = 1.05f;
        director.minFollowGapM = 2.0f;
        director.blockLateralM = 0.85f;
        director.blockApproachGain = 0.9f;
        director.overtakeClearM = 4.5f;
        director.yieldSeconds = 14f;
        director.yieldPaceScale = 0.84f;
        director.npcPassShiftM = 1.1f;   // legacy; the shift now comes from TrafficLine
        director.riderLod = true;
        director.lodDetailM = 40f;
        director.lodShadowM = 40f;
        director.lodCoarseM = 65f;
        director.lodCullM = 110f;
        EditorUtility.SetDirty(director);

        AssetDatabase.SaveAssets();
        Debug.Log($"[shiosai-traffic] staged {PoolSize} riders across {Liveries.Length} liveries " +
                  $"under '{EnvironmentRootName}/{TrafficRootName}' " +
                  $"(pace {director.zone2SpeedMinMps * 3.6f:0.0}-{director.zone2SpeedMaxMps * 3.6f:0.0} km/h, " +
                  $"window -{director.windowBehindM:0}/+{director.windowAheadM:0} m).");
    }

    // ------------------------------------------------------------------ one rider

    static Transform BuildRider(Transform parent, int index, Livery livery, float targetHeight,
                                GameObject rigAsset, GameObject bikeAsset,
                                Dictionary<Material, Material> celLitCache)
    {
        // Slot index in the name, not just the livery name: thirty riders share sixteen liveries
        // and an exact-name prune needs thirty distinct names to converge on.
        string objName = $"Shiosai Rider {index:00} {livery.Name}";
        float rigScale = NpcCanonicalConformance.RigScaleForAnyProductionNpc();

        // A livery may bring its own sculpt; if it does and the asset is missing, fall back to
        // Coral rather than dropping the rider, or a bad path would silently thin the traffic.
        if (livery.Rig != null)
        {
            var own = LoadPrefab(livery.Rig);
            if (own != null) rigAsset = own;
            else Debug.LogWarning($"[shiosai-traffic] {livery.Name}: missing rig {livery.Rig} - " +
                                  "falling back to Coral's sculpt.");
        }
        // Kuro-based body for every named rider in a KuroRiders manifest (see KuroRiderBodies).
        else if (KuroRiderBodies.Has(livery.Name))
            rigAsset = KuroRiderBodies.Rig(livery.Name) ?? rigAsset;

        // The director drives THIS root along the road and aims it down its own travel
        // direction; NpcGreeting reads its forward axis for the view cone.
        var npc = new GameObject(objName);
        npc.transform.SetParent(parent, false);

        var rider = new GameObject(livery.Name + "Rider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * rigScale;

        // MUST be a child named exactly "Bike". KuroBikeRig.Setup falls back to a GLOBAL
        // GameObject.Find("Bike") otherwise - with 31 bicycles in the scene that is a lottery
        // that can hand this rider the player's drivetrain.
        var bikeAnchor = new GameObject("Bike");
        bikeAnchor.transform.SetParent(rider.transform, false);
        bikeAnchor.transform.localScale = Vector3.one * BikeScale;
        var bikeModel = Instantiate(bikeAsset, bikeAnchor.transform);
        bikeModel.name = "BikeMesh";
        var bikeClones = RepaintBike(livery, index, bikeModel);
        Renderer[] bikeDetail;
        Renderer bikeFar;

        var body = Instantiate(rigAsset, rider.transform);
        body.name = livery.Name + "ArmatureAndMesh";
        ApplyKit(livery, index, body);
        KuroRiderBodies.Finish(body, livery.Name, MaterialDir);

        // QA #2 fix: ApplyKit only ever recolours the body atlas (and does nothing at all for a
        // livery.Rig rider, whose own-atlas body was never cloned) - no roster ever converted the
        // raw glTF-pbrMetallicRoughness shader itself, and none ever touched SmileDecal beyond
        // disabling its renderer. metallicFactor=1/roughnessFactor=1 mirrors the sky: flat/blown
        // out on the broad face, banded on the curved helmet - same shader bug, two different
        // apparent "looks" on one rider. Converts whatever ApplyKit already cloned in place, and
        // redirects any still-shared material (SmileDecal, or a livery.Rig body) to a persisted,
        // region-owned CelLit clone.
        NpcCelLitConversion.ConvertRiderBody(body, MaterialDir, sharedCache: celLitCache);
        NpcCelLitConversion.AssertNoGltfMaterials(body, $"shiosai-traffic {objName} body");

        var rig = rider.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        // DriveDrivetrain divides WORLD distance by this, so BOTH scales must be folded in or
        // the wheels spin at the wrong rate for the speed the rider is travelling.
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        // PROVISIONAL: a zone 2 rider spins around 80 rpm. Only used as a preview cadence -
        // once the director moves them, travelled distance drives the drivetrain.
        rig.previewCadenceRpm = 80f;
        NpcCanonicalConformance.Configure(rig);

        // Build LOD renderers only after the canonical rig has resolved the imported socket
        // hierarchy. The LOD bake performs synchronous asset imports; doing that before Setup
        // could leave the just-added rig with unresolved transient prefab references.
        ShiosaiBikeLod.Apply(bikeModel, bikeClones, out bikeDetail, out bikeFar);

        var greeting = npc.AddComponent<NpcGreeting>();
        greeting.triggerDistance = 12f;
        greeting.viewAngle = 140f;
        greeting.visibleSeconds = 2.8f;
        // Longer than Sakura's 6 s: with thirty riders passing, a short re-arm would leave
        // speech bubbles up almost continuously.
        greeting.rearmSeconds = 14f;
        greeting.bubbleOffset = new Vector3(0f, NpcCanonicalConformance.BubbleHeight, 0f);
        greeting.smileRendererName = "SmileDecal";
        greeting.ownPhrases = livery.Lines;

        // --- face card ----------------------------------------------------------------------
        // Explicitly assigned rather than left to NpcGreeting.ResolveIdentity(). Identity
        // resolution derives the name from the LAST space-separated token of the object name,
        // which happens to work here ("Shiosai Rider 07 Kanon" -> "Kanon") but is silent
        // coupling: the slot index was added to the object name for prune convergence, and any
        // future rename would have quietly broken every coastal name card. Staging is the
        // authority for identity, exactly as it is for the director's tuning.
        greeting.useFaceCard = true;
        greeting.riderName = livery.Name;

        // Portrait, if one has been baked. Coastal liveries mostly have none yet (see
        // BakeCoastPortraits below), and that is FINE: the card falls back to a silhouette and
        // still shows the name and the line, which is the part that was missing. What must NOT
        // happen is shipping a portrait baked while the project is on a pipeline its shaders do
        // not compile under - a magenta face is worse than no face.
        var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(
            $"Assets/Resources/{NpcGreeting.PortraitResourceDir}{livery.Name}.png");
        greeting.portrait = baked;
        if (baked == null)
            Debug.Log($"[shiosai-traffic] {livery.Name}: no baked portrait - card will use the " +
                      "silhouette. Run MapleRide/NPCs/Bake Shiosai Portraits once the project " +
                      "is back on a pipeline these shaders compile under.");

        EditorUtility.SetDirty(greeting);

        // No KuroOutline - see SakuraNpcRoster: on a SKINNED body the inverted-hull pass renders
        // exactly coincident with the mesh (bones ignore the localScale it relies on), so it
        // costs a second 109k-triangle mesh for zero visible silhouette. Thirty times over.
        SetSmileNeutral(npc, objName);

        // Seat the rider once in the editor so the saved scene holds a real pose rather than a
        // body standing through its own bicycle.
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        SakuraNpcRoster.FreezeSkinnedBounds(npc);

        var lod = npc.AddComponent<ShiosaiRiderLod>();
        lod.body = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        lod.bikeDetail = bikeDetail;
        lod.bikeFar = bikeFar;
        lod.rig = rig;
        lod.greeting = greeting;
        EditorUtility.SetDirty(lod);

        // Parked until the director spawns it. Also means the SAVED scene has no rider standing
        // at the world origin (which on this map is out over the water).
        npc.SetActive(false);
        return npc.transform;
    }

    // ------------------------------------------------------------------ livery

    static void ApplyKit(Livery livery, int index, GameObject body)
    {
        if (livery.Rig != null) return;  // kit is painted into the rider's own atlas.

        var kitPath = KuroRiderBodies.Has(livery.Name) ? KuroRiderBodies.KitPath(livery.Name)
                                                       : $"{KitDir}/CoralKit_{livery.Name}.png";
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(kitPath);
        if (kit == null)
        {
            Debug.LogWarning($"[shiosai-traffic] {livery.Name}: no kit at {kitPath} - run " +
                             "python assets/3d/kuro/shiosai_palette_variants.py. " +
                             "This rider will wear Coral's colours.");
            return;
        }

        Material clone = null;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                // Body atlas only - the smile decal keeps its own material.
                if (!mats[i].name.StartsWith("Material_")) continue;
                if (clone == null)
                {
                    clone = new Material(mats[i]) { name = $"Shiosai{index:00}_{livery.Name}_Body" };
                    SetTexture(clone, kit);
                    SaveMaterial(clone);
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

    /// <summary>
    /// Clones the bike's materials before recolouring. The shared GLB's materials are shared BY
    /// REFERENCE across every instance in the scene: tinting one in place repaints all thirty
    /// riders AND the player's bicycle, and dirties an asset any reimport silently reverts.
    /// One clone per source material per rider - the Colnago is ~78 parts sharing two materials.
    /// </summary>
    static Dictionary<string, Material> RepaintBike(Livery livery, int index, GameObject bike)
    {
        var frame = Hex(livery.Frame);
        var accent = Hex(livery.Accent);
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
                if (tint == null) continue;

                if (!clones.TryGetValue(src, out var clone))
                {
                    clone = new Material(mats[i]) { name = $"Shiosai{index:00}_{livery.Name}_{src}" };
                    SetColor(clone, tint.Value);
                    SaveMaterial(clone);
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
        return clones;
    }

    /// <summary>
    /// Deletes this pass's own generated materials. SaveMaterial uses GenerateUniqueAssetPath,
    /// so without this every run would leak a full set (30 riders x 3 materials) and the folder
    /// would grow without bound while the scene referenced whichever copy was newest.
    /// Scoped to Shiosai's own folder, so Sakura's roster materials can never be touched.
    /// </summary>
    static void PurgeOldMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
    }

    // ------------------------------------------------------------------ helpers

    static void SetSmileNeutral(GameObject npc, string name)
    {
        var smile = FindChild(npc.transform, "SmileDecal");
        if (smile == null)
        {
            Debug.LogWarning($"[shiosai-traffic] {name}: no 'SmileDecal' under the rig.");
            return;
        }
        var renderer = smile.GetComponent<Renderer>();
        if (renderer != null) { renderer.enabled = false; EditorUtility.SetDirty(renderer); }
    }

    // glTFast materials do not use the built-in _Color/_MainTex names, and which properties
    // exist depends on the import path, so set every variant that is actually present.
    static readonly string[] TextureProps = { "baseColorTexture", "_BaseMap", "_MainTex", "_BaseColorMap" };
    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    static void SetTexture(Material m, Texture2D tex)
    {
        bool any = false;
        foreach (var p in TextureProps)
            if (m.HasProperty(p)) { m.SetTexture(p, tex); any = true; }
        if (!any)
            Debug.LogWarning($"[shiosai-traffic] '{m.shader.name}' exposes no base-colour slot.");
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, Color.white);
    }

    static void SetColor(Material m, Color c)
    {
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, p == "baseColorFactor" ? c.linear : c);
    }

    static void SaveMaterial(Material m) =>
        AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath($"{MaterialDir}/{m.name}.mat"));

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
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

    static GameObject FindRootByExactName(string exactName)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == exactName) return go;
        return null;
    }

    static GameObject LoadPrefab(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path) ??
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().FirstOrDefault();

    static GameObject Instantiate(GameObject prefab, Transform parent)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        if (go == null) go = Object.Instantiate(prefab, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }
}
