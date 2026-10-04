using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Empirically test CelLit ramp tweaks on the PLAYER-ONLY body material to dissolve the
/// lower-back hard-ramp 'box'. Renders the waist crop for several settings. Read-only (no save).</summary>
public static class KuroBoxShadeTest
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

        // baseline
        Shot(dir, "shade_base", pos, look, body);
        // save originals
        float rs = m.GetFloat("_RampSmooth"), sa = m.GetFloat("_ShadowAmbient"), ss = m.GetFloat("_ShadeStrength");
        float steps = m.GetFloat("_RampSteps");
        Color mf = m.GetColor("_MatteFloor");

        m.SetFloat("_RampSmooth", 0.40f); Shot(dir, "shade_smooth040", pos, look, body); m.SetFloat("_RampSmooth", rs);
        m.SetFloat("_ShadowAmbient", 0.80f); Shot(dir, "shade_amb080", pos, look, body); m.SetFloat("_ShadowAmbient", sa);
        m.SetFloat("_ShadeStrength", 0.45f); Shot(dir, "shade_shd045", pos, look, body); m.SetFloat("_ShadeStrength", ss);
        m.SetColor("_MatteFloor", new Color(0.30f,0.31f,0.34f)); Shot(dir, "shade_floor030", pos, look, body); m.SetColor("_MatteFloor", mf);
        // combined gentle
        m.SetFloat("_RampSmooth", 0.28f); m.SetFloat("_ShadowAmbient", 0.70f);
        Shot(dir, "shade_combo", pos, look, body);
        m.SetFloat("_RampSmooth", rs); m.SetFloat("_ShadowAmbient", sa);

        Debug.Log("[shade] wrote shade_base/smooth040/amb080/shd045/floor030/combo");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void Shot(string dir, string name, Vector3 pos, Vector3 look, Renderer forceRefresh)
    {
        if (forceRefresh is SkinnedMeshRenderer smr){ smr.forceMatrixRecalculationPerRender=true; smr.updateWhenOffscreen=true; }
        var go = new GameObject("~SCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f,0.55f,0.6f);
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
