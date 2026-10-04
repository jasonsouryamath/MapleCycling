using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Renders every roster rider so they can be checked BY EYE. A material dump and a scale
/// number have both previously reported success on a rider that looked wrong, so nothing here
/// is trusted until the PNGs have been looked at.
///
/// Read-only: discards scene changes on the way out, or Unity reopens to a recovery scene.
public static class NpcRosterCapture
{
    const string OutDir = "../good_graphics/npc_roster";

    static readonly string[] Names =
    { "Aoi", "Haruka", "Mika", "Nao", "Ren", "Daichi", "Sora", "Emi", "Yuki", "Takumi", "Coral" };

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        Directory.CreateDirectory(OutDir);

        var camGo = new GameObject("RosterCam");
        var cam = camGo.AddComponent<Camera>();
        cam.allowHDR = true;                 // without HDR + the grade, renders blow out near-white
        camGo.AddComponent<SakuraPostFX>();
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 9000f;

        var rt = new RenderTexture(900, 1100, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
        var shot = new Texture2D(900, 1100, TextureFormat.RGB24, false);

        foreach (var n in Names)
        {
            var go = GameObject.Find("Sakura NPC " + n);
            if (go == null) { Debug.LogWarning($"[cap] MISSING Sakura NPC {n}"); continue; }

            // Bake the skinned meshes: SkinnedMeshRenderer.bounds is the import-time estimate
            // and has reported a chibi as 2.09 m tall.
            var b = BakedBounds(go);
            float h = b.size.y;

            // Three-quarter front view, framed on the rider rather than the whole bike+rider box.
            var fwd = go.transform.forward;
            var right = go.transform.right;
            var eye = b.center + fwd * (h * 1.9f) + right * (h * 1.15f) + Vector3.up * (h * 0.30f);
            cam.transform.position = eye;
            cam.transform.LookAt(b.center + Vector3.up * h * 0.10f);

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, 900, 1100), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes($"{OutDir}/npc_{n}.png", shot.EncodeToPNG());

            var cyc = go.GetComponent<NPCCyclist>();
            var grt = go.GetComponent<NpcGreeting>();
            Debug.Log($"[cap] {n}: height={h:F3} centre={b.center:F1} " +
                      $"speed={(cyc != null ? cyc.speed : -1f):F1} lines={(grt != null ? grt.ownPhrases.Length : -1)} " +
                      $"-> npc_{n}.png");
        }

        Object.DestroyImmediate(camGo);
        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(0);
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
