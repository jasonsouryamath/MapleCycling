using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Test whether the lower-back 'box' is mip-bleed: set the PlayerBack texture to NO
/// mipmaps (and reimport), reseat, and capture a waist render. If the box is gone, mip-bleed of
/// the adjacent kanji was the cause and disabling mipmaps is the fix. Also captures a baseline.</summary>
public static class KuroBoxMipTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string TexPath = "Assets/Kuro/NPC/Textures/KuroAnime_Image_0_PlayerBack.png";

    public static void Run()
    {
        var ti = AssetImporter.GetAtPath(TexPath) as TextureImporter;
        if (ti == null) { Debug.LogError("[miptest] no importer at " + TexPath); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        Debug.Log($"[miptest] before: mipmapEnabled={ti.mipmapEnabled} filter={ti.filterMode} aniso={ti.anisoLevel}");
        ti.mipmapEnabled = false;
        EditorUtility.SetDirty(ti);
        ti.SaveAndReimport();
        Debug.Log("[miptest] set mipmapEnabled=false and reimported");

        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        Vector3 f = player.transform.forward, hips = player.transform.position;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));
        Shot(dir, "box_nomip", hips - f * 1.3f + Vector3.up * 0.72f, hips + Vector3.up * 0.60f, 30f);
        Debug.Log("[miptest] wrote box_nomip.png");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~MipCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
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
