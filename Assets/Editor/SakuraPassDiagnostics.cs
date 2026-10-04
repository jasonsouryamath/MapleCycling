using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnostic renders of the built Sakura Pass scene.
///
/// The gameplay camera sits 1.5 m off the deck, which is a terrible vantage for telling
/// whether the world actually assembled correctly. These views pull back so bounds, scale and
/// placement problems are obvious, and log the bounds of every staged group.
/// </summary>
public static class SakuraPassDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Capture Diagnostic Views", priority = 30)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        LogBounds();

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        // Framed on the whole world. The Fuji approach carries the route out to x -215 / z 817
        // and the terrain to x -285..190, z -295..900, so the old overhead at (-45, 560, 70)
        // cropped off the entire lakeshore descent.
        Shot(dir, "diag_overhead", new Vector3(-47f, 1200f, 300f), new Vector3(-47f, 0f, 300f), 60f, false);
        Shot(dir, "diag_valley", new Vector3(-120f, 70f, -150f), new Vector3(10f, 0f, 60f), 32f, true);
        // Aimed straight down the valley at the hero volcano, which sits off the route's forward
        // cone and so appears in none of the rider's-eye shots.
        Shot(dir, "diag_backdrop", new Vector3(-30f, 60f, -40f), new Vector3(615f, 150f, 992f), 46f, true);
        RouteShots(dir);
        SegmentShots(dir);
    }

    /// <summary>
    /// Rider's-eye stops on the three expansion segments, taken from their own published
    /// centrelines. Without these there is no frame in the whole diagnostic set that contains
    /// 5.3 km of new road, and "it built" would be the only evidence it exists.
    /// </summary>
    private static void SegmentShots(string dir)
    {
        var segments = SakuraPassEnvironment.SakuraRoute.LoadSegments();
        var stops = new (string seg, float f, string name, float fov)[]
        {
            ("s1", 0.06f, "diag_s1_lakehead", 55f),
            ("s1", 0.26f, "diag_s1_causeway", 55f),
            ("s1", 0.55f, "diag_s1_village", 55f),
            ("s1", 0.86f, "diag_s1_bridge", 55f),
            ("aozora", 0.08f, "diag_aozora_junction", 55f),
            ("aozora", 0.34f, "diag_aozora_switchback", 55f),
            ("aozora", 0.66f, "diag_aozora_upper", 55f),
            ("aozora", 0.98f, "diag_aozora_shrine", 55f),
            ("maple", 0.30f, "diag_maple_terraces", 55f),
            ("maple", 0.96f, "diag_maple_gate", 55f),
        };

        foreach (var (segId, f, name, fov) in stops)
        {
            if (!segments.TryGetValue(segId, out var route) || route.Count < 4) continue;
            int i = Mathf.Clamp(route.IndexAt(f * route.Length), 0, route.Count - 1);
            int ahead = Mathf.Min(route.Count - 1, i + Mathf.Max(1, route.Count / 40));
            var eye = route.Position[i] + Vector3.up * 2.4f - route.Tangent[i] * 6f
                      - route.Side[i] * 1.2f;
            var look = route.Position[ahead] + Vector3.up * 1.4f;
            Debug.Log($"[diag] {name}: {segId} d={route.Distance[i]:0} m eye {eye}");
            Shot(dir, name, eye, look, fov, true);
        }

        // The switchback field is the one thing no rider's-eye frame can show: six limbs stacked
        // on one face. Stand well off it and look back down the mountain.
        if (segments.TryGetValue("aozora", out var az) && az.Count > 8)
        {
            int top = az.IndexAt(az.Length * 0.92f);
            int bottom = az.IndexAt(az.Length * 0.12f);
            var eye = az.Position[top] + Vector3.up * 120f + new Vector3(120f, 0f, -260f);
            Shot(dir, "diag_aozora_overview", eye, az.Position[bottom], 52f, true);
        }

        // The whole network from above, so the circuit, the spur and the hub link read as one
        // place rather than four unrelated roads.
        // Terrain spans x -490..280 (770 m) and z -780..970 (1750 m), centred near (-105, 95).
        // The long axis is laid along the screen's 16:9 wide axis via worldUp = +X, and the
        // altitude is solved so the 1750 m axis fills the frame with a margin:
        //   horizontal cover 1900 m -> vertical 1069 m -> h = 1069 / (2*tan(30)) = 926 m.
        Shot(dir, "diag_network_overhead", new Vector3(-105f, 930f, 95f),
             new Vector3(-105f, 0f, 95f), 60f, false, Vector3.right);
    }

    /// <summary>
    /// Rider's-eye views taken from the published route rather than hand-typed coordinates.
    /// Hard-coded camera positions silently ended up *below* the terrain when the route was
    /// re-sculpted, which reads as "the scene is broken" when it is only the camera that is.
    /// </summary>
    private static void RouteShots(string dir)
    {
        var route = SakuraPassEnvironment.SakuraRoute.Load();
        // Stops on the climb are fractions of the *climb*, so they still frame the landmark they
        // were chosen for now that the descent has more than doubled the route length. Stops on
        // the descent are metres past the summit.
        var climbStops = new (string name, float t)[]
        {
            ("diag_road_start", 0.06f), ("diag_forest", 0.09f), ("diag_gate", 0.125f),
            ("diag_hairpin", 0.30f), ("diag_road_mid", 0.45f), ("diag_cliffside", 0.50f),
            ("diag_tunnel_approach", 0.545f), ("diag_tunnel_inside", 0.60f),
            ("diag_tunnel_exit", 0.645f), ("diag_hillside", 0.69f),
            ("diag_summit", 0.88f), ("diag_summit_approach", 0.92f), ("diag_overlook", 0.97f),
        };
        var descentStops = new (string name, float past)[]
        {
            // Measured against the re-cut switchback descent: limb A 44-142 m past the summit,
            // hairpin 1 ~166, limb B 186-218, hairpin 2 ~246, limb C 268-336, then the lakeshore
            // traverse out to the finish at ~848.
            ("diag_crest", 25f), ("diag_descent_top", 95f), ("diag_descent_overlook", 140f),
            ("diag_descent_hairpin1", 168f), ("diag_descent_sweep", 205f),
            ("diag_descent_hairpin2", 248f), ("diag_descent_flank", 300f),
            ("diag_descent_bend", 430f), ("diag_descent_lower", 580f),
            ("diag_route_end", 720f), ("diag_run_out", 820f),
        };

        var stops = climbStops.Select(s => (name: s.name, idx: route.IndexAtClimbFraction(s.t)))
            .Concat(descentStops.Select(s => (name: s.name, idx: route.IndexAt(route.ClimbLength + s.past))))
            .ToArray();

        foreach (var (name, idx) in stops)
        {
            int i = Mathf.Clamp(idx, 0, route.Count - 1);
            int ahead = Mathf.Min(route.Count - 1, i + Mathf.Max(1, route.Count / 40));

            var eye = route.Position[i] + Vector3.up * 2.4f - route.Tangent[i] * 6f
                      - route.Side[i] * 1.2f;
            var look = route.Position[ahead] + Vector3.up * 1.4f;
            Debug.Log($"[diag] {name}: sample {i} d={route.Distance[i]:0} m eye {eye} look {look}");
            Shot(dir, name, eye, look, 55f, true);

            // The mid shot is also taken raw, so a washed-out frame can be blamed on the
            // grade/bloom or on the scene itself rather than guessed at.
            if (name == "diag_road_mid") Shot(dir, name + "_raw", eye, look, 55f, false);
        }

        // The whole point of the descent is the view *down* it, and no rider's-eye shot at 2.4 m
        // can show that. This one stands off the flank above the crest and looks along the road
        // falling away below.
        {
            int top = route.IndexAt(route.ClimbLength + 50f);
            int far = route.IndexAt(route.ClimbLength + 300f);
            // Well above the treeline: at 48 m the crest's own sakura grove filled the frame and
            // the road it exists to show was completely hidden behind blossom. The target is now
            // the bottom of the switchback section rather than the far lakeshore, so both
            // hairpins and all three limbs are inside the frame.
            var eye = route.Position[top] + Vector3.up * 135f
                      - route.Tangent[top] * 70f + route.Side[top] * 60f;
            Shot(dir, "diag_descent_overview", eye, route.Position[far], 55f, true);
        }

        // Looking out across the water at the far-shore town and the torii standing in the lake.
        // The valley falls away on the rider's right, so these face +Side rather than forward.
        int vi = Mathf.Clamp(route.IndexAtClimbFraction(0.30f), 0, route.Count - 1);
        var veye = route.Position[vi] + Vector3.up * 3.0f;
        Shot(dir, "diag_lakeside_view", veye,
             veye + route.Side[vi] * 200f + Vector3.down * 24f, 52f, true);

        // Mid-descent the route runs the lakeshore with Fuji roughly beam-on to the rider's right,
        // so it never enters the forward-facing stops between the crest and the last hairpin.
        // This is the only frame that proves the volcano still reads during that traverse.
        {
            var volcano = GameObject.Find("Hero Volcano");
            if (volcano == null) Debug.LogWarning("[diag] Hero Volcano not in scene");
            else
            {
                var rs = volcano.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                // Aim below the summit: the cone's mass, not the sky above it, is what should fill
                // the frame from a rider's eye height.
                var target = new Vector3(b.center.x, b.center.y + b.size.y * 0.18f, b.center.z);
                int fi = route.IndexAt(route.ClimbLength + 600f);
                var feye = route.Position[fi] + Vector3.up * 3.0f;
                Shot(dir, "diag_descent_fuji", feye, target, 55f, true);
            }
        }

        // The torii is placed from lake-level maths at build time, so aim at where it actually
        // landed rather than at a hard-coded guess (which framed the road instead of the water).
        AimAt(dir, "diag_lake_torii", "Lake Torii", 46f, 14f);

        // Landmarks are placed from route maths at build time, so the only reliable way to frame
        // one is to find the object and shoot at where it actually landed.
        AimAt(dir, "diag_sign_check", "Hillside Lettering", 70f, 26f);

        // "diag_signboard" used to be a plain route stop at climb 0.92, which looks *forward*
        // down the road while the summit signboard stands behind the camera - the shot never
        // contained the prop it is named after. The board faces back down the road, so the
        // camera has to stand in front of it (+forward), not behind it.
        AimAt(dir, "diag_signboard", "Summit Signboard", 9f, 2.2f, front: true, fov: 42f);
    }

    private static void AimAt(string dir, string shot, string objectName, float back, float up,
                              bool front = false, float fov = 50f)
    {
        var go = GameObject.Find(objectName);
        if (go == null) { Debug.LogWarning($"[diag] {objectName} not in scene"); return; }
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) { Debug.LogWarning($"[diag] {objectName} has no renderers"); return; }
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        Debug.Log($"[diag] {objectName} centre {b.center} size {b.size}");
        var fwd = front ? go.transform.forward : -go.transform.forward;
        Shot(dir, shot, b.center + fwd * back + Vector3.up * up, b.center, fov, true);
    }

    private static void LogBounds()
    {
        var root = GameObject.Find("Sakura Pass Environment");
        if (root == null) { Debug.LogWarning("No environment root found."); return; }

        foreach (Transform group in root.transform)
        {
            var rs = group.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.Log($"[diag] {group.name}: no renderers"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"[diag] {group.name}: {rs.Length} renderers, centre {b.center}, size {b.size}");
        }

        // Probe along the route's own footprint rather than the x=0 line: past the summit the
        // road swings out to x = -215, so an x=0 probe reports open water and looks like a hole.
        var probes = new[]
        {
            new Vector3(0f, 0f, -200f), new Vector3(10f, 0f, -50f), new Vector3(5f, 0f, 100f),
            new Vector3(-10f, 0f, 250f), new Vector3(15f, 0f, 340f), new Vector3(-60f, 0f, 420f),
            new Vector3(-130f, 0f, 520f), new Vector3(-195f, 0f, 660f), new Vector3(-214f, 0f, 760f),
            new Vector3(-170f, 0f, 815f),
        };
        foreach (var pr in probes)
        {
            if (Physics.Raycast(new Vector3(pr.x, 500f, pr.z), Vector3.down, out var hit, 2000f))
                Debug.Log($"[diag] ground at ({pr.x}, {pr.z}): y={hit.point.y:0.00} ({hit.collider.name})");
            else
                Debug.Log($"[diag] ground at ({pr.x}, {pr.z}): NO HIT");
        }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, bool postFx)
        => Shot(dir, name, pos, look, fov, postFx, Vector3.up);

    /// <summary>
    /// <paramref name="worldUp"/> is the world direction that should point "up" on screen. It only
    /// matters for a straight-down map shot: there Unity's default +Y up is parallel to the view
    /// direction, LookAt degenerates and picks an arbitrary roll (which is why the first
    /// diag_network_overhead came out rotated with the long axis squeezed into the short screen
    /// axis). Passing Vector3.right puts world +Z along the screen's wide axis.
    /// </summary>
    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                             bool postFx, Vector3 worldUp)
    {
        var go = new GameObject("~DiagCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, worldUp);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        if (postFx) go.AddComponent<SakuraPostFX>();

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
        Debug.Log($"[diag] wrote {name}.png");
    }
}
