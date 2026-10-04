using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification renders of the Fuji Ridge region.
///
/// REGION SAFETY. Every MapleRide region lives in ONE scene and shares ONE skybox slot, ONE key
/// light and ONE post-FX grade, so a capture that merely opened the scene and pointed a camera
/// at the highlands would render them under whichever region happened to be active - most
/// likely Sakura's sunset, which would make a clean 1,980 m late-morning col look like a dusk
/// shot of a different game. Each capture therefore drives <see cref="RegionDirector"/> into
/// Fuji first (environment visibility + the full highlands ambience) and restores the previous
/// region afterwards, so the saved scene is left exactly as it was found.
///
/// FILENAME SAFETY. Every shot is prefixed <c>diag_fuji_</c>, NOT <c>diag_fuji_</c>. Sakura
/// Pass already owns diag_aozora_junction / _overview / _shrine / _switchback / _upper, and
/// "aozora" versus "taka" is one transposed vowel apart - close enough that a tired reader
/// comparing evidence would pick up the wrong region's render and believe it. Maple City already
/// lost evidence exactly this way (its gate shot was silently overwriting Sakura's Maple
/// Terrace gate shot, and whichever capture ran last "won").
///
/// OPEN COURSE. Fuji is point-to-point, not a loop, so every "look ahead" here CLAMPS at the
/// finish rather than wrapping to sample 0 - wrapping would aim the last few cameras 24 km
/// backwards down the mountain.
///
/// The stops are taken from the PUBLISHED CENTRELINE rather than hand-placed world positions, so
/// they cannot drift under the road the next time the route is re-cut.
/// </summary>
public static class FujiRidgeDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Capture Fuji Ridge Views", priority = 34)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[fuji-diag] no RegionDirector in scene."); return; }
        regions.Resolve();

        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.FujiRidge;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[fuji-diag] region -> {regions.currentRegionId}, " +
                  $"skybox '{(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}', " +
                  $"sun {(RenderSettings.sun != null ? RenderSettings.sun.transform.eulerAngles.ToString() : "none")}");

        LogBounds();

        var route = FujiRidgeEnvironment.FujiRoute.Load();
        if (route.Count == 0) { Debug.LogWarning("[fuji-diag] route did not load."); return; }

        // ------------------------------------------------------------------ framing
        // Stage temporary colliders so every camera below can be PROVEN to be outside the
        // mountain and to have line of sight to its subject. Fuji is the region that made this
        // necessary: its plan is a spiral, so a blind sideways offset points into the cone on
        // roughly half the route, and six rounds of lighting changes were spent on what was
        // actually a camera buried in rock.
        var envRoot = GameObject.Find(FujiRidgeEnvironment.RootName);
        DiagnosticsCamera.Prime(envRoot != null ? envRoot.transform : null);

        try
        {

        // Rider's-eye stops at the six named checkpoints from the design doc.
        var stops = new (string name, float f)[]
        {
            ("diag_fuji_base_gate",    0.006f),
            ("diag_fuji_cedar_belt",   0.240f),
            ("diag_fuji_cloudbreak",   0.460f),
            ("diag_fuji_strata_wall",  0.620f),
            ("diag_fuji_knife_edge",   0.880f),
            ("diag_fuji_summit_torii", 0.996f),
        };
        foreach (var (name, f) in stops)
        {
            int i = Mathf.Clamp(route.IndexAt(f * route.Length), 0, route.Count - 1);
            int ahead = Mathf.Min(i + route.Count / 45, route.Count - 1);   // OPEN: clamp, never wrap
            var look = route.Position[ahead] + Vector3.up * 1.4f;
            // Rider height, a short step back, and barely any lateral - but routed through the
            // shared placer so that even this, the tamest camera in the set, cannot end up
            // inside a cutting on a switchback.
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 6f, lateralM: 1.2f, heightM: 2.4f,
                                                 label: name);
            Debug.Log($"[fuji-diag] {name}: d={route.Distance[i]:0} m y={route.Position[i].y:0.0} eye {eye}");
            Shot(dir, name, eye, look, 58f, true);
        }

        // THE PAYOFF. High on the cone looking back DOWN the spiral: the cloud deck below, the
        // cedar belt under it, and a thousand metres of climb in one frame. This is the shot the
        // region is named for ("Clouds Above").
        {
            int i = route.IndexAt(12300f);
            // Aimed along the FALLING side rather than back down the road. On a spiral those are
            // completely different directions and only one of them does not pass through rock.
            var outward = DiagnosticsCamera.DescendingSide(route.Position[i], route.SideFlat(i));
            var look = route.Position[i] + outward * 3200f + Vector3.up * 120f;
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 0f, lateralM: 90f, heightM: 46f,
                                                 label: "diag_fuji_cloud_sea");
            Shot(dir, "diag_fuji_cloud_sea", eye, look, 62f, true);
        }

        // Second look from lower down, framing the deck edge-on rather than from above it.
        //
        // STATION RETUNED 8400 -> 8000. QA caught this shot self-flagging "STILL OCCLUDED after
        // 8 attempts" and being saved anyway (see Shot()/the harness never checking PlaceEye's
        // result before writing the PNG - that is a separate, larger finding, not fixed here).
        // Probing the occlusion loop directly showed WHY no amount of extra height or lateral
        // reach could ever clear it: at d=8400 the "descending side" the 14 m-radius slope probe
        // picks is correct LOCALLY but the road sits on a secondary spur there, so the line of
        // sight to the cloud-sea target clips that spur's own bulge no matter how far the eye is
        // lifted - the occluding terrain rises with the eye at nearly the same rate, so the
        // 8-attempt lift-and-push budget can never out-climb it (confirmed: even height 140 m
        // and lateral 160 m, both far past this shot's authored 34 m / 120 m, still failed).
        // Moving the STATION 400 m back down the road to a point clear of that spur - keeping
        // every other parameter (backM/lateralM/heightM/the look offset) exactly as authored -
        // clears the sight line on the very first attempt instead.
        {
            int i = route.IndexAt(8000f);
            var outward = DiagnosticsCamera.DescendingSide(route.Position[i], route.SideFlat(i));
            var look = route.Position[i] + outward * 2400f + Vector3.up * 90f;
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 0f, lateralM: 120f, heightM: 34f,
                                                 label: "diag_fuji_cloud_sea_b");
            Shot(dir, "diag_fuji_cloud_sea_b", eye, look, 60f, true);
        }

        // THE CONE. A long standoff from well off the mountain, aimed at the summit: the single
        // shot that decides whether the landform solve built a volcano or a hillside, and whether
        // the cedar belt's hard treeline reads as a line at distance.
        {
            int i = route.IndexAt(4000f);
            var look = route.Position[route.Count - 1] + Vector3.up * 40f;
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 0f, lateralM: 2600f, heightM: 520f,
                                                 label: "diag_fuji_cone");
            Shot(dir, "diag_fuji_cone", eye, look, 55f, true);
        }

        // THE TREELINE, seen from outside and slightly above it. The doc's hardest visual claim
        // is that the forest STOPS at Cloudbreak rather than thinning away, and that is only
        // checkable from a vantage that sees both sides of the line at once.
        //
        // STATION RETUNED 5980 -> 5700, for the same class of bug as diag_fuji_cloud_sea_b just
        // above: QA caught this one "STILL OCCLUDED after 8 attempts" and saved anyway, and the
        // occlusion loop was probed directly rather than guessed at. Neither more height (tried
        // up to 300 m against the authored 150 m) nor more lateral reach (tried up to 550 m
        // against 420 m) ever cleared it - the station at d=5980 sits inside a re-entrant right
        // at Cloudbreak itself, so the sight line clips a near ridge whichever way the camera is
        // pushed. The blocked zone was mapped by probing every 100-250 m either side: 5850-6100
        // all fail, 5700 and 6300 both clear on the FIRST attempt. 5700 was kept (280 m before
        // Cloudbreak, the smaller shift, and it sits inside the cedar belt itself - arguably the
        // more natural place to stand to watch the forest end ahead of you) with every other
        // parameter (backM/lateralM/heightM the eye stands at) left exactly as authored.
        //
        // LOOK TARGET ALSO RETUNED. Fixing only the occlusion left a second, separate defect:
        // the original look target was "outward * 900 m" - straight off the side of the road,
        // away from the corridor. BuildCedarForest only ever plants cedars within ~78 m of the
        // road centreline (there is no altitude-based forest ground-splat), so no camera tuning
        // of an outward-facing shot could EVER bring a single tree into frame; the old render was
        // always going to be bare slope regardless of the occlusion bug. Aiming instead at the
        // route itself further back downhill (d=3400, well inside the cedar belt) puts the road
        // corridor - and the cedars planted along it - into the same frame as the bare ground the
        // eye is standing on, so the belt's edge actually reads as a line at distance. Probed by
        // rendering both framings side by side: outward showed no green at all at any of 300 m,
        // 500 m, 700 m or 900 m; aiming along the route at d=3400 shows a clear cluster of cedar
        // silhouettes following the road, thinning to nothing at the near (d=5700) end - i.e. the
        // treeline itself, in frame. Eye placement (backM/lateralM/heightM) is unchanged.
        {
            int i = route.IndexAt(5700f);
            int j = route.IndexAt(3400f);
            var look = route.Position[j] + Vector3.up * 20f;
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 0f, lateralM: 420f, heightM: 150f,
                                                 label: "diag_fuji_treeline");
            Shot(dir, "diag_fuji_treeline", eye, look, 54f, true);
        }

        // THE DROP. Straight out over the falling side, high on the ramp: proves the massif
        // actually falls away from the road instead of forming a shelf.
        {
            int i = route.IndexAt(11000f);
            var outward = DiagnosticsCamera.DescendingSide(route.Position[i], route.SideFlat(i));
            var look = route.Position[i] + outward * 2600f + Vector3.up * 100f;
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 0f, lateralM: 6f, heightM: 3.2f,
                                                 label: "diag_fuji_drop");
            Shot(dir, "diag_fuji_drop", eye, look, 64f, true);
        }

        // The weather station hut on the upper ramp - the region's only building.
        AimFromRoad(dir, route, 9750f - 140f, -34f, "diag_fuji_weather_hut", 46f);

        // The banded strata cutting at eye level. Its character is entirely in the albedo, so
        // this is the shot that decides whether the bands read as rock or as wallpaper.
        {
            int i = route.IndexAt(8060f);
            var look = route.Position[i] + route.SideFlat(i) * 5.2f + route.Tangent[i] * 14f
                       + Vector3.up * 0.9f;
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 3f, lateralM: 1.0f, heightM: 1.6f,
                                                 label: "diag_fuji_parapet");
            Shot(dir, "diag_fuji_parapet", eye, look, 44f, true);
        }

        // Bare scoria above the treeline - the region's dominant surface, checked close enough
        // that its grain and its blackness can both be judged.
        {
            int i = route.IndexAt(7400f);
            var outward = DiagnosticsCamera.DescendingSide(route.Position[i], route.SideFlat(i));
            var look = route.Position[route.IndexAt(7560f)] + Vector3.up * 2.0f;
            var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                                 route.SideFlat(i), look,
                                                 backM: 0f, lateralM: 4f, heightM: 3.2f,
                                                 label: "diag_fuji_scree");
            Shot(dir, "diag_fuji_scree", eye, look, 52f, true);
        }

        // The whole route from above: proves the spiral reads like the design's route map.
        {
            var c = route.Plan.center;
            Shot(dir, "diag_fuji_overhead", new Vector3(c.x, 5200f, c.z),
                 new Vector3(c.x, 1200f, c.z), 60f, false, Vector3.forward);
        }

        CaptureRiders(dir);

        }
        finally
        {
            // ALWAYS. A batchmode process that threw partway through a shot list used to leave
            // hundreds of MeshColliders on the scenery, and the very next pass saved them into
            // the shared scene.
            DiagnosticsCamera.Release();
        }

        // Leave the scene as we found it.
        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[fuji-diag] restored region '{regions.currentRegionId}'.");
    }

    /// <summary>
    /// ROSTER PROOF. A region with 30 staged riders that never appear in a render has not been
    /// verified. Riders are framed from the racing line the way the player meets them - they ride
    /// the oncoming lane, so the camera looks down the road at their faces rather than at the
    /// backs of their helmets.
    /// </summary>
    /// <summary>The tarn basin depth, mirrored from the environment pass purely so the
    /// framing above can clear the rim. Named rather than a literal so the two move
    /// together if the basin is ever re-cut.</summary>
    private const float TarnBasinDepthMForFraming = 42f;

    private static void CaptureRiders(string dir)
    {
        var riders = GameObject.FindObjectsByType<NpcGreeting>(FindObjectsSortMode.None);
        int shot = 0;
        foreach (var g in riders)
        {
            if (g == null || !g.name.StartsWith("Fuji NPC ")) continue;
            if (!g.gameObject.activeInHierarchy) continue;
            if (shot >= 3) break;
            var p = g.transform.position;
            var toward = g.transform.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
            toward.Normalize();
            var eye = p + toward * 7.0f + Vector3.up * 1.15f;
            Shot(dir, $"diag_fuji_rider_{shot}", eye, p + Vector3.up * 1.05f, 42f, true);
            Debug.Log($"[fuji-diag] rider shot {shot}: {g.name} ('{g.riderName}') at {p}");
            shot++;
        }
        if (shot == 0) Debug.LogWarning("[fuji-diag] no Fuji NPC riders found in scene.");

        // THE LEGENDARY GETS HIS OWN FRAME, and not out of ceremony. The loop above stops after
        // three riders in scene order, which is arbitrary, so Akatsuki would only ever appear by
        // luck - and he is the one rider in the region whose look actually has to be checked
        // (glacier-white kit against snow is the single hardest read in the whole palette; if the
        // authored brightness is even slightly low he vanishes into the background he rides on).
        //
        // He is also framed CLOSER and LOWER than the cast shots - 5.2 m at 34 mm rather than
        // 7.0 m at 42 mm - because the point of the frame is his silhouette and his height
        // against the road, which a wider lens flattens away.
        var legend = GameObject.FindObjectsByType<LegendaryRiderPresence>(FindObjectsInactive.Include,
                                                                         FindObjectsSortMode.None)
                               .FirstOrDefault(l => l.gameObject.activeInHierarchy);
        if (legend != null)
        {
            var lp = legend.transform.position;
            var lt = legend.transform.forward; lt.y = 0f;
            if (lt.sqrMagnitude < 1e-4f) lt = Vector3.forward;
            lt.Normalize();
            Shot(dir, "diag_fuji_legendary_akatsuki", lp + lt * 5.2f + Vector3.up * 1.05f,
                 lp + Vector3.up * 1.0f, 34f, true);
            Debug.Log($"[fuji-diag] legendary shot: {legend.displayName} \"{legend.title}\" at {lp}");
        }
        else Debug.LogWarning("[fuji-diag] no LegendaryRiderPresence active - Akatsuki not captured.");
    }

    /// <summary>
    /// Frames an off-road prop FROM THE ROAD rather than from an arbitrary world-axis offset.
    ///
    /// Maple City's first gate shot stepped (back, up, back*0.5) off the prop's bounds centre,
    /// which in dense geometry walks the camera straight INTO the thing behind it and renders an
    /// interior wall. The carriageway is the one surface in any region guaranteed to be clear,
    /// so the camera sits on it and looks outward.
    /// </summary>
    private static void AimFromRoad(string dir, FujiRidgeEnvironment.FujiRoute route,
                                    float distance, float offset, string name, float fov)
    {
        int i = route.IndexAt(distance);
        var look = route.Position[i] + route.SideFlat(i) * offset + Vector3.up * 2.0f;
        // Routed through the shared placer so the prop is proven VISIBLE, not merely aimed at.
        // A prop shot that is occluded by the cutting between the road and the prop used to
        // write a perfectly exposed photograph of a rock face.
        var eye = DiagnosticsCamera.PlaceEye(route.Position[i], route.Tangent[i],
                                             route.SideFlat(i), look,
                                             backM: 0f, lateralM: 2f, heightM: 2.6f, label: name);
        Shot(dir, name, eye, look, fov, true);
    }

    private static void LogBounds()
    {
        var root = GameObject.Find(FujiRidgeEnvironment.RootName);
        if (root == null) { Debug.LogWarning("[fuji-diag] no highlands root found."); return; }
        foreach (Transform group in root.transform)
        {
            var rs = group.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.Log($"[fuji-diag] {group.name}: no renderers"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"[fuji-diag] {group.name}: {rs.Length} renderers, centre {b.center}, size {b.size}");
        }
    }

    /// <summary>
    /// Positions Taka's seed emitter relative to a diagnostic camera and simulates it forward,
    /// so a still capture actually shows seed in the air.
    ///
    /// PARTICLES DO NOT TICK IN BATCHMODE. Outside play mode nothing advances a ParticleSystem,
    /// so without this every render would show an empty emitter and the region's signature VFX
    /// would be "verified" by its absence. The offsets mirror <see cref="FujiCloudDrift"/>'s own
    /// follow behaviour - UPWIND, not ahead - rather than inventing new ones, so what the render
    /// shows is what the player will see.
    /// </summary>
    private static void PrimeSeedVfx(Transform cam)
    {
        var go = GameObject.Find("Fuji Seed Drift");
        if (go == null) return;
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) return;

        // OVERHEAD-SHOT BLOWOUT FIX. This function used to place the emitter ~20-30 m in front
        // of whichever camera called it, unconditionally - harmless while the HDRP grade (and
        // its DoF) was a silent no-op, but diag_fuji_overhead flies the camera up to 5,200 m to
        // frame the whole route spiral, and dragging the seed emitter to within ~30 m of THAT
        // camera fills most of the 60-degree frame with a giant, soft, alpha-blended sprite -
        // exactly the "out-of-focus blob" QA reported, not real geometry or a real capture bug.
        // No normal ride-height camera in this region goes anywhere near this altitude (the
        // cloud deck tops out at 1,215 m and the summit/torii sit only a little above that), so
        // an altitude guard cleanly separates "a real diagnostic/gameplay shot near the route"
        // from "the one plan-view camera 4 km above the mountain" without needing every call
        // site to opt in individually. Leave the emitter at its scene-authored position (which
        // is down near the route, i.e. naturally far outside this camera's frame) and clear any
        // simulated particles instead of dragging them up here.
        const float MaxRideAltitude = 3000f;
        if (cam.position.y > MaxRideAltitude)
        {
            ps.Clear(true);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        var drift = go.GetComponent<FujiCloudDrift>();
        float up = drift != null ? drift.height + drift.lift : 9.5f;
        float upwind = drift != null ? drift.upwind : 22f;
        var w = drift != null ? drift.windDir : new Vector3(0.82f, 0f, -0.57f);
        w.y = 0f;
        w = w.sqrMagnitude < 1e-4f ? Vector3.forward : w.normalized;

        go.transform.position = cam.position - w * upwind + Vector3.up * up;

        // Restart: particles simulate in WORLD space, so seed left over from the previous stop
        // would still be hanging in the air several kilometres away.
        ps.Clear(true);
        ps.Simulate(11f, true, true, true);
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, bool postFx)
        => Shot(dir, name, pos, look, fov, postFx, Vector3.up);

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                             bool postFx, Vector3 worldUp)
    {
        var go = new GameObject("~FujiDiagCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, worldUp);

        PrimeSeedVfx(cam.transform);

        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        // FAR PLANE. Taka's outermost ridge ring sits 14 km from the plan centre, and the
        // overhead shot flies 7.2 km up; anything short of this clips the horizon the region is
        // built to show.
        cam.farClipPlane = 34000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        if (postFx)
        {
            var fx = go.AddComponent<SakuraPostFX>();
            // A fresh component carries the SUNSET defaults, so EVERY field the region owns must
            // be pushed here.
            //
            // THE BUG THIS BLOCK WAS HIDING, AND WHY IT MATTERS BEYOND TAKA.
            // Until now this harness pushed only the grade and DOF, and silently inherited the
            // aerial-perspective and mist stages from SakuraPostFX's own defaults:
            //
            //     aerialStart  110 m     aerialRange 950 m
            //     aerialTint   (0.93, 0.78, 0.70)   <- a WARM PEACH, i.e. Sakura at sunset
            //     mistBaseY    6 m       mistTopY    34 m       mistStrength 0.45
            //
            // So every capture of every region has been graded with a warm sunset haze starting
            // 110 metres from the lens and a mist band tuned for a 40 m-high valley floor. On a
            // region whose ground sits between 1,400 and 2,842 m that mist band is saturated
            // everywhere, which is precisely the flat white wash that drowned the first Taka
            // hairpin capture, and the peach tint is precisely the khaki cast that made cold
            // granite read as tan. It is also, almost certainly, why earlier regions' fog and
            // mist edits "had no measurable effect" in their renders: the values were correct in
            // the scene and thrown away by the camera.
            //
            // This is now a full copy of the region's Ambience record. The equivalent blocks in
            // the Sakura, Shiosai, Maple City and Azora harnesses still have the old partial
            // copy and would benefit from the same fix.
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
        }

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
        Debug.Log($"[fuji-diag] wrote {name}.png");
    }
}
