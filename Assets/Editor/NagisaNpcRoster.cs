using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// B5 NAGISA BAY rider cast (copilot CLI Opus builder, 2026-09-27): thirteen Kuro-based riders
/// in fictional island club kits on the 16.2 km "nagisa" segment - marina front, resort
/// beachfront, the hill loop and KOM, the east coast road and the bridge approach.
///
/// Built on the proven N-rule-1 pipeline and nothing else: every construction number and step is
/// the TakaNpcRoster moving-cyclist path, verbatim (the bike scale, the ground offset, the
/// CoralBikeRig + NpcCanonicalConformance.Configure, the NPCCyclist.RebuildFromGraph seating by
/// distance, NpcGreeting + face card, the SmileDecal neutral, SakuraNpcRoster.FreezeSkinnedBounds,
/// TrafficAvoidance on the root and a region rider LOD (NagisaRiderLod)). Bodies come ONLY through
/// KuroRiderBodies (the shared Kuro rig per hair style + KuroKit_&lt;Name&gt;.png atlases written by
/// design_assets/3d/kuro/nagisa_npc_liveries.py -> nagisa_riders.json), and KuroRiderBodies.Finish
/// runs NpcCelLitConversion. Never a per-name KuroNPC_&lt;name&gt;.glb.
///
/// Kaimana is the region's rival hook: the hill-loop KOM specialist, parked at the foot of the
/// 9.5 % wall (5.5 km) so RaceDirector can take him over in place (RaceRoster "kaimana").
///
/// Names were checked against every C# roster and every *_riders.json: no collisions.
/// PROVISIONAL throughout: meet distances, speeds, lanes, liveries and lines are art direction.
/// Entry point (runner): NagisaNpcRoster.Stage.  Menu: MapleRide/NPCs/Stage Nagisa Riders.
/// </summary>
public static class NagisaNpcRoster
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;
    const string MaterialDir = "Assets/Kuro/NPC/Materials";
    public const string SegmentId = "nagisa";
    public const string ParentName = "Nagisa NPCs";
    public const string NamePrefix = "Nagisa NPC ";
    const float BikeScale = NpcCanonicalConformance.BikeScale;
    const float GroundOffset = 0.065f;
    const float RouteSpacing = 3.0f;
    /// <summary>PROVISIONAL: Taka/Sakura-verified pace scale (chibi riders read fast at true speed).</summary>
    public const float PaceScale = 0.56f;
    public static float StagedSpeed(float authored) => authored * PaceScale;

    sealed class Npc
    {
        public string Name;
        public float Speed;          // m/s authored; PaceScale applied at staging
        public float Lane;           // m from the centreline; negative = oncoming lane
        public float MeetDistance;   // m along the segment from the marina start
        public bool Ascending;       // true = rides the course direction with the player
        public string Frame, Accent; // bike livery hexes
        public string Where;
        public string[] Lines;
    }

    static readonly Npc[] Riders =
    {
        new Npc { Name = "Nalu", Speed = 4.4f, Lane = -1.60f, MeetDistance = 380f,
                  Frame = "19B8C9", Accent = "FFD23F", Where = "marina front, riding back to the pontoons - Nagisa Surf CC",
                  Lines = new[] { "Morning! The water taxi beat me here again.", "Smell that? Sea salt and sunscreen.", "Easy spin along the marina, yeah?" } },
        new Npc { Name = "Makoa", Speed = 4.2f, Lane = -0.95f, Ascending = true, MeetDistance = 1250f,
                  Frame = "1F3F8F", Accent = "FFD23F", Where = "pastel waterfront, riding out - Harbour Blue VC",
                  Lines = new[] { "Coffee at the surf shop after?", "The pastel houses mean the beach is close.", "Ride with me to the promenade." } },
        new Npc { Name = "Leilani", Speed = 4.0f, Lane = -1.00f, Ascending = true, MeetDistance = 2300f,
                  Frame = "FF7F6A", Accent = "F4F6F8", Where = "beach promenade, riding out - Coral Coast Riders",
                  Lines = new[] { "Palm trees and a tailwind. Perfect.", "Don't stare at the tower - watch the road!", "Aloha! Enjoy the beachfront." } },
        new Npc { Name = "Umi", Speed = 4.9f, Lane = -1.62f, MeetDistance = 3350f,
                  Frame = "F4F2EE", Accent = "1F4E79", Where = "past the Grand Shiokaze, riding DOWN the beach - Shirahama Cycle",
                  Lines = new[] { "The pool deck up there has the best view on the coast.", "Coast road's windy today!", "Bike, beach, shaved ice. Repeat." } },
        new Npc { Name = "Kaimana", Speed = 3.8f, Lane = -1.05f, Ascending = true, MeetDistance = 5480f,
                  Frame = "1C2C52", Accent = "FFB42E", Where = "foot of the 9.5 % hill wall - the KOM rival",
                  Lines = new[] { "This hill is mine. Nine and a half percent, all the way.", "Think you can take the Diamond Crest?", "The jungle keeps the heat in. Suffer well." } },
        new Npc { Name = "Mahina", Speed = 3.7f, Lane = -0.95f, Ascending = true, MeetDistance = 6100f,
                  Frame = "2E9D6A", Accent = "FF8FB1", Where = "halfway up the hill wall - Palm Hill Club",
                  Lines = new[] { "Keep breathing. The shade helps.", "Halfway! The view opens soon.", "Tropical climbs hit different." } },
        new Npc { Name = "Keoni", Speed = 5.3f, Lane = -1.60f, MeetDistance = 6900f,
                  Frame = "E0A82E", Accent = "1F6F8B", Where = "upper hill wall, descending - Sunset Ridge CC",
                  Lines = new[] { "Whoo! Downhill to the beach!", "Summit's not far - you've got this.", "Watch the gravel on the bends." } },
        new Npc { Name = "Minami", Speed = 3.9f, Lane = -1.00f, Ascending = true, MeetDistance = 7700f,
                  Frame = "C4A3E0", Accent = "2E9D6A", Where = "ridge, riding up to the KOM - Orchid Cyclo",
                  Lines = new[] { "You can see the whole bay from the top.", "Last ramp before the KOM banner!", "Orchids grow wild up here." } },
        new Npc { Name = "Kaiyo", Speed = 5.0f, Lane = -1.62f, MeetDistance = 9400f,
                  Frame = "3A7BFF", Accent = "F4F6F8", Where = "ridge road after the KOM, riding back - Blue Lagoon Velo",
                  Lines = new[] { "The lagoon's that colour for real.", "Nice climbing!", "Coast road next - flat and fast." } },
        new Npc { Name = "Kailani", Speed = 4.3f, Lane = -0.95f, Ascending = true, MeetDistance = 12450f,
                  Frame = "FFD23F", Accent = "E63946", Where = "east coast road, riding out - Pineapple Wheelers",
                  Lines = new[] { "Pineapple stand at the bridge. Trust me.", "Sea on one side, jungle on the other.", "Keep a steady pace to the bridge." } },
        new Npc { Name = "Kanoa", Speed = 5.1f, Lane = -1.60f, MeetDistance = 13300f,
                  Frame = "0B6E4F", Accent = "FFD23F", Where = "east coast road, riding back - Reef Line CC",
                  Lines = new[] { "Spotted turtles off the rocks!", "The bridge is gorgeous at sunset.", "Tailwind back this way. Lucky me." } },
        new Npc { Name = "Natsu", Speed = 4.5f, Lane = -1.00f, Ascending = true, MeetDistance = 14300f,
                  Frame = "E63946", Accent = "F4F6F8", Where = "coast road toward the bridge - Hibiscus Racing",
                  Lines = new[] { "Summer never ends in Nagisa.", "Sprint to the bridge? Just kidding. Maybe.", "Hibiscus Racing says hi!" } },
        new Npc { Name = "Noelani", Speed = 4.6f, Lane = -1.62f, MeetDistance = 15400f,
                  Frame = "7FD1C9", Accent = "FF7F6A", Where = "bridge approach, riding back into town - Seafoam Club",
                  Lines = new[] { "The city's just across the water.", "Welcome to Nagisa Bay!", "Mind the expansion joints on the bridge." } },
    };

    public static string[] RiderNames => Riders.Select(r => r.Name).ToArray();

    [MenuItem("MapleRide/NPCs/Stage Nagisa Riders")]
    public static void Stage()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        KuroRiderBodies.Reload();
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Find-or-create by EXACT name; prune duplicate roots so re-runs converge.
        GameObject root = null;
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name != ParentName) continue;
            if (root == null) root = go; else Object.DestroyImmediate(go);
        }
        if (root == null) root = new GameObject(ParentName);

        var route = MapleRideKuroSetup.ResampleRoute(MapleRideKuroSetup.GetRoadPoints(), RouteSpacing);
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");
        PurgeOldMaterials();
        int staged = 0;
        foreach (var def in Riders)
        {
            if (!KuroRiderBodies.Has(def.Name))
            {
                Debug.LogError($"[nagisa-npc] {def.Name}: not in any KuroRiders manifest - run " +
                               "design_assets/3d/kuro/nagisa_npc_liveries.py first. Skipped (never Coral's sculpt).");
                continue;
            }
            var go = StageOne(def, root.transform, route);
            if (go == null) continue;
            staged++;
            var cyc = go.GetComponent<NPCCyclist>();
            Debug.Log($"[nagisa-npc] {def.Name}: {def.MeetDistance:F0} m {(def.Ascending ? "WITH" : "ONCOMING")}, " +
                      $"{StagedSpeed(def.Speed):F2} m/s, lane {def.Lane:F2}, progress {cyc.progress}/{cyc.route.Length}, " +
                      $"pos {go.transform.position:F1} - {def.Where}");
        }
        AssetDatabase.SaveAssets();
        EnsureTrafficAndLod(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        int baked = 0;
        foreach (var def in Riders)
            if (NpcPortraitBake.BakeSingle(def.Name, exposureEv: -1.5f)) baked++;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[nagisa-npc] staged {staged}/{Riders.Length} riders on segment '{SegmentId}', {baked} portraits; " +
                  $"'{ParentName}' has {root.transform.childCount} children.");
    }

    static void EnsureTrafficAndLod(GameObject root)
    {
        var riders = root.GetComponentsInChildren<NPCCyclist>(true)
                         .Where(c => c.gameObject.name.StartsWith(NamePrefix)).ToArray();
        var avoid = root.GetComponent<TrafficAvoidance>();
        if (avoid == null) avoid = root.AddComponent<TrafficAvoidance>();
        avoid.riders = riders;
        EditorUtility.SetDirty(avoid);
        int lods = 0;
        foreach (var c in riders)
            if (c.GetComponent<NagisaRiderLod>() == null) { c.gameObject.AddComponent<NagisaRiderLod>(); lods++; }
        Debug.Log($"[nagisa-npc] traffic avoidance over {riders.Length} riders; NagisaRiderLod added to {lods}.");
    }

    static void PurgeOldMaterials()
    {
        var owned = new HashSet<string>(Riders.Select(r => r.Name));
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var file = System.IO.Path.GetFileNameWithoutExtension(path);
            int underscore = file.IndexOf('_');
            if (underscore <= 0) continue;
            if (owned.Contains(file.Substring(0, underscore))) AssetDatabase.DeleteAsset(path);
        }
    }

    static GameObject StageOne(Npc def, Transform parent, Vector3[] route)
    {
        string objName = NamePrefix + def.Name;
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).name == objName) Object.DestroyImmediate(parent.GetChild(i).gameObject);

        var rigAsset = KuroRiderBodies.Rig(def.Name);
        var bikeAsset = LoadPrefab(BikePath);
        if (rigAsset == null) { Debug.LogError($"[nagisa-npc] missing Kuro rig for {def.Name}"); return null; }
        if (bikeAsset == null) { Debug.LogError($"[nagisa-npc] missing bike {BikePath}"); return null; }

        float rigScale = NpcCanonicalConformance.RigScaleForAnyProductionNpc();   // player-matched
        var npc = new GameObject(objName);
        npc.transform.SetParent(parent, false);
        var rider = new GameObject(def.Name + "Rider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * rigScale;

        // MUST be a local child named exactly "Bike" (KuroBikeRig's global fallback grabs the player's).
        var bikeAnchor = new GameObject("Bike");
        bikeAnchor.transform.SetParent(rider.transform, false);
        bikeAnchor.transform.localScale = Vector3.one * BikeScale;
        var bikeModel = Instantiate(bikeAsset, bikeAnchor.transform);
        bikeModel.name = "BikeMesh";
        RepaintBike(def, bikeModel);

        var body = Instantiate(rigAsset, rider.transform);
        body.name = def.Name + "ArmatureAndMesh";
        ApplyKit(def, body);
        KuroRiderBodies.Finish(body, def.Name, MaterialDir);   // NpcCelLitConversion + hair tint

        var rig = rider.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        rig.previewCadenceRpm = 55f;
        NpcCanonicalConformance.Configure(rig);

        var cyclist = npc.AddComponent<NPCCyclist>();
        cyclist.speed = StagedSpeed(def.Speed);
        cyclist.laneOffset = def.Lane;
        cyclist.groundOffset = GroundOffset;
        cyclist.reverse = !def.Ascending;
        cyclist.segmentId = SegmentId;
        if (!cyclist.RebuildFromGraph())
        {
            Debug.LogError($"[nagisa-npc] {def.Name}: no '{SegmentId}' segment in the route graph - bake it " +
                           "(RouteGraphBaker) and re-run. Falling back to the resampled Sakura road.");
            cyclist.route = def.Ascending ? route : route.Reverse().ToArray();
            cyclist.routeIncludesOffsets = false;
        }
        int back = Mathf.RoundToInt(def.MeetDistance / NPCCyclist.Spacing);
        int last = cyclist.route.Length - 1;
        cyclist.progress = Mathf.Clamp(def.Ascending ? back : last - back, 0, last);
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
        greeting.riderName = def.Name;
        greeting.useFaceCard = true;
        EditorUtility.SetDirty(greeting);

        var smile = FindChild(npc.transform, "SmileDecal");
        if (smile != null && smile.GetComponent<Renderer>() is Renderer sr) { sr.enabled = false; EditorUtility.SetDirty(sr); }
        else Debug.LogWarning($"[nagisa-npc] {def.Name}: no 'SmileDecal' under the rig.");

        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        SakuraNpcRoster.FreezeSkinnedBounds(npc);
        return npc;
    }

    static void ApplyKit(Npc def, GameObject body)
    {
        var kitPath = KuroRiderBodies.KitPath(def.Name);
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(kitPath);
        if (kit == null) { Debug.LogError($"[nagisa-npc] {def.Name}: no kit at {kitPath}"); return; }
        Material clone = null;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].name.StartsWith("Material_")) continue;
                if (clone == null)
                {
                    clone = new Material(mats[i]) { name = $"{def.Name}_Body" };
                    SetTexture(clone, kit);
                    SaveMaterial(clone);
                }
                mats[i] = clone;
                touched = true;
            }
            if (touched) { renderer.sharedMaterials = mats; EditorUtility.SetDirty(renderer); }
        }
    }

    /// <summary>Livery on CLONES of the shared bike materials (never tint the shared GLB).</summary>
    static void RepaintBike(Npc def, GameObject bike)
    {
        var frame = Hex(def.Frame);
        var accent = Hex(def.Accent);
        var frames = NpcCanonicalConformance.FrameMaterialNames;
        var accents = NpcCanonicalConformance.AccentMaterialNames;
        var clones = new Dictionary<string, Material>();
        foreach (var renderer in bike.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string src = mats[i].name.Replace(" (Instance)", "");
                Color? tint = frames.Contains(src) ? frame : accents.Contains(src) ? accent : (Color?)null;
                if (tint == null) continue;
                if (!clones.TryGetValue(src, out var clone))
                {
                    clone = new Material(mats[i]) { name = $"{def.Name}_{src}" };
                    foreach (var p in ColorProps)
                        if (clone.HasProperty(p)) clone.SetColor(p, p == "baseColorFactor" ? tint.Value.linear : tint.Value);
                    SaveMaterial(clone);
                    clones[src] = clone;
                }
                mats[i] = clone;
                touched = true;
            }
            if (touched) { renderer.sharedMaterials = mats; EditorUtility.SetDirty(renderer); }
        }
    }

    static readonly string[] TextureProps = { "baseColorTexture", "_BaseMap", "_MainTex", "_BaseColorMap" };
    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    static void SetTexture(Material m, Texture2D tex)
    {
        foreach (var p in TextureProps) if (m.HasProperty(p)) m.SetTexture(p, tex);
        foreach (var p in ColorProps) if (m.HasProperty(p)) m.SetColor(p, Color.white);
    }

    static void SaveMaterial(Material m) =>
        AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath($"{MaterialDir}/{m.name}.mat"));

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root) { var f = FindChild(child, name); if (f != null) return f; }
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
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }
}
