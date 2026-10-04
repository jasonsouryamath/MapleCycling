using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Low, level, close-in shots of each roster rider's WHEEL-TO-ROAD CONTACT.
///
/// NpcRosterCapture frames the whole rider from three-quarter high, which is the right shot for
/// judging a livery and the wrong one for judging whether a rider is buried: from up there a
/// sunken rider still looks seated. This puts the camera at road height so the contact patch is
/// unmistakable, and it makes an invisible rider obvious by simply not being in the frame.
/// </summary>
public static class NpcContactCapture
{
    const string OutDir = "../good_graphics/npc_contact";

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        SakuraSceneDefaults.Fix();
        Directory.CreateDirectory(OutDir);

        string tag = System.Environment.GetEnvironmentVariable("MR_NPC_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "before";

        var camGo = new GameObject("ContactCam");
        var cam = camGo.AddComponent<Camera>();
        cam.allowHDR = true;
        camGo.AddComponent<SakuraPostFX>();
        cam.fieldOfView = 42f;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 9000f;
        cam.renderingPath = RenderingPath.Forward;

        var rt = new RenderTexture(1200, 800, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
        var shot = new Texture2D(1200, 800, TextureFormat.RGB24, false);

        var npcs = Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                        FindObjectsSortMode.None);
        foreach (var cyc in npcs)
        {
            var t = cyc.transform;
            // Road height, just off the rider's shoulder, looking slightly down at the tyres.
            cam.transform.position = t.position + t.forward * 3.0f + t.right * 2.1f + Vector3.up * 0.62f;
            cam.transform.LookAt(t.position + Vector3.up * 0.34f);

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string name = t.name.Replace("Sakura NPC ", "").Replace(" ", "_");
            File.WriteAllBytes($"{OutDir}/contact_{tag}_{name}.png", shot.EncodeToPNG());
            Debug.Log($"[contact] {t.name} at {t.position:F2} -> contact_{tag}_{name}.png");
        }

        Object.DestroyImmediate(camGo);
        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(0);
    }
}
