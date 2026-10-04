using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification renders of the Maple City region.
///
/// Every MapleRide region lives in ONE scene and shares ONE skybox slot, ONE key light and ONE
/// post-FX grade, so a capture that merely opened the scene and pointed a camera at the city
/// would render it under whichever region happened to be active - most likely Sakura's sunset.
/// Each capture therefore drives <see cref="RegionDirector"/> into Maple City first (environment
/// visibility + the full city ambience), and restores the previous region afterwards so the
/// saved scene is left exactly as it was found.
///
/// The stops are taken from the PUBLISHED CENTRELINE rather than hand-placed world positions, so
/// they cannot drift under the road the next time the loop is re-cut.
/// </summary>
public static class MapleCityDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Capture Maple City Views", priority = 33)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[maple-diag] no RegionDirector in scene."); return; }
        regions.Resolve();

        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.MapleCity;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[maple-diag] region -> {regions.currentRegionId}, " +
                  $"skybox '{(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}', " +
                  $"sun {(RenderSettings.sun != null ? RenderSettings.sun.transform.eulerAngles.ToString() : "none")}");

        LogBounds();

        var route = MapleCityEnvironment.CityRoute.Load();
        if (route.Count == 0) { Debug.LogWarning("[maple-diag] route did not load."); return; }

        // Rider's-eye stops at the six named checkpoints from the design doc. The arc fractions
        // are the ones the route build actually reported, not the doc's nominal ones.
        var stops = new (string name, float f, float fov)[]
        {
            ("diag_maple_gate_plaza",    0.00f, 58f),
            ("diag_maple_canal_sprint",  0.16f, 58f),
            ("diag_maple_oldtown_ramps", 0.36f, 58f),
            ("diag_maple_sky_terrace",   0.52f, 58f),
            ("diag_maple_ginkgo_blvd",   0.74f, 58f),
            ("diag_maple_river_finish",  0.92f, 58f),
        };
        foreach (var (name, f, fov) in stops)
        {
            int i = Mathf.Clamp(route.IndexAt(f * route.Length), 0, route.Count - 1);
            int ahead = (i + Mathf.Max(1, route.Count / 45)) % route.Count;   // loop: wrap, never clamp
            var eye = route.Position[i] + Vector3.up * 2.4f - route.Tangent[i] * 6f
                      - route.SideFlat(i) * 1.2f;
            var look = route.Position[ahead] + Vector3.up * 1.4f;
            Debug.Log($"[maple-diag] {name}: d={route.Distance[i]:0} m eye {eye}");
            Shot(dir, name, eye, look, fov, true);
        }

        // Street-level look UP: a city is defined by what is above the rider, and no forward
        // stop contains the skyline.
        {
            int i = route.IndexAt(route.Length * 0.62f);
            var eye = route.Position[i] + Vector3.up * 2.2f;
            Shot(dir, "diag_maple_skyline_up", eye,
                 eye + route.Tangent[i] * 60f + Vector3.up * 38f, 62f, true);
        }

        // Standing off the Sky Terrace crest looking back down over the city - the region's
        // money shot and the proof that the terraced ground actually steps.
        {
            int i = route.IndexAt(route.Length * 0.52f);
            int back = route.IndexAt(route.Length * 0.30f);
            var eye = route.Position[i] + Vector3.up * 70f
                      + route.SideFlat(i) * 150f - route.Tangent[i] * 90f;
            Shot(dir, "diag_maple_terrace_overlook", eye,
                 route.Position[back] + Vector3.up * 12f, 54f, true);
        }

        // The whole circuit from above: proves the filleted-polygon crit reads like the design
        // map - long avenues joined by distinct corners, not a smooth oval.
        {
            var c = LoopCentre(route);
            Shot(dir, "diag_maple_overhead", new Vector3(c.x, 2100f, c.z),
                 new Vector3(c.x, 0f, c.z), 60f, false, Vector3.forward);
        }

        // The Maple Gate hero shot. This MUST be framed from the ROAD, not from an arbitrary
        // world-axis offset: the previous version stepped (back, up, back*0.5) off the gate's
        // bounds centre, which in a dense city walks the camera straight into the facade behind
        // it and renders an interior wall. The carriageway is the one place in Maple City
        // guaranteed to be clear of geometry, so we sit the camera on it, a little way back down
        // the approach, and look at the gate's actual bounds centre.
        {
            var gate = GameObject.Find("Maple Gate");
            if (gate == null) Debug.LogWarning("[maple-diag] Maple Gate not in scene");
            else
            {
                var rs = gate.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) Debug.LogWarning("[maple-diag] Maple Gate has no renderers");
                else
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    Debug.Log($"[maple-diag] Maple Gate centre {b.center} size {b.size}");

                    // Station ~52 m BEHIND the gate, wrapping round the closed loop rather than
                    // clamping to sample 0 (which would put every "behind" shot on top of the
                    // gate itself).
                    const float BackM = 52f;
                    int back0 = route.IndexAt(route.Length - BackM);
                    var eye = route.Position[back0] + Vector3.up * 6.5f;
                    // NOTE: must NOT be "diag_maple_gate" - Sakura Pass has its own
                    // "maple" (Maple Terrace) segment whose diagnostics already write
                    // diag_maple_gate.png. The two regions were silently overwriting each
                    // other's evidence, so whichever capture ran last "won". Keep the
                    // maplecity_ prefix on any shot whose stem could clash.
                    Shot(dir, "diag_maplecity_gate", eye, b.center, 52f, true);
                }
            }
        }

        // ---- ROSTER PROOF. A city with 30 staged riders that never appear in a single render
        // has not been verified. Frame a couple of them from the racing line, the way the player
        // meets them: they ride the oncoming lane, so the shot looks down the road at their
        // faces rather than at the backs of their helmets.
        {
            var riders = GameObject.FindObjectsByType<NpcGreeting>(FindObjectsSortMode.None);
            int shot = 0;
            foreach (var g in riders)
            {
                if (g == null || !g.name.StartsWith("Maple City NPC ")) continue;
                if (shot >= 2) break;
                var p = g.transform.position;
                // Stand off down the road, at rider eye height, looking slightly down at them.
                var toward = g.transform.forward;
                toward.y = 0f;
                if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
                toward.Normalize();
                var eye = p + toward * 7.0f + Vector3.up * 1.15f;
                Shot(dir, $"diag_maple_rider_{shot}", eye, p + Vector3.up * 1.05f, 42f, true);
                Debug.Log($"[maple-diag] rider shot {shot}: {g.name} ('{g.riderName}') at {p}");
                shot++;
            }
            if (shot == 0) Debug.LogWarning("[maple-diag] no Maple City NPC riders found in scene.");
        }

        // Leave the scene as we found it.
        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[maple-diag] restored region '{regions.currentRegionId}'.");
    }

    /// <summary>Plan centroid of the loop, so the overhead shot is actually centred on it.</summary>
    private static Vector3 LoopCentre(MapleCityEnvironment.CityRoute route)
    {
        var sum = Vector3.zero;
        for (int i = 0; i < route.Count; i++) sum += route.Position[i];
        return sum / route.Count;
    }

    private static void AimAt(string dir, string objectName, string shot, float back, float up,
                              float fov)
    {
        var go = GameObject.Find(objectName);
        if (go == null) { Debug.LogWarning($"[maple-diag] {objectName} not in scene"); return; }
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) { Debug.LogWarning($"[maple-diag] {objectName} has no renderers"); return; }
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        Debug.Log($"[maple-diag] {objectName} centre {b.center} size {b.size}");
        Shot(dir, shot, b.center + new Vector3(back, up, back * 0.5f), b.center, fov, true);
    }

    /// <summary>
    /// Positions Maple City's leaf emitter relative to a diagnostic camera and simulates it
    /// forward, so a still capture shows leaves actually in the air.
    ///
    /// The offsets mirror MapleLeafDrift's own follow behaviour rather than inventing new ones,
    /// so what the render shows is what the player will see.
    /// </summary>
    private static void PrimeLeafVfx(Transform cam)
    {
        var go = GameObject.Find("Maple Leaf Drift");
        if (go == null) return;
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) return;

        var drift = go.GetComponent<MapleLeafDrift>();
        float up = drift != null ? drift.height : 13f;
        float ahead = drift != null ? drift.ahead : 16f;

        var flat = cam.forward; flat.y = 0f;
        flat = flat.sqrMagnitude < 1e-4f ? Vector3.forward : flat.normalized;
        go.transform.position = cam.position + flat * ahead + Vector3.up * up;

        // restart: particles are simulated in WORLD space, so leaves left over from the previous
        // stop would still be hanging in the air a kilometre away.
        ps.Clear(true);
        ps.Simulate(9f, true, true, true);
    }

    private static void LogBounds()
    {
        var root = GameObject.Find("Maple City Environment");
        if (root == null) { Debug.LogWarning("[maple-diag] no city root found."); return; }
        foreach (Transform group in root.transform)
        {
            var rs = group.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.Log($"[maple-diag] {group.name}: no renderers"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"[maple-diag] {group.name}: {rs.Length} renderers, centre {b.center}, size {b.size}");
        }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, bool postFx)
        => Shot(dir, name, pos, look, fov, postFx, Vector3.up);

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                             bool postFx, Vector3 worldUp)
    {
        var go = new GameObject("~MapleDiagCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, worldUp);

        // PARTICLES DO NOT TICK IN BATCHMODE. The signature maple-leaf drift is a ParticleSystem,
        // and outside play mode nothing advances it - so every diagnostic render would show an
        // empty emitter and the VFX would be "verified" by its absence. Park the emitter on this
        // camera (which is what MapleLeafDrift does at runtime) and hand-simulate it to a
        // steady state before the frame is taken.
        PrimeLeafVfx(cam.transform);

        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        if (postFx)
        {
            var fx = go.AddComponent<SakuraPostFX>();
            // A fresh component carries the SUNSET defaults; push the city grade explicitly or
            // every Maple City render comes back looking like Sakura Pass.
            var a = RegionDirector.MapleCityAmbience;
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
        Debug.Log($"[maple-diag] wrote {name}.png");
    }
}
