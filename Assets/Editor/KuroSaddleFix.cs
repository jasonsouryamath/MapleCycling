using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Item-4 fix (repeatable, idempotent): the bike SADDLE (SaddleRear/SaddleNose) protrudes behind
/// the seated rider and renders as a stray black box because it uses the glossy pure-black tyre
/// material (rim=0.85 -> orange rim) and its flat rear cube face pokes past the rider in the chase
/// view. This pass (a) reassigns those two renderers to the matte frame-black material so no orange
/// rim, and (b) tucks the saddle forward+down under the rider so the body occludes it from behind.
/// Player-scope only (NPC bikes untouched). Idempotent: guarded by the matte-material marker so it
/// never double-tucks. Saves the scene.
/// </summary>
public static class KuroSaddleFix
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string MatteMat = "KuroBike_Frame_Black";
    const float TuckFwd = 0.035f;   // metres forward (player forward) so the rider occludes the rear
    const float TuckDown = 0.030f;  // metres down

    [MenuItem("MapleRide/Fix Player Saddle Box")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[saddlefix] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);

        var matte = FindMat(MatteMat);
        if (matte == null) { Debug.LogError("[saddlefix] matte material '" + MatteMat + "' not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        Renderer sRear = null, sNose = null;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        { if (r.gameObject.name == "SaddleRear") sRear = r; if (r.gameObject.name == "SaddleNose") sNose = r; }
        if (sRear == null || sNose == null) { Debug.LogError("[saddlefix] saddle renderers not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        bool alreadyFixed = sRear.sharedMaterial == matte;
        if (alreadyFixed)
        {
            Debug.Log("[saddlefix] already fixed (saddle already matte) - skipping tuck to stay idempotent");
        }
        else
        {
            // (a) material: matte, no orange rim
            sRear.sharedMaterial = matte; sNose.sharedMaterial = matte;
            // (b) tuck forward+down (in the saddle's parent space)
            var f = player.transform.forward;
            foreach (var t in new[] { sRear.transform, sNose.transform })
            {
                Vector3 fwdLocal = t.parent.InverseTransformVector(f).normalized;
                Vector3 downLocal = t.parent.InverseTransformVector(Vector3.up).normalized;
                t.localPosition = t.localPosition + fwdLocal * TuckFwd - downLocal * TuckDown;
                EditorUtility.SetDirty(t);
            }
            Debug.Log($"[saddlefix] applied matte material + tuck (fwd {TuckFwd}, down {TuckDown}) to SaddleRear/SaddleNose");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[saddlefix] scene saved");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static Material FindMat(string name)
    {
        foreach (var g in AssetDatabase.FindAssets("t:Material " + name))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m && m.name == name) return m;
        }
        return null;
    }
}
