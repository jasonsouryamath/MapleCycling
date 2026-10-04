using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification harness for the FUJI RIDGE roster staged by <see cref="FujiNpcRoster"/>.
///
/// This exists because a roster that compiles, logs thirty lines and reports a clean scale ratio
/// has not been verified. This project has previously produced "DEFORM: OK", "0.0 degree
/// residual" and "6.7 mm contact" on a visibly destroyed rider, so everything below is built to
/// be LOOKED AT rather than read:
///
///   * <see cref="Measure"/> bakes every staged rider's skinned mesh and reports her real seated
///     height against the canonical reference, so a scale regression is a number that can be
///     compared - but it is only ever the SECOND check.
///   * <see cref="Capture"/> writes the renders that decide it. Three DENSITY frames use the
///     identical camera recipe at the base gate, the mid-climb and the summit shrine, so the
///     clustering the roster is built around can be judged by counting riders in three
///     otherwise-identical photographs. Five LANDMARK frames put a named rider at the landmark
///     her role belongs to, close enough to see posture, hands on the hoods, and kit colour.
///
/// Read-only: it drives RegionDirector to Fuji, captures, restores the previous region and
/// DISCARDS its scene changes. A batchmode run that left the shared boot scene dirty has
/// previously made Unity reopen to an empty Untitled scene for the user.
/// </summary>
public static class FujiNpcRosterCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string NamePrefix = "Fuji NPC ";

    /// <summary>Seated height of the shared sculpt at scale 1, from a BAKED skinned mesh. Every
    /// production roster lands here; a Fuji rider that does not has a scale regression.</summary>
    const float CanonicalSeatedHeight = 1.185f;
    const float HeightToleranceM = 0.12f;

    // The three density stations. SAME camera recipe for all three - that is the entire point:
    // a difference in how many riders are in frame is then a difference in the roster, not in
    // the framing. Distances are the centre of the base cluster, a deliberately empty-ish point
    // on the volcanic ridge, and the centre of the summit cluster.
    // 430 m back put every rider under a pixel; these stand at road level just below each band
    // and look up it, which is the framing the player actually gets.
    static readonly (string name, float centreM, float backM)[] DensityStations =
    {
        ("fuji_density_1_base_gate", 170f, 45f),
        ("fuji_density_2_mid_climb", 3900f, 45f),
        ("fuji_density_3_summit", 12500f, 45f),
    };

    /// <summary>Published length of the 'fuji' segment (FujiRoute.json). Needed because
    /// NPCCyclist.ArcM is measured along the REVERSED route array for oncoming riders, so a
    /// rider staged 60 m above the base gate reports ArcM 12940.</summary>
    const float RouteLengthM = 13000f;

    // One named rider per landmark, chosen because her ROLE is why the landmark is populated.
    static readonly (string rider, string label)[] LandmarkRiders =
    {
        ("Tsukasa", "fuji_rider_base_gate_Tsukasa"),
        ("Kaede", "fuji_rider_cloudbreak_Kaede"),
        ("Naoya", "fuji_rider_strata_Naoya"),
        ("Katsuo", "fuji_rider_weather_hut_Katsuo"),
        ("Minoru", "fuji_rider_knife_edge_Minoru"),
        ("Koji", "fuji_rider_summit_Koji"),
    };

    [MenuItem("MapleRide/NPCs/Capture Fuji Roster", priority = 63)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.RenderDir("fuji_npc");

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[fuji-cap] no RegionDirector in scene."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;

        // The Fuji root is INACTIVE whenever another region is the drawn one, and so are its
        // riders - a capture that skipped this would render an empty mountain and report it as
        // a pass.
        regions.currentRegionId = RegionCatalog.FujiRidge;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var route = FujiRidgeEnvironment.FujiRoute.Load();
        if (route.Count == 0) { Debug.LogWarning("[fuji-cap] route did not load."); return; }

        var envRoot = GameObject.Find(FujiRidgeEnvironment.RootName);
        DiagnosticsCamera.Prime(envRoot != null ? envRoot.transform : null);
        try
        {
            MeasureInternal();

            // ---- density: three identical frames, three different parts of the climb --------
            foreach (var (name, centreM, backM) in DensityStations)
            {
                int i = route.IndexAt(centreM);
                var look = route.Position[i] + Vector3.up * 1.2f;
                var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                     route.SideFlat(i), look,
                                                     backM: backM, lateralM: 2.0f, heightM: 3.0f,
                                                     label: name);
                int inFrame = CountRidersWithin(centreM - 60f, centreM + 260f);
                Debug.Log($"[fuji-cap] {name}: centre {centreM:0} m, {inFrame} roster rider(s) " +
                          $"in the -60..+260 m band it frames.");
                Shot(dir, name, eye, look, 55f);
            }

            // ---- landmark riders: close enough to judge posture, grip and kit ---------------
            foreach (var (rider, label) in LandmarkRiders)
            {
                var go = FindRider(rider);
                if (go == null) { Debug.LogWarning($"[fuji-cap] MISSING {NamePrefix}{rider}"); continue; }

                var b = BakedBounds(go);
                float h = b.size.y;
                // Three-quarter FRONT view. The cast rides the oncoming lane, so their own
                // forward axis points back down the mountain at the player - standing off along
                // it gives the shot the player actually gets, rather than the back of a helmet.
                var fwd = go.transform.forward; fwd.y = 0f;
                fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                var eye = b.center + fwd * 2.9f + right * 1.5f + Vector3.up * 0.55f;
                Shot(dir, label, eye, b.center + Vector3.up * 0.06f, 40f);
                Debug.Log($"[fuji-cap] {label}: baked seated height {h:F3} m at {b.center:F1}");
            }
        }
        finally
        {
            // ALWAYS. A batchmode process that threw partway through a shot list used to leave
            // hundreds of MeshColliders on the scenery, and the next pass saved them.
            DiagnosticsCamera.Release();
        }

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[fuji-cap] restored region '{regions.currentRegionId}'. Renders -> {dir}");

        MapleRideSceneBootstrap.DiscardChanges();
    }

    /// <summary>
    /// Scale proof. Bakes every staged Fuji rider and compares her real seated height with the
    /// canonical figure every other production roster lands on.
    ///
    /// SkinnedMeshRenderer.bounds is the IMPORT-TIME estimate and has reported this same chibi
    /// as 2.09 m tall, so nothing here reads it.
    /// </summary>
    [MenuItem("MapleRide/NPCs/Measure Fuji Roster", priority = 64)]
    public static void Measure()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionCatalog.FujiRidge;
            regions.ApplyEnvironmentVisibility();
        }
        MeasureInternal();
        MapleRideSceneBootstrap.DiscardChanges();
    }

    static void MeasureInternal()
    {
        var riders = AllRiders();
        if (riders.Count == 0) { Debug.LogWarning("[fuji-cap] no Fuji riders staged."); return; }

        int bad = 0;
        var heights = new List<float>();
        foreach (var go in riders)
        {
            var b = BakedBounds(go);
            float h = b.size.y;
            heights.Add(h);
            bool ok = Mathf.Abs(h - CanonicalSeatedHeight) <= HeightToleranceM;
            if (!ok) bad++;

            var cyc = go.GetComponent<NPCCyclist>();
            var grt = go.GetComponent<NpcGreeting>();
            var rig = go.GetComponentInChildren<CoralBikeRig>(true);
            float riderScale = rig != null ? rig.transform.localScale.y : -1f;
            Debug.Log($"[fuji-cap] {go.name,-24} h={h:F3} m ({(ok ? "OK" : "OUT OF RANGE")}) " +
                      $"rigScale={riderScale:F3} above-gate={(cyc != null ? BaseGateDistance(cyc) : -1f):F0} m " +
                      $"speed={(cyc != null ? cyc.speed : -1f):F2} m/s " +
                      $"lines={(grt != null && grt.ownPhrases != null ? grt.ownPhrases.Length : -1)}");
        }

        Debug.Log($"[fuji-cap] {riders.Count} rider(s); baked seated height " +
                  $"min {heights.Min():F3} / mean {heights.Average():F3} / max {heights.Max():F3} m " +
                  $"against canonical {CanonicalSeatedHeight:F3} m; {bad} outside " +
                  $"+/-{HeightToleranceM:F2} m.");

        // Density, reported as the thing the design doc actually asks for.
        Debug.Log($"[fuji-cap] density: base gate 0-700 m = {CountRidersWithin(0f, 700f)}, " +
                  $"mid climb 6400-9500 m = {CountRidersWithin(6400f, 9500f)}, " +
                  $"summit 12300-13000 m = {CountRidersWithin(12300f, 13000f)}.");
    }

    static List<GameObject> AllRiders() =>
        Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include, FindObjectsSortMode.None)
              .Where(c => c.gameObject.name.StartsWith(NamePrefix))
              .Select(c => c.gameObject)
              .OrderBy(g => g.name)
              .ToList();

    /// <summary>Distance of a staged rider ABOVE THE BASE GATE, i.e. the number the roster was
    /// authored in. ArcM runs the other way for reversed (oncoming) riders - see RouteLengthM.</summary>
    static float BaseGateDistance(NPCCyclist c) =>
        c.reverse ? RouteLengthM - c.ArcM : c.ArcM;

    static int CountRidersWithin(float fromM, float toM) =>
        Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include, FindObjectsSortMode.None)
              .Count(c => c.gameObject.name.StartsWith(NamePrefix) &&
                          BaseGateDistance(c) >= fromM && BaseGateDistance(c) <= toM);

    static GameObject FindRider(string rider) =>
        Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include, FindObjectsSortMode.None)
              .Where(c => c.gameObject.name == NamePrefix + rider)
              .Select(c => c.gameObject)
              .FirstOrDefault();

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

    /// <summary>
    /// Copy of FujiRidgeDiagnostics' camera recipe, including the FULL region Ambience push.
    ///
    /// A fresh SakuraPostFX carries the SUNSET defaults - a warm peach aerial tint starting
    /// 110 m from the lens and a mist band tuned for a 40 m-high valley floor. On a region whose
    /// ground sits between 776 and 1,776 m that mist band is saturated everywhere, which is the
    /// flat white wash that once drowned a mountain capture entirely. Every field the region
    /// owns has to be pushed, not just the grade.
    /// </summary>
    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~FujiRosterCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 34000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.FujiAmbience;
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofFocusDistance = a.dofFocusDistance; fx.dofFocusRange = a.dofFocusRange;
        fx.dofFalloff = a.dofFalloff; fx.dofStrength = a.dofStrength;
        fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange;
        fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
        fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
        fx.mistBaseY = a.mistBaseY; fx.mistTopY = a.mistTopY;
        fx.mistStrength = a.mistStrength; fx.mistStart = a.mistStart;
        fx.mistColor = a.mistColor;

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
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(go);
        Debug.Log($"[fuji-cap] wrote {name}.png");
    }
}
