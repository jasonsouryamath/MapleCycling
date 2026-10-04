// Second ShiosaiCoast start-line lab: isolate WHAT is drawing the mauve/cream "sky" and WHY no
// cast shadows appear, both of which survive post-FX off + fog off + shadowDistance 300
// (see lab_2_nopostfx / lab_6_rawlight).
//
// Test 1 (magenta clear): render with clearFlags = SolidColor magenta. Any pixel that is still
//   mauve/cream is GEOMETRY (a backdrop dome/plane), not the skybox. Any pixel that turns
//   magenta is genuinely the skybox showing through.
// Test 2 (shadow canary): drop a big opaque cube over the carriageway and render. If the cube
//   casts no shadow onto the road, shadow rendering is broken scene-wide rather than being a
//   property of the coast dressing.
// Test 3 (sakura root off): deactivate the Sakura Pass Environment root and re-render, to prove
//   or clear HideForeignBackdrop's "foreign backdrop bleeds onto the coast horizon" note - that
//   helper is REPORT ONLY today, so nothing has ever actually hidden it.
//
// Diagnostic only: every mutation is reverted before the method returns.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShiosaiStartLab2
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Lab Shiosai Start Isolation")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        var p = route.Position[0];
        var t = new Vector3(route.Tangent[0].x, 0f, route.Tangent[0].z).normalized;
        var s = route.SideFlat(0);
        var eye = p - t * 6.5f + Vector3.up * 2.4f - s * 0.6f;
        var look = p + t * 22f + Vector3.up * 1.4f;

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);
        bool fogWas = RenderSettings.fog;
        RenderSettings.fog = false;

        // --- Test 1: is the "sky" the skybox, or geometry? ---
        Shot(dir, "lab2_1_magentaclear", eye, look, CameraClearFlags.SolidColor);

        // --- Test 2: shadow canary ---
        var canary = GameObject.CreatePrimitive(PrimitiveType.Cube);
        canary.name = "~ShadowCanary";
        canary.transform.position = p + t * 12f + Vector3.up * 6f;
        canary.transform.localScale = new Vector3(6f, 6f, 6f);
        var cr = canary.GetComponent<MeshRenderer>();
        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        Debug.Log($"[lab2] canary at {canary.transform.position}, sun={(RenderSettings.sun == null ? "<null>" : RenderSettings.sun.name)} " +
                  $"shadows={(RenderSettings.sun != null ? RenderSettings.sun.shadows.ToString() : "-")} " +
                  $"qShadows={QualitySettings.shadows} qDist={QualitySettings.shadowDistance}");
        Shot(dir, "lab2_2_shadowcanary", eye, look, CameraClearFlags.Skybox);
        Object.DestroyImmediate(canary);

        // --- Test 3: Sakura environment root off ---
        var toggled = new List<GameObject>();
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == "Shiosai Coast Environment") continue;
            if (!go.activeSelf) continue;
            // Only turn off the other big ENVIRONMENT roots, leave lights/director alone.
            if (go.name.EndsWith("Environment") || go.name.Contains("Backdrop"))
            {
                go.SetActive(false);
                toggled.Add(go);
                Debug.Log($"[lab2] deactivated root '{go.name}'");
            }
        }
        Shot(dir, "lab2_3_sakuraoff", eye, look, CameraClearFlags.Skybox);
        foreach (var go in toggled) go.SetActive(true);

        RenderSettings.fog = fogWas;
        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log("[lab2] isolation matrix done.");
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look,
                             CameraClearFlags clear)
    {
        var go = new GameObject("~ShiosaiLab2Cam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 30000f;
        cam.clearFlags = clear;
        cam.backgroundColor = Color.magenta;
        cam.allowHDR = true;

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());

        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[lab2] wrote {name}.png");
    }
}
