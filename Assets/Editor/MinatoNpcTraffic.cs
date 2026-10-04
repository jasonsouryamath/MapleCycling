using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the MINATO COAST ambient traffic pool - thirty riders parked under the port city's
/// environment root and driven at runtime by <see cref="ShiosaiTrafficDirector"/>.
///
/// WHY THIS IS A MIRROR OF <see cref="ShiosaiNpcTraffic"/> AND NOT A NEW DESIGN: the runtime
/// director is already SEGMENT-driven (it takes a <c>segmentId</c> and a pool of rider roots and
/// reads everything else out of the baked route graph), so the only thing Minato actually needed
/// was its own pool and its own palette. Duplicating the staging class rather than generalising
/// ShiosaiNpcTraffic keeps each region's livery table, greeting lines, material folder and
/// prune-by-exact-name set independent, which is how every other region's roster is built here
/// (Sakura / Maple City / Azora / Taka all have their own staging class over shared runtime).
///
/// BODY (changed 2026-09-24): Minato riders are built on KURO's production body, not Coral's.
/// Coral's mesh is a 21k-island Meshy triangle soup that shears open when posed - the torn
/// helmet / jersey / shoes seen from the chase camera, reproducible in a plain Blender render.
/// Kuro's body is clean from every angle and shares Coral's 24-bone skeleton, so the rider GLBs
/// in Assets/Kuro/NPC/KuroRiders are Kuro + one procedural anime hair piece per style
/// (design_assets/3d/kuro/kuro_npc_build.py), each rider wearing its own recoloured Kuro atlas
/// (kuro_npc_liveries.py, which also writes minato_riders.json: style + hair colour per name).
///
/// What is copied verbatim from the coast pass - because it was expensive to get right and is
/// not zone-specific:
///   * the verified bike scale, wheel radius and seated height via
///     <see cref="NpcCanonicalConformance"/>, so a Minato rider is exactly the same physical
///     size as a Shiosai rider and as the player,
///   * per-rider RECOLOURED KIT TEXTURES rather than material tints (her whole body is one
///     atlas on one material slot - a tint would recolour her face with her jersey),
///   * CLONED bike materials, never the shared imported ones.
///
/// The livery table is Minato's own (design_assets/3d/kuro/minato_palette_variants.py): bright
/// modern city-commuter colours - hi-vis lemon, courier orange, pillar-box red, signal teal,
/// cobalt, magenta, mint - so a rider on the bay crossing reads as port-city traffic rather
/// than as one of Shiosai's sea-palette club riders. Twelve liveries round-robin across thirty
/// slots, each slot pairing a livery with its own bike paint and seated height.
///
/// REGION SURVIVAL / GATING (the decision, and why):
/// riders live UNDER the "Minato Coast Environment" root, exactly as Shiosai's live under the
/// coast root, so <see cref="RegionDirector"/>'s existing one-shared-scene visibility model
/// (it toggles each region's EnvironmentRoot with currentRegionId) hides and PARKS them the
/// instant the player is anywhere else - <see cref="ShiosaiTrafficDirector.OnDisable"/> parks
/// the whole pool when its root deactivates. No second gating mechanism, and no riders on the
/// Sakura or Shiosai roads. Because <see cref="MinatoCoastEnvironment.Apply"/> DESTROYS and
/// rebuilds that root from scratch on every run, Apply() calls <see cref="Stage"/> at the end of
/// every build, exactly as ShiosaiCoastEnvironment.Apply does - the environment build owns its
/// own dressing, so a rebuild can never silently strip the traffic.
///
/// Menu: MapleRide/NPCs/Stage Minato Coast Traffic
/// </summary>
public static class MinatoNpcTraffic
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string EnvironmentRootName = "Minato Coast Environment";
    const string TrafficRootName = "Minato Traffic";

    /// <summary>Segment id of the Minato crossing in the baked route graph.</summary>
    const string SegmentId = "minato";

    /// <summary>Kuro-based rider GLBs, per-rider kits and the style/hair manifest.</summary>
    const string KuroRiderDir = "Assets/Kuro/NPC/KuroRiders";
    const string ManifestPath = KuroRiderDir + "/minato_riders.json";

    /// <summary>"short" is Kuro's own cut, so it is Kuro's GLB itself - no extra hair mesh.</summary>
    const string ShortHairRigPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";

    /// <summary>The procedural hair piece's renderer / material, named by kuro_npc_build.py.</summary>
    const string HairRendererName = "NpcHair";

    /// <summary>Coral's Colnago. Her hand grip was baked against THIS frame at BikeScale.</summary>
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;

    /// <summary>Minato's OWN material folder, so no other region's purge can touch it.</summary>
    const string MaterialDir = "Assets/Kuro/NPC/Materials/Minato";

    // ---- Coral's verified numbers, reused unchanged (see ShiosaiNpcTraffic / CoralNpcSetup) ----
    const float BikeScale = NpcCanonicalConformance.BikeScale;
    const float CoralSeatedHeight = 1.185f;

    /// <summary>PROVISIONAL: how many riders' worth of traffic the crossing sustains.</summary>
    public const int PoolSize = 30;

    sealed class Livery
    {
        public string Name;
        public string Frame, Accent;   // bike paint hexes
        public string[] Lines;
        // From minato_riders.json (kuro_npc_liveries.py is the single source of these).
        public string Style = "short";
        public Color Hair = Color.black;
    }

    [System.Serializable] sealed class ManifestRider { public string name, style, hair; }
    [System.Serializable] sealed class Manifest { public ManifestRider[] riders; }

    static string RigPathForStyle(string style) =>
        style == "short" ? ShortHairRigPath : $"{KuroRiderDir}/KuroRider_{style}.glb";

    /// <summary>Copies each rider's hair style + colour out of the painter's manifest onto the
    /// livery table. A rider missing from the manifest keeps Kuro's short cut and is logged.</summary>
    static bool LoadManifest()
    {
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
        if (json == null)
        {
            Debug.LogWarning($"[minato-traffic] no manifest at {ManifestPath} - run " +
                             "python design_assets/3d/kuro/kuro_npc_liveries.py.");
            return false;
        }
        var byName = JsonUtility.FromJson<Manifest>(json.text).riders.ToDictionary(r => r.name);
        foreach (var livery in Liveries)
        {
            if (!byName.TryGetValue(livery.Name, out var r))
            {
                Debug.LogWarning($"[minato-traffic] {livery.Name}: not in {ManifestPath}.");
                continue;
            }
            livery.Style = r.style;
            ColorUtility.TryParseHtmlString(r.hair, out livery.Hair);
        }
        return true;
    }

    /// <summary>
    /// Twelve city-commuter liveries. Kit colours live in minato_palette_variants.py keyed by
    /// these same names; the bike paint below is matched to each kit. Greeting lines are
    /// Minato-specific - a line about blossom or the lighthouse would give the zone away.
    /// ALL PROVISIONAL art tuning.
    /// </summary>
    static readonly Livery[] Liveries =
    {
        new Livery { Name = "Minori", Frame = "F2C511", Accent = "17A5B5",
                     Lines = new[] { "Morning! Bridge is clear today.",
                                     "Mind the tram rails through the port.",
                                     "Best commute in the city, this." } },
        new Livery { Name = "Yutaka", Frame = "F0872A", Accent = "3A4148",
                     Lines = new[] { "Coming past on your left!",
                                     "Deliveries all the way to the far shore.",
                                     "Keep it steady over the span." } },
        new Livery { Name = "Kohaku", Frame = "E0A21A", Accent = "1E7A4E",
                     Lines = new[] { "Lovely light off the water.",
                                     "Crosswind picks up mid-crossing - careful.",
                                     "Nice rhythm you've got there." } },
        new Livery { Name = "Kaoru", Frame = "D62630", Accent = "F2F4F7",
                     Lines = new[] { "Hey! Great pace.",
                                     "Ferry terminal's backed up again.",
                                     "Ride safe out on the deck." } },
        new Livery { Name = "Tatsuya", Frame = "1F2C63", Accent = "A9D830",
                     Lines = new[] { "Long way across, isn't it.",
                                     "Watch the expansion joints.",
                                     "See you on the far side!" } },
        new Livery { Name = "Hibiki", Frame = "1878C8", Accent = "FFD62E",
                     Lines = new[] { "Beautiful morning for the crossing!",
                                     "Tailwind heading out of the port today.",
                                     "Just spinning the legs to work." } },
        new Livery { Name = "Ryoko", Frame = "16A6A0", Accent = "F0872A",
                     Lines = new[] { "Hi there! Enjoy the bay.",
                                     "Coffee at the terminal after?",
                                     "Cranes are busy this morning." } },
        new Livery { Name = "Rina", Frame = "3FCBA8", Accent = "E4553C",
                     Lines = new[] { "Room for two, come on through!",
                                     "Road's quiet before the shift change.",
                                     "Zone two all the way for me today." } },
        new Livery { Name = "Marina", Frame = "D4409C", Accent = "35C8C0",
                     Lines = new[] { "Nice bike! Ride safe.",
                                     "Love this view, every single day.",
                                     "Don't chase - it's a long span." } },
        new Livery { Name = "Asuka", Frame = "7B4FD1", Accent = "E8B838",
                     Lines = new[] { "Out for a long one today.",
                                     "Mountains on the far side are worth it.",
                                     "Good line through there!" } },
        new Livery { Name = "Sena", Frame = "E8EEF4", Accent = "2059B8",
                     Lines = new[] { "Cracking day to be out.",
                                     "Careful, gulls on the deck again.",
                                     "Easy does it up the approach." } },
        new Livery { Name = "Junpei", Frame = "3A3F46", Accent = "D62630",
                     Lines = new[] { "Left side, coming past!",
                                     "Been riding this crossing for years.",
                                     "City side is quicker this hour." } },
    };

    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    /// <summary>
    /// PROVISIONAL seated heights, in metres, cycled across the pool so thirty riders are not
    /// thirty identically-sized people. Same band as Shiosai's pool (1.31 - 1.48 m seated).
    /// </summary>
    static readonly float[] HeightCycle = { 1.40f, 1.34f, 1.46f, 1.36f, 1.31f, 1.44f, 1.38f, 1.48f };

    [MenuItem("MapleRide/NPCs/Stage Minato Coast Traffic", priority = 33)]
    public static void StageFromMenu()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var env = FindRootByExactName(EnvironmentRootName);
        if (env == null)
        {
            Debug.LogWarning($"[minato-traffic] no '{EnvironmentRootName}' root in the scene - " +
                             "run MapleRide/Environments/Build Minato Coast first.");
            return;
        }

        Stage(env.transform);

        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[minato-traffic] saved '{active.path}'.");
    }

    /// <summary>
    /// Bakes one greeting-card headshot per livery NAME (twelve, not thirty - slots sharing a
    /// livery share a face) via <see cref="NpcPortraitBake.BakeSingle"/>, which wakes the parked
    /// rider, isolates it from the co-located pool and writes only that rider's PNG. Then
    /// re-stages so every slot's NpcGreeting holds the portrait directly (NpcGreeting would also
    /// find it by name through Resources at runtime).
    /// Menu: MapleRide/NPCs/Bake Minato Traffic Portraits
    /// </summary>
    [MenuItem("MapleRide/NPCs/Bake Minato Traffic Portraits", priority = 63)]
    public static void BakePortraits()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int ok = 0;
        foreach (var livery in Liveries)
            // Well over two stops brighter than the default: the pool is parked out over the bay under
            // Minato's dusk grade, and the default EV baked every face noticeably dark.
            if (NpcPortraitBake.BakeSingle(livery.Name, exposureEv: -1.5f)) ok++;
        Debug.Log($"[minato-traffic] baked {ok}/{Liveries.Length} greeting portraits.");
        StageFromMenu();
    }

    /// <summary>
    /// Builds the pool under <paramref name="environmentRoot"/>. Idempotent: the traffic root is
    /// matched by EXACT name, EVERY match is destroyed first, and this pass's own materials are
    /// purged, so repeated Minato builds converge instead of accumulating.
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

        if (!LoadManifest()) return;
        var rigAssets = new Dictionary<string, GameObject>();
        foreach (var style in Liveries.Select(l => l.Style).Distinct())
        {
            var path = RigPathForStyle(style);
            rigAssets[style] = LoadPrefab(path);
            if (rigAssets[style] == null) { Debug.LogWarning($"[minato-traffic] missing rig {path}"); return; }
        }
        var bikeAsset = LoadPrefab(BikePath);
        if (bikeAsset == null) { Debug.LogWarning($"[minato-traffic] missing bike {BikePath}"); return; }

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
            pool[i] = BuildRider(trafficRoot.transform, i, livery, height, rigAssets[livery.Style],
                                 bikeAsset, celLitCache);
            Debug.Log($"[minato-traffic] rider {i:00} {livery.Name} staged " +
                      $"(seated {height:0.00} m, scale {height / CoralSeatedHeight:0.000}).");
        }

        var director = trafficRoot.AddComponent<ShiosaiTrafficDirector>();
        director.segmentId = SegmentId;
        director.pool = pool;
        // Pushed onto the SERIALIZED component, not left to the code defaults - staging is the
        // authority for this director's tuning (see ShiosaiNpcTraffic for why). ALL PROVISIONAL.
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
        // PROVISIONAL: the crossing is 19 km of open, mostly straight deck - a rider is in sight
        // far longer here than on the coast's twisty road, so the relevance window is widened
        // rather than leaving the road visibly empty ahead.
        director.windowAheadM = 520f;
        director.windowBehindM = 360f;
        // Different seed from the coast, so the two regions do not deal out the same sequence of
        // lanes and directions.
        director.seed = 20260923;
        EditorUtility.SetDirty(director);

        AssetDatabase.SaveAssets();
        Debug.Log($"[minato-traffic] staged {PoolSize} riders across {Liveries.Length} liveries " +
                  $"under '{EnvironmentRootName}/{TrafficRootName}' " +
                  $"(segment '{SegmentId}', pace {director.zone2SpeedMinMps * 3.6f:0.0}-" +
                  $"{director.zone2SpeedMaxMps * 3.6f:0.0} km/h, " +
                  $"window -{director.windowBehindM:0}/+{director.windowAheadM:0} m).");
    }

    // ------------------------------------------------------------------ one rider

    static Transform BuildRider(Transform parent, int index, Livery livery, float targetHeight,
                                GameObject rigAsset, GameObject bikeAsset,
                                Dictionary<Material, Material> celLitCache)
    {
        // Slot index in the name, not just the livery name: thirty riders share twelve liveries
        // and an exact-name prune needs thirty distinct names to converge on.
        string objName = $"Minato Rider {index:00} {livery.Name}";
        float rigScale = NpcCanonicalConformance.RigScaleForAnyProductionNpc();

        // The director drives THIS root along the road and aims it down its own travel
        // direction; NpcGreeting reads its forward axis for the view cone.
        var npc = new GameObject(objName);
        npc.transform.SetParent(parent, false);

        var rider = new GameObject(livery.Name + "Rider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * rigScale;

        // MUST be a child named exactly "Bike". KuroBikeRig.Setup falls back to a GLOBAL
        // GameObject.Find("Bike") otherwise - with dozens of bicycles in the scene that is a
        // lottery that can hand this rider the player's drivetrain.
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

        // QA #2 fix: ApplyKit only ever recolours the body atlas - it (and every other roster)
        // never converted the raw glTF-pbrMetallicRoughness shader itself, and never touched
        // SmileDecal at all (only disabled its renderer). metallicFactor=1/roughnessFactor=1
        // mirrors the sky: flat/blown-out on the broad face, banded on the curved helmet - same
        // shader bug, two different apparent "looks" on one rider. Converts the just-cloned body
        // material in place (ApplyKit already gave it its own persisted .mat) and redirects the
        // still-shared SmileDecal material to a persisted, region-owned CelLit clone.
        NpcCelLitConversion.ConvertRiderBody(body, MaterialDir, sharedCache: celLitCache);
        TintHair(livery, index, body);
        NpcCelLitConversion.AssertNoGltfMaterials(body, $"minato-traffic {objName} body");

        var rig = rider.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        // PROVISIONAL preview cadence only - once the director moves them, travelled distance
        // drives the drivetrain.
        rig.previewCadenceRpm = 80f;
        NpcCanonicalConformance.Configure(rig);

        // Build LOD renderers only after the canonical rig has resolved the imported socket
        // hierarchy (the LOD bake performs synchronous asset imports).
        ShiosaiBikeLod.Apply(bikeModel, bikeClones, out bikeDetail, out bikeFar);

        var greeting = npc.AddComponent<NpcGreeting>();
        greeting.triggerDistance = 12f;
        greeting.viewAngle = 140f;
        greeting.visibleSeconds = 2.8f;
        greeting.rearmSeconds = 14f;
        greeting.bubbleOffset = new Vector3(0f, NpcCanonicalConformance.BubbleHeight, 0f);
        greeting.smileRendererName = "SmileDecal";
        greeting.ownPhrases = livery.Lines;

        // Identity is assigned by STAGING rather than left to NpcGreeting.ResolveIdentity(),
        // which derives the name from the last token of the object name - silent coupling to a
        // name that exists for prune convergence, not for display.
        greeting.useFaceCard = true;
        greeting.riderName = livery.Name;

        // Portrait if one has been baked; a missing one falls back to the silhouette card,
        // which still carries the name and the line.
        var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(
            $"Assets/Resources/{NpcGreeting.PortraitResourceDir}{livery.Name}.png");
        greeting.portrait = baked;
        if (baked == null)
            Debug.Log($"[minato-traffic] {livery.Name}: no baked portrait - card will use the " +
                      "silhouette.");

        EditorUtility.SetDirty(greeting);

        // No KuroOutline - on a SKINNED body the inverted-hull pass renders exactly coincident
        // with the mesh, costing a second 109k-triangle mesh for zero silhouette. Thirty times.
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

        // Parked until the director spawns it, so the SAVED scene has no rider standing at the
        // world origin (which on this map is out over the bay).
        npc.SetActive(false);
        return npc.transform;
    }

    // ------------------------------------------------------------------ livery

    static void ApplyKit(Livery livery, int index, GameObject body)
    {
        var kitPath = $"{KuroRiderDir}/KuroKit_{livery.Name}.png";
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(kitPath);
        if (kit == null)
        {
            Debug.LogWarning($"[minato-traffic] {livery.Name}: no kit at {kitPath} - run " +
                             "python design_assets/3d/kuro/kuro_npc_liveries.py. " +
                             "This rider will wear Kuro's black kit.");
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
                    clone = new Material(mats[i]) { name = $"Minato{index:00}_{livery.Name}_Body" };
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
    /// REFERENCE across every instance in the scene: tinting one in place repaints every rider
    /// in every region AND the player's bicycle, and dirties an asset any reimport reverts.
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
                    clone = new Material(mats[i]) { name = $"Minato{index:00}_{livery.Name}_{src}" };
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
    /// so without this every run would leak a full set and the folder would grow without bound
    /// while the scene referenced whichever copy was newest. Scoped to Minato's own folder.
    /// </summary>
    static void PurgeOldMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The hair piece's material (already redirected to a shared CelLit clone by
    /// ConvertRiderBody) carries only a GREYSCALE gradient; each rider gets its own clone tinted
    /// with its hair colour via CelLit's _Color. A tint is correct HERE, unlike the body atlas,
    /// because nothing but hair uses this material.
    /// </summary>
    static void TintHair(Livery livery, int index, GameObject body)
    {
        var hair = FindChild(body.transform, HairRendererName);
        var renderer = hair != null ? hair.GetComponent<Renderer>() : null;
        if (renderer == null) return; // "short" riders wear Kuro's own (atlas-recoloured) cut
        var clone = new Material(renderer.sharedMaterial) { name = $"Minato{index:00}_{livery.Name}_Hair" };
        if (clone.HasProperty("_Color")) clone.SetColor("_Color", livery.Hair);
        SaveMaterial(clone);
        renderer.sharedMaterial = clone;
        EditorUtility.SetDirty(renderer);
    }

    static void SetSmileNeutral(GameObject npc, string name)
    {
        // Kuro-based riders have no SmileDecal (Kuro's expression is in his atlas) - nothing to
        // hide. Kept so a donor that does carry one is still neutralised.
        var smile = FindChild(npc.transform, "SmileDecal");
        if (smile == null) return;
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
            Debug.LogWarning($"[minato-traffic] '{m.shader.name}' exposes no base-colour slot.");
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
