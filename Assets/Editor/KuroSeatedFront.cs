using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Capture the seated player's FRONT torso and a tight REAR torso so the reported back
/// 'white glove + black box' can be compared against the FRONT chest graphics (maple leaf, KURO,
/// white patch, zip panel) to test the front-bleed-through hypothesis. Read-only.</summary>
public static class KuroSeatedFront
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[front] no player"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }
        Transform chest = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true)) if (t.name == "Spine02") { chest = t; break; }
        Vector3 c = chest != null ? chest.position : player.transform.position + Vector3.up;
        Vector3 f = player.transform.forward;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));
        Directory.CreateDirectory(dir);
        // front torso: camera in front (+f), slightly above, looking back-down at chest
        Shot(dir, "seated_front_torso", c + f * 1.2f + Vector3.up * 0.15f, c, 34f);
        // tight rear torso
        Shot(dir, "seated_rear_torso", c - f * 1.2f + Vector3.up * 0.15f, c, 34f);
        Debug.Log("[front] wrote seated_front_torso + seated_rear_torso");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~FCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f,0.55f,0.6f);
        const int w = 900, h = 1000;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32){ antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0,0,w,h),0,0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log("[front] wrote " + name);
    }
}
