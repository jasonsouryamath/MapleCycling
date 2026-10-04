using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// NAGISA BAY (B5) review cameras. Same convention as MinatoCoastDiagnostics: switch the region
/// visible FIRST (GameObject.Find never finds inactive roots), a throwaway ~DiagCam with a long
/// far clip, a 4x-AA 1600x900 RenderTexture read back to PNG, and the previous region restored
/// in finally so the saved scene state is never changed by a capture.
///
/// Output: reference/good_graphics/nagisa_bay/diag_nagisa_*.png
/// </summary>
public static class NagisaBayDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    public const string OutDir = "reference/good_graphics/nagisa_bay";
    private const int W = 1600, H = 900;

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa Bay")]
    public static void Capture()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = NagisaBayEnvironment.RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }

        var root = GameObject.Find(NagisaBayEnvironment.RootName);
        if (root == null)
        {
            Debug.LogError($"[nagisa] '{NagisaBayEnvironment.RootName}' not in scene - run NagisaBayEnvironment.Apply.");
            return;
        }

        var r = NagisaBayEnvironment.NagisaRoute.Load();
        Directory.CreateDirectory(OutDir);
        int shots = 0;
        try
        {
            // Route cameras: (file, metres, right offset, up, look-ahead, fov)
            var list = new List<(string, float, float, float, float, float)>
            {
                ("diag_nagisa_01_marina_front.png",   120f, -6f, 9f, 420f, 55f),
                ("diag_nagisa_03_beach_chase.png",    r.BeachStartM + 380f, 0f, 1.45f, 110f, 44f),
                ("diag_nagisa_04_kom_climb.png",      r.KomM - 900f, 0f, 1.6f, 160f, 50f),
                ("diag_nagisa_04b_kom_summit.png",    r.KomM, -8f, 14f, 700f, 60f),
                ("diag_nagisa_05_coast_road.png",     r.CoastStartM + 900f, 0f, 2.2f, 260f, 50f),
                ("diag_nagisa_06_bridge_approach.png", r.BridgeStartM + 60f, 0f, 2.2f, 300f, 50f),
            };
            foreach (var (file, d, right, up, ahead, fov) in list)
            {
                int i = r.IndexAt(d);
                var eye = r.Position[i] + r.SideFlat(i) * right + Vector3.up * up;
                int j = r.IndexAt(Mathf.Clamp(d + ahead, 0f, r.Length));
                var target = r.Position[j] + Vector3.up * 1.6f;
                if ((target - eye).sqrMagnitude < 1f) target = eye + r.Tangent[i] * 50f;
                Shot(eye, target, fov, file); shots++;
            }

            // Hero hotel from the ride road, looking seaward across the porte-cochere.
            var hotel = Pad(r, "hotel", new Vector3(300f, 3.2f, -33690f));
            int hi = Nearest(r, hotel);
            var hEye = r.Position[hi] + Vector3.up * 1.6f
                       - (r.Tangent[hi].normalized * 140f);
            Shot(hEye, hotel + Vector3.up * 45f, 55f, "diag_nagisa_02_hero_hotel_road.png"); shots++;

            // Hotel from the beach / sea side (the pool deck + beach side of the resort).
            Shot(hotel + new Vector3(-160f, 18f, -260f), hotel + Vector3.up * 40f, 50f,
                 "diag_nagisa_02b_hero_hotel_beach.png"); shots++;

            // Marina: from the water looking at the quay and the town behind.
            var quay = Pad(r, "marina_quay", new Vector3(-2330f, 2.2f, -32900f));
            Shot(quay + new Vector3(-320f, 22f, -180f), quay + Vector3.up * 6f, 52f,
                 "diag_nagisa_07_marina.png"); shots++;

            // Marina pier close: standing on the quay looking out along the pier to the berths.
            Shot(quay + new Vector3(-40f, 3.5f, -20f),
                 quay + new Vector3(-150f, 0f, -90f), 55f, "diag_nagisa_07b_marina_pier.png"); shots++;

            // Promenade life: eye-height on the promenade between the road and the beach, 250 m
            // before the hotel, looking along the strollers toward the tower.
            {
                int pi = r.IndexAt(r.HotelM - 260f);
                var sea = r.SideFlat(pi);                        // the hotel sits on the seaward side
                if (Vector3.Dot(sea, hotel - r.Position[pi]) < 0f) sea = -sea;
                var eye = r.Position[pi] + sea * 10.5f + Vector3.up * 1.7f;
                var tgt = r.Position[r.IndexAt(r.HotelM - 120f)] + sea * 10.5f + Vector3.up * 1.4f;
                Shot(eye, tgt, 50f, "diag_nagisa_09_promenade_people.png"); shots++;
            }

            // Aerial over the whole peninsula.
            var c = r.Plan.center;
            Shot(new Vector3(c.x - 600f, 2600f, c.z - 4200f), new Vector3(c.x, 60f, c.z), 52f,
                 "diag_nagisa_08_aerial.png"); shots++;

            // ---- pass 2 town / resort close-ups (town frame: d along route, off inland)
            {
                Vector3 T(float d, float off, float up) => NagisaBayEnvironment.DiagTownPoint(d, off) + Vector3.up * up;
                Shot(T(700f, 44f, 1.7f), T(770f, 44.5f, 2.2f), 55f, "diag_nagisa_10_main_street.png"); shots++;
                Shot(T(1180f, 52f, 1.6f), T(1135f, 60f, 4.5f), 60f, "diag_nagisa_10b_shopfronts.png"); shots++;
                Shot(T(900f, 97f, 1.7f), T(960f, 97f, 2.5f), 58f, "diag_nagisa_11_residential_street.png"); shots++;
                Shot(T(1100f, 0f, 1.6f), T(1170f, 38f, 8f), 55f, "diag_nagisa_12_town_from_road.png"); shots++;
                Shot(T(1250f, -260f, 110f), T(1300f, 70f, 5f), 50f, "diag_nagisa_13_town_oblique.png"); shots++;
                Shot(T(1500f, 20f, 380f), T(1500f, 90f, 0f), 60f, "diag_nagisa_13b_town_topdown.png"); shots++;
                // pool deck: elevated three-quarter over the podium from the sea side
                Shot(hotel + new Vector3(-70f, 32f, -150f), hotel + new Vector3(0f, 4f, -25f), 50f,
                     "diag_nagisa_14_pool_deck.png"); shots++;
                // beach close-up with people, standing on the sand looking along the shore
                int bi = r.IndexAt(r.HotelM - 520f);
                var sea = r.SideFlat(bi);
                if (Vector3.Dot(sea, hotel - r.Position[bi]) < 0f) sea = -sea;
                var be = r.Position[bi] + sea * 34f; be.y = 1.9f;
                var bt = r.Position[r.IndexAt(r.HotelM - 440f)] + sea * 46f; bt.y = 1.2f;
                Shot(be, bt, 52f, "diag_nagisa_15_beach_people.png"); shots++;
            }
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previous))
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
        Debug.Log($"[nagisa] captured {shots} frames to {OutDir}");
    }

    private static Vector3 Pad(NagisaBayEnvironment.NagisaRoute r, string name, Vector3 fallback) =>
        r.Pads.TryGetValue(name, out var p) ? p.c : fallback;

    private static int Nearest(NagisaBayEnvironment.NagisaRoute r, Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < r.Count; i++)
        {
            float d = (new Vector2(r.Position[i].x - p.x, r.Position[i].z - p.z)).sqrMagnitude;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private static readonly Dictionary<Mesh, long> _triCache = new Dictionary<Mesh, long>();
    private static long Tris(Mesh m)
    {
        if (m == null) return 0;
        if (!_triCache.TryGetValue(m, out long t))
        {
            for (int s = 0; s < m.subMeshCount; s++) t += (long)m.GetIndexCount(s) / 3;
            _triCache[m] = t;
        }
        return t;
    }

    private static long RendererTris(Renderer r) =>
        r is SkinnedMeshRenderer sk ? Tris(sk.sharedMesh)
        : r.TryGetComponent<MeshFilter>(out var mf) ? Tris(mf.sharedMesh) : 0;

    /// <summary>
    /// Budget check ("stay within ~+3M visible tris"): frustum-culled triangles under the Nagisa
    /// root (+ its riders) as the camera would draw them - per LODGroup only the LOD that Unity's
    /// screen-relative-height rule selects (culled past the last level), plain renderers whole.
    /// An estimate (ignores occlusion and the LOD bias), logged per shot.
    /// </summary>
    public static long EstimateVisibleTris(Camera cam)
    {
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        long total = 0;
        var inGroup = new HashSet<Renderer>();
        var roots = new List<GameObject>();
        var env = GameObject.Find(NagisaBayEnvironment.RootName); if (env != null) roots.Add(env);
        var npcs = GameObject.Find("Nagisa NPCs"); if (npcs != null) roots.Add(npcs);
        float halfTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float bias = QualitySettings.lodBias;
        foreach (var root in roots)
        {
            foreach (var g in root.GetComponentsInChildren<LODGroup>(false))
            {
                var lods = g.GetLODs();
                foreach (var l in lods) foreach (var r in l.renderers) if (r != null) inGroup.Add(r);
                Vector3 c = g.transform.TransformPoint(g.localReferencePoint);
                float size = g.size * Mathf.Max(Mathf.Abs(g.transform.lossyScale.x), Mathf.Abs(g.transform.lossyScale.y), Mathf.Abs(g.transform.lossyScale.z));
                float dist = Vector3.Distance(cam.transform.position, c);
                float rel = dist < 0.01f ? 1f : size / (2f * dist * halfTan) * bias;
                int pick = -1;
                for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { pick = i; break; }
                if (pick < 0) continue;
                foreach (var r in lods[pick].renderers)
                    if (r != null && r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds)) total += RendererTris(r);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || inGroup.Contains(r)) continue;
                if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) total += RendererTris(r);
            }
        }
        return total;
    }

    public static void Shot(Vector3 eye, Vector3 target, float fov, string file)
    {
        var camGo = new GameObject("~DiagCam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.transform.position = eye;
        cam.transform.rotation = Quaternion.LookRotation((target - eye).normalized, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 34000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
        Debug.Log($"[nagisa] shot {file} eye {eye} fov {fov}; est. visible Nagisa tris {EstimateVisibleTris(cam):N0}");
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(tex);
    }
}

/// <summary>
/// Editor side of the Nagisa Bay play test (pattern of TakaNpcPlaymodeCapture): enters play mode,
/// spawns NagisaPlaymodeRunner with the roster names, waits, leaves play mode, DISCARDS scene
/// changes and (batchmode) exits with the result. Run WITHOUT -quit:
///   run_steps.ps1 "NagisaPlaymodeCapture.Run|copilot_b5_play1.log|0"
/// Frames: reference/good_graphics/nagisa_bay/play/*.png
/// </summary>
[InitializeOnLoad]
public static class NagisaPlaymodeCapture
{
    private const string PrefKey = "mapleride.nagisa.playcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/nagisa_bay/play";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static NagisaPlaymodeCapture() { EditorApplication.playModeStateChanged += OnPlayModeChanged; }

    [MenuItem("MapleRide/QA/Play-test Nagisa Bay", priority = 66)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[nagisa-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~NagisaPlaymodeRunner").AddComponent<NagisaPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.riderNames = NagisaNpcRoster.RiderNames;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[nagisa-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(NagisaPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[nagisa-play] TIMED OUT waiting for the runner.");
            NagisaPlaymodeRunner.Failed = true;
            NagisaPlaymodeRunner.Finished = true;
        }
        if (!NagisaPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}
