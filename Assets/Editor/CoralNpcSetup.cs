using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Coral's verification capture, and the retired menu entry that used to stage her.
///
/// Coral is now built by <see cref="SakuraNpcRoster"/> like every other rider. Her own staging
/// pass predated the roster and had drifted out of line with it in two ways that showed up in
/// QA: it attached a <see cref="KuroOutline"/> (a redundant inverted hull that doubled her to
/// 218,796 triangles against every other rider's 109,438, for no visible silhouette on a
/// SKINNED mesh), and it never set <c>ownPhrases</c>, so she was the only rider falling back to
/// the shared static greeting pool. Keeping two staging paths alive is also exactly how a full
/// scene rebuild once quietly replaced good riders with poor ones.
///
/// Her verified numbers were not lost - bike scale 0.9, seated height 1.384 m, 4.5 m/s, lane
/// -1.8, 140 m up the pass, teal frame with coral accents - they were moved into the roster's
/// "Coral" entry verbatim, so she has not moved and does not look different.
/// </summary>
public static class CoralNpcSetup
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>Exact object name, used for idempotent match-and-prune.</summary>
    public const string NpcName = "Sakura NPC Coral";

    /// <summary>Spacing used when resampling the authored route.</summary>
    public const float RouteSpacing = 3.0f;

    /// <summary>
    /// How far up the pass from the player's start line she begins. She rides down at 4.5 m/s
    /// while the player climbs, so they close at roughly 10 m/s and meet within ~15 seconds.
    /// Kept here because other passes read it; the value now lives in SakuraNpcRoster.
    /// </summary>
    public const float MeetDistance = 140f;

    /// <summary>Stages the whole cast, Coral included. See the class remarks.</summary>
    [MenuItem("MapleRide/Coral/Add Coral NPC To Sakura Pass")]
    public static void AddToScene() => SakuraNpcRoster.AddAllToScene();

    /// <summary>
    /// Renders Coral as she actually appears in the playable scene, neutral and smiling, from
    /// the angle the player meets her at. Numbers in a log cannot show whether a face reads as
    /// happy, so this exists to be looked at.
    /// </summary>
    [MenuItem("MapleRide/Coral/Capture Coral NPC Verification")]
    public static void CaptureVerification()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var npc = GameObject.Find(NpcName);
        if (npc == null) throw new FileNotFoundException("Coral NPC missing; stage her first.");

        var cyclist = npc.GetComponent<NPCCyclist>();
        if (cyclist != null) cyclist.ApplyPose();

        var rig = npc.GetComponentInChildren<CoralBikeRig>();
        if (rig != null)
        {
            rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
            rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        }

        var head = FindChild(npc.transform, "Head");
        var smileGo = FindChild(npc.transform, "SmileDecal");
        var smile = smileGo != null ? smileGo.GetComponent<Renderer>() : null;
        Debug.Log("[coral] npc at " + npc.transform.position.ToString("F2") +
                  " forward " + npc.transform.forward.ToString("F2") +
                  " head " + (head != null ? head.position.ToString("F2") : "MISSING") +
                  " smile " + (smile != null ? "present" : "MISSING"));

        var cameraGo = new GameObject("CoralNpcVerifyCamera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.fieldOfView = 40f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 9000f;
        camera.clearFlags = CameraClearFlags.Skybox;
        // Match the gameplay look. A bare camera with no HDR and no post stack renders her
        // face completely blown out, which hides the very thing this capture exists to check.
        camera.allowHDR = true;
        cameraGo.AddComponent<SakuraPostFX>();

        string dir = MapleRidePaths.RenderDir("coral_npc");
        Directory.CreateDirectory(dir);

        // Meet her head-on, the way the player does: she rides toward the camera.
        Vector3 eye = head != null ? head.position : npc.transform.position + Vector3.up * 1.1f;
        var shots = new[]
        {
            ("face", eye + npc.transform.forward * 0.85f + Vector3.up * 0.05f, eye, 40f),
            ("meet", eye + npc.transform.forward * 3.2f + Vector3.up * 0.35f, eye, 40f),
            ("side", eye + npc.transform.right * 2.4f + Vector3.up * 0.1f,
                     npc.transform.position + Vector3.up * 0.6f, 40f)
        };
        foreach (var shot in shots)
        {
            camera.fieldOfView = shot.Item4;
            camera.transform.position = shot.Item2;
            camera.transform.LookAt(shot.Item3);
            foreach (var state in new[] { "neutral", "smile" })
            {
                if (smile != null) smile.enabled = state == "smile";
                Render(camera, Path.Combine(dir, "coral_npc_" + shot.Item1 + "_" + state + ".png"));
            }
        }
        if (smile != null) smile.enabled = false;
        Object.DestroyImmediate(cameraGo);
        Debug.Log("[coral] captured verification to " + dir);
        // Rendering is read-only as far as the project is concerned; drop the incidental
        // dirtiness so no crash-recovery backup of this pass is left behind.
        MapleRideSceneBootstrap.DiscardChanges();
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

    static void Render(Camera camera, string path)
    {
        const int width = 1100, height = 850;
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