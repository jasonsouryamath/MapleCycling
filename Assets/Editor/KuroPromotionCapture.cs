using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// In-engine acceptance capture for the promoted A-pose Kuro.
///
/// The Blender fit (zz_bike_fit.py) proved the pose in Blender's renderer. That is NOT proof
/// the SHIPPED asset is right: the Unity path re-derives the whole pose at runtime from
/// KuroBikeRig (hips to SaddleTop, lean split across the spine, two-bone IK to the Hood_L/R
/// and Pedal_L/R sockets). So this pass re-renders the six acceptance angles from the actual
/// scene and, just as importantly, RE-MEASURES the two things that have produced false greens
/// on this asset before:
///
///   1. DELIVERED torso lean. The request is 47 deg; the rebuilt spine delivers ~34.5. Only the
///      delivered angle counts, so it is measured here from the Hips->neck vector rather than
///      read back off the parameter.
///   2. MESH-LEVEL foot contact. The bone-tail IK residual reads 0.0000 m even when the visible
///      shoe hangs a full shoe-height below the pedal - that exact false green is what hid the
///      floating left foot for several passes. So the check bakes the posed SkinnedMeshRenderer
///      and compares the lowest SOLE vertices against the pedal platform, which is what a player
///      actually sees.
///
/// Read-only with respect to the scene: it adds a camera, renders, and destroys it again, so it
/// never leaves SakuraPass dirty.
/// </summary>
public static class KuroPromotionCapture
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PlayerRootName = "Kuro on Sakura Pass";
    private const int Width = 1400;
    private const int Height = 1050;

    // Fraction of the shoe's vertical extent treated as the sole patch, matching the
    // BALL_LOW_FRAC used by the Blender foot solve so the two numbers are comparable.
    private const float SoleFraction = 0.12f;

    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == PlayerRootName);
        if (player == null)
        {
            Debug.LogError("[kuro-promo] player root '" + PlayerRootName + "' not found.");
            return;
        }

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig == null)
        {
            Debug.LogError("[kuro-promo] no KuroBikeRig on the player.");
            return;
        }
        var pose = player.GetComponent<KuroRidePose>();
        if (pose != null)
        {
            pose.SelectPosture(KuroRidePose.CyclingPosture.RoadRacerAggressive, false);
            pose.enableClimbOverlay = false;
            pose.enableSprintOverlay = false;
            pose.SprintOverride = 0f;
            pose.Tick(1f);
        }
        rig.ForceSolveOnce();

        Report(player, rig);

        var dir = Path.Combine(MapleRidePaths.Renders, "kuro_promo");
        Directory.CreateDirectory(dir);
        Debug.Log("[kuro-promo] output dir: " + Path.GetFullPath(dir));

        // Frame on the rider's chest so the bike stays in shot at gameplay-ish distance.
        var pivot = player.transform.position + Vector3.up * 0.55f;
        var camObj = new GameObject("KuroPromoCam");
        var cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.03f;
        cam.clearFlags = CameraClearFlags.Skybox;

        // Azimuths match the Blender convention used all through this rebuild:
        // front = 0, front34 = 40, left = 90, right = 270, rear34 = 140, rear = 180.
        var shots = new (string name, float az)[]
        {
            ("front", 0f), ("front34", 40f), ("left", 90f),
            ("right", 270f), ("rear34", 140f), ("rear", 180f),
        };

        foreach (var shot in shots)
        {
            // Azimuths match the Blender convention used all through this rebuild:
            // front = 0, front34 = 40, left = 90, right = 270, rear34 = 140, rear = 180.
            // The ring starts on the rider's FORWARD axis: starting from -forward silently
            // swapped front with rear (and front34 with rear34) in the first capture.
            var q = Quaternion.AngleAxis(shot.az, Vector3.up);
            var offset = q * player.transform.forward;
            cam.transform.position = pivot + offset * 2.35f + Vector3.up * 0.28f;
            cam.transform.LookAt(pivot);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            img.Apply();
            var outPath = Path.Combine(dir, "PROMO_" + shot.name + ".png");
            File.WriteAllBytes(outPath, img.EncodeToPNG());
            Object.DestroyImmediate(img);
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Debug.Log("[kuro-promo] wrote " + Path.GetFullPath(outPath));
        }

        Object.DestroyImmediate(camObj);
    }

    private static void Report(GameObject player, KuroBikeRig rig)
    {
        var all = player.GetComponentsInChildren<Transform>(true);
        Transform Find(string n) => all.FirstOrDefault(t => t.name == n);

        var hips = Find("Hips");
        var neck = Find("neck");
        if (hips != null && neck != null)
        {
            var v = neck.position - hips.position;
            // Angle of the torso axis away from world up = the lean a viewer reads.
            float lean = Vector3.Angle(v, Vector3.up);
            Debug.Log(string.Format(
                "[kuro-promo] DELIVERED torso lean = {0:F1} deg (requested spineLean={1}, hipTilt={2}) " +
                "-- target window 30-45",
                lean, rig.spineLeanDegrees, rig.hipTiltDegrees));
        }
        else
        {
            Debug.LogWarning("[kuro-promo] could not find Hips/neck to measure lean.");
        }

        var smr = player.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
        if (smr == null)
        {
            Debug.LogWarning("[kuro-promo] no SkinnedMeshRenderer; skipping mesh-level foot check.");
            return;
        }

        // Bake the POSED mesh. This is the whole point: bone transforms can be perfect while
        // the skinned shoe sits somewhere else entirely.
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices;
        var weights = smr.sharedMesh.boneWeights;
        var bones = smr.bones;

        foreach (var side in new[] { "Left", "Right" })
        {
            int footIdx = System.Array.FindIndex(bones, b => b != null && b.name == side + "Foot");
            int toeIdx = System.Array.FindIndex(bones, b => b != null && b.name == side + "ToeBase");
            if (footIdx < 0)
            {
                Debug.LogWarning("[kuro-promo] missing " + side + "Foot bone.");
                continue;
            }

            var shoe = new System.Collections.Generic.List<Vector3>();
            int n = Mathf.Min(verts.Length, weights.Length);
            for (int i = 0; i < n; i++)
            {
                var w = weights[i];
                float m = 0f;
                if (w.boneIndex0 == footIdx || w.boneIndex0 == toeIdx) m += w.weight0;
                if (w.boneIndex1 == footIdx || w.boneIndex1 == toeIdx) m += w.weight1;
                if (w.boneIndex2 == footIdx || w.boneIndex2 == toeIdx) m += w.weight2;
                if (w.boneIndex3 == footIdx || w.boneIndex3 == toeIdx) m += w.weight3;
                if (m > 0.5f) shoe.Add(smr.transform.TransformPoint(verts[i]));
            }
            if (shoe.Count == 0)
            {
                Debug.LogWarning("[kuro-promo] no shoe verts for " + side);
                continue;
            }

            float lo = shoe.Min(p => p.y), hi = shoe.Max(p => p.y);
            float cut = lo + (hi - lo) * SoleFraction;
            var sole = shoe.Where(p => p.y <= cut).ToList();
            float soleY = sole.Average(p => p.y);

            var pedal = player.GetComponentsInChildren<Transform>(true)
                              .FirstOrDefault(t => t.name == "Pedal_" + side.Substring(0, 1));
            if (pedal == null)
            {
                Debug.LogWarning("[kuro-promo] no Pedal_" + side.Substring(0, 1) + " socket.");
                continue;
            }

            float dz = soleY - pedal.position.y;
            Debug.Log(string.Format(
                "[kuro-promo] {0} foot: sole Y={1:F4} pedal Y={2:F4} -> dz={3:+0.0000;-0.0000} m " +
                "({4} verts, sole patch {5}) [negative = shoe BELOW pedal surface]",
                side, soleY, pedal.position.y, dz, shoe.Count, sole.Count));
        }

        Object.DestroyImmediate(baked);
    }
}
