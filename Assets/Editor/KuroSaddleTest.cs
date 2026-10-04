using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Confirm the black box is the bike saddle/seatpost: render the seated waist crop with
/// SaddleRear/SaddleNose/SeatPost hidden. If the box vanishes, it is the saddle. Read-only.</summary>
public static class KuroSaddleTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    static readonly string[] Parts = { "SaddleRear", "SaddleNose", "SeatPost" };
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        Vector3 f = player.transform.forward, hips = player.transform.position;
        Vector3 pos = hips - f * 1.3f + Vector3.up * 0.72f, look = hips + Vector3.up * 0.60f;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));

        // find & report the saddle parts
        var found = new System.Collections.Generic.List<Renderer>();
        var sb = new StringBuilder();
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
            foreach (var p in Parts) if (r.gameObject.name == p)
            {
                found.Add(r);
                sb.AppendLine($"[saddle] {p}: bounds center={r.bounds.center:F3} size={r.bounds.size:F3} mat={r.sharedMaterial?.name}");
            }
        Debug.Log(sb.ToString());

        Shot(dir, "saddle_on", pos, look);
        foreach (var r in found) r.enabled = false;
        Shot(dir, "saddle_off", pos, look);
        foreach (var r in found) r.enabled = true;
        Debug.Log("[saddle] wrote saddle_on + saddle_off");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void Shot(string dir, string name, Vector3 pos, Vector3 look)
    {
        var go = new GameObject("~SadCam"); var cam = go.AddComponent<Camera>();
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
