using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// THROWAWAY QA probe: casts a forward ray (bounds-based, not physics - this must find a
/// renderer even if it has zero colliders) from rider-eye height at a spread of route metres
/// near the suspected route-opening obstruction (~150-260 m), reports the first renderer hit,
/// its distance, whether ANY collider exists anywhere in its object hierarchy, and shoots a
/// wide overhead frame plus a pulled-back side frame of the area for visual proof.
/// </summary>
public static class QaMinatoOpeningRaycastProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RootName = "Minato Coast Environment";

    [MenuItem("MapleRide/QA/Probe Minato Opening Raycast")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find(RootName);
        if (root == null) { Debug.LogError("[qa-minato4] root not found."); return; }

        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"[qa-minato4] {renderers.Length} renderers loaded for bounds raycast");

        var log = new StringBuilder();
        log.AppendLine("=== MINATO OPENING FORWARD BOUNDS-RAYCAST (rider eye, d=140-260 m) ===");

        foreach (var d in new[] { 140f, 150f, 160f, 170f, 180f, 190f, 200f, 210f, 220f, 230f, 240f, 250f })
        {
            int i = route.IndexAt(d);
            var origin = route.Position[i] + Vector3.up * 1.5f;
            var fwd = route.Tangent[i].normalized;

            Renderer hit = null; float hitDist = float.MaxValue;
            foreach (var r in renderers)
            {
                if (r.transform.IsChildOf(root.transform) == false) continue;
                var b = r.bounds;
                if (b.IntersectRay(new Ray(origin, fwd), out float t) && t > 0.05f && t < hitDist)
                {
                    // Confirm the ray actually enters within a short forward cone (avoid picking
                    // up distant background silhouette hits through open air at extreme range).
                    if (t < 60f) { hit = r; hitDist = t; }
                }
            }

            if (hit != null)
            {
                var top = hit.transform;
                while (top.parent != null && top.parent != root.transform) top = top.parent;
                bool hasCollider = hit.GetComponentInParent<Collider>() != null ||
                                   hit.GetComponentInChildren<Collider>() != null;
                log.AppendLine($"d={d:N0} m: forward ray hits '{hit.name}' (top='{top.name}') " +
                                $"at {hitDist:N2} m ahead, world~{origin + fwd * hitDist}, " +
                                $"HAS_COLLIDER={hasCollider}");
            }
            else
            {
                log.AppendLine($"d={d:N0} m: forward ray clear within 60 m.");
            }
        }

        Debug.Log(log.ToString());
        Directory.CreateDirectory("reference/good_graphics");
        File.WriteAllText("reference/good_graphics/qa_minato_opening_raycast.txt", log.ToString());

        // Visual proof: pulled-back 3/4 view and overhead view centred on d=200.
        int ci = route.IndexAt(200f);
        var cp = route.Position[ci];
        var side = route.SideFlat(ci);
        Shot(cp - route.Tangent[ci].normalized * 40f + side * 30f + Vector3.up * 20f, cp, 60f,
             "qa_minato_open_d200_pullback34.png");
        Shot(cp + Vector3.up * 160f - route.Tangent[ci].normalized * 5f, cp, 65f,
             "qa_minato_open_d200_topdown.png");
        Shot(cp + side * 45f + Vector3.up * 6f, cp, 55f,
             "qa_minato_open_d200_sideon.png");
    }

    private static void Shot(Vector3 eye, Vector3 target, float fov, string file)
    {
        var camGo = new GameObject("~QaCam4", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.transform.position = eye;
        var dir = target - eye;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        cam.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 5000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        File.WriteAllBytes(Path.Combine("reference/good_graphics", file), tex.EncodeToPNG());
        Debug.Log($"[qa-minato4] shot {file}  eye {eye}");

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(tex);
    }
}
