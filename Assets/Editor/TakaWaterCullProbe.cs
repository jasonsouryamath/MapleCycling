using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ONE-OFF CORRECTNESS PROBE for the frozen Kori Lake slab in TakaMountains.
///
/// WHY THIS EXISTS. AzoraHighlands' summit tarn was built by the exact same fan-triangulated
/// horizontal-disc routine (centre vertex + Ring rim vertices, triangles (0, k, k+1)) and its
/// material - like this one - was created through CelMaterial() with no _Cull override, so it
/// inherited MapleRideCelLit's default Cull Back (2). The fan's winding makes the disc's FRONT
/// face point DOWN, so the tarn was entirely backface-culled for any camera looking down at it
/// and only survived in the one near-level grazing capture. TakaMountainsEnvironment.BuildTarn()
/// is a line-for-line twin of that routine, so the same latent defect is expected here.
///
/// This probe settles it with geometry, not opinion: it reads the staged mesh, computes the
/// world-space geometric normal of the first fan triangle, reports the material's actual _Cull,
/// and renders the slab from straight down and from a grazing angle so the conclusion can be
/// SEEN rather than inferred from a number.
/// </summary>
public static class TakaWaterCullProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string LakePath  = "Taka Tarn/Summit Tarn";

    [MenuItem("MapleRide/Environment/Probe Taka Water Cull", priority = 400)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[taka-cull] no RegionDirector."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.TakaMountains;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var go = GameObject.Find(LakePath);
        if (go == null) { Debug.LogWarning($"[taka-cull] '{LakePath}' not found in scene."); return; }

        var mf = go.GetComponent<MeshFilter>();
        var mr = go.GetComponent<MeshRenderer>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null) { Debug.LogWarning("[taka-cull] no mesh."); return; }

        var mat = mr != null ? mr.sharedMaterial : null;
        float cull = (mat != null && mat.HasProperty("_Cull")) ? mat.GetFloat("_Cull") : -1f;
        Debug.Log($"[taka-cull] material '{(mat != null ? mat.name : "none")}' " +
                  $"shader '{(mat != null ? mat.shader.name : "none")}' _Cull={cull} " +
                  "(2=Back, 1=Front, 0=Off, -1=property absent)");

        // Geometric normal of the first fan triangle, in WORLD space.
        var v = mesh.vertices;
        var t = mesh.triangles;
        var tr = go.transform;
        Vector3 a = tr.TransformPoint(v[t[0]]), b = tr.TransformPoint(v[t[1]]), c = tr.TransformPoint(v[t[2]]);
        // Unity is CLOCKWISE-front / left-handed: the front-face normal of (a,b,c) is
        // cross(b-a, c-a).
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;
        Debug.Log($"[taka-cull] first-tri front normal = {n} (n.y={n.y:0.000}); " +
                  (n.y > 0f ? "FRONT FACE POINTS UP - visible from above."
                            : "FRONT FACE POINTS DOWN - backface-culled from above. DEFECT."));

        var bounds = mr.bounds;
        Debug.Log($"[taka-cull] slab bounds centre={bounds.center} size={bounds.size} " +
                  $"tris={t.Length / 3}");

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);
        string tag = System.Environment.GetEnvironmentVariable("MR_CULL_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "before";

        Vector3 lake = bounds.center;
        float r = Mathf.Max(bounds.extents.x, bounds.extents.z);

        // 1. STRAIGHT DOWN (plan view). The decisive shot: a correctly wound disc reads as a
        //    pale slab filling the basin; a culled one shows bare basin floor.
        Shot(dir, $"probe_taka_lake_top_{tag}", lake + Vector3.up * (r * 1.9f), lake, 55f,
             Vector3.forward);

        // 2. STEEP 55-degree look-down - the angle a rider on the col above actually has.
        Vector3 off = new Vector3(0.9f, 0f, -0.44f).normalized * (r * 1.25f);
        Shot(dir, $"probe_taka_lake_steep_{tag}", lake + off + Vector3.up * (r * 1.8f), lake, 55f,
             Vector3.up);

        // 3. GRAZING ~8 degrees - the angle that hid Azora's identical defect.
        Vector3 off2 = new Vector3(0.9f, 0f, -0.44f).normalized * (r * 4.0f);
        Shot(dir, $"probe_taka_lake_graze_{tag}", lake + off2 + Vector3.up * (r * 0.55f), lake, 45f,
             Vector3.up);

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        // Leave the scene exactly as found - no MarkSceneDirty, no save.
        Debug.Log($"[taka-cull] done, tag='{tag}'.");
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, Vector3 worldUp)
    {
        var go = new GameObject("~TakaCullCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, worldUp);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 34000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        var amb = RegionDirector.TakaAmbience;
        fx.bloomThreshold = amb.bloomThreshold; fx.bloomIntensity = amb.bloomIntensity;
        fx.exposure = amb.exposure; fx.saturation = amb.saturation; fx.contrast = amb.contrast;
        fx.lift = amb.lift; fx.gain = amb.gain;
        fx.vignetteStrength = amb.vignette; fx.vignetteSoftness = amb.vignetteSoftness;
        fx.dofFocusDistance = amb.dofFocusDistance; fx.dofFocusRange = amb.dofFocusRange;
        fx.dofFalloff = amb.dofFalloff; fx.dofStrength = amb.dofStrength;
        fx.aerialStart = amb.aerialStart; fx.aerialRange = amb.aerialRange;
        fx.aerialDesaturation = amb.aerialDesaturation; fx.aerialFlatten = amb.aerialFlatten;
        fx.aerialTint = amb.aerialTint; fx.aerialTintAmount = amb.aerialTintAmount;
        fx.mistBaseY = amb.mistBaseY; fx.mistTopY = amb.mistTopY;
        fx.mistStrength = amb.mistStrength; fx.mistStart = amb.mistStart;
        fx.mistColor = amb.mistColor;

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
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[taka-cull] wrote {name}.png");
    }
}
