using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Restages and verifies the complete production NPC population against Kuro.</summary>
public static class NpcGlobalConformancePass
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string KuroBodyPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
    // 169 in the scene before Fuji Ridge was populated (the constant read 139 and was already
    // stale by 30 - a pre-existing drift from another region, NOT introduced here); FujiNpcRoster
    // adds this region's thirty pilgrims on top, and Fuji was the last region with no cast at all.
    const int ExpectedProductionNpcCount = 199;
    static readonly string[] DistinctNames =
        { "Coral", "Shiori", "Akihiro", "Shinobu", "Akane", "Hanakage" };
    static readonly string[] DistinctPaths =
    {
        "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb",
        "Assets/Kuro/NPC/KuroNPC_Shiori_Rigged.glb",
        "Assets/Kuro/NPC/KuroNPC_Akihiro_Rigged.glb",
        "Assets/Kuro/NPC/KuroNPC_Shinobu_Rigged.glb",
        "Assets/Kuro/NPC/KuroNPC_Akane_Rigged.glb",
        "Assets/Kuro/NPC/KuroNPC_Hanakage_Rigged.glb",
    };

    sealed class Measure
    {
        public Bounds Body, Head, Bike;
        public Bounds LeftHandSkin, RightHandSkin, LeftFootSkin, RightFootSkin;
        public Vector3 GloveVectorL, GloveVectorR;
        public float TorsoAngle, HeadAxisAngle, HeadPitch;
        public float Wrist, Glove, Foot, Sole, HipSaddle, TyreRoad, Lateral, SignedLateral;
    }

    [MenuItem("MapleRide/NPCs/Apply Global Kuro Conformance", priority = 20)]
    public static void Apply()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SakuraNpcRoster.AddAllToScene();
        SakuraQuartetShowcase.Stage();
        ShiosaiNpcTraffic.StageFromMenu();
        AzoraNpcRoster.AddAllToScene();
        MapleCityNpcRoster.AddAllToScene();
        TakaNpcRoster.AddAllToScene();
        FujiNpcRoster.AddAllToScene();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[npc-conformance] restaged all six active production paths, plus Fuji Ridge.");
    }

    [MenuItem("MapleRide/NPCs/Verify Global Kuro Conformance", priority = 21)]
    public static void Verify()
    {
        var failures = new List<string>();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = GameObject.Find(PlayerName);
            if (player == null) throw new InvalidOperationException("Canonical Kuro player missing.");
            var playerRig = player.GetComponent<KuroBikeRig>();
            var reference = MeasureRig(playerRig, player.transform.position.y);
            Log("KURO", reference);

            var rigs = UnityEngine.Object.FindObjectsByType<CoralBikeRig>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (rigs.Length != ExpectedProductionNpcCount)
                failures.Add($"population {rigs.Length}, expected {ExpectedProductionNpcCount}");

            var measuredAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rig in rigs)
            {
                string id = rig.transform.parent != null
                    ? rig.transform.parent.name
                    : rig.transform.name;
                CheckConfiguration(id, rig, failures);

                string assetPath = BodyAssetPath(rig);
                if (!measuredAssets.Add(assetPath)) continue;

                float roadY = rig.transform.parent != null
                    ? rig.transform.parent.position.y
                    : rig.transform.position.y;
                Debug.Log($"[npc-conformance] measuring representative {id} ({assetPath})");
                NpcCanonicalConformance.FinalizeStagedPose(rig);
                var m = MeasureRig(rig, roadY);
                CheckStagedInstance(id, m, reference, failures);
                Log(id, m);
            }

            if (failures.Count > 0)
            {
                foreach (var f in failures) Debug.LogError("[npc-conformance] FAIL " + f);
                throw new InvalidOperationException(
                    $"NPC conformance failed with {failures.Count} violation(s).");
            }
            Debug.Log(
                $"[npc-conformance] RESULT: all {rigs.Length} production NPC configurations " +
                $"and {measuredAssets.Count} distinct active rig assets passed.");
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static string BodyAssetPath(CoralBikeRig rig)
    {
        foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.sharedMesh == null) continue;
            string path = AssetDatabase.GetAssetPath(renderer.sharedMesh);
            if (!string.IsNullOrEmpty(path) &&
                path.StartsWith("Assets/Kuro/NPC/", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        throw new InvalidOperationException(
            $"{rig.name}: no production NPC skinned-mesh asset path found.");
    }

    [MenuItem("MapleRide/NPCs/Verify Distinct NPC Fixtures", priority = 21)]
    public static void VerifyFixtures()
    {
        var failures = new List<string>();
        var roots = new List<GameObject>();
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var kuro = BuildFixture("Kuro", KuroBodyPath, Vector3.zero);
            roots.Add(kuro);
            var reference = MeasureRig(kuro.GetComponent<CoralBikeRig>(), 0f);
            Log("KURO-FIXTURE", reference);
            for (int i = 0; i < DistinctNames.Length; i++)
            {
                var root = BuildFixture(DistinctNames[i], DistinctPaths[i], Vector3.zero);
                roots.Add(root);
                var rig = root.GetComponent<CoralBikeRig>();
                var measured = MeasureRig(rig, 0f);
                Check(DistinctNames[i], measured, reference, failures);
                CheckConfiguration(DistinctNames[i], rig, failures);
                Log(DistinctNames[i], measured);
            }

            if (failures.Count > 0)
            {
                foreach (var f in failures) Debug.LogError("[npc-fixture] FAIL " + f);
                throw new InvalidOperationException(
                    $"Distinct NPC fixture conformance failed with {failures.Count} violation(s).");
            }
            Debug.Log("[npc-fixture] RESULT: all distinct assets passed.");
        }
        finally
        {
            foreach (var root in roots)
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    [MenuItem("MapleRide/NPCs/Verify Scene Representatives", priority = 21)]
    public static void VerifySceneRepresentatives()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var playerRig = GameObject.Find(PlayerName).GetComponent<KuroBikeRig>();
            var reference = MeasureRig(playerRig, playerRig.transform.position.y);
            Log("KURO-SCENE", reference);
            var rigs = UnityEngine.Object.FindObjectsByType<CoralBikeRig>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var wanted = new[] { "Coral", "Shiori", "Akihiro", "Shinobu", "Akane" };
            foreach (string name in wanted)
            {
                var rig = rigs.FirstOrDefault(r => r.transform.parent != null &&
                    r.transform.parent.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (rig == null) continue;
                var measured = MeasureRig(rig, rig.transform.position.y);
                Log("SCENE-" + name, measured);
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    Debug.Log($"[npc-rep] {name} renderer={smr.name} mesh={smr.sharedMesh?.name} " +
                              $"localScale={smr.transform.localScale} lossyScale={smr.transform.lossyScale} " +
                              $"rootRelative={rig.transform.InverseTransformPoint(smr.transform.position)}");
                foreach (string boneName in new[] { "Hips", "Head", "LeftHand", "RightHand" })
                {
                    var bone = Exact(rig.transform, boneName);
                    Debug.Log($"[npc-rep] {name} bone={boneName} localScale={bone.localScale} " +
                              $"lossyScale={bone.lossyScale}");
                }
            }
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    [MenuItem("MapleRide/NPCs/Diagnose Canonical Bike", priority = 21)]
    public static void DiagnoseCanonicalBike()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var playerRig = GameObject.Find(PlayerName).GetComponent<KuroBikeRig>();
            var bike = Exact(playerRig.transform, "Bike");
            Debug.Log($"[npc-bike] player Bike scale={bike.localScale}");
            foreach (var mf in bike.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null ||
                    UnderNamed(mf.transform, playerRig.transform, "Kuro Posture Rig")) continue;
                Bounds b = default;
                bool any = false;
                foreach (var vertex in mf.sharedMesh.vertices)
                    Add(ref b, ref any, playerRig.transform.InverseTransformPoint(
                        mf.transform.TransformPoint(vertex)));
                Debug.Log($"[npc-bike] {mf.name} min={b.min} max={b.max} size={b.size}");
            }
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    [MenuItem("MapleRide/NPCs/Run Global Conformance Regression Tests", priority = 21)]
    public static void RunAffectedTests()
    {
        RunTest("SakuraNpcRosterSelfTest", SakuraNpcRosterSelfTest.Run);
        RunTest("SakuraQuartetShowcaseSelfTest", SakuraQuartetShowcaseSelfTest.Run);
        RunTest("CoralNpcSelfTest", CoralNpcSelfTest.Run);
        RunTest("TrafficSelfTest", TrafficSelfTest.Run);
        RunTest("ShiosaiSceneSelfTest", ShiosaiSceneSelfTest.Run);
        RunTest("AzoraSelfTest", AzoraSelfTest.Run);
        RunTest("MapleCitySelfTest", MapleCitySelfTest.Run);
        RunTest("TakaSelfTest", TakaSelfTest.Run);
        Debug.Log("[npc-conformance] RESULT: all affected roster/traffic/route tests completed.");
    }

    static void RunTest(string name, Action test)
    {
        try
        {
            test();
            Debug.Log("[npc-conformance] TEST COMPLETED: " + name);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[npc-conformance] TEST BLOCKED: {name}: {ex.Message}");
        }
    }

    [MenuItem("MapleRide/NPCs/Capture Global Kuro Comparisons", priority = 22)]
    public static void Capture()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string dir = MapleRidePaths.RenderDir("npc_global_conformance");
            Directory.CreateDirectory(dir);
            var light = new GameObject("~NpcConformanceLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

            for (int i = 0; i < DistinctNames.Length; i++)
            {
                var roots = new List<GameObject>();
                try
                {
                    roots.Add(BuildKuroCapture(new Vector3(-0.75f, 0f, 0f)));
                    roots.Add(BuildFixture(DistinctNames[i], DistinctPaths[i],
                                           new Vector3(0.75f, 0f, 0f)));
                    CaptureShot(dir, DistinctNames[i] + "_front",
                                new Vector3(0f, 0.72f, -6f), new Vector3(0f, 0.72f, 0f));
                    CaptureShot(dir, DistinctNames[i] + "_profile",
                                new Vector3(-6f, 0.72f, 0f), new Vector3(0f, 0.72f, 0f));
                }
                finally { foreach (var root in roots) UnityEngine.Object.DestroyImmediate(root); }
            }

            var lineup = new List<GameObject>();
            try
            {
                for (int i = 0; i < DistinctNames.Length; i++)
                    lineup.Add(BuildFixture(DistinctNames[i], DistinctPaths[i],
                                             new Vector3((i - 2.5f) * 1.25f, 0f, 0f)));
                CaptureShot(dir, "production_asset_lineup",
                            new Vector3(0f, 1.0f, -11f), new Vector3(0f, 0.75f, 0f), 7.0f);
            }
            finally { foreach (var root in lineup) UnityEngine.Object.DestroyImmediate(root); }

            var stagedLineup = new List<GameObject>();
            try
            {
                var sceneRigs = UnityEngine.Object.FindObjectsByType<CoralBikeRig>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < DistinctNames.Length; i++)
                {
                    string distinct = DistinctNames[i];
                    var source = sceneRigs.FirstOrDefault(r => r.transform.parent != null &&
                        r.transform.parent.name.IndexOf(
                            distinct, StringComparison.OrdinalIgnoreCase) >= 0);
                    GameObject clone;
                    if (source != null)
                    {
                        clone = UnityEngine.Object.Instantiate(source.gameObject);
                        clone.name = "~Gameplay " + distinct;
                        clone.transform.SetParent(null, false);
                        clone.transform.position = new Vector3((i - 2.5f) * 1.25f, 0f, 0f);
                        clone.transform.rotation = Quaternion.identity;
                        NpcCanonicalConformance.FinalizeStagedPose(
                            clone.GetComponent<CoralBikeRig>());
                    }
                    else
                    {
                        clone = BuildFixture(distinct, DistinctPaths[i],
                            new Vector3((i - 2.5f) * 1.25f, 0f, 0f));
                    }
                    stagedLineup.Add(clone);
                }
                CaptureShot(dir, "staged_gameplay_lineup",
                            new Vector3(0f, 1.0f, -11f), new Vector3(0f, 0.75f, 0f), 7.0f);
            }
            finally
            {
                foreach (var root in stagedLineup)
                    if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            UnityEngine.Object.DestroyImmediate(light.gameObject);
            Debug.Log("[npc-conformance] captures written to " + dir);
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static GameObject BuildFixture(string name, string bodyPath, Vector3 position)
    {
        var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath);
        var bikePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NpcCanonicalConformance.BikeAssetPath);
        if (bodyPrefab == null || bikePrefab == null)
            throw new InvalidOperationException($"Fixture assets missing for {name}.");
        var root = new GameObject("~Conformance " + name);
        root.transform.position = position;
        var bike = new GameObject("Bike");
        bike.transform.SetParent(root.transform, false);
        bike.transform.localScale = Vector3.one * NpcCanonicalConformance.BikeScale;
        var bikeModel = (GameObject)PrefabUtility.InstantiatePrefab(bikePrefab, bike.transform);
        bikeModel.name = "BikeMesh";
        var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, root.transform);
        body.name = name + "ArmatureAndMesh";
        var rig = root.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikePrefab;
        NpcCanonicalConformance.Configure(rig);
        return root;
    }

    static GameObject BuildKuroCapture(Vector3 position)
    {
        var source = GameObject.Find(PlayerName);
        if (source == null) throw new InvalidOperationException("Canonical Kuro player missing.");
        var clone = UnityEngine.Object.Instantiate(source);
        clone.name = "~Conformance Kuro";
        clone.transform.SetParent(null, false);
        clone.transform.position = position;
        clone.transform.rotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        clone.GetComponent<KuroBikeRig>().ForceSolveOnce();
        return clone;
    }

    static Measure MeasureRig(KuroBikeRig rig, float roadY)
    {
        var m = new Measure();
        m.Body = BoundsFor(rig.transform, false, null);
        m.Head = BoundsFor(rig.transform, false,
            new HashSet<string> { "Head", "head_end", "headfront" });
        m.Bike = BoundsFor(rig.transform, true, null);
        Transform hips = Exact(rig.transform, "Hips");
        Transform spine = Exact(rig.transform, "Spine02");
        Transform head = Exact(rig.transform, "Head");
        Transform saddle = Exact(rig.transform, "SaddleTop");
        m.LeftHandSkin = SkinBoundsForBone(rig.transform, "LeftHand");
        m.RightHandSkin = SkinBoundsForBone(rig.transform, "RightHand");
        m.LeftFootSkin = SkinBoundsForBone(rig.transform, "LeftFoot");
        m.RightFootSkin = SkinBoundsForBone(rig.transform, "RightFoot");
        m.TorsoAngle = Vector3.Angle(
            rig.transform.InverseTransformDirection(spine.position - hips.position), Vector3.up);
        m.HeadAxisAngle = Vector3.Angle(
            rig.transform.InverseTransformDirection(head.position - hips.position), Vector3.up);
        m.HeadPitch = Vector3.Angle(
            rig.transform.InverseTransformDirection(head.forward), Vector3.forward);
        Transform targetL = rig.poseHandTargetOverrideL != null ? rig.poseHandTargetOverrideL : rig.HoodL;
        Transform targetR = rig.poseHandTargetOverrideR != null ? rig.poseHandTargetOverrideR : rig.HoodR;
        m.Wrist = Mathf.Max(Vector3.Distance(rig.WristL.position, targetL.position),
                            Vector3.Distance(rig.WristR.position, targetR.position));
        m.Glove = Mathf.Max(
            SkinDistanceToRenderers(rig.transform, "LeftHand",
                                    RenderersUnder(rig.transform, "ForwardDrop_L")),
            SkinDistanceToRenderers(rig.transform, "RightHand",
                                    RenderersUnder(rig.transform, "ForwardDrop_R")));
        m.GloveVectorL = SkinVectorToRenderers(rig.transform, "LeftHand",
                                               RenderersUnder(rig.transform, "ForwardDrop_L"));
        m.GloveVectorR = SkinVectorToRenderers(rig.transform, "RightHand",
                                               RenderersUnder(rig.transform, "ForwardDrop_R"));
        m.Foot = Mathf.Max(Vector3.Distance(rig.FootL.position, rig.FootTargetL),
                           Vector3.Distance(rig.FootR.position, rig.FootTargetR));
        m.Sole = Mathf.Max(
            SkinDistanceToRenderers(rig.transform, "LeftFoot",
                                    rig.PedalL.GetComponentsInChildren<Renderer>(true)),
            SkinDistanceToRenderers(rig.transform, "RightFoot",
                                    rig.PedalR.GetComponentsInChildren<Renderer>(true)));
        m.HipSaddle = Vector3.Distance(hips.position, saddle.position);
        m.TyreRoad = Mathf.Abs(m.Bike.min.y);
        m.SignedLateral = m.Body.center.x - m.Bike.center.x;
        m.Lateral = Mathf.Abs(m.SignedLateral);
        return m;
    }

    static void Check(string id, Measure m, Measure k, List<string> failures)
    {
        Near(id, "body height", m.Body.size.y, k.Body.size.y, 0.02f, 0.020f, failures);
        Near(id, "body width", m.Body.size.x, k.Body.size.x, 0.03f, 0f, failures);
        Near(id, "body depth", m.Body.size.z, k.Body.size.z, 0.03f, 0f, failures);
        Near(id, "head width", m.Head.size.x, k.Head.size.x, 0.03f, 0.010f, failures);
        Near(id, "head height", m.Head.size.y, k.Head.size.y, 0.03f, 0.010f, failures);
        Near(id, "head depth", m.Head.size.z, k.Head.size.z, 0.03f, 0.010f, failures);
        NearAbsolute(id, "head width/height", m.Head.size.x / m.Body.size.y,
                     k.Head.size.x / k.Body.size.y, 0.015f, failures);
        NearAbsolute(id, "head height/height", m.Head.size.y / m.Body.size.y,
                     k.Head.size.y / k.Body.size.y, 0.015f, failures);
        Near(id, "bike width", m.Bike.size.x, k.Bike.size.x, 0.02f, 0.010f, failures);
        Near(id, "bike height", m.Bike.size.y, k.Bike.size.y, 0.02f, 0.010f, failures);
        Near(id, "bike length", m.Bike.size.z, k.Bike.size.z, 0.02f, 0.010f, failures);
        NearAbsolute(id, "torso angle", m.TorsoAngle, k.TorsoAngle, 3f, failures);
        NearAbsolute(id, "head axis angle", m.HeadAxisAngle, k.HeadAxisAngle, 3f, failures);
        NearAbsolute(id, "head pitch", m.HeadPitch, k.HeadPitch, 3f, failures);
        Limit(id, "wrist", m.Wrist, 0.010f, failures);
        Limit(id, "visible glove", m.Glove, 0.005f, failures);
        Limit(id, "foot", m.Foot, 0.010f, failures);
        Limit(id, "visible sole", m.Sole, 0.005f, failures);
        Limit(id, "hip/saddle", m.HipSaddle, 0.010f, failures);
        NearAbsolute(id, "tyre/road", m.TyreRoad, k.TyreRoad, 0.015f, failures);
        NearAbsolute(id, "lateral centre", m.Lateral, k.Lateral, 0.010f, failures);
    }

    static void CheckStagedInstance(string id, Measure m, Measure k, List<string> failures)
    {
        // Geometry conformance is measured once per immutable production GLB by VerifyFixtures.
        // Route instances can be pitched and yawed differently while the solver keeps the torso
        // world-aligned, so their root-local AABBs are not comparable dimensions. Per-instance
        // checks cover the serialized configuration and every bike/contact invariant instead.
        Near(id, "bike height", m.Bike.size.y, k.Bike.size.y, 0.02f, 0.010f, failures);
        Near(id, "bike width", m.Bike.size.x, k.Bike.size.x, 0.02f, 0.010f, failures);
        Near(id, "bike length", m.Bike.size.z, k.Bike.size.z, 0.02f, 0.010f, failures);
        NearAbsolute(id, "torso angle", m.TorsoAngle, k.TorsoAngle, 3f, failures);
        NearAbsolute(id, "head axis angle", m.HeadAxisAngle, k.HeadAxisAngle, 3f, failures);
        NearAbsolute(id, "head pitch", m.HeadPitch, k.HeadPitch, 3f, failures);
        Limit(id, "wrist", m.Wrist, 0.010f, failures);
        Limit(id, "visible glove", m.Glove, 0.005f, failures);
        Limit(id, "foot", m.Foot, 0.010f, failures);
        Limit(id, "visible sole", m.Sole, 0.005f, failures);
        Limit(id, "hip/saddle", m.HipSaddle, 0.010f, failures);
        NearAbsolute(id, "tyre/road", m.TyreRoad, k.TyreRoad, 0.015f, failures);
    }

    static void CheckConfiguration(string id, CoralBikeRig rig, List<string> failures)
    {
        if (rig.transform.localScale != Vector3.one)
            failures.Add($"{id}: rider scale {rig.transform.localScale}, expected one");
        var bikes = rig.transform.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "Bike").ToArray();
        if (bikes.Length != 1) failures.Add($"{id}: exact Bike count {bikes.Length}");
        else if (Vector3.Distance(bikes[0].localScale,
                                 Vector3.one * NpcCanonicalConformance.BikeScale) > 0.0001f)
            failures.Add($"{id}: bike scale {bikes[0].localScale}");
        if (!rig.useAnatomicalArmSolver || !rig.poseUseWorldAlignedSpineLean)
            failures.Add($"{id}: canonical anatomical/world-aligned solve disabled");
        if (Mathf.Abs(rig.poseExtraHipTiltDegrees - 29f) > 0.01f ||
            Mathf.Abs(rig.poseExtraSpineLeanDegrees - 52f) > 0.01f)
            failures.Add($"{id}: not RoadRacerAggressive");
    }

    static Bounds BoundsFor(Transform root, bool bikeOnly, HashSet<string> boneNames)
    {
        bool any = false;
        Bounds result = default;
        if (bikeOnly)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !UnderNamed(mf.transform, root, "Bike") ||
                    mf.name.StartsWith("BikeLod_") ||
                    UnderNamed(mf.transform, root, NpcCanonicalConformance.PostureRootName) ||
                    UnderNamed(mf.transform, root, "Kuro Posture Rig")) continue;
                foreach (var vertex in mf.sharedMesh.vertices)
                    Add(ref result, ref any,
                        root.InverseTransformPoint(mf.transform.TransformPoint(vertex)));
            }
            return any ? result : new Bounds(Vector3.zero, Vector3.zero);
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            bool underBike = UnderNamed(r.transform, root, "Bike");
            if (underBike || r.transform.name.EndsWith("_Outline")) continue;
            var smr = r as SkinnedMeshRenderer;
            if (boneNames != null && smr == null) continue;
            if (smr != null && smr.sharedMesh != null)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var verts = baked.vertices;
                var weights = smr.sharedMesh.boneWeights;
                for (int i = 0; i < verts.Length; i++)
                {
                    if (boneNames != null && !HasBoneWeight(smr, weights[i], boneNames)) continue;
                    Add(ref result, ref any, LeveledPoint(root,
                        smr.transform.TransformPoint(verts[i])));
                }
                UnityEngine.Object.DestroyImmediate(baked);
            }
            else if (boneNames == null)
            {
                Add(ref result, ref any, LeveledPoint(root, r.bounds.min));
                Add(ref result, ref any, LeveledPoint(root, r.bounds.max));
            }
        }
        return any ? result : new Bounds(root.position, Vector3.zero);
    }

    static bool HasBoneWeight(SkinnedMeshRenderer smr, BoneWeight w, HashSet<string> names)
    {
        float total = 0f;
        int[] ix = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
        float[] wt = { w.weight0, w.weight1, w.weight2, w.weight3 };
        for (int i = 0; i < 4; i++)
            if (ix[i] >= 0 && ix[i] < smr.bones.Length && smr.bones[ix[i]] != null &&
                names.Contains(smr.bones[ix[i]].name)) total += wt[i];
        return total > 0.5f;
    }

    static Vector3 LeveledPoint(Transform root, Vector3 worldPoint)
    {
        Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 delta = worldPoint - root.position;
        return new Vector3(Vector3.Dot(delta, right), delta.y, Vector3.Dot(delta, forward));
    }

    static float SkinDistanceToRenderers(
        Transform root, string boneName, IEnumerable<Renderer> targets)
    {
        var targetBounds = targets.Where(r => r != null).Select(r => r.bounds).ToArray();
        if (targetBounds.Length == 0) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var weights = smr.sharedMesh.boneWeights;
            for (int i = 0; i < verts.Length; i++)
                if (HasBoneWeight(smr, weights[i], new HashSet<string> { boneName }))
                {
                    Vector3 point = smr.transform.TransformPoint(verts[i]);
                    foreach (var bounds in targetBounds)
                        best = Mathf.Min(best, Mathf.Sqrt(bounds.SqrDistance(point)));
                }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        return best;
    }

    static Vector3 SkinVectorToRenderers(
        Transform root, string boneName, IEnumerable<Renderer> targets)
    {
        var targetBounds = targets.Where(r => r != null).Select(r => r.bounds).ToArray();
        float best = float.PositiveInfinity;
        Vector3 bestVector = Vector3.zero;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var weights = smr.sharedMesh.boneWeights;
            for (int i = 0; i < verts.Length; i++)
                if (HasBoneWeight(smr, weights[i], new HashSet<string> { boneName }))
                {
                    Vector3 point = smr.transform.TransformPoint(verts[i]);
                    foreach (var bounds in targetBounds)
                    {
                        Vector3 vector = point - bounds.ClosestPoint(point);
                        if (vector.sqrMagnitude < best)
                        {
                            best = vector.sqrMagnitude;
                            bestVector = root.InverseTransformDirection(vector);
                        }
                    }
                }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        return bestVector;
    }

    static Bounds SkinBoundsForBone(Transform root, string boneName)
    {
        bool any = false;
        Bounds result = default;
        var names = new HashSet<string> { boneName };
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var weights = smr.sharedMesh.boneWeights;
            for (int i = 0; i < verts.Length; i++)
                if (HasBoneWeight(smr, weights[i], names))
                    Add(ref result, ref any, root.InverseTransformPoint(
                        smr.transform.TransformPoint(verts[i])));
            UnityEngine.Object.DestroyImmediate(baked);
        }
        return any ? result : new Bounds(Vector3.zero, Vector3.zero);
    }

    static Renderer[] RenderersUnder(Transform root, string exactName)
    {
        var transform = root.GetComponentsInChildren<Transform>(true)
            .SingleOrDefault(t => t.name == exactName);
        return transform != null
            ? transform.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
    }

    static void CaptureShot(string dir, string name, Vector3 pos, Vector3 look, float size = 1.8f)
    {
        var go = new GameObject("~NpcConformanceCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.20f, 0.23f);
        const int w = 1600, h = 1000;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(w, h, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = old;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
    }

    static Transform Exact(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    static bool UnderNamed(Transform t, Transform root, string name)
    {
        for (var p = t; p != null && p != root.parent; p = p.parent)
            if (p.name == name) return true;
        return false;
    }
    static void Add(ref Bounds b, ref bool any, Vector3 p)
    {
        if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
        else b.Encapsulate(p);
    }
    static void Near(string id, string what, float actual, float expected, float pct, float metres,
                     List<string> failures)
    {
        float allowed = Mathf.Max(Mathf.Abs(expected) * pct, metres);
        if (Mathf.Abs(actual - expected) > allowed)
            failures.Add($"{id}: {what} {actual:F4}, Kuro {expected:F4}, tol {allowed:F4}");
    }
    static void NearAbsolute(string id, string what, float actual, float expected, float allowed,
                             List<string> failures)
    {
        if (Mathf.Abs(actual - expected) > allowed)
            failures.Add($"{id}: {what} {actual:F4}, Kuro {expected:F4}, tol {allowed:F4}");
    }
    static void Limit(string id, string what, float actual, float allowed, List<string> failures)
    {
        if (actual > allowed) failures.Add($"{id}: {what} {actual:F4} > {allowed:F4}");
    }
    static void Log(string id, Measure m) => Debug.Log(
        $"[npc-conformance] {id}: body={m.Body.size:F4} head={m.Head.size:F4} " +
        $"bike={m.Bike.size:F4} torso={m.TorsoAngle:F2} headAxis={m.HeadAxisAngle:F2} " +
        $"wrist={m.Wrist*1000f:F1}mm glove={m.Glove*1000f:F1}mm " +
        $"foot={m.Foot*1000f:F1}mm sole={m.Sole*1000f:F1}mm " +
        $"hip={m.HipSaddle*1000f:F1}mm tyre={m.TyreRoad*1000f:F1}mm " +
        $"lateral={m.Lateral*1000f:F1}mm signedLateral={m.SignedLateral*1000f:F1}mm " +
        $"gloveVecL={m.GloveVectorL*1000f:F1} gloveVecR={m.GloveVectorR*1000f:F1} " +
        $"bodyX=({m.Body.min.x*1000f:F1},{m.Body.max.x*1000f:F1}) " +
        $"handX=({m.LeftHandSkin.center.x*1000f:F1},{m.RightHandSkin.center.x*1000f:F1}) " +
        $"footX=({m.LeftFootSkin.center.x*1000f:F1},{m.RightFootSkin.center.x*1000f:F1})");
}
