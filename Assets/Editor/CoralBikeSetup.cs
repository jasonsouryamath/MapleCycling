using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CoralBikeSetup
{
    const string CoralPath = "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb";
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;
    const string ScenePath = "Assets/Scenes/CoralBikePreview.unity";
    const string CaptureDir = "../good_graphics/coral_bike";

    /// <summary>Uniform scale applied to the bike under Coral.</summary>
    /// <remarks>
    /// Tuned against KuroBikeRig's actual solver, which drives the *wrist* onto the hood.
    /// With Coral's road-bike lean the wrist residual is 0.003 m at 0.90 and 0.160 m at the
    /// old 1.35, where her hands hung well short of the levers.
    /// </remarks>
    const float BikeScale = NpcCanonicalConformance.BikeScale;

    /// <summary>
    /// Unscaled wheel radius of kuro_bike_colnago.glb. The rider controller needs the
    /// radius of the bike as actually instantiated, so it must track BikeScale.
    /// </summary>
    const float UnscaledWheelRadius = 0.175f;

    [MenuItem("MapleRide/Coral/Build Bike Preview")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var coralAsset = LoadPrefab(CoralPath);
        var bikeAsset = LoadPrefab(BikePath);
        if (coralAsset == null) throw new FileNotFoundException("Coral rig not imported", CoralPath);
        if (bikeAsset == null) throw new FileNotFoundException("Coral bike not imported", BikePath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "CoralBikePreview";

        var sceneRoot = new GameObject("SceneRoot").transform;
        var bikeRoot = new GameObject("BikeRoot").transform;
        bikeRoot.SetParent(sceneRoot, false);
        // KuroBikeRig searches for this stable runtime name when the bicycle is not
        // nested under the rider. Keep the logical BikeRoot name visible above it.
        var bikeRuntime = new GameObject("Bike").transform;
        bikeRuntime.SetParent(bikeRoot, false);
        // 0.9, not 1.35. Coral is a chibi build: short limbs under a very large head, and
        // she inherits Kuro's skeleton, so her arm chain is only ~0.39 m. Measured by
        // running the two-bone IK solve against this bike's own sockets
        // (assets/3d/kuro/fit_coral_to_bike.py) the worst contact residual is
        // 0.0026 m at 0.90, 0.0356 m at 1.00 and 0.1596 m at 1.35 - at 1.35 her hands
        // hang 16 cm short of the brake hoods. This pairing was never exercised before
        // because the old Coral export was 0.024 m tall.
        bikeRuntime.localScale = Vector3.one * BikeScale;

        var bikeModel = Instantiate(bikeAsset, bikeRuntime);
        bikeModel.name = "BikeMesh";

        var riderMount = new GameObject("RiderMount").transform;
        riderMount.SetParent(bikeRoot, false);
        var coralRoot = new GameObject("CoralRoot").transform;
        coralRoot.SetParent(riderMount, false);
        var coral = Instantiate(coralAsset, coralRoot);
        coral.name = "CoralArmatureAndMesh";

        Transform hoodL = Require(bikeModel.transform, "Hood_L");
        Transform hoodR = Require(bikeModel.transform, "Hood_R");
        Transform pedalL = Require(bikeModel.transform, "Pedal_L");
        Transform pedalR = Require(bikeModel.transform, "Pedal_R");
        Transform saddle = Require(bikeModel.transform, "SaddleTop");
        Marker("IK_Hand_L", hoodL);
        Marker("IK_Hand_R", hoodR);
        Marker("IK_Foot_L", pedalL);
        Marker("IK_Foot_R", pedalR);
        Marker("Mount_Hips", saddle);
        Marker("Pole_Elbow_L", riderMount, new Vector3(0.45f, 0.55f, 0.15f));
        Marker("Pole_Elbow_R", riderMount, new Vector3(-0.45f, 0.55f, 0.15f));
        Marker("Pole_Knee_L", riderMount, new Vector3(0.18f, 0.30f, 0.65f));
        Marker("Pole_Knee_R", riderMount, new Vector3(-0.18f, 0.30f, 0.65f));

        var rig = coralRoot.gameObject.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        rig.previewCadenceRpm = 55f;
        NpcCanonicalConformance.Configure(rig);

        // AddComponent invokes OnEnable before the serialized bike settings above exist.
        // Reinitialize after the complete hierarchy is present, then bake one fitted pose
        // into the preview scene so it opens in a useful state outside Play mode.
        rig.SendMessage("Setup", SendMessageOptions.RequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);

        AddLightingAndCamera(sceneRoot, coralRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeTransform = sceneRoot;
        Debug.Log("Built Coral bike preview: " + ScenePath);
    }

    [MenuItem("MapleRide/Coral/Capture Bike Verification")]
    public static void CaptureVerification()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var rig = Object.FindFirstObjectByType<CoralBikeRig>();
        if (rig == null) throw new FileNotFoundException("CoralBikeRig missing; build the preview first.");
        var bikeRoot = GameObject.Find("BikeRoot").transform;
        var pedalL = Require(bikeRoot, "Pedal_L");
        var pedalR = Require(bikeRoot, "Pedal_R");
        var hoodL = Require(bikeRoot, "Hood_L");
        var hoodR = Require(bikeRoot, "Hood_R");
        var handL = Require(rig.transform, "LeftHand");
        var handR = Require(rig.transform, "RightHand");
        var footL = Require(rig.transform, "LeftFoot");
        var footR = Require(rig.transform, "RightFoot");

        // Force the deterministic solver before measuring in batch mode; there is no normal
        // editor repaint/player loop between opening the scene and this execute method.
        rig.SendMessage("Setup", SendMessageOptions.RequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
        Debug.Log($"Coral contact residuals (m): handL={Vector3.Distance(handL.position, hoodL.position):F4}, " +
                  $"handR={Vector3.Distance(handR.position, hoodR.position):F4}, " +
                  $"footL={Vector3.Distance(footL.position, pedalL.position):F4}, " +
                  $"footR={Vector3.Distance(footR.position, pedalR.position):F4}");

        var camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.name == "CoralVerifyCamera");
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, CaptureDir));
        Directory.CreateDirectory(dir);
        var pivot = rig.transform.position + Vector3.up * 0.65f;
        var shots = new[] {
            ("side", new Vector3(2.1f, .45f, 0f)),
            ("front34", new Vector3(1.55f, .55f, 1.55f)),
            ("rear34", new Vector3(-1.55f, .55f, -1.55f))
        };
        foreach (var shot in shots)
        {
            camera.transform.position = pivot + shot.Item2;
            camera.transform.LookAt(pivot);
            Render(camera, Path.Combine(dir, "coral_bike_" + shot.Item1 + ".png"));
        }
        Debug.Log("Captured Coral bike verification to " + dir);
    }

    static GameObject LoadPrefab(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path) ??
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().FirstOrDefault();

    static GameObject Instantiate(GameObject prefab, Transform parent)
    {
        var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (go == null) go = Object.Instantiate(prefab);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    static Transform Require(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = RequireOptional(child, name);
            if (found != null) return found;
        }
        throw new MissingReferenceException($"'{name}' missing below '{root.name}'.");
    }

    static Transform RequireOptional(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = RequireOptional(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static Transform Marker(string name, Transform parent, Vector3? localPosition = null)
    {
        var marker = new GameObject(name).transform;
        marker.SetParent(parent, false);
        marker.localPosition = localPosition ?? Vector3.zero;
        return marker;
    }

    static void AddLightingAndCamera(Transform parent, Transform target)
    {
        var light = new GameObject("KeyLight");
        light.transform.SetParent(parent, false);
        light.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
        var sun = light.AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 2.2f;

        var cameraObject = new GameObject("CoralVerifyCamera");
        cameraObject.transform.SetParent(parent, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 38f; camera.nearClipPlane = .03f;
        camera.transform.position = new Vector3(2.1f, 1.1f, 0f);
        camera.transform.LookAt(target.position + Vector3.up * .65f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.72f, .74f, .78f);
    }

    static void Render(Camera camera, string path)
    {
        const int width = 1200, height = 900;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        camera.targetTexture = rt;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        Object.DestroyImmediate(image);
        RenderTexture.active = previous;
        camera.targetTexture = null;
        Object.DestroyImmediate(rt);
    }
}
