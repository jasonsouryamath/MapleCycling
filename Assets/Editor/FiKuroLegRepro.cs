using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FIX-IMPLEMENTER reproduction: renders the PLAYER "Kuro on Sakura Pass" from a CLOSE rear
/// camera across the full pedal stroke, so the lower-body mangling reported in
/// reference/improve/PROBLEM_kuro_lower_body_mangled_rear.png can be reproduced and later
/// verified. Read-only: never saves the scene.
///
/// For each crank phase it also logs the two-bone leg IK state (thigh len, shin len, hip->pedal
/// target distance, resulting knee fold angle) so the "which phase folds hardest" question is
/// answered numerically as well as by the render.
/// </summary>
public static class FiKuroLegRepro
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    const string Player = "Kuro on Sakura Pass";

    public static void Run() { Run(0f, "low"); }
    public static void RunBig() { Run(1.35f, "big"); }

    /// Renders the RAW saved scene pose the way a naive batchmode capture does: NO Setup, NO
    /// ForceSolveOnce, NO forceMatrixRecalculationPerRender. This reproduces the documented
    /// "batchmode re-draws a stale/first pose" hazard that hid broken renders on this project.
    public static void RunRaw()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/fi_legrepro"));
        Directory.CreateDirectory(dir);
        var player = GameObject.Find(Player);
        if (player == null) { Debug.LogError("[fi-legrepro] player not found"); EditorApplication.Exit(0); return; }
        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 origin = player.transform.position;
        Debug.Log("[fi-legrepro] RAW saved-pose capture (no re-solve, no per-render recalc)");
        Shot(dir, "raw_rear", origin - f * 1.45f + up * 0.28f, origin + up * 0.72f, 46f);
        Shot(dir, "raw_left", origin - r * 1.7f + up * 0.35f + f * 0.1f, origin + up * 0.62f, 44f);
        Debug.Log("[fi-legrepro] done");
        EditorApplication.Exit(0);
    }

    static void Run(float overrideBikeScale, string tag)
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/fi_legrepro"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find(Player);
        if (player == null) { Debug.LogError("[fi-legrepro] player not found"); EditorApplication.Exit(0); return; }

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig == null) { Debug.LogError("[fi-legrepro] no KuroBikeRig"); EditorApplication.Exit(0); return; }
        var pose = player.GetComponent<KuroRidePose>();

        // Optional: force a different bike scale to reproduce a PRE-REFIT state (never saved).
        if (overrideBikeScale > 0f)
        {
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
                if (t.name == "Bike") { t.localScale = Vector3.one * overrideBikeScale; }
            rig.wheelRadius = 0.175f * overrideBikeScale;
            Debug.Log($"[fi-legrepro] OVERRIDE bike scale -> {overrideBikeScale} (not saved)");
        }

        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        if (pose != null)
        {
            pose.SelectPosture(KuroRidePose.CyclingPosture.RoadRacerAggressive, false);
            pose.enableClimbOverlay = false;
            pose.enableSprintOverlay = false;
            pose.SprintOverride = 0f;
            pose.Tick(1f);
        }
        rig.ForceSolveOnce();

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            s.forceMatrixRecalculationPerRender = true;
            s.updateWhenOffscreen = true;
        }

        // bones for measurement
        Transform Find(string n)
        {
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
                if (t.name == n) return t;
            return null;
        }
        Transform upLegL = Find("LeftUpLeg"), legL = Find("LeftLeg"), footL = Find("LeftFoot");
        Transform upLegR = Find("RightUpLeg"), legR = Find("RightLeg"), footR = Find("RightFoot");
        Transform pedalL = Find("Pedal_L"), pedalR = Find("Pedal_R");
        Transform hips = Find("Hips");
        var skin = player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0)
            .FirstOrDefault();

        // Log the actual serialized fit so the pose we reproduce is the pose shipped.
        Transform bikeAnchor = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
            if (t.name == "Bike") { bikeAnchor = t; break; }
        Debug.Log($"[fi-legrepro] playerScale={player.transform.localScale} bikeScale={(bikeAnchor ? bikeAnchor.localScale.ToString() : "n/a")} " +
                  $"wheelRadius={rig.wheelRadius} hipTilt={rig.hipTiltDegrees} spineLean={rig.spineLeanDegrees} ankleHeight={rig.ankleHeight}");

        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 origin = player.transform.position;

        int[] cranks = { 0, 90, 180, 270 };
        float prev = 0f;
        foreach (int target in cranks)
        {
            rig.AdvanceCrank(target - prev);
            prev = target;

            Debug.Log($"[fi-legrepro] crank={target:F0} L-leg " +
                      LegMeasurement(player.transform, upLegL, legL, footL, pedalL, hips) +
                      " soleGap=" + SoleGap(skin, "Left", pedalL).ToString("+0.0;-0.0") + " mm");
            Debug.Log($"[fi-legrepro] crank={target:F0} R-leg " +
                      LegMeasurement(player.transform, upLegR, legR, footR, pedalR, hips) +
                      " soleGap=" + SoleGap(skin, "Right", pedalR).ToString("+0.0;-0.0") + " mm");

            // LOW rear framing, looking UP at the thighs/knees/inner-thigh where the PROBLEM shows mangling.
            Shot(dir, $"{tag}_rear_crank{(int)target:D3}", origin - f * 1.45f + up * 0.28f, origin + up * 0.72f, 46f);
            // LOW left-profile framing, so the knee fold and any thigh/shin interpenetration is unambiguous.
            Shot(dir, $"{tag}_left_crank{(int)target:D3}", origin - r * 1.7f + up * 0.35f + f * 0.1f, origin + up * 0.62f, 44f);
        }

        static string LegMeasurement(
            Transform player, Transform upper, Transform lower, Transform foot,
            Transform pedal, Transform hips)
        {
            if (!upper || !lower || !foot || !pedal || !hips) return "n/a";
            float a = Vector3.Distance(upper.position, lower.position);
            float b = Vector3.Distance(lower.position, foot.position);
            float reach = Vector3.Distance(upper.position, foot.position);
            float knee = Vector3.Angle(
                upper.position - lower.position, foot.position - lower.position);
            float hipToFoot = Vector3.Distance(hips.position, foot.position);
            float kneeX = player.InverseTransformPoint(lower.position).x;
            float pedalX = player.InverseTransformPoint(pedal.position).x;
            return string.Format(
                "thigh={0:F3} shin={1:F3} reach={2:F3}(max {3:F3}) kneeAngle={4:F1} " +
                "hipToFoot={5:F3} kneeX={6:+0.000;-0.000} pedalX={7:+0.000;-0.000}",
                a, b, reach, a + b, knee, hipToFoot, kneeX, pedalX);
        }

        static float SoleGap(SkinnedMeshRenderer skin, string side, Transform pedal)
        {
            if (skin == null || pedal == null) return float.NaN;
            int footIndex = System.Array.FindIndex(
                skin.bones, b => b != null && b.name == side + "Foot");
            int toeIndex = System.Array.FindIndex(
                skin.bones, b => b != null && b.name == side + "ToeBase");
            if (footIndex < 0) return float.NaN;

            var baked = new Mesh();
            skin.BakeMesh(baked, true);
            var vertices = baked.vertices;
            var weights = skin.sharedMesh.boneWeights;
            var shoe = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < vertices.Length && i < weights.Length; i++)
            {
                BoneWeight weight = weights[i];
                float influence = 0f;
                if (weight.boneIndex0 == footIndex || weight.boneIndex0 == toeIndex) influence += weight.weight0;
                if (weight.boneIndex1 == footIndex || weight.boneIndex1 == toeIndex) influence += weight.weight1;
                if (weight.boneIndex2 == footIndex || weight.boneIndex2 == toeIndex) influence += weight.weight2;
                if (weight.boneIndex3 == footIndex || weight.boneIndex3 == toeIndex) influence += weight.weight3;
                if (influence > 0.5f)
                    shoe.Add(skin.transform.TransformPoint(vertices[i]));
            }
            Object.DestroyImmediate(baked);
            if (shoe.Count == 0) return float.NaN;
            float low = shoe.Min(point => point.y);
            float high = shoe.Max(point => point.y);
            float cut = low + (high - low) * 0.12f;
            float soleY = shoe.Where(point => point.y <= cut).Average(point => point.y);
            return (soleY - pedal.position.y) * 1000f;
        }

        Debug.Log("[fi-legrepro] done");
        EditorApplication.Exit(0);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~FiLegCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var a = RegionDirector.SakuraAmbience;
        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 900, h = 1100;
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
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[fi-legrepro] wrote {name}.png");
    }
}
