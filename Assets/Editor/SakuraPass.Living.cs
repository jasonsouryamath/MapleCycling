using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stages living world elements for Sakura Pass (L1 package).
/// Builds on the W0 ambient-mover kit:
///   1. Local Train (Enoden/Hakone 2-car mountain train) shuttling along the valley flank
///   2. Trestle Viaduct spanning the hillside ravine
///   3. Level Crossing (踏切) with animated barrier gate arm and alternating warning lamps
///   4. Mountain Ropeway Gondola across the valley with rotating station bullwheels and swinging cabins
///   5. Shrine Garden Pond with circling Nishikigoi (ornamental koi)
///   6. Countryside Crows & Black Kites circling and drifting high over the valley
/// </summary>
public static class SakuraPassLiving
{
    private const string LivingRootName = "Living World";
    private const string LivingAssetDir = "Assets/Environment/SakuraPass/Living";
    private const string OutputRenderDir = "reference/good_graphics/sakura_living";
    private static readonly Vector3 PondCenter = new Vector3(-42f, 27.2f, -38f);

    [MenuItem("MapleRide/Environment/Stage Sakura Living World Only", priority = 31)]
    public static void StageLivingOnly()
    {
        string scenePath = "Assets/Scenes/SakuraPass.unity";
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != scenePath)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        var env = GameObject.Find("Sakura Pass Environment");
        if (env == null)
        {
            // Scene not yet staged — run the full Apply first (it calls StageLiving via the hook).
            Debug.Log("[sakura-living] 'Sakura Pass Environment' not found; running SakuraPassEnvironment.Apply first.");
            SakuraPassEnvironment.Apply();
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
            return;
        }

        StageLiving(env.transform);

        var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(active);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
        Debug.Log($"[sakura-living] Staged and saved '{active.path}'.");
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
    }

    [MenuItem("MapleRide/Environment/Capture Sakura Living Shots", priority = 32)]
    public static void CaptureLivingShots()
    {
        string scenePath = "Assets/Scenes/SakuraPass.unity";
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != scenePath)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        var env = GameObject.Find("Sakura Pass Environment");
        if (env == null)
        {
            Debug.Log("[sakura-living] 'Sakura Pass Environment' not found; running SakuraPassEnvironment.Apply first.");
            SakuraPassEnvironment.Apply();
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
            return;
        }

        // Ensure living elements are present
        if (env.transform.Find(LivingRootName) == null)
        {
            StageLiving(env.transform);
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputRenderDir));
        Directory.CreateDirectory(dir);

        // Frame 1: Train & Viaduct
        RenderShot(dir, "sakura_living_train_viaduct",
            pos: new Vector3(22f, 25f, 32f),
            look: new Vector3(48f, 21.5f, 55f),
            fov: 52f);

        // Frame 2: Level Crossing & Warning Post
        RenderShot(dir, "sakura_living_level_crossing",
            pos: new Vector3(22f, 18.5f, -32f),
            look: new Vector3(31.5f, 16.5f, -22f),
            fov: 55f);

        // Frame 3: Valley Ropeway Gondola & Pylon
        RenderShot(dir, "sakura_living_ropeway_gondola",
            pos: new Vector3(78f, 24f, -15f),
            look: new Vector3(128f, 58f, 95f),
            fov: 56f);

        // Frame 4: Shrine Garden Koi Pond
        RenderShot(dir, "sakura_living_shrine_koi",
            pos: new Vector3(PondCenter.x + 3.2f, PondCenter.y + 2.5f, PondCenter.z - 3.2f),
            look: PondCenter,
            fov: 50f);

        // Motion Verification: Capture frame 0, advance movers by 3.5s, capture frame 1
        var mover = env.GetComponentInChildren<AmbientPathMover>(true);
        Vector3 posBefore = Vector3.zero;
        if (mover != null && mover.cars != null && mover.cars.Length > 0 && mover.cars[0] != null)
        {
            posBefore = mover.cars[0].position;
        }

        RenderShot(dir, "sakura_living_motion_frame0",
            pos: new Vector3(24f, 24f, 40f),
            look: new Vector3(48f, 21.5f, 55f),
            fov: 52f);

        // Advance all movers in living world
        foreach (var m in env.GetComponentsInChildren<AmbientPathMover>(true)) m.Step(3.5f);
        foreach (var r in env.GetComponentsInChildren<AmbientRotor>(true)) r.Step(3.5f);
        foreach (var s in env.GetComponentsInChildren<AmbientSway>(true)) s.Step(3.5f);
        foreach (var f in env.GetComponentsInChildren<AmbientFlock>(true)) f.Step(3.5f);

        Vector3 posAfter = Vector3.zero;
        if (mover != null && mover.cars != null && mover.cars.Length > 0 && mover.cars[0] != null)
        {
            posAfter = mover.cars[0].position;
        }

        RenderShot(dir, "sakura_living_motion_frame1",
            pos: new Vector3(24f, 24f, 40f),
            look: new Vector3(48f, 21.5f, 55f),
            fov: 52f);

        float moved = Vector3.Distance(posBefore, posAfter);
        Debug.Log($"[sakura-living] Train motion test: moved {moved:0.00} m across 3.5s step.");
        bool motionOk = moved > 5.0f;

        Debug.Log($"[sakura-living] Capture complete. Results saved in '{dir}'. Motion verified: {motionOk}");
        if (Application.isBatchMode)
        {
            UnityEditor.EditorApplication.Exit(motionOk ? 0 : 1);
        }
    }

    private static void RenderShot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~LivingDiagCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();

        string outPath = Path.Combine(dir, name + ".png");
        File.WriteAllBytes(outPath, img.EncodeToPNG());

        RenderTexture.active = prev;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(img);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log($"[sakura-living] Wrote capture '{name}.png'");
    }

    public static void StageLiving(Transform envRoot)
    {
        if (envRoot == null) return;

        var livingGroup = AmbientMoverStaging.Converge(envRoot, LivingRootName);

        StageLocalTrainAndViaduct(livingGroup.transform);
        StageRopewayGondola(livingGroup.transform);
        StageShrineKoiPond(livingGroup.transform);
        StageFlocks(livingGroup.transform);

        Debug.Log("[sakura-living] Living world elements staged successfully into Sakura Pass.");
    }

    private static GameObject LoadLivingPrefab(string filename)
    {
        string path = $"{LivingAssetDir}/{filename}";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"[sakura-living] Could not load GLB asset at '{path}'.");
        }
        return prefab;
    }

    private static void StageLocalTrainAndViaduct(Transform parent)
    {
        var trainGroup = AmbientMoverStaging.Converge(parent, "Hillside Train & Viaduct");

        // 1. Hillside Track Polyline (runs along the valley flank, length ~320m)
        Vector3[] rawTrackPoints = new[]
        {
            new Vector3(18f, 12f, -120f),
            new Vector3(25f, 14f, -70f),
            new Vector3(34f, 17f, -15f),  // Level crossing near here
            new Vector3(42f, 19.5f, 25f), // Start of viaduct
            new Vector3(48f, 21.5f, 55f), // Mid viaduct
            new Vector3(54f, 23.5f, 85f), // End of viaduct
            new Vector3(60f, 25f, 135f),
            new Vector3(66f, 26f, 180f)
        };
        Vector3[] smoothTrack = AmbientMoverStaging.BakeCatmullRom(rawTrackPoints, subdivisions: 8);

        // 2. Trestle Viaduct Bents across the ravine (Y ~ 21m, ground drops below)
        var viaductPrefab = LoadLivingPrefab("Sakura_Trestle_Viaduct.glb");
        if (viaductPrefab != null)
        {
            var viaductContainer = new GameObject("Trestle Viaduct");
            viaductContainer.transform.SetParent(trainGroup.transform, false);

            Vector3[] viaductPositions = new[]
            {
                new Vector3(44f, 20.2f, 35f),
                new Vector3(48f, 21.5f, 55f),
                new Vector3(52f, 22.8f, 75f)
            };

            for (int i = 0; i < viaductPositions.Length; i++)
            {
                var bent = UnityEngine.Object.Instantiate(viaductPrefab, viaductContainer.transform);
                bent.name = $"Viaduct_Bent_{i}";
                bent.transform.position = viaductPositions[i];
                bent.transform.rotation = Quaternion.Euler(0f, 18f, 0f);
            }
        }

        // 3. Train Consist: 2-Car Enoden/Hakone Mountain Train
        var trainCarPrefab = LoadLivingPrefab("Sakura_Train_Car.glb");
        Transform car0 = null, car1 = null;

        if (trainCarPrefab != null)
        {
            var consistContainer = new GameObject("Train Consist");
            consistContainer.transform.SetParent(trainGroup.transform, false);

            var carGo0 = UnityEngine.Object.Instantiate(trainCarPrefab, consistContainer.transform);
            carGo0.name = "Train_Car_Lead";
            car0 = carGo0.transform;

            var carGo1 = UnityEngine.Object.Instantiate(trainCarPrefab, consistContainer.transform);
            carGo1.name = "Train_Car_Tail";
            car1 = carGo1.transform;

            var pathMover = consistContainer.AddComponent<AmbientPathMover>();
            pathMover.path = smoothTrack;
            pathMover.cars = new[] { car0, car1 };
            pathMover.carOffsets = new[] { 0f, 12.8f };
            pathMover.carYaw = new[] { 0f, 180f }; // Tail car reversed
            pathMover.speed = 11.5f;              // ~41 km/h cruise
            pathMover.accel = 3.5f;               // Smooth braking and acceleration
            pathMover.mode = AmbientPathMover.PathMode.Shuttle;
            pathMover.dwellSeconds = 7.5f;        // Station dwell
            pathMover.bankFactor = 0.35f;         // Banking into curves
            pathMover.bobHeave = 0.04f;           // Gentle rail heave
            pathMover.bobTilt = 0.3f;             // Gentle bogie tilt
            pathMover.seed = 701;
            pathMover.Bake();
            pathMover.SetHead(50f);
        }

        // 4. Level Crossing (踏切) near the approach road
        var crossingPrefab = LoadLivingPrefab("Sakura_Level_Crossing.glb");
        if (crossingPrefab != null)
        {
            var crossingGo = UnityEngine.Object.Instantiate(crossingPrefab, trainGroup.transform);
            crossingGo.name = "Railway Level Crossing";
            crossingGo.transform.position = new Vector3(31.5f, 16.2f, -22f);
            crossingGo.transform.rotation = Quaternion.Euler(0f, 115f, 0f);

            var crossingCtrl = crossingGo.AddComponent<SakuraLivingLevelCrossing>();
            crossingCtrl.barrierArm = crossingGo.transform.Find("Barrier_Arm");
            crossingCtrl.trainHead = car0;
            crossingCtrl.crossingPosition = crossingGo.transform.position;
            crossingCtrl.triggerDistance = 55f;

            var leftHood = crossingGo.transform.Find("Light_Hood_-0.45");
            var rightHood = crossingGo.transform.Find("Light_Hood_0.45");
            if (leftHood != null) crossingCtrl.redLightLeft = leftHood.GetComponent<Renderer>();
            if (rightHood != null) crossingCtrl.redLightRight = rightHood.GetComponent<Renderer>();
        }
    }

    private static void StageRopewayGondola(Transform parent)
    {
        var gondolaGroup = AmbientMoverStaging.Converge(parent, "Valley Ropeway Gondola");

        var stationPrefab = LoadLivingPrefab("Sakura_Gondola_Station.glb");
        var pylonPrefab = LoadLivingPrefab("Sakura_Gondola_Pylon.glb");
        var cabinPrefab = LoadLivingPrefab("Sakura_Gondola_Cabin.glb");

        // Stations: Valley Terminal & Ridge Summit Terminal
        Vector3 valleyPos = new Vector3(72f, 10f, -45f);
        Vector3 summitPos = new Vector3(155f, 88f, 170f);

        if (stationPrefab != null)
        {
            var valleyStation = UnityEngine.Object.Instantiate(stationPrefab, gondolaGroup.transform);
            valleyStation.name = "Ropeway_Valley_Station";
            valleyStation.transform.position = valleyPos;
            valleyStation.transform.rotation = Quaternion.Euler(0f, 22f, 0f);

            // Spin valley station bullwheel
            var bwValley = valleyStation.transform.Find("Bullwheel_Rim");
            if (bwValley != null)
            {
                var rotor = bwValley.gameObject.AddComponent<AmbientRotor>();
                rotor.axis = Vector3.up;
                rotor.degreesPerSecond = 36f;
                rotor.accel = 5f;
                rotor.Bake();
            }

            var summitStation = UnityEngine.Object.Instantiate(stationPrefab, gondolaGroup.transform);
            summitStation.name = "Ropeway_Summit_Station";
            summitStation.transform.position = summitPos;
            summitStation.transform.rotation = Quaternion.Euler(0f, 202f, 0f);

            // Spin summit station bullwheel
            var bwSummit = summitStation.transform.Find("Bullwheel_Rim");
            if (bwSummit != null)
            {
                var rotor = bwSummit.gameObject.AddComponent<AmbientRotor>();
                rotor.axis = Vector3.up;
                rotor.degreesPerSecond = 36f;
                rotor.accel = 5f;
                rotor.Bake();
            }
        }

        // Pylons in the valley
        if (pylonPrefab != null)
        {
            var pylon1 = UnityEngine.Object.Instantiate(pylonPrefab, gondolaGroup.transform);
            pylon1.name = "Ropeway_Pylon_1";
            pylon1.transform.position = new Vector3(98f, 32f, 20f);
            pylon1.transform.rotation = Quaternion.Euler(0f, 22f, 0f);

            var pylon2 = UnityEngine.Object.Instantiate(pylonPrefab, gondolaGroup.transform);
            pylon2.name = "Ropeway_Pylon_2";
            pylon2.transform.position = new Vector3(128f, 60f, 95f);
            pylon2.transform.rotation = Quaternion.Euler(0f, 22f, 0f);
        }

        // Cable path & Gondola Cabins
        Vector3[] cablePoints = new[]
        {
            valleyPos + new Vector3(0f, 3.6f, 0f),
            new Vector3(98f, 32f + 16f, 20f),
            new Vector3(128f, 60f + 16f, 95f),
            summitPos + new Vector3(0f, 3.6f, 0f)
        };
        Vector3[] smoothCable = AmbientMoverStaging.BakeCatmullRom(cablePoints, subdivisions: 6);

        if (cabinPrefab != null)
        {
            var cabinContainer = new GameObject("Ropeway Cabins");
            cabinContainer.transform.SetParent(gondolaGroup.transform, false);

            var cabin0 = UnityEngine.Object.Instantiate(cabinPrefab, cabinContainer.transform);
            cabin0.name = "Gondola_Cabin_0";
            var sway0 = cabin0.AddComponent<AmbientSway>();
            sway0.mode = AmbientSway.SwayMode.Pendulum;
            sway0.axis = Vector3.forward;
            sway0.secondaryAxis = Vector3.right;
            sway0.maxAngle = 5.5f;
            sway0.secondaryAngle = 2.5f;
            sway0.frequency = 0.45f;
            sway0.Bake();

            var cabin1 = UnityEngine.Object.Instantiate(cabinPrefab, cabinContainer.transform);
            cabin1.name = "Gondola_Cabin_1";
            var sway1 = cabin1.AddComponent<AmbientSway>();
            sway1.mode = AmbientSway.SwayMode.Pendulum;
            sway1.axis = Vector3.forward;
            sway1.secondaryAxis = Vector3.right;
            sway1.maxAngle = 5.5f;
            sway1.secondaryAngle = 2.5f;
            sway1.frequency = 0.45f;
            sway1.Bake();

            float totalCableLen = AmbientMoverStaging.GetPolylineLength(smoothCable);
            var mover = cabinContainer.AddComponent<AmbientPathMover>();
            mover.path = smoothCable;
            mover.cars = new[] { cabin0.transform, cabin1.transform };
            mover.carOffsets = new[] { 0f, totalCableLen * 0.5f };
            mover.speed = 4.8f;
            mover.accel = 1.5f;
            mover.mode = AmbientPathMover.PathMode.Loop;
            mover.seed = 801;
            mover.Bake();
            mover.SetHead(40f);
        }
    }

    private static void StageShrineKoiPond(Transform parent)
    {
        var pondGroup = AmbientMoverStaging.Converge(parent, "Shrine Garden Koi Pond");

        // Placed in the scenic garden alcove near the Torii gate
        Vector3 pondCenter = new Vector3(-42f, 27.2f, -38f);
        pondGroup.transform.position = pondCenter;

        var koiPrefab = LoadLivingPrefab("Sakura_Koi.glb");
        if (koiPrefab == null) return;

        int koiCount = 6;
        var members = new Transform[koiCount];
        for (int i = 0; i < koiCount; i++)
        {
            var koi = UnityEngine.Object.Instantiate(koiPrefab, pondGroup.transform);
            koi.name = $"Koi_{i}";
            members[i] = koi.transform;
        }

        var flock = pondGroup.AddComponent<AmbientFlock>();
        flock.members = members;
        flock.mode = AmbientFlock.FlockMode.Circling;
        flock.radius = 3.2f;
        flock.radiusVariance = 0.8f;
        flock.heightVariance = 0.12f;
        flock.speed = 1.3f;
        flock.flapFrequency = 1.6f; // Gentle caudal wag
        flock.flapBobHeave = 0.04f;
        flock.flapBobTilt = 2.0f;
        flock.seed = 901;
        flock.Bake();
    }

    private static void StageFlocks(Transform parent)
    {
        var birdGroup = AmbientMoverStaging.Converge(parent, "Valley Birds");

        var crowPrefab = LoadLivingPrefab("Sakura_Crow.glb");
        if (crowPrefab == null) return;

        // 1. Shrine Crows circling over cedar grove
        var crowRoot = new GameObject("Shrine Crows");
        crowRoot.transform.SetParent(birdGroup.transform, false);
        crowRoot.transform.position = new Vector3(-45f, 65f, 25f);

        int crowCount = 5;
        var crowMembers = new Transform[crowCount];
        for (int i = 0; i < crowCount; i++)
        {
            var crow = UnityEngine.Object.Instantiate(crowPrefab, crowRoot.transform);
            crow.name = $"Crow_{i}";
            crowMembers[i] = crow.transform;
        }

        var crowFlock = crowRoot.AddComponent<AmbientFlock>();
        crowFlock.members = crowMembers;
        crowFlock.mode = AmbientFlock.FlockMode.Circling;
        crowFlock.radius = 32f;
        crowFlock.radiusVariance = 12f;
        crowFlock.heightVariance = 6f;
        crowFlock.speed = 7.5f;
        crowFlock.flapFrequency = 2.6f;
        crowFlock.seed = 950;
        crowFlock.Bake();

        // 2. Black Kites (トビ) soaring in thermals over central valley
        var kiteRoot = new GameObject("Valley Black Kites");
        kiteRoot.transform.SetParent(birdGroup.transform, false);
        kiteRoot.transform.position = new Vector3(25f, 110f, 55f);

        int kiteCount = 3;
        var kiteMembers = new Transform[kiteCount];
        for (int i = 0; i < kiteCount; i++)
        {
            var kite = UnityEngine.Object.Instantiate(crowPrefab, kiteRoot.transform);
            kite.name = $"Kite_{i}";
            kite.transform.localScale = Vector3.one * 1.45f; // Larger raptor wingspan
            kiteMembers[i] = kite.transform;
        }

        var kiteFlock = kiteRoot.AddComponent<AmbientFlock>();
        kiteFlock.members = kiteMembers;
        kiteFlock.mode = AmbientFlock.FlockMode.Drifting;
        kiteFlock.volumeSize = new Vector3(150f, 25f, 150f);
        kiteFlock.speed = 9.2f;
        kiteFlock.flapFrequency = 1.1f; // Slow majestic gliding
        kiteFlock.flapBobHeave = 0.08f;
        kiteFlock.flapBobTilt = 2.2f;
        kiteFlock.seed = 960;
        kiteFlock.Bake();
    }
}
