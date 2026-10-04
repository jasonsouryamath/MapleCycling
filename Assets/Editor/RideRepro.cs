using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Reproduces what the player actually sees while riding: moves the real rider along the
/// authored route, applies KuroRoadGrounding and KuroRoadSafety exactly as they run in game
/// (grounding first, then safety at execution order 200), and renders from the follow camera.
public static class RideRepro
{
    const string OutDir = "../good_graphics/ride_check";

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        Physics.SyncTransforms();
        Directory.CreateDirectory(OutDir);

        var player = GameObject.Find("Kuro on Sakura Pass");
        var grounding = player.GetComponent<KuroRoadGrounding>();
        var safety = player.GetComponent<KuroRoadSafety>();
        var route = safety.route;
        Debug.Log($"[ride] route {route.Length} samples, corridor {safety.roadCorridor}, z<= {safety.maxZ}");

        var cam = GameObject.Find("Sakura Camera")?.GetComponent<Camera>();
        if (cam == null) { Debug.LogError("[ride] no Sakura Camera"); EditorApplication.Exit(0); return; }
        cam.allowHDR = true;
        var follow = cam.GetComponent<KuroFollowCamera>();
        Vector3 camOffset = follow != null ? follow.offset : new Vector3(0f, 3.2f, -7f);

        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
        var shot = new Texture2D(1600, 900, TextureFormat.RGB24, false);

        // Sample the whole pass, weighted toward the descent where the route changed.
        float[] fracs = { 0.05f, 0.18f, 0.32f, 0.46f, 0.55f, 0.64f, 0.72f, 0.80f, 0.88f, 0.96f };
        int sunk = 0;

        foreach (var f in fracs)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(f * (route.Length - 1)), 0, route.Length - 2);
            var fwd = (route[i + 1] - route[i]); fwd.y = 0f; fwd.Normalize();

            // Place the rider on the centreline, then run the two scripts in game order.
            player.transform.position = route[i] + Vector3.up * 0.5f;
            player.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Physics.SyncTransforms();

            var before = player.transform.position;
            Invoke(grounding, "LateUpdate");
            var afterGround = player.transform.position;
            Invoke(safety, "LateUpdate");
            var afterSafety = player.transform.position;

            // What is the road surface directly under him?
            float roadY = float.NaN;
            if (Physics.Raycast(afterSafety + Vector3.up * 8f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore))
                roadY = hit.point.y;
            float sink = roadY - afterSafety.y;
            if (sink > 0.15f) sunk++;

            Debug.Log($"[ride] f={f:F2} i={i} ground.y={afterGround.y:F3} safety.y={afterSafety.y:F3} " +
                      $"roadY={roadY:F3} sink={sink:F3} movedBySafety={(afterSafety - afterGround).magnitude:F3}");

            cam.transform.position = afterSafety + player.transform.TransformDirection(camOffset);
            cam.transform.LookAt(afterSafety + Vector3.up * 1.1f);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes($"{OutDir}/ride_{(int)(f * 100):D2}.png", shot.EncodeToPNG());
            Debug.Log($"[ride] wrote ride_{(int)(f * 100):D2}.png");
        }

        Debug.Log($"[ride] samples where rider sits >15 cm below the road: {sunk}");
        EditorApplication.Exit(0);
    }

    private static void Invoke(MonoBehaviour mb, string method)
    {
        if (mb == null) return;
        var m = mb.GetType().GetMethod(method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        m?.Invoke(mb, null);
        Physics.SyncTransforms();
    }
}
