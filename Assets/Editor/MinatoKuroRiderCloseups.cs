using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CAPTURE-ONLY close-ups of the STAGED Minato traffic riders (the Kuro-based bodies with their
/// per-rider kit and hair piece - see MinatoNpcTraffic), one rider per hair style, seated on the
/// bike by the same CoralBikeRig solve the director uses, lit by Minato's own gameplay ambience.
///
/// Why this exists: the play-mode capture shows the road traffic only at 20 m+, too small to
/// judge a hair piece or a kit seam, and the crowd render sheet builds its own donor fixtures
/// under Maple City's grade (which hides the Minato root these riders live under). Nothing is
/// saved - the scene is discarded afterwards.
///
/// Writes reference/good_graphics/minato_play/kuro_rider_&lt;name&gt;_&lt;view&gt;.png
/// Menu: MapleRide/NPCs/Capture Minato Kuro Rider Close-ups
/// </summary>
public static class MinatoKuroRiderCloseups
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string TrafficRootName = "Minato Traffic";

    /// <summary>One rider per style: short, bob, ponytail, twintails, long.</summary>
    static readonly string[] Riders = { "Kaoru", "Minori", "Kohaku", "Rina", "Marina" };

    /// <summary>Well above the map so nothing occludes the rider. PROVISIONAL framing.</summary>
    static readonly Vector3 Stage = new Vector3(0f, 600f, 0f);

    /// <summary>Riders face +Z. Degrees around the rider; 180 = the chase-camera side.</summary>
    static readonly (string View, float Deg, float Dist, float EyeY)[] Views =
    {
        ("chase", 180f, 2.6f, 1.45f),
        ("rear34", 215f, 2.1f, 1.15f),
        ("side", 90f, 2.1f, 0.95f),
        ("front34", 35f, 2.1f, 1.05f),
    };

    [MenuItem("MapleRide/NPCs/Capture Minato Kuro Rider Close-ups", priority = 34)]
    public static void Capture()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath,
                                                      "../reference/good_graphics/minato_play"));
        Directory.CreateDirectory(outDir);
        int written = 0;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
                FindObjectsInactive.Include);
            if (regions != null)
            {
                regions.Resolve();
                regions.currentRegionId = RegionCatalog.MinatoCoast;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }

            var traffic = UnityEngine.Object.FindObjectsByType<Transform>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == TrafficRootName);
            if (traffic == null) throw new InvalidOperationException("no staged Minato Traffic");

            foreach (var name in Riders)
            {
                var npc = traffic.Cast<Transform>().FirstOrDefault(t => t.name.EndsWith(" " + name));
                if (npc == null) { Debug.LogWarning($"[kuro-closeup] no rider {name}"); continue; }
                npc.gameObject.SetActive(true);
                npc.SetPositionAndRotation(Stage, Quaternion.identity);
                var rig = npc.GetComponentInChildren<CoralBikeRig>(true);
                rig.ForceSolveOnce();
                NpcCanonicalConformance.FinalizeStagedPose(rig);

                foreach (var (view, deg, dist, eyeY) in Views)
                {
                    float a = deg * Mathf.Deg2Rad;
                    var eye = Stage + new Vector3(Mathf.Sin(a) * dist, eyeY, Mathf.Cos(a) * dist);
                    Shot(Path.Combine(outDir, $"kuro_rider_{name}_{view}.png"),
                         eye, Stage + new Vector3(0f, 0.72f, 0f));
                    written++;
                }
                npc.gameObject.SetActive(false);
            }
            Debug.Log($"[kuro-closeup] wrote {written} PNG to {outDir}");
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    /// <summary>A sample of Kuro-based riders from every OTHER region, found by rider name.</summary>
    static readonly string[] RegionSample =
        { "Aoi", "Nao", "Nami", "Tsubasa", "Rin", "Yuna", "Takane", "Asahi",
          "Kenzan", "Fubuki", "Hotaru", "Momoka" };

    [MenuItem("MapleRide/NPCs/Capture Kuro Rider Close-ups (All Regions Sample)", priority = 35)]
    public static void CaptureRegionSample()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath,
                                                      "../reference/good_graphics/kuro_riders_regions"));
        Directory.CreateDirectory(outDir);
        int written = 0;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            if (regions != null)
            {
                regions.Resolve();
                regions.currentRegionId = RegionCatalog.MinatoCoast;   // neutral bright daylight
                regions.ApplyAmbience();
            }
            var greeters = UnityEngine.Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None);
            foreach (var name in RegionSample)
            {
                var g = greeters.FirstOrDefault(x => x.riderName == name);
                if (g == null) { Debug.LogWarning($"[kuro-closeup] no staged rider {name}"); continue; }
                for (var t = g.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                var npc = g.transform;
                npc.SetPositionAndRotation(Stage, Quaternion.identity);
                var rig = npc.GetComponentInChildren<CoralBikeRig>(true);
                if (rig != null) { rig.ForceSolveOnce(); NpcCanonicalConformance.FinalizeStagedPose(rig); }
                foreach (var (view, deg, dist, eyeY) in Views)
                {
                    if (view == "side") continue;
                    float a = deg * Mathf.Deg2Rad;
                    var eye = Stage + new Vector3(Mathf.Sin(a) * dist, eyeY, Mathf.Cos(a) * dist);
                    Shot(Path.Combine(outDir, $"rider_{name}_{view}.png"), eye,
                         Stage + new Vector3(0f, 0.72f, 0f));
                    written++;
                }
                npc.gameObject.SetActive(false);
            }
            Debug.Log($"[kuro-closeup] wrote {written} region-sample PNG to {outDir}");
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static void Shot(string file, Vector3 pos, Vector3 look, int w = 900, int h = 900)
    {
        var go = new GameObject("~KuroCloseupCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 3000f;
        cam.allowHDR = true;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(w, h, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        image.Apply();
        File.WriteAllBytes(file, image.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
    }
}
