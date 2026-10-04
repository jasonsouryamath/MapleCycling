using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification renders of the Taka Mountains region.
///
/// REGION SAFETY. Every MapleRide region lives in ONE scene and shares ONE skybox slot, ONE key
/// light and ONE post-FX grade, so a capture that merely opened the scene and pointed a camera
/// at the highlands would render them under whichever region happened to be active - most
/// likely Sakura's sunset, which would make a clean 1,980 m late-morning col look like a dusk
/// shot of a different game. Each capture therefore drives <see cref="RegionDirector"/> into
/// Taka first (environment visibility + the full highlands ambience) and restores the previous
/// region afterwards, so the saved scene is left exactly as it was found.
///
/// FILENAME SAFETY. Every shot is prefixed <c>diag_taka_</c>, NOT <c>diag_taka_</c>. Sakura
/// Pass already owns diag_aozora_junction / _overview / _shrine / _switchback / _upper, and
/// "aozora" versus "taka" is one transposed vowel apart - close enough that a tired reader
/// comparing evidence would pick up the wrong region's render and believe it. Maple City already
/// lost evidence exactly this way (its gate shot was silently overwriting Sakura's Maple
/// Terrace gate shot, and whichever capture ran last "won").
///
/// OPEN COURSE. Taka is point-to-point, not a loop, so every "look ahead" here CLAMPS at the
/// finish rather than wrapping to sample 0 - wrapping would aim the last few cameras 24 km
/// backwards down the mountain.
///
/// The stops are taken from the PUBLISHED CENTRELINE rather than hand-placed world positions, so
/// they cannot drift under the road the next time the route is re-cut.
/// </summary>
public static class TakaMountainsDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Capture Taka Mountains Views", priority = 34)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[taka-diag] no RegionDirector in scene."); return; }
        regions.Resolve();

        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.TakaMountains;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[taka-diag] region -> {regions.currentRegionId}, " +
                  $"skybox '{(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}', " +
                  $"sun {(RenderSettings.sun != null ? RenderSettings.sun.transform.eulerAngles.ToString() : "none")}");

        LogBounds();

        var route = TakaMountainsEnvironment.TakaRoute.Load();
        if (route.Count == 0) { Debug.LogWarning("[taka-diag] route did not load."); return; }

        // Rider's-eye stops at the six named checkpoints from the design doc.
        var stops = new (string name, float f)[]
        {
            // 0.010, not 0.000. Station 0 sits exactly ON the route plan's bounding box, which is
            // also where the detailed fell plate gives out and the coarse apron takes over, so a
            // camera parked there frames the region's own outer seam - a pale 256 m ground quad
            // cutting across the bottom of the shot. 240 m up the road is still the gate and is
            // clear of the boundary. The seam itself is invisible in play: the rider starts
            // facing UP the climb and never looks back off the end of the world.
            ("diag_taka_trailhead",   0.010f),
            ("diag_taka_gallery",   0.250f),
            ("diag_taka_hairpins",    0.450f),
            ("diag_taka_north_wall", 0.600f),
            ("diag_taka_summit",           0.820f),
            ("diag_taka_ice_descent",       0.940f),
        };
        foreach (var (name, f) in stops)
        {
            int i = Mathf.Clamp(route.IndexAt(f * route.Length), 0, route.Count - 1);
            int ahead = Mathf.Min(i + route.Count / 45, route.Count - 1);   // OPEN: clamp, never wrap
            var eye = route.Position[i] + Vector3.up * 2.4f - route.Tangent[i] * 6f
                      - route.SideFlat(i) * 1.2f;
            var look = route.Position[ahead] + Vector3.up * 1.4f;
            Debug.Log($"[taka-diag] {name}: d={route.Distance[i]:0} m y={route.Position[i].y:0.0} eye {eye}");
            Shot(dir, name, eye, look, 58f, true);
        }

        // THE PAYOFF. Standing just past the col, looking BACK down the whole climb: 1,080 m of
        // descent and roughly 7 km of switchback stem in one frame. This is the shot the region
        // is named for ("Earn the View") and the one that proves the landform solve actually
        // built a mountain rather than a road on a plate.
        {
            int col = route.IndexAt(15400f);
            int back = route.IndexAt(9200f);
            var eye = route.Position[col] + Vector3.up * 34f
                      + route.SideFlat(col) * 40f;
            Shot(dir, "diag_taka_summit_panorama", eye,
                 route.Position[back] + Vector3.up * 40f, 62f, true);
        }

        // Second panorama from the opposite side, in case the col's drop is on the other hand
        // than the first one guessed. Cheap insurance against a hero shot facing a hillside.
        {
            int col = route.IndexAt(15400f);
            int back = route.IndexAt(11000f);
            var eye = route.Position[col] + Vector3.up * 26f
                      - route.SideFlat(col) * 55f;
            Shot(dir, "diag_taka_summit_panorama_b", eye,
                 route.Position[back] + Vector3.up * 20f, 60f, true);
        }

        // The switchback stack seen from the side and above: the single best proof that the
        // inverse-distance landform genuinely built a hillside BETWEEN the legs rather than
        // leaving each leg on its own shelf.
        {
            int i = route.IndexAt(8800f);
            var eye = route.Position[i] + Vector3.up * 420f + route.SideFlat(i) * 900f;
            Shot(dir, "diag_taka_hairpin_stack", eye,
                 route.Position[route.IndexAt(11600f)], 55f, true);
        }

        // The summit tarn, framed from above the shoulder so the camera looks DOWN into the
        // carved basin rather than across a water plane edge-on.
        {
            int i = route.IndexAt(15400f - 700f);
            // AIM AT THE SOLVED LAKE CENTRE, not at a fixed sideways offset. The highlands shot
            // guessed "155 m to the left of the road" and got away with it because its tarn was
            // placed from the same guess; Taka's basin is solved in BuildLandform and sits
            // wherever the landform put it, so a fixed offset photographed empty snowfield -
            // which is exactly what the first capture returned.
            // FIND THE LAKE IN THE SCENE. Reading TakaMountainsEnvironment._tarnCentre would
            // return (0,0) here: the landform solve that fills it runs in the BUILD process, and
            // the capture is a second, separate batchmode process with fresh statics. Asking the
            // renderer where it actually ended up cannot go stale that way.
            var lakeGo = GameObject.Find("Taka Tarn/Summit Tarn");
            var lakeR = lakeGo != null ? lakeGo.GetComponent<Renderer>() : null;
            var lake = lakeR != null ? lakeR.bounds.center : route.Position[i];

            // REFRAMED after the first two capture passes rendered a white void.
            //
            // The original eye sat 150 m ABOVE the tarn and 330 m back, looking down. That is the
            // worst possible camera for this subject and for two independent reasons. First, a
            // top-down look at a frozen lake surrounded by snow is white-on-white: the ice plane
            // and the snowfield differ by a few percent of albedo, both near 0.8, and with any
            // bloom at all they merge into one blown sheet. Second, from 150 m up the ice is seen
            // almost face-on, so it returns its flat diffuse value and the only thing that
            // distinguishes it - a specular, near-grazing reflection of a deep cobalt sky - never
            // reaches the lens at all.
            //
            // Dropping to 6 m above the surface and looking ACROSS it fixes both. The ice now
            // reads at a grazing angle, where it picks up the sky and goes markedly DARKER and
            // bluer than the snow banks behind it, and the far shore gives the eye a horizon to
            // measure the lake against. The camera is also kept inside aerialStart (950 m), so
            // the aerial stage does not wash the far bank away.
            var toLake = lakeR != null ? lakeR.bounds.extents.magnitude : 120f;
            var back = Mathf.Clamp(toLake * 1.35f, 90f, 420f);

            // EYE HEIGHT RAISED to clear the basin RIM, which the previous two attempts both
            // failed on for the same reason: the tarn sits in a hollow, so an eye 6 m above the
            // WATER is 35-40 m BELOW the ground at the distance the camera stands back to. Both
            // earlier frames were rendered from inside the snow bank, looking out through the
            // terrain - which is why they came back as a white fog with a dark smudge and no
            // shoreline. It was never a grade, bloom or aerial problem.
            //
            // Sitting one basin-depth plus a margin above the water puts the lens just over the
            // lip, which is also the honest vantage: this is what a rider stopped at the col
            // actually sees when they look down at Kori Lake.
            var eye = lake + Vector3.up * (TarnBasinDepthMForFraming + 16f) - Vector3.forward * back;
            var look = lake + Vector3.up * 1.0f + Vector3.forward * (back * 0.35f);
            Shot(dir, "diag_taka_kori_lake", eye, look, 52f, true);
        }

        // THE DROP. Looking straight out over the side from the windward ramp: the one shot that
        // proves the massif falls away instead of forming a plateau.
        {
            int i = route.IndexAt(12600f);
            var eye = route.Position[i] + Vector3.up * 3.0f;
            var look = route.Position[i] - route.SideFlat(i) * 2600f - Vector3.up * 420f;
            Shot(dir, "diag_taka_drop", eye, look, 64f, true);
        }

        // The shepherd's hut on the false flat - the region's only building.
        AimFromRoad(dir, route, 10800f - 140f, -34f, "diag_taka_refuge", 46f);

        // Dry-stone wall at eye level in the enclosed lower pastures. The walls are a SWEPT
        // RIBBON with all their character in the albedo/normal, so this shot is the one that
        // decides whether that gamble paid off or whether they read as extruded curves.
        {
            int i = route.IndexAt(8660f);
            var eye = route.Position[i] + Vector3.up * 1.6f + route.SideFlat(i) * 1.0f
                      - route.Tangent[i] * 3f;
            var look = route.Position[i] + route.SideFlat(i) * 5.2f + route.Tangent[i] * 14f
                       + Vector3.up * 0.9f;
            Shot(dir, "diag_taka_parapet", eye, look, 44f, true);
        }

        // Grazing sheep - the region's only scale reference, so they have to be verified at a
        // believable size against the road they stand beside.
        {
            int i = route.IndexAt(6400f);
            var eye = route.Position[i] + Vector3.up * 3.2f;
            var look = route.Position[i] + route.SideFlat(i) * 55f + Vector3.up * 1f;
            Shot(dir, "diag_taka_scree", eye, look, 52f, true);
        }

        // The whole route from above: proves the serpentine reads like the design's route map -
        // stacked hairpins, a plateau arc, an unwinding descent.
        {
            var c = route.Plan.center;
            Shot(dir, "diag_taka_overhead", new Vector3(c.x, 7200f, c.z),
                 new Vector3(c.x, 0f, c.z), 60f, false, Vector3.forward);
        }

        CaptureRiders(dir);

        // Leave the scene as we found it.
        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[taka-diag] restored region '{regions.currentRegionId}'.");
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
            if (g == null || !g.name.StartsWith("Taka NPC ")) continue;
            if (!g.gameObject.activeInHierarchy) continue;
            if (shot >= 3) break;
            var p = g.transform.position;
            var toward = g.transform.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
            toward.Normalize();
            var eye = p + toward * 7.0f + Vector3.up * 1.15f;
            Shot(dir, $"diag_taka_rider_{shot}", eye, p + Vector3.up * 1.05f, 42f, true);
            Debug.Log($"[taka-diag] rider shot {shot}: {g.name} ('{g.riderName}') at {p}");
            shot++;
        }
        if (shot == 0) Debug.LogWarning("[taka-diag] no Taka NPC riders found in scene.");

        // THE LEGENDARY GETS HIS OWN FRAME, and not out of ceremony. The loop above stops after
        // three riders in scene order, which is arbitrary, so Hyoga would only ever appear by
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
            Shot(dir, "diag_taka_legendary_hyoga", lp + lt * 5.2f + Vector3.up * 1.05f,
                 lp + Vector3.up * 1.0f, 34f, true);
            Debug.Log($"[taka-diag] legendary shot: {legend.displayName} \"{legend.title}\" at {lp}");
        }
        else Debug.LogWarning("[taka-diag] no LegendaryRiderPresence active - Hyoga not captured.");
    }

    /// <summary>
    /// Frames an off-road prop FROM THE ROAD rather than from an arbitrary world-axis offset.
    ///
    /// Maple City's first gate shot stepped (back, up, back*0.5) off the prop's bounds centre,
    /// which in dense geometry walks the camera straight INTO the thing behind it and renders an
    /// interior wall. The carriageway is the one surface in any region guaranteed to be clear,
    /// so the camera sits on it and looks outward.
    /// </summary>
    private static void AimFromRoad(string dir, TakaMountainsEnvironment.TakaRoute route,
                                    float distance, float offset, string name, float fov)
    {
        int i = route.IndexAt(distance);
        var eye = route.Position[i] + Vector3.up * 2.6f;
        var look = route.Position[i] + route.SideFlat(i) * offset + Vector3.up * 2.0f;
        Shot(dir, name, eye, look, fov, true);
    }

    private static void LogBounds()
    {
        var root = GameObject.Find(TakaMountainsEnvironment.RootName);
        if (root == null) { Debug.LogWarning("[taka-diag] no highlands root found."); return; }
        foreach (Transform group in root.transform)
        {
            var rs = group.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.Log($"[taka-diag] {group.name}: no renderers"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"[taka-diag] {group.name}: {rs.Length} renderers, centre {b.center}, size {b.size}");
        }
    }

    /// <summary>
    /// Positions Taka's seed emitter relative to a diagnostic camera and simulates it forward,
    /// so a still capture actually shows seed in the air.
    ///
    /// PARTICLES DO NOT TICK IN BATCHMODE. Outside play mode nothing advances a ParticleSystem,
    /// so without this every render would show an empty emitter and the region's signature VFX
    /// would be "verified" by its absence. The offsets mirror <see cref="TakaSpindrift"/>'s own
    /// follow behaviour - UPWIND, not ahead - rather than inventing new ones, so what the render
    /// shows is what the player will see.
    /// </summary>
    private static void PrimeSeedVfx(Transform cam)
    {
        var go = GameObject.Find("Taka Seed Drift");
        if (go == null) return;
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) return;

        var drift = go.GetComponent<TakaSpindrift>();
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
        var go = new GameObject("~TakaDiagCam");
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
            var a = RegionDirector.TakaAmbience;
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
        Debug.Log($"[taka-diag] wrote {name}.png");
    }
}
