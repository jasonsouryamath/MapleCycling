using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification renders of the Azora Highlands region.
///
/// REGION SAFETY. Every MapleRide region lives in ONE scene and shares ONE skybox slot, ONE key
/// light and ONE post-FX grade, so a capture that merely opened the scene and pointed a camera
/// at the highlands would render them under whichever region happened to be active - most
/// likely Sakura's sunset, which would make a clean 1,980 m late-morning col look like a dusk
/// shot of a different game. Each capture therefore drives <see cref="RegionDirector"/> into
/// Azora first (environment visibility + the full highlands ambience) and restores the previous
/// region afterwards, so the saved scene is left exactly as it was found.
///
/// FILENAME SAFETY. Every shot is prefixed <c>diag_azorahl_</c>, NOT <c>diag_azora_</c>. Sakura
/// Pass already owns diag_aozora_junction / _overview / _shrine / _switchback / _upper, and
/// "aozora" versus "azora" is one transposed vowel apart - close enough that a tired reader
/// comparing evidence would pick up the wrong region's render and believe it. Maple City already
/// lost evidence exactly this way (its gate shot was silently overwriting Sakura's Maple
/// Terrace gate shot, and whichever capture ran last "won").
///
/// OPEN COURSE. Azora is point-to-point, not a loop, so every "look ahead" here CLAMPS at the
/// finish rather than wrapping to sample 0 - wrapping would aim the last few cameras 24 km
/// backwards down the mountain.
///
/// The stops are taken from the PUBLISHED CENTRELINE rather than hand-placed world positions, so
/// they cannot drift under the road the next time the route is re-cut.
/// </summary>
public static class AzoraHighlandsDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Capture Azora Highlands Views", priority = 34)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[azora-diag] no RegionDirector in scene."); return; }
        regions.Resolve();

        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.AzoraHighlands;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[azora-diag] region -> {regions.currentRegionId}, " +
                  $"skybox '{(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}', " +
                  $"sun {(RenderSettings.sun != null ? RenderSettings.sun.transform.eulerAngles.ToString() : "none")}");

        LogBounds();

        var route = AzoraHighlandsEnvironment.AzoraRoute.Load();
        if (route.Count == 0) { Debug.LogWarning("[azora-diag] route did not load."); return; }

        // Rider's-eye stops at the six named checkpoints from the design doc.
        var stops = new (string name, float f)[]
        {
            // 0.010, not 0.000. Station 0 sits exactly ON the route plan's bounding box, which is
            // also where the detailed fell plate gives out and the coarse apron takes over, so a
            // camera parked there frames the region's own outer seam - a pale 256 m ground quad
            // cutting across the bottom of the shot. 240 m up the road is still the gate and is
            // clear of the boundary. The seam itself is invisible in play: the rider starts
            // facing UP the climb and never looks back off the end of the world.
            ("diag_azorahl_meadow_gate",   0.010f),
            ("diag_azorahl_switchbacks",   0.250f),
            ("diag_azorahl_false_flat",    0.450f),
            ("diag_azorahl_windward_ramp", 0.600f),
            ("diag_azorahl_col",           0.820f),
            ("diag_azorahl_descent",       0.940f),
        };
        foreach (var (name, f) in stops)
        {
            int i = Mathf.Clamp(route.IndexAt(f * route.Length), 0, route.Count - 1);
            int ahead = Mathf.Min(i + route.Count / 45, route.Count - 1);   // OPEN: clamp, never wrap
            var eye = route.Position[i] + Vector3.up * 2.4f - route.Tangent[i] * 6f
                      - route.SideFlat(i) * 1.2f;
            var look = route.Position[ahead] + Vector3.up * 1.4f;
            Debug.Log($"[azora-diag] {name}: d={route.Distance[i]:0} m y={route.Position[i].y:0.0} eye {eye}");
            Shot(dir, name, eye, look, 58f, true);
        }

        // THE PAYOFF. Standing just past the col, looking BACK down the whole climb: 1,080 m of
        // descent and roughly 7 km of switchback stem in one frame. This is the shot the region
        // is named for ("Earn the View") and the one that proves the landform solve actually
        // built a mountain rather than a road on a plate.
        {
            int col = route.IndexAt(19680f);
            int back = route.IndexAt(7000f);
            var eye = route.Position[col] + Vector3.up * 34f
                      + route.SideFlat(col) * 40f;
            Shot(dir, "diag_azorahl_col_panorama", eye,
                 route.Position[back] + Vector3.up * 40f, 62f, true);
        }

        // Second panorama from the opposite side, in case the col's drop is on the other hand
        // than the first one guessed. Cheap insurance against a hero shot facing a hillside.
        {
            int col = route.IndexAt(19680f);
            int back = route.IndexAt(12000f);
            var eye = route.Position[col] + Vector3.up * 26f
                      - route.SideFlat(col) * 55f;
            Shot(dir, "diag_azorahl_col_panorama_b", eye,
                 route.Position[back] + Vector3.up * 20f, 60f, true);
        }

        // The switchback stack seen from the side and above: the single best proof that the
        // inverse-distance landform genuinely built a hillside BETWEEN the legs rather than
        // leaving each leg on its own shelf.
        {
            int i = route.IndexAt(6000f);
            var eye = route.Position[i] + Vector3.up * 420f + route.SideFlat(i) * 900f;
            Shot(dir, "diag_azorahl_switchback_stack", eye,
                 route.Position[route.IndexAt(9000f)], 55f, true);
        }

        // The summit tarn, framed from above the shoulder so the camera looks DOWN into the
        // carved basin rather than across a water plane edge-on.
        //
        // TWO SEPARATE BUGS WERE STACKED HERE, and either alone was enough to reproduce "flat
        // pasture to the horizon, no water, no basin":
        //
        //  1) THE LOOK TARGET WAS A GUESS. The old aim used the road's height + a flat 4 m, which
        //     has nothing to do with the water's real elevation - the basin is carved 14 m deep
        //     and filled to only 10.5 m, so its surface sits wherever the open massif happens to
        //     crest 155 m off to the side, not at "road + 4". Reading the bounds of the
        //     already-built "Azora Tarn" renderer instead means the aim tracks the real geometry
        //     even if the terrain solve is retuned later - the same pattern Sakura's
        //     diag_lake_torii / diag_backdrop shots use to aim at built props instead of
        //     hard-coded heights.
        //
        //  2) THE WATER WAS INVISIBLE FROM ABOVE REGARDLESS OF AIM. Confirmed by probe: even
        //     with the look target corrected to the real water height and the eye raised well
        //     clear of the basin's rim, the render STILL showed nothing but grass - from any
        //     downward-looking angle. Forcing the tarn material's _Cull to 0 made it appear
        //     immediately, which meant the disc's front face was pointing the wrong way (its fan
        //     winding faces AWAY from a camera looking down at it) and every capture except the
        //     one near-level station 2-3 km down the valley (diag_azorahl_descent, which grazes
        //     it edge-on) was looking at the culled back face. That is a geometry/material bug,
        //     not a camera bug, and it is fixed at the source in
        //     AzoraHighlandsEnvironment.WaterMaterial() (_Cull = 0), the same invariant already
        //     applied to petals, moss, soil and every ring/skirt/bore interior in this project.
        //     This camera only needed the aim fix once that was in place.
        {
            int i = route.IndexAt(19680f - 1250f);
            var side = route.SideFlat(i);

            Vector3 look;
            var tarnGroup = GameObject.Find(AzoraHighlandsEnvironment.RootName)?.transform.Find("Azora Tarn");
            var tarnRenderers = tarnGroup != null ? tarnGroup.GetComponentsInChildren<Renderer>(true) : null;
            if (tarnRenderers != null && tarnRenderers.Length > 0)
            {
                var b = tarnRenderers[0].bounds;
                foreach (var r in tarnRenderers) b.Encapsulate(r.bounds);
                look = b.center;
            }
            else
            {
                Debug.LogWarning("[azora-diag] Azora Tarn not found in scene; falling back to a guessed height.");
                look = route.Position[i] - side * 155f + Vector3.up * 4f;
            }

            // A 45-degree overlook: high and far enough back to clear the rim with margin to
            // spare and read the basin's shape against the surrounding fell, not so steep that
            // it flattens into a top-down blue disc with no sense of the hollow it sits in.
            var eye = look + Vector3.up * 170f + side * 170f;
            Shot(dir, "diag_azorahl_tarn", eye, look, 58f, true);
        }

        // THE DROP. Looking straight out over the side from the windward ramp: the one shot that
        // proves the massif falls away instead of forming a plateau.
        {
            int i = route.IndexAt(16800f);
            var eye = route.Position[i] + Vector3.up * 3.0f;
            var look = route.Position[i] - route.SideFlat(i) * 2600f - Vector3.up * 420f;
            Shot(dir, "diag_azorahl_drop", eye, look, 64f, true);
        }

        // The shepherd's hut on the false flat - the region's only building.
        AimFromRoad(dir, route, 10800f - 140f, -34f, "diag_azorahl_hut", 46f);

        // Dry-stone wall at eye level in the enclosed lower pastures. The walls are a SWEPT
        // RIBBON with all their character in the albedo/normal, so this shot is the one that
        // decides whether that gamble paid off or whether they read as extruded curves.
        {
            int i = route.IndexAt(3400f);
            var eye = route.Position[i] + Vector3.up * 1.6f + route.SideFlat(i) * 1.0f
                      - route.Tangent[i] * 3f;
            var look = route.Position[i] + route.SideFlat(i) * 5.2f + route.Tangent[i] * 14f
                       + Vector3.up * 0.9f;
            Shot(dir, "diag_azorahl_wall_detail", eye, look, 44f, true);
        }

        // Grazing sheep - the region's only scale reference, so they have to be verified at a
        // believable size against the road they stand beside.
        {
            int i = route.IndexAt(5200f);
            var eye = route.Position[i] + Vector3.up * 3.2f;
            var look = route.Position[i] + route.SideFlat(i) * 55f + Vector3.up * 1f;
            Shot(dir, "diag_azorahl_pasture", eye, look, 52f, true);
        }

        // The whole route from above: proves the serpentine reads like the design's route map -
        // stacked hairpins, a plateau arc, an unwinding descent.
        {
            var c = route.Plan.center;
            Shot(dir, "diag_azorahl_overhead", new Vector3(c.x, 7200f, c.z),
                 new Vector3(c.x, 0f, c.z), 60f, false, Vector3.forward);
        }

        CaptureRiders(dir);

        // Leave the scene as we found it.
        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[azora-diag] restored region '{regions.currentRegionId}'.");
    }

    /// <summary>
    /// ROSTER PROOF. A region with 30 staged riders that never appear in a render has not been
    /// verified. Riders are framed from the racing line the way the player meets them - they ride
    /// the oncoming lane, so the camera looks down the road at their faces rather than at the
    /// backs of their helmets.
    /// </summary>
    private static void CaptureRiders(string dir)
    {
        var riders = GameObject.FindObjectsByType<NpcGreeting>(FindObjectsSortMode.None);
        int shot = 0;
        foreach (var g in riders)
        {
            if (g == null || !g.name.StartsWith("Azora NPC ")) continue;
            if (!g.gameObject.activeInHierarchy) continue;
            if (shot >= 3) break;
            var p = g.transform.position;
            var toward = g.transform.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
            toward.Normalize();
            var eye = p + toward * 7.0f + Vector3.up * 1.15f;
            Shot(dir, $"diag_azorahl_rider_{shot}", eye, p + Vector3.up * 1.05f, 42f, true);
            Debug.Log($"[azora-diag] rider shot {shot}: {g.name} ('{g.riderName}') at {p}");
            shot++;
        }
        if (shot == 0) Debug.LogWarning("[azora-diag] no Azora NPC riders found in scene.");
    }

    /// <summary>
    /// Frames an off-road prop FROM THE ROAD rather than from an arbitrary world-axis offset.
    ///
    /// Maple City's first gate shot stepped (back, up, back*0.5) off the prop's bounds centre,
    /// which in dense geometry walks the camera straight INTO the thing behind it and renders an
    /// interior wall. The carriageway is the one surface in any region guaranteed to be clear,
    /// so the camera sits on it and looks outward.
    /// </summary>
    private static void AimFromRoad(string dir, AzoraHighlandsEnvironment.AzoraRoute route,
                                    float distance, float offset, string name, float fov)
    {
        int i = route.IndexAt(distance);
        var eye = route.Position[i] + Vector3.up * 2.6f;
        var look = route.Position[i] + route.SideFlat(i) * offset + Vector3.up * 2.0f;
        Shot(dir, name, eye, look, fov, true);
    }

    private static void LogBounds()
    {
        var root = GameObject.Find(AzoraHighlandsEnvironment.RootName);
        if (root == null) { Debug.LogWarning("[azora-diag] no highlands root found."); return; }
        foreach (Transform group in root.transform)
        {
            var rs = group.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.Log($"[azora-diag] {group.name}: no renderers"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"[azora-diag] {group.name}: {rs.Length} renderers, centre {b.center}, size {b.size}");
        }
    }

    /// <summary>
    /// Positions Azora's seed emitter relative to a diagnostic camera and simulates it forward,
    /// so a still capture actually shows seed in the air.
    ///
    /// PARTICLES DO NOT TICK IN BATCHMODE. Outside play mode nothing advances a ParticleSystem,
    /// so without this every render would show an empty emitter and the region's signature VFX
    /// would be "verified" by its absence. The offsets mirror <see cref="AzoraSeedDrift"/>'s own
    /// follow behaviour - UPWIND, not ahead - rather than inventing new ones, so what the render
    /// shows is what the player will see.
    /// </summary>
    private static void PrimeSeedVfx(Transform cam)
    {
        var go = GameObject.Find("Azora Seed Drift");
        if (go == null) return;
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) return;

        var drift = go.GetComponent<AzoraSeedDrift>();
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
        var go = new GameObject("~AzoraDiagCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, worldUp);

        PrimeSeedVfx(cam.transform);

        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        // FAR PLANE. Azora's outermost ridge ring sits 14 km from the plan centre, and the
        // overhead shot flies 7.2 km up; anything short of this clips the horizon the region is
        // built to show.
        cam.farClipPlane = 34000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        if (postFx)
        {
            var fx = go.AddComponent<SakuraPostFX>();
            // A fresh component carries the SUNSET defaults; push the highlands grade explicitly
            // or every Azora render comes back looking like Sakura Pass at dusk.
            var a = RegionDirector.AzoraAmbience;
            fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
            fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
            fx.lift = a.lift; fx.gain = a.gain;
            fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
            fx.dofFocusDistance = a.dofFocusDistance; fx.dofFocusRange = a.dofFocusRange;
            fx.dofFalloff = a.dofFalloff; fx.dofStrength = a.dofStrength;

            // REGION-ATMOSPHERE FIX (2026-09-14). Everything below used to be absent, and the
            // absence was invisible: SakuraPostFX is constructed fresh for each shot, so any
            // stage this block does not push silently keeps the COMPONENT'S OWN DEFAULTS - which
            // are Sakura-at-sunset:
            //
            //     aerialStart 110 m   aerialRange 950 m
            //     aerialTint  (0.93, 0.78, 0.70)  <- a warm peach
            //     mistBaseY   6 m     mistTopY 34 m    mistStrength 0.45
            //
            // Every capture this harness has ever produced was therefore graded with a warm
            // sunset haze beginning 110 m from the lens and a mist band tuned for a 40 m valley
            // floor, regardless of what this region's Ambience record actually said. That is why
            // this region's fog and mist tuning appeared to have "no measurable effect" in its
            // renders: the values were correct in the scene and thrown away at the camera.
            //
            // Note that mistBaseY/mistTopY are ABSOLUTE WORLD Y, so on any region whose ground
            // does not sit near y = 0 the inherited band was not merely wrong but saturated or
            // entirely underground. Copying the full record is the only safe form of this block.
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
        Debug.Log($"[azora-diag] wrote {name}.png");
    }
}
