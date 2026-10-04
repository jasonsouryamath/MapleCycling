using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-off verification capture for the fix-implementer follow-up session: confirms (a) the
/// rear-helmet vent pareidolia fix and (b) the character-weathering-zero fix both read correctly
/// in Sakura Pass, the one region that was NOT covered by the earlier Minato-focused capture
/// loop. Rear + 3/4-rear views, since that is the angle the original pareidolia finding and the
/// Minato tear finding were both reported from.
///
/// Read-only: discards scene changes on the way out.
/// </summary>
public static class FiSakuraRearHelmetCheck
{
    const string OutDir = "../good_graphics/npc_roster";

    static readonly string[] Names =
    { "Aoi", "Haruka", "Mika", "Nao", "Ren", "Daichi", "Sora", "Emi", "Yuki", "Takumi", "Coral" };

    [MenuItem("MapleRide/Diagnostics/FI - Sakura Rear Helmet Check")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        Directory.CreateDirectory(OutDir);

        var camGo = new GameObject("FiRearCam");
        var cam = camGo.AddComponent<Camera>();
        cam.allowHDR = true;
        camGo.AddComponent<SakuraPostFX>();
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 9000f;

        var rt = new RenderTexture(900, 1100, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
        var shot = new Texture2D(900, 1100, TextureFormat.RGB24, false);

        foreach (var n in Names)
        {
            var go = GameObject.Find("Sakura NPC " + n);
            if (go == null) { Debug.LogWarning($"[fi-sak-rear] MISSING Sakura NPC {n}"); continue; }

            var b = BakedBounds(go);
            float h = b.size.y;

            // Rear 3/4, matching the Minato chase-cam vantage: behind and slightly above,
            // looking forward at the back of the helmet/jersey. Distance/offsets mirror the
            // working front-view capture above (h*1.9 / h*1.15 / h*0.30) rather than a tight
            // close-up, because a tight offset risks embedding the camera inside the NEXT
            // rider queued directly behind this one in the roster line-up.
            var back = -go.transform.forward;
            var right = go.transform.right;
            var eye = b.center + back * (h * 1.9f) + right * (h * 1.15f) + Vector3.up * (h * 0.55f);
            cam.transform.position = eye;
            cam.transform.LookAt(b.center + Vector3.up * h * 0.35f);

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, 900, 1100), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes($"{OutDir}/fi_rear_{n}.png", shot.EncodeToPNG());
            Debug.Log($"[fi-sak-rear] {n}: height={h:F3} centre={b.center:F1} -> fi_rear_{n}.png");
        }

        Object.DestroyImmediate(camGo);
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static Bounds BakedBounds(GameObject go)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = new Mesh();
            s.BakeMesh(m, true);
            foreach (var v in m.vertices)
            {
                var w = s.transform.TransformPoint(v);
                if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                else b.Encapsulate(w);
            }
            Object.DestroyImmediate(m);
        }
        if (!any)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
        }
        return b;
    }
}
