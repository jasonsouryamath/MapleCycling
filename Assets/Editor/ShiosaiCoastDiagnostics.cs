using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification renders of the Shiosai Coast region.
///
/// Shiosai and Sakura Pass share ONE scene, one skybox slot, one key light and one post-FX
/// grade, so a capture that simply opened the scene and pointed a camera at the coast would
/// render it under the *pass's* sunset - which is the exact failure this milestone exists to
/// fix. Every capture therefore drives <see cref="RegionDirector"/> into the coast region first
/// (environment visibility + the full daylight ambience) and restores Sakura afterwards, so the
/// saved scene is left exactly as it was found.
/// </summary>
public static class ShiosaiCoastDiagnostics
{
    /// <summary>
    /// When set, <see cref="Shot"/> grades with this ambience instead of the region default.
    /// Used to give the SC08 finale its warm "golden light" without touching the shared region
    /// grade. Cleared immediately after the shot.
    /// </summary>
    private static RegionDirector.Ambience? _gradeOverride;

    /// <summary>
    /// Scene the coast is captured from: the SHARED, PLAYABLE SakuraPass scene - deliberately
        /// the same constant the build pass uses.
        ///
        /// DO NOT restore the File.Exists(ShiosaiSceneBuilder.ShiosaiScenePath) preference. A
        /// verification pass that silently captures a different scene from the one the game loads
        /// does not verify anything: it produced a full set of beautiful coast renders while the
        /// player's scene was untouched. Verification must target the playable scene, always.
        /// </summary>
        private static string ScenePath => ShiosaiCoastEnvironment.ScenePath;

    [MenuItem("MapleRide/Environment/Capture Shiosai Coast Views", priority = 31)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[shiosai-diag] no RegionDirector in scene."); return; }
        regions.Resolve();

        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[shiosai-diag] region -> {regions.currentRegionId}, " +
                  $"skybox '{(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}', " +
                  $"sun {(RenderSettings.sun != null ? RenderSettings.sun.transform.eulerAngles.ToString() : "none")}");

        LogBounds();

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();

        // Rider's-eye stop in each of the eight chapters (spec section 2), keyed to ABSOLUTE
        // chainage rather than a fraction of the route: the fractions were chosen against the
        // 2.892 km mock and a chapter is a chainage span, so a fraction would silently drift out
        // of its chapter the next time the route length changes.
        //
        // EACH STOP IS PLACED ~150-200 m SHORT OF ITS CHAPTER'S HERO LANDMARK, so the capture
        // answers the question it exists to answer ("is the landmark there, and is it framed?")
        // rather than showing an arbitrary 55-degree slice of the same coast road eight times.
        // The landmark chainages are the published route anchors (spec 4.4) that
        // ShiosaiCoastEnvironment now builds against - keep the two in step.
        // ROUTE LENGTH REVISION: these were authored on the 42 km course; each has been
        // remapped through its chapter onto the ~20 km course. PROVISIONAL framing.
        var stops = new (string name, float metres, float fov)[]
        {
            ("diag_shiosai_ch1_gateway", 885f, 55f),       // Mountain Gateway arch @ 930 m
            ("diag_shiosai_ch3_hydrangea", 4155f, 55f),    // Shrine Overlook torii @ 4,203 m
            ("diag_shiosai_ch4_village", 5100f, 55f),      // Fishing Village @ 5,204 m
            ("diag_shiosai_ch5_redbridge", 7609f, 55f),    // Red Bridge midpoint @ 7,706 m
            ("diag_shiosai_ch6_lighthouse", 10838f, 55f),  // Lighthouse summit @ 10,939 m
            ("diag_shiosai_ch7_seaarch", 12725f, 55f),     // Sea-arch gate @ 12,811 m
            // --- the Ch3 dark tunnel and its harbour reveal (spec 5.3) ---------------------
            // PROVISIONAL stations. The bore runs 4,654 -> 4,804 m.
            ("diag_shiosai_tunnel_approach", 4620f, 55f),  // approach: the portal ahead
            ("diag_shiosai_tunnel_interior", 4720f, 60f),  // INSIDE the bore - must not be
                                                           // black/culled; proves _Cull = 0
            // ch2 (Upper Switchbacks) and ch8 (Island Reveal finale) are NOT rider's-eye stops:
            // both are hero VISTAS that a 6 m-behind eye cannot frame, so they get dedicated
            // stand-off cameras below.
        };
        foreach (var (name, metres, fov) in stops)
        {
            int i = Mathf.Clamp(route.IndexAt(Mathf.Min(metres, route.Length * 0.999f)),
                                0, route.Count - 1);
            int ahead = Mathf.Min(route.Count - 1, i + 30);
            var eye = route.Position[i] + Vector3.up * 2.4f - route.Tangent[i] * 6f
                      - route.SideFlat(i) * 1.2f;
            var look = route.Position[ahead] + Vector3.up * 1.4f;
            Debug.Log($"[shiosai-diag] {name}: d={route.Distance[i]:0} m eye {eye}");
            Shot(dir, name, eye, look, fov, true);
        }

        // ---- the Ch3 reveal: emerging from the dark bore onto the port town ------------------
        // The generic stop above only looks 90 m ahead, which is the wrong question here. This
        // one stands just inside the mouth and looks a long way down the road, so the frame is
        // dark portal ring -> bright harbour. PROVISIONAL stations.
        foreach (var (tag, at, fwd, fov) in new (string, float, float, float)[]
                 { ("diag_shiosai_tunnel_exit", 4778f, 620f, 58f),
                   ("diag_shiosai_tunnel_reveal", 4812f, 900f, 50f) })
        {
            int i = Mathf.Clamp(route.IndexAt(at), 0, route.Count - 1);
            int ahead = Mathf.Clamp(route.IndexAt(at + fwd), 0, route.Count - 1);
            var eye = route.Position[i] + Vector3.up * 2.6f - route.Tangent[i] * 7f;
            var look = route.Position[ahead] + Vector3.up * 1.0f;
            Debug.Log($"[shiosai-diag] {tag}: d={route.Distance[i]:0} m eye {eye} -> {look}");
            Shot(dir, tag, eye, look, fov, true);
        }

        // ---- SC02 Upper Switchbacks vista (spec 5.2: >=4 traceable connected hairpins) -------
        // The ladder IS built into the route (the centreline folds +/-160 deg twelve times while
        // descending 420 -> 150 m over 6-11 km), but a rider's-eye stop on one leg only ever
        // sees a single straight leg. A side-on stand-off is no good either: the seaward flank is
        // now a continuous cliff that occludes the lower legs from the side. So frame the middle
        // of the ladder from a STEEP near-overhead oblique, looking down onto the zigzag where no
        // terrain wall can hide it - which is where the connected hairpins actually trace.
        {
            int lo = route.IndexAt(2010f), hi = route.IndexAt(2970f);
            float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float sumX = 0f, sumY = 0f, sumZ = 0f; int nn = 0;
            for (int i = lo; i <= hi; i++)
            {
                var q = route.Position[i];
                if (q.x < minX) minX = q.x; if (q.x > maxX) maxX = q.x;
                if (q.z < minZ) minZ = q.z; if (q.z > maxZ) maxZ = q.z;
                if (q.y > maxY) maxY = q.y;
                sumX += q.x; sumY += q.y; sumZ += q.z; nn++;
            }
            var centre = new Vector3(sumX / nn, sumY / nn, sumZ / nn);
            var eye = new Vector3(centre.x + 280f, maxY + 540f, centre.z + 320f);
            Debug.Log($"[shiosai-diag] ch2 switchbacks vista: eye {eye} -> {centre}");
            Shot(dir, "diag_shiosai_ch2_switchbacks", eye, centre, 56f, true);
        }


        // ---- SC08 Island Reveal finale (spec 5.8) --------------------------------------------
        // The Coastal Highway payoff: distant islands growing into the objective, the overlook
        // turnaround, and warm golden finale light. Framed from an elevated stand-off just inland
        // and behind the reveal point, looking forward and seaward across the island cluster that
        // BuildFinaleIslands seats off the bow.
        {
            int i = route.IndexAt(18355f);
            var p = route.Position[i];
            var fwd = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            var sideF = route.SideFlat(i);   // seaward is -sideF
            var eye = p + Vector3.up * 52f + sideF * 46f - fwd * 46f;
            var look = p + fwd * 560f - sideF * 430f + Vector3.up * 4f;
            var warm = RegionDirector.ShiosaiAmbience;
            warm.exposure = 0.86f;
            warm.saturation = 1.30f;
            warm.contrast = 1.12f;
            warm.gain = new Color(1.20f, 1.02f, 0.78f, 0f);   // strong golden sunlight
            warm.lift = new Color(0.050f, 0.020f, 0.0f, 0f);  // warm shadow
            warm.bloomThreshold = 1.12f; warm.bloomIntensity = 0.36f;
            _gradeOverride = warm;
            // Golden finale LIGHT, not just grade: post-FX gain alone cannot repaint the
            // skybox/fog blue, so warm the scene's fog and sun for this one shot and restore
            // them before any later capture runs. The zenith sky stays blue (correct for golden
            // hour); the horizon, sea and lit terrain go warm.
            var sun = RenderSettings.sun;
            bool fogWas2 = RenderSettings.fog; var fogColWas = RenderSettings.fogColor;
            Color sunColWas = default; float sunIntWas = 0f; Quaternion sunRotWas = default;
            RenderSettings.fogColor = new Color(0.72f, 0.74f, 0.76f, 1f);
            float fogDensWas = RenderSettings.fogDensity;
            // THE GOLD HORIZON BAND LIVED HERE, and it was this shot's own styling - not a fault
            // in the ocean, the sky, the clouds, the backdrop or the camera far plane (all five
            // were probed and cleared). Fog is ExponentialSquared at Shiosai's density 0.00026,
            // so f = exp(-(d*density)^2) is already ~0.82 at 5 km and indistinguishable from 1
            // by 8 km. A fully saturated warm fogColour therefore repainted EVERY distant pixel
            // - the whole far ocean included - into one solid tan bar across the horizon, which
            // is exactly the (230,191,136) gold strip measured in the render.
            //
            // Golden hour is still the intent, so keep the warm sun and grade, but pull the fog
            // colour back toward the sea and thin the fog so the far water never fully reaches
            // it. The sea then stays cobalt out to a clean sea/sky horizon while the sun side
            // still reads golden. PROVISIONAL, tuned against diag_shiosai_ch8_highway.png.
            RenderSettings.fogDensity = 0.00009f;
            if (sun != null)
            {
                sunColWas = sun.color; sunIntWas = sun.intensity; sunRotWas = sun.transform.rotation;
                sun.color = new Color(1f, 0.86f, 0.62f, 1f);
                sun.intensity *= 1.06f;
                sun.transform.rotation = Quaternion.Euler(22f, 118f, 0f);   // low, raking finale sun
            }
            Debug.Log($"[shiosai-diag] ch8 finale vista: eye {eye} -> {look}");
            Shot(dir, "diag_shiosai_ch8_highway", eye, look, 58f, true);
            _gradeOverride = null;
            RenderSettings.fog = fogWas2; RenderSettings.fogColor = fogColWas;
            RenderSettings.fogDensity = fogDensWas;
            if (sun != null)
            {
                sun.color = sunColWas; sun.intensity = sunIntWas; sun.transform.rotation = sunRotWas;
            }
        }

        // Seaward views: the whole point of the region is the water on the rider's left, which no
        // forward-facing stop contains.
        SeaView(dir, "diag_shiosai_seaview_cliff", route, 0.34f, 4f, -34f);
        SeaView(dir, "diag_shiosai_seaview_cove", route, 0.55f, 3f, -18f);
        SeaView(dir, "diag_shiosai_seaview_light", route, 0.86f, 5f, -30f);

        // Headland overlook: stand off the cliff and look back down the winding road.
        {
            int i = route.IndexAt(route.Length * 0.80f);
            int back = route.IndexAt(route.Length * 0.52f);
            var eye = route.Position[i] + Vector3.up * 95f
                      - route.SideFlat(i) * 210f - route.Tangent[i] * 120f;
            Shot(dir, "diag_shiosai_overlook", eye, route.Position[back] + Vector3.up * 10f, 52f, true);
        }

        // The whole coast from above: proves the winding cliff-road layout reads like the map.
        // Framed from the ROUTE'S BOUNDING BOX - a fixed 1,500 m eye over the midpoint framed the
        // 2.9 km mock but sees about 6% of a 42 km region.
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < route.Count; i++)
            {
                var q = route.Position[i];
                if (q.x < minX) minX = q.x; if (q.x > maxX) maxX = q.x;
                if (q.z < minZ) minZ = q.z; if (q.z > maxZ) maxZ = q.z;
            }
            var c = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            float span = Mathf.Max(maxX - minX, maxZ - minZ);
            // ORTHOGRAPHIC, not a 24 km-high perspective eye. Framing a 25 km region with a
            // 60 deg lens put the camera 19-24 km up, which (a) sat outside Shot()'s hard-coded
            // 14 km far plane so the whole region was clipped away and the render was a blank
            // grey skybox, and (b) even once the far plane followed the shot, pushed every
            // renderer past HDRP's fog/scattering distance. A map shot wants a map projection.
            float eyeUp = Mathf.Max(3000f, route.MaxY + 2000f);
            Ortho(dir, "diag_shiosai_overhead", c + Vector3.up * eyeUp, c, span * 0.56f);
            Debug.Log($"[shiosai-diag] overhead: span {span:0} m, ortho size {span * 0.56f:0}, " +
                      $"eye {eyeUp:0} m over {c}.");
        }

        AimAt(dir, "diag_shiosai_lighthouse", "Shiosai Light", 95f, 42f, 46f);
        AimAt(dir, "diag_shiosai_harbour", "Shiosai Harbour", 190f, 80f, 50f);
        AimAt(dir, "diag_shiosai_sea_torii", "Shiosai Sea Torii", 46f, 12f, 42f);
        AimAt(dir, "diag_shiosai_gateway_arch", "Shiosai Mountain Gateway", 34f, 10f, 48f);
        AimAt(dir, "diag_shiosai_seaarch_gate", "Shiosai Sea Arch", 70f, 26f, 50f);
        AimAt(dir, "diag_shiosai_redbridge", "Shiosai Red Bridge", 150f, 55f, 48f);

        // Leave the scene as we found it.
        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[shiosai-diag] restored region '{regions.currentRegionId}'.");
    }

    /// <summary>
    /// Rider's-eye renders of the ride START (course arc 0). This is what the player looks at
    /// during the 3-2-1 countdown. Used to verify the checkered start line + start flag replace
    /// the old "out of bounds" road strip. Placed exactly like the environment builder does:
    /// route.Position[0], tangent, side.
    /// </summary>
    [MenuItem("MapleRide/Environment/Capture Shiosai Start Line", priority = 32)]
    public static void CaptureStart()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[shiosai-diag] no RegionDirector in scene."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        int i = 0;
        var p = route.Position[i];
        var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
        var s = route.SideFlat(i);

        // 1) Chase-cam-alike: ~6 m behind the start, 2.4 m up, looking down the road - the frame
        //    the player sees at the countdown.
        {
            var eye = p - t * 6.5f + Vector3.up * 2.4f - s * 0.6f;
            var look = p + t * 22f + Vector3.up * 1.4f;
            Debug.Log($"[shiosai-diag] start chase: eye {eye} at arc0 {p}");
            Shot(dir, "diag_shiosai_start_chase", eye, look, 55f, true);
        }
        // 2) Low three-quarter looking across the carriageway, so the line laid ON the road is
        //    obviously flat/flush and not floating or sunk.
        {
            var eye = p - t * 10f + Vector3.up * 3.6f - s * 7f;
            var look = p + t * 3f + Vector3.up * 0.5f;
            Debug.Log($"[shiosai-diag] start 3q: eye {eye}");
            Shot(dir, "diag_shiosai_start_3q", eye, look, 50f, true);
        }
        // 3) Forward from just ahead looking back at the line + flag, framing the whole feature.
        {
            var eye = p + t * 26f + Vector3.up * 4.5f + s * 5f;
            var look = p + Vector3.up * 2.5f;
            Debug.Log($"[shiosai-diag] start reverse: eye {eye}");
            Shot(dir, "diag_shiosai_start_reverse", eye, look, 52f, true);
        }

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[shiosai-diag] start-line capture done, restored '{regions.currentRegionId}'.");
    }

    /// <summary>
    /// VERGE CHECK - rider's-eye frames that reproduce the two gameplay screenshots the user
    /// reported the roadside defects from (Assets/Environment/badcliff.png at KM 0.54 and
    /// messedup.png at KM 2.09): seaward guardrail filling the left of frame, the hydrangea
    /// bank lining both verges. Anything claimed fixed on those two defects has to be verified
    /// HERE, by looking at these PNGs - not by a build log.
    /// </summary>
    [MenuItem("MapleRide/Environment/Capture Shiosai Verge Check", priority = 35)]
    public static void CaptureVergeCheck()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[shiosai-diag] no RegionDirector in scene."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        // (tag, chainage m, eye height m, back m) - the two reported frames plus a close
        // three-quarter stand-off on each so the guardrail's posts/beam junction is legible.
        foreach (var (tag, chainage) in
                 new[] { ("km054", 540f), ("km209", 2090f) })
        {
            int i = route.IndexAt(chainage);
            var p = route.Position[i];
            var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            var s = route.SideFlat(i);

            // 1) The reported framing: chase camera behind the rider, looking up the road.
            {
                var eye = p - t * 6.0f + Vector3.up * 2.35f;
                var look = p + t * 26f + Vector3.up * 0.9f;
                Debug.Log($"[shiosai-diag] verge {tag}: chase eye {eye} at {chainage:0} m");
                Shot(dir, $"diag_shiosai_verge_{tag}", eye, look, 55f, true);
            }
            // 2) Close on the SEAWARD guardrail: 3 m out, 1.4 m up, looking along the rail, so
            //    a floating post or a broken beam joint cannot hide behind distance.
            {
                var eye = p - t * 3f + Vector3.up * 1.4f + s * 1.5f;
                var look = p + t * 16f - s * 5.2f + Vector3.up * 0.7f;
                Shot(dir, $"diag_shiosai_verge_{tag}_rail", eye, look, 46f, true);
            }
            // 3) Close on the hydrangea bank on the inland verge.
            {
                var eye = p - t * 4f + Vector3.up * 1.6f + s * 2.0f;
                var look = p + t * 10f + s * 9.5f + Vector3.up * 0.5f;
                Shot(dir, $"diag_shiosai_verge_{tag}_flora", eye, look, 44f, true);
            }
        }

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[shiosai-diag] verge check done, restored '{regions.currentRegionId}'.");
    }

    // ======================================================================================
    //  HARBOUR OVERLOOK  (art-director reference Assets/Environment/ShiosaiCoast/
    //                     ShiosaiCoast_06.png)
    // ======================================================================================
    //
    // The reference is a HIGH COASTAL-ROAD OVERLOOK: a curving two-lane road with guardrail
    // across the near foreground, the fishing harbour + breakwater + lighthouse + boats filling
    // the mid-ground, turquoise shallows grading to deep blue, and forested headlands hazing
    // off behind.
    //
    // WHY THE CAMERA IS NOT ON THE ROAD. The published centreline puts the Fishing Village
    // chapter at SEA LEVEL: the carriageway is y = 7-14 m for the whole 16.0-20.0 km chapter
    // and the harbour basin is at y = 0. There is therefore no point ON the road from which the
    // harbour can be looked DOWN on - a rider's-eye stop there is a flat, side-on view, which is
    // exactly what diag_shiosai_ch4_village.png already shows. Rather than invent a clifftop
    // spur (new terrain + road + guardrail = a different milestone), this shot stands the camera
    // on the hillside ABOVE the road, so the coast road and its guardrail sweep through the
    // lower third of the frame and the harbour sits ~500 m out and ~60 m below - the reference's
    // geometry, taken from ground the region already has.
    //
    // ALL VALUES BELOW ARE PROVISIONAL FRAMING TUNABLES, not design requirements. They were
    // chosen on a render (Probe Shiosai Harbour Overlook writes the candidate ladder).

    /// <summary>Chainage the overlook eye stands at, metres. Village centre is 5,204 m
    /// on the revised ~20 km route (was 17,720 m of 42 km).</summary>
    private const float OverlookChainageM = 5052f;
    /// <summary>Metres INLAND of the centreline (the seaward side is negative).</summary>
    private const float OverlookInlandM = 80f;
    /// <summary>Metres above the carriageway at that station.</summary>
    private const float OverlookHeightM = 50f;
    /// <summary>Lens. 38 deg fills the mid-ground with the basin the way the reference does.</summary>
    private const float OverlookFov = 38f;
    /// <summary>Aim bias from the harbour centre toward the mole head, 0..1.</summary>
    private const float OverlookAimToMole = 0.55f;

    [MenuItem("MapleRide/Environment/Capture Shiosai Harbour Overlook", priority = 33)]
    public static void CaptureHarbourOverlook() => HarbourOverlook(false);

    [MenuItem("MapleRide/Environment/Probe Shiosai Harbour Overlook", priority = 34)]
    public static void ProbeHarbourOverlook() => HarbourOverlook(true);

    private static void HarbourOverlook(bool probe)
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[shiosai-diag] no RegionDirector in scene."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();

        // ---- the subject: harbour basin, biased toward the mole head ------------------------
        Vector3 harbour = HarbourAim(out Vector3 moleHead, out bool found);
        if (!found)
        {
            Debug.LogWarning("[shiosai-diag] harbour landmarks not in scene - overlook skipped.");
            regions.currentRegionId = previous;
            regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience();
            return;
        }
        Debug.Log($"[shiosai-diag] overlook subject: harbour {harbour}, mole head {moleHead}");

        if (probe)
        {
            // (chainage, inland, height, fov) ladder. Judged on the render, not on a metric.
            var ladder = new (string tag, float m, float inland, float up, float fov)[]
            {
                ("g", 5160f,  60f, 40f, 46f),
                ("h", 5052f,  80f, 50f, 44f),
                ("i", 5200f,  50f, 34f, 42f),
                ("j", 5015f, 100f, 62f, 44f),
            };
            foreach (var (tag, m, inland, up, fov) in ladder)
            {
                var eye = OverlookEye(route, m, inland, up);
                var look = Vector3.Lerp(harbour, moleHead, OverlookAimToMole);
                float rng = Vector3.Distance(eye, look);
                Debug.Log($"[shiosai-diag] overlook probe {tag}: eye {eye} -> {look} " +
                          $"(range {rng:0} m, drop {eye.y - look.y:0} m)");
                Shot(dir, $"diag_shiosai_overlook_probe_{tag}", eye, look, fov, true);
                if (tag == "h")
                    Shot(dir, "diag_shiosai_overlook_probe_h_dof", eye, look, fov, true,
                         Vector3.up, DofFor(rng));
            }
        }
        else
        {
            var eye = OverlookEye(route, OverlookChainageM, OverlookInlandM, OverlookHeightM);
            var look = Vector3.Lerp(harbour, moleHead, OverlookAimToMole);
            float range = Vector3.Distance(eye, look);
            Debug.Log($"[shiosai-diag] harbour overlook: eye {eye} -> {look} (range {range:0} m)");
            Shot(dir, "diag_shiosai_harbour_overlook", eye, look, OverlookFov, true,
                 Vector3.up, DofFor(range));
            // Same vantage WITHOUT defocus, so the DoF can be judged as a difference rather
            // than asserted.
            Shot(dir, "diag_shiosai_harbour_overlook_nodof", eye, look, OverlookFov, true);
        }

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[shiosai-diag] overlook done, restored '{regions.currentRegionId}'.");
    }

    /// <summary>Eye on the hillside above the coast road: chainage + inland offset + height.</summary>
    private static Vector3 OverlookEye(ShiosaiCoastEnvironment.CoastRoute route,
                                       float metres, float inland, float up)
    {
        int i = Mathf.Clamp(route.IndexAt(metres), 0, route.Count - 1);
        var p = route.Position[i];
        var s = route.SideFlat(i);          // +s is INLAND; the guardrail lives at -5.2 (seaward)
        return new Vector3(p.x + s.x * inland, p.y + up, p.z + s.z * inland);
    }

    /// <summary>Harbour basin centre and mole-head light position, read out of the scene.</summary>
    private static Vector3 HarbourAim(out Vector3 moleHead, out bool found)
    {
        moleHead = Vector3.zero; found = false;
        var town = GameObject.Find("Shiosai Harbour");
        if (town == null) return Vector3.zero;
        var rs = town.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return Vector3.zero;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);

        // Mole head: the harbour light if it is there, else the last mole section.
        var light = GameObject.Find("Shiosai Harbour Light");
        if (light != null)
        {
            var lr = light.GetComponentsInChildren<Renderer>(true);
            if (lr.Length > 0)
            {
                var lb = lr[0].bounds;
                foreach (var r in lr) lb.Encapsulate(r.bounds);
                moleHead = lb.center;
            }
        }
        if (moleHead == Vector3.zero) moleHead = b.center;
        found = true;
        return new Vector3(b.center.x, 2f, b.center.z);
    }

    // ---- depth of field ------------------------------------------------------------------
    // PROVISIONAL optical tuning, expressed as multiples of the shot's own subject range so the
    // framing can move without re-tuning. HDRP DoF in MANUAL mode: everything between nearEnd
    // and farStart is sharp; blur ramps in over [nearStart..nearEnd] toward the lens and over
    // [farStart..farEnd] toward the horizon.
    // FIRST PASS WAS A TILT-SHIFT, NOT A LENS. nearEnd at 0.55x the subject range put the
    // whole near hillside and half the road into full blur and the sky into max far blur, which
    // reads as a miniature diorama - the opposite of the reference, whose foreground is only
    // slightly soft and whose clouds are crisp. The sharp zone now covers everything from ~0.3x
    // to ~2.2x the subject range, and the blur radii are roughly halved.
    private const float DofNearStartK = 0.04f;
    private const float DofNearEndK = 0.30f;
    private const float DofFarStartK = 2.2f;
    private const float DofFarEndK = 16.0f;
    private const float DofNearMaxBlur = 1.3f;
    private const float DofFarMaxBlur = 2.6f;

    private struct DofSpec
    {
        public bool enabled;
        public float nearStart, nearEnd, farStart, farEnd, nearBlur, farBlur;
    }

    /// <summary>
    /// DEPTH OF FIELD for a shot whose subject sits <paramref name="range"/> metres away.
    /// Focus is pinned on the MID-GROUND subject, so the far headlands soften and the near
    /// roadside verge goes slightly soft, as the reference plate does. Ranges are multiples of
    /// the shot's own range so the framing constants can move without re-tuning the optics.
    /// </summary>
    private static DofSpec DofFor(float range) => new DofSpec
    {
        enabled = true,
        nearStart = range * DofNearStartK,
        nearEnd = range * DofNearEndK,
        farStart = range * DofFarStartK,
        farEnd = range * DofFarEndK,
        nearBlur = DofNearMaxBlur,
        farBlur = DofFarMaxBlur,
    };

    private static void SeaView(string dir, string name, ShiosaiCoastEnvironment.CoastRoute route,
                                float f, float up, float pitchDown)
    {
        int i = Mathf.Clamp(route.IndexAt(f * route.Length), 0, route.Count - 1);
        var eye = route.Position[i] + Vector3.up * up;
        var side = route.SideFlat(i);
        var look = eye - side * 260f + Vector3.up * pitchDown;
        Debug.Log($"[shiosai-diag] {name}: eye {eye}");
        Shot(dir, name, eye, look, 55f, true);
    }

    private static void AimAt(string dir, string shot, string objectName, float back, float up,
                              float fov)
    {
        var go = GameObject.Find(objectName);
        if (go == null) { Debug.LogWarning($"[shiosai-diag] {objectName} not in scene"); return; }
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) { Debug.LogWarning($"[shiosai-diag] {objectName} has no renderers"); return; }
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        Debug.Log($"[shiosai-diag] {objectName} centre {b.center} size {b.size}");
        // Stand off seawards (-X is the open water in this region) so the subject is framed
        // against the sea rather than against the hillside behind it.
        Shot(dir, shot, b.center + new Vector3(-back, up, back * 0.45f), b.center, fov, true);
    }

    private static void LogBounds()
    {
        var root = GameObject.Find("Shiosai Coast Environment");
        if (root == null) { Debug.LogWarning("[shiosai-diag] no coast root found."); return; }
        foreach (Transform group in root.transform)
        {
            var rs = group.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.Log($"[shiosai-diag] {group.name}: no renderers"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"[shiosai-diag] {group.name}: {rs.Length} renderers, centre {b.center}, size {b.size}");
        }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, bool postFx)
        => Shot(dir, name, pos, look, fov, postFx, Vector3.up);

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                             bool postFx, Vector3 worldUp)
        => Shot(dir, name, pos, look, fov, postFx, worldUp, default);

    /// <summary>
    /// Straight-down ORTHOGRAPHIC map render. Kept separate from <see cref="Shot"/> because the
    /// overhead is the one capture whose job is a plan of a 25 km region, and a perspective lens
    /// can only frame that from an altitude where clipping and atmospheric scattering eat it.
    /// </summary>
    private static void Ortho(string dir, string name, Vector3 pos, Vector3 look, float size)
    {
        var go = new GameObject("~ShiosaiOrthoCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.forward);
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.nearClipPlane = 1f;
        cam.farClipPlane = Mathf.Max(20000f, (pos.y - look.y) * 2f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.13f, 0.20f);
        cam.allowHDR = true;

        // FOG MUST BE OFF FOR THE MAP SHOT.
        //
        // The MapleRide HDRP shaders reconstruct the scene's Built-in exponential-squared fog
        // from RenderSettings and apply it by DISTANCE FROM CAMERA. An orthographic map camera
        // sits 3 km+ above the region, so every fragment in the frame is past the fog's full
        // saturation distance and the whole map came out as a flat cream/blue wash with a faint
        // ghost of the coast in it - the "blank overhead" QA finding. Fog is scene state, not
        // camera state, so it has to be toggled around the render and restored afterwards.
        bool fogWas = RenderSettings.fog;
        RenderSettings.fog = false;

        const int w = 1600, h = 1600;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        cam.targetTexture = rt;
        WarmUpWater(cam);
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
        RenderSettings.fog = fogWas;
        Debug.Log($"[shiosai-diag] wrote {name}.png (ortho {size:0} m)");
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                             bool postFx, Vector3 worldUp, DofSpec dof)
    {
        var go = new GameObject("~ShiosaiDiagCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, worldUp);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        // Far clip has to follow the shot, not be a constant: the region is 25 km across and the
        // overhead eye stands ~24 km up, which a fixed 14 km far plane clipped away entirely.
        //
        // THE FLOOR IS SET BY THE OCEAN, NOT BY THE SHOT. BuildOcean lays a 29 x 43 km sheet,
        // and from a 60 m clifftop the true sea horizon is ~28 km out. A 14 km far plane
        // therefore sliced the sea off SHORT of its own horizon and left a band of bare
        // background between the cut edge and the skyline - read as the "gold horizon band" in
        // diag_shiosai_ch8_highway.png. The sea must be allowed to run all the way out to where
        // it actually meets the sky, so the floor now clears the ocean's diagonal.
        cam.farClipPlane = Mathf.Max(14000f, Vector3.Distance(pos, look) * 3f);
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        if (postFx)
        {
            var fx = go.AddComponent<SakuraPostFX>();
            // A fresh component carries the SUNSET defaults; push the coast grade explicitly.
            var a = _gradeOverride ?? RegionDirector.ShiosaiAmbience;
            fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
            fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
            fx.lift = a.lift; fx.gain = a.gain;
            fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
            fx.dofFocusDistance = a.dofFocusDistance; fx.dofFocusRange = a.dofFocusRange;
            fx.dofStrength = a.dofStrength;
            // FLAT "CARDBOARD" DISTANT RANGES FIX (#2). A fresh SakuraPostFX also carries the
            // SUNSET aerial-perspective defaults - aerialRange 950 m with heavy desaturation
            // (0.50) and flatten (0.40) and a warm tint - so every inland range at 2-6 km sat
            // PAST the 950 m saturation point and was washed into a flat, desaturated, un-modelled
            // pale-blue cutout. In actual play ApplyAmbience pushes the COAST's own aerial (range
            // 7500 m, cool marine tint), under which those same ranges keep their sunlit/shadow
            // modelling and read as layered receding silhouettes. The diag never pushed it, so it
            // silently graded the coast with Sakura's dusk aerial. Push it here so the capture
            // mirrors what the game renders (opt out only when a region left aerialRange <= 0).
            if (a.aerialRange > 0f)
            {
                fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange;
                fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
                fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
            }
        }

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;

        // ---- DEPTH OF FIELD ------------------------------------------------------------
        // Driven from the CAPTURE, not from a scene volume, so no other region's render and no
        // gameplay camera inherits it. The volume + its runtime profile are created here and
        // destroyed below, and the object is named exactly "~ShiosaiDiagDof" so a re-run never
        // leaves a second one behind.
        GameObject dofGo = null;
        UnityEngine.Rendering.VolumeProfile dofProfile = null;
        if (dof.enabled)
        {
            foreach (var stale in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (stale.gameObject.name == "~ShiosaiDiagDof") Object.DestroyImmediate(stale.gameObject);

            dofGo = new GameObject("~ShiosaiDiagDof");
            var vol = dofGo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true;
            vol.priority = 10000f;
            dofProfile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            dofProfile.hideFlags = HideFlags.HideAndDontSave;
            vol.sharedProfile = dofProfile;

            var d = dofProfile.Add<UnityEngine.Rendering.HighDefinition.DepthOfField>(true);
            d.active = true;
            d.focusMode.overrideState = true;
            d.focusMode.value = UnityEngine.Rendering.HighDefinition.DepthOfFieldMode.Manual;
            d.nearFocusStart.overrideState = true; d.nearFocusStart.value = dof.nearStart;
            d.nearFocusEnd.overrideState = true; d.nearFocusEnd.value = dof.nearEnd;
            d.farFocusStart.overrideState = true; d.farFocusStart.value = dof.farStart;
            d.farFocusEnd.overrideState = true; d.farFocusEnd.value = dof.farEnd;
            d.nearSampleCount = 8;
            d.nearMaxBlur = dof.nearBlur;
            d.farSampleCount = 14;
            d.farMaxBlur = dof.farBlur;
            d.quality.levelAndOverride = (2, false);   // High
            Debug.Log($"[shiosai-diag] {name}: DoF manual near {dof.nearStart:0}-{dof.nearEnd:0} m, " +
                      $"far {dof.farStart:0}-{dof.farEnd:0} m, blur {dof.nearBlur:0.0}/{dof.farBlur:0.0}");
        }

        WarmUpWater(cam);
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
        if (dofGo != null) Object.DestroyImmediate(dofGo);
        if (dofProfile != null) Object.DestroyImmediate(dofProfile);
        Debug.Log($"[shiosai-diag] wrote {name}.png");
    }

    // ---------------------------------------------------------------- HDRP water warm-up

    /// <summary>
    /// Number of throwaway renders issued before every capture so the HDRP Water System has
    /// actually produced a surface by the time the pixels are read.
    ///
    /// WHY THIS EXISTS. The HDRP water simulation is advanced by the render loop, not by the
    /// scene load, and it needs a couple of update cycles before it evaluates. A batchmode
    /// capture that spawns a camera and calls Render() exactly once therefore reads the frame
    /// BEFORE the water exists, and the sea comes out as a flat pale sheet - bare sea floor and
    /// fog with no ocean over it.
    ///
    /// This cost a whole debugging cycle to find because the symptom mimics a content bug so
    /// well: retuning absorption changed nothing, painting the water pure red changed nothing,
    /// and deactivating the water surface entirely changed nothing - every one of those A/B
    /// renders was itself inside the cold window. The tell was that a LATER shot in the same
    /// batch rendered a perfect ocean from the same camera while an EARLIER one did not, which
    /// is a frame-count effect and cannot be a geometry or material effect.
    ///
    /// Measured: render 1 had no water, render 3 was fully correct. PROVISIONAL value - 4 is
    /// double the measured requirement so the margin survives a slower machine or a heavier
    /// scene. Only a capture needs this; at runtime the camera renders continuously and the
    /// water is correct from the third frame onward.
    /// </summary>
    private const int WaterWarmUpRenders = 4;

    private static void WarmUpWater(Camera cam)
    {
        for (int i = 0; i < WaterWarmUpRenders; i++) cam.Render();
    }
}
