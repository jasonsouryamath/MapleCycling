using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Decisive box test: seat player, render the waist crop TWICE - once normal (CelLit),
/// once with Mesh_0 swapped to an UNLIT material using the SAME body atlas. If the black box is
/// gone in the unlit render, it is a CelLit shading artifact; if it persists, it is in the texture.
/// Read-only (no scene save).</summary>
public static class KuroBoxUnlitTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[unlit] no player"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        SkinnedMeshRenderer body = null;
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.sharedMesh != null && s.sharedMesh.name == "Mesh_0") { body = s; break; }
        var mat = body.sharedMaterial;
        Texture tex = null;
        foreach (var pn in new[]{"_BaseColorMap","_BaseMap","_MainTex"})
            if (mat.HasProperty(pn) && mat.GetTexture(pn) != null) { tex = mat.GetTexture(pn); break; }

        Vector3 f = player.transform.forward, hips = player.transform.position;
        Vector3 camPos = hips - f * 1.3f + Vector3.up * 0.72f;
        Vector3 look = hips + Vector3.up * 0.60f;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));
        Directory.CreateDirectory(dir);

        Shot(dir, "box_cellit", camPos, look, 30f);           // normal

        // swap to unlit
        var orig = body.sharedMaterials;
        var unlit = new Material(Shader.Find("Unlit/Texture"));
        if (tex != null) unlit.mainTexture = tex;
        var arr = new Material[orig.Length];
        for (int i=0;i<arr.Length;i++) arr[i] = unlit;
        body.sharedMaterials = arr;
        Shot(dir, "box_unlit", camPos, look, 30f);
        body.sharedMaterials = orig;                          // restore (not saved anyway)

        Debug.Log("[unlit] wrote box_cellit + box_unlit");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~UCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f,0.55f,0.6f);
        const int w = 1100, h = 1100;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32){ antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0,0,w,h),0,0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log("[unlit] wrote " + name);
    }
}
