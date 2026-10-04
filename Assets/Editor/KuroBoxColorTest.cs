using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Tint body _Color red + flood ambient. If the black box turns red it is the jersey
/// surface (shading quirk); if it stays black a different surface shows there. Read-only.</summary>
public static class KuroBoxColorTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        SkinnedMeshRenderer body = null;
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.sharedMesh != null && s.sharedMesh.name == "Mesh_0") { body = s; break; }
        var m = body.sharedMaterial;
        Vector3 f = player.transform.forward, hips = player.transform.position;
        Vector3 pos = hips - f * 1.3f + Vector3.up * 0.72f, look = hips + Vector3.up * 0.60f;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));

        var c0 = m.GetColor("_Color");
        m.SetColor("_Color", new Color(1f,0f,0f,1f));
        if (m.HasProperty("_AmbientStrength")) m.SetFloat("_AmbientStrength", 3f);
        if (m.HasProperty("_CharAmbient")) m.SetFloat("_CharAmbient", 2f);
        if (m.HasProperty("_ShadeStrength")) m.SetFloat("_ShadeStrength", 0f);
        Shot(dir, "box_redtint", pos, look, body);
        m.SetColor("_Color", c0);
        Debug.Log("[colortest] wrote box_redtint (if box is red -> jersey surface; if black -> other surface)");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void Shot(string dir, string name, Vector3 pos, Vector3 look, SkinnedMeshRenderer smr)
    {
        smr.forceMatrixRecalculationPerRender=true; smr.updateWhenOffscreen=true;
        var go = new GameObject("~CCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f,0.6f,0.2f);
        const int w=1100,h=1100;
        var rt = new RenderTexture(w,h,24,RenderTextureFormat.ARGB32){antiAliasing=4};
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w,h,TextureFormat.RGB24,false);
        img.ReadPixels(new Rect(0,0,w,h),0,0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture=null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }
}
