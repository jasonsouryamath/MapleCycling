using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Player-only cockpit-reach fit harness. Swaps ONLY the player's bike model to a
/// candidate GLB (with a moved handlebar cluster), solves the seated pose, logs arm
/// reach/elbow metrics, and captures the five judged angles plus a hands close-up.
///
/// Modes (driven by env vars so one compiled script iterates without edits):
///   FiPlayerCockpitFit.Dump     - read-only: print the player's bike child hierarchy.
///   FiPlayerCockpitFit.Capture  - MR_BIKE=<glb path>, MR_OUT=<subdir>: swap in-memory,
///                                 pose, log metrics, capture. Does NOT save the scene.
///   FiPlayerCockpitFit.Commit   - MR_BIKE=<glb path>: swap, set bikePrefab, SAVE scene.
/// The shared bike GLB is never touched by this script.
/// </summary>
public static class FiPlayerCockpitFit
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    const string Player = "Kuro on Sakura Pass";

    static GameObject OpenAndFindPlayer()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        var p = GameObject.Find(Player);
        if (p == null) Debug.LogError("[cockpit] player not found: " + Player);
        return p;
    }

    static Transform FindChildNamed(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }

    // The bike model is the child (under the "Bike" anchor) that contains a SteerPivot.
    static Transform FindBikeModel(Transform anchor)
    {
        foreach (Transform child in anchor)
            if (child.GetComponentsInChildren<Transform>(true).Any(t => t.name == "SteerPivot"))
                return child;
        return null;
    }

    public static void Dump()
    {
        var p = OpenAndFindPlayer();
        if (p == null) return;
        var anchor = FindChildNamed(p.transform, "Bike");
        Debug.Log("[cockpit] Bike anchor: " + (anchor ? anchor.name : "NULL") +
                  " scale=" + (anchor ? anchor.localScale.ToString("F3") : "-"));
        if (anchor == null) return;
        foreach (Transform child in anchor)
        {
            bool hasBike = child.GetComponentsInChildren<Transform>(true).Any(t => t.name == "SteerPivot");
            Debug.Log($"[cockpit]   child '{child.name}'  hasSteerPivot={hasBike}  " +
                      $"prefab={PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)}  " +
                      $"children={child.childCount}");
        }
        var model = FindBikeModel(anchor);
        if (model != null)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
            string srcPath = src ? AssetDatabase.GetAssetPath(src) : "(not a prefab instance)";
            Debug.Log("[cockpit] bike model = '" + model.name + "'  source=" + srcPath);
        }
        LogArmMetrics(p);
    }

    static GameObject LoadBike(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) go = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
        if (go == null) go = AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().FirstOrDefault();
        return go;
    }

    // Hierarchy path of a transform relative to (and excluding) an ancestor root.
    static string RelPath(Transform t, Transform root)
    {
        var stack = new System.Collections.Generic.List<string>();
        for (var c = t; c != null && c != root; c = c.parent) stack.Add(c.name);
        stack.Reverse();
        return string.Join("/", stack);
    }

    static bool SwapBike(GameObject player, string bikePath, out string modelName)
    {
        modelName = "BikeModel";
        var anchor = FindChildNamed(player.transform, "Bike");
        if (anchor == null) { Debug.LogError("[cockpit] no Bike anchor"); return false; }
        var bikeAsset = LoadBike(bikePath);
        if (bikeAsset == null) { Debug.LogError("[cockpit] failed to load bike " + bikePath); return false; }

        var old = FindBikeModel(anchor);
        // Capture the PLAYER's current per-renderer materials (matte-black frame, CelLit wheels,
        // etc. baked into the scene by PlayerBikeFrameBlackFix/CelLit). The new model has an
        // identical node/mesh structure (only handlebar translations changed), so we re-apply
        // these by relative path and never lose the player's livery to the GLB's raw red/blue.
        var matMap = new System.Collections.Generic.Dictionary<string, Material[]>();
        if (old != null)
        {
            modelName = old.name;
            foreach (var r in old.GetComponentsInChildren<Renderer>(true))
                matMap[RelPath(r.transform, old)] = r.sharedMaterials;
        }
        // Remove every existing bike-model child (that has a SteerPivot).
        foreach (Transform child in anchor.Cast<Transform>().ToList())
            if (child.GetComponentsInChildren<Transform>(true).Any(t => t.name == "SteerPivot"))
                Object.DestroyImmediate(child.gameObject);

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(bikeAsset);
        if (inst == null) inst = Object.Instantiate(bikeAsset);
        inst.name = modelName;
        inst.transform.SetParent(anchor, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;

        int reapplied = 0, missed = 0;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (matMap.TryGetValue(RelPath(r.transform, inst.transform), out var mats))
            { r.sharedMaterials = mats; reapplied++; }
            else missed++;
        }
        Debug.Log($"[cockpit] livery re-applied to {reapplied} renderers ({missed} without a source match)");

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) rig.bikePrefab = bikeAsset;
        Debug.Log("[cockpit] swapped player bike -> " + bikePath + " (model '" + modelName + "')");
        return true;
    }

    static void Pose(GameObject player)
    {
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null)
        {
            rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
            rig.ForceSolveOnce();
        }
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }
    }

    static void LogArmMetrics(GameObject player)
    {
        Transform Find(string n) => player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
        foreach (var side in new[] { "Left", "Right" })
        {
            var arm = Find(side + "Arm");
            var fore = Find(side + "ForeArm");
            var hand = Find(side + "Hand");
            var hood = Find(side == "Left" ? "Hood_L" : "Hood_R");
            if (arm == null || fore == null || hand == null) { Debug.Log("[cockpit] " + side + " arm bones missing"); continue; }
            float upper = Vector3.Distance(arm.position, fore.position);
            float forel = Vector3.Distance(fore.position, hand.position);
            float chain = upper + forel;
            Vector3 a = (fore.position - arm.position);
            Vector3 b = (hand.position - fore.position);
            float elbow = 180f - Vector3.Angle(a, b);
            string hoodInfo = "";
            if (hood != null)
            {
                float handToHood = Vector3.Distance(hand.position, hood.position);
                float shToHood = Vector3.Distance(arm.position, hood.position);
                hoodInfo = $" hood={hood.position.ToString("F3")} handToHood={handToHood*1000f:F0}mm shToHood={shToHood:F3}";
            }
            Debug.Log($"[cockpit] {side}: chain={chain:F3} upper={upper:F3} fore={forel:F3} elbow={elbow:F1}deg " +
                      $"hand={hand.position.ToString("F3")}{hoodInfo}");
        }
    }

    public static void Capture()
    {
        var p = OpenAndFindPlayer();
        if (p == null) return;
        string bike = System.Environment.GetEnvironmentVariable("MR_BIKE");
        string outSub = System.Environment.GetEnvironmentVariable("MR_OUT") ?? "fi_cockpit_tmp";
        if (string.IsNullOrEmpty(bike)) { Debug.LogError("[cockpit] MR_BIKE not set"); return; }
        if (!SwapBike(p, bike, out _)) return;
        Pose(p);
        LogArmMetrics(p);
        RenderAll(p, outSub);
        Debug.Log("[cockpit] Capture done (scene NOT saved).");
    }

    public static void Commit()
    {
        var p = OpenAndFindPlayer();
        if (p == null) return;
        string bike = System.Environment.GetEnvironmentVariable("MR_BIKE");
        if (string.IsNullOrEmpty(bike)) { Debug.LogError("[cockpit] MR_BIKE not set"); return; }
        if (!SwapBike(p, bike, out _)) return;
        // Deliberately do NOT Pose() before saving: the rig re-solves from its serialized rest
        // binds (bindCaptured=1) every LateUpdate, so we only need to swap the bike model child
        // and the bikePrefab reference and leave the rider bones untouched (rest pose in-scene).
        EditorSceneManager.MarkSceneDirty(p.scene);
        EditorSceneManager.SaveScene(p.scene);
        Debug.Log("[cockpit] COMMIT: saved scene with player bike -> " + bike);
    }

    static void RenderAll(GameObject player, string outSub)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/" + outSub));
        Directory.CreateDirectory(dir);

        Transform Find(string n) => player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
        var hips = Find("Hips");
        var hoodL = Find("Hood_L"); var hoodR = Find("Hood_R");

        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 mid = (hips != null ? hips.position : player.transform.position + up * 0.9f);
        Vector3 aim = mid + up * 0.15f;
        float d = 2.6f;
        Shot(dir, "player_front",   aim + f * d + up * 0.25f, aim, 38f);
        Shot(dir, "player_front34", aim + (f * 0.8f + r * 0.8f).normalized * d + up * 0.25f, aim, 38f);
        Shot(dir, "player_side",    aim + r * d + up * 0.20f, aim, 38f);
        Shot(dir, "player_rear34",  aim + (-f * 0.8f + r * 0.8f).normalized * d + up * 0.25f, aim, 38f);
        Shot(dir, "player_rear",    aim - f * d + up * 0.25f, aim, 38f);

        // Hands close-up: aim at the midpoint of the two hoods.
        if (hoodL != null && hoodR != null)
        {
            Vector3 hc = (hoodL.position + hoodR.position) * 0.5f;
            Shot(dir, "hands_closeup_front", hc + f * 0.85f + up * 0.12f, hc, 26f);
            Shot(dir, "hands_closeup_side",  hc + r * 0.85f + up * 0.10f, hc, 26f);
        }
        Debug.Log("[cockpit] wrote captures to " + dir);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.SakuraAmbience;
        var go = new GameObject("~CockpitCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 1000, h = 1000;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log("[cockpit] wrote " + name + ".png");
    }
}
