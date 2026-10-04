using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the five Minato Coast reference cameras, one per chapter, framed to match the
/// authoritative concept art in Assets/Environment/MinatoCoast/Concept/RideSections.
///
/// Follows the project's diagnostics convention exactly (see TakaMountainsDiagnostics): a
/// throwaway ~DiagCam, a long far clip because the crossing is visible from 10 km away, and a
/// 4x-AA RenderTexture read back to PNG. No HDAdditionalCameraData - the other regions do not
/// use one and adding it here would diverge the grade.
/// </summary>
public static class MinatoCoastDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "reference/good_graphics";
    private const string RootName = "Minato Coast Environment";

    private const int W = 1600, H = 900;

    /// <summary>(file, route metres, eye offset right, eye height, look-ahead metres, fov)</summary>
    private static readonly (string file, float d, float right, float up, float ahead, float fov)[] Shots =
    {
        // 01 - port city departure: elevated waterfront view, the working harbour in front and
        // the crossing visible on the horizon (concept 01). The old framing sat 9 m up looking
        // only 260 m ahead, which cropped the bridge out of the shot entirely.
        ("diag_minato_01_port_departure.png",      700f, -16f, 28f, 1500f, 55f),
        // 02 - city-side bridge approach: THE quality gate frame. On the climb, arch ahead.
        ("diag_minato_02_bridge_approach.png",    2150f,  -3f,  6f,  300f, 46f),
        // 03 - open ocean midpoint: on-deck, spans receding into haze.
        ("diag_minato_03_ocean_midpoint.png",     6400f,   0f,  4.2f, 700f, 40f),
        // 04 - far-shore landfall: descending onto the green populated coast.
        ("diag_minato_04_far_shore.png",         10600f,  -9f, 22f,  340f, 50f),
        // 05 - inland mountain continuation: FORWARD up the switchbacks, so the required
        // concept-05 coverage (road visibly continuing deeper inland through forested
        // switchbacks, rock cuts and walls) is preserved.
        ("diag_minato_05_mountain.png",          16200f,  14f, 42f,  760f, 54f),
        // 05V - REAR VIEWPOINT from a high switchback, looking back down the valley at the
        // complete crossing, the bay and the far port. Added as its own camera rather than
        // stealing the forward coverage that concept 05 requires.
        ("diag_minato_05v_rear_viewpoint.png",   15400f,  26f, 112f, -5900f, 64f),
        // 02K - THE quality gate checked from KURO'S ACTUAL GAMEPLAY CAMERA. Values cloned from
        // KuroFollowCamera.gameplayOffset (0, 1.45, -3.60) and gameplayFieldOfView 44, read from
        // the component - Kuro itself is NOT modified. This is the frame that decides whether the
        // terrain reads detailed at real cycling distance rather than from a 6 m drone height.
        ("diag_minato_02k_kuro_eye.png",          2150f,   0f,  1.45f, 110f, 44f),
        // 06 - red suspension landfall (title-screen bridge): deck approach, sea broadside,
        // and the landfall viewpoint looking back at the span.
        ("diag_minato_06_red_bridge_deck.png",    9925f,  -2f,  3.2f, 330f, 52f),
        ("diag_minato_06s_red_bridge_side.png",  10150f, -420f, 55f,  10f, 55f),
        ("diag_minato_06l_red_bridge_landfall.png", 10470f, 12f, 16f, -300f, 58f),
    };

    [MenuItem("MapleRide/Diagnostics/Capture Minato Coast")]
    public static void Capture()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // DIAGNOSTICS-TOOLING ORDERING FIX. This used to GameObject.Find(RootName) BEFORE
        // switching region visibility below. GameObject.Find never finds an inactive object, so
        // if any other region (e.g. Sakura, left active by the scene's default lighting pass)
        // was the last one made visible, Minato Coast Environment was still inactive at the
        // point of the Find and this silently no-op'd with just a LogError - no exception, no
        // capture, and nothing about the failure pointed at "wrong region was active". Region
        // visibility must be switched on FIRST so the root is actually active by the time it is
        // looked up. Harness-only change: no environment/content asset touched.
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previousRegion = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionCatalog.MinatoCoast;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }

        var root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogError($"[minato] '{RootName}' is not in the scene - run " +
                           "MinatoCoastEnvironment.Apply first.");
            return;
        }

        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        Directory.CreateDirectory(OutDir);

        try
        {
            foreach (var s in Shots)
            {
                int i = route.IndexAt(s.d);
                var p = route.Position[i];
                var side = route.SideFlat(i);
                var eye = p + side * s.right + Vector3.up * s.up;

                int j = route.IndexAt(Mathf.Clamp(s.d + s.ahead, 0f, route.Length - 1f));
                var target = route.Position[j] + Vector3.up * 2f;

                Shot(eye, target, s.fov, s.file);
            }

            // Focused art-review frames. These are deliberately additional to the seven route
            // references, so improving a blocker never steals the chapter coverage.
            int portI = route.IndexAt(620f);
            var portP = route.Position[portI];
            var crane = FindDescendantContaining(root.transform, "Minato_Port_ContainerCrane");
            var portTarget = crane != null ? RendererCentre(crane) : portP + Vector3.up * 18f;
            Shot(portTarget - route.Tangent[portI].normalized * 105f + Vector3.up * 26f,
                 portTarget + Vector3.up * 10f,
                 54f, "diag_minato_target_port.png");

            int seaI = route.IndexAt(6400f);
            var seaP = route.Position[seaI];
            Shot(seaP + route.SideFlat(seaI) * 24f + Vector3.up * 12f,
                 route.Position[route.IndexAt(7700f)] + Vector3.down * 3f,
                 43f, "diag_minato_target_ocean.png");

            int mountainI = route.IndexAt(16000f);
            var mountainP = route.Position[mountainI];
            Shot(mountainP - route.SideFlat(mountainI) * 24f + Vector3.up * 18f,
                 route.Position[route.IndexAt(16650f)] + Vector3.up * 46f,
                 48f, "diag_minato_target_mountain.png");

            // --- bright city pass review frames: skyline, beach and crowds.
            int skyI = route.IndexAt(900f);
            var skyP = route.Position[skyI];
            var cityDir = route.SideFlat(skyI) * -MinatoCoastEnvironment.SeaSideSignFor(route, skyI);
            Shot(skyP - route.Tangent[skyI].normalized * 60f + Vector3.up * 16f,
                 skyP + cityDir * 700f + Vector3.up * 90f,
                 55f, "diag_minato_target_skyline.png");

            int beachI = route.IndexAt(1560f);
            var beachP = route.Position[beachI];
            var seaDir = route.SideFlat(beachI) * MinatoCoastEnvironment.SeaSideSignFor(route, beachI);
            Shot(beachP - seaDir * 28f + Vector3.up * 22f,
                 beachP + seaDir * 170f + Vector3.up * 2f,
                 52f, "diag_minato_target_beach.png");

            // Beach existence check: frame the actual sand renderer from above and to the side,
            // so an invisible / sunken / backface-culled sand ribbon is unmistakable.
            var sandT = FindDescendant(root.transform, "Beach Sand");
            if (sandT != null)
            {
                var sc = RendererCentre(sandT);
                Shot(sc - seaDir * 190f + Vector3.up * 130f, sc, 55f,
                     "diag_minato_target_beach_sand.png");
            }
            else
            {
                Debug.LogError("[minato] beach sand renderer not found");
            }

            var ferris = FindDescendant(root.transform, "Minato_Landmark_FerrisWheel");
            if (ferris != null)
            {
                var fc = RendererCentre(ferris);
                int fi = route.IndexAt(1430f);
                var away = route.Position[fi] - fc; away.y = 0f;
                if (away.sqrMagnitude < 1f) away = Vector3.forward;
                Shot(fc + away.normalized * 210f + Vector3.up * 40f, fc, 46f,
                     "diag_minato_target_ferris.png");
            }
            else
            {
                Debug.LogError("[minato] targeted ferris shot skipped: landmark not found");
            }

            // Crowd close-up: eye height on the boulevard, looking down the road.
            int crowdI = route.IndexAt(760f);
            Shot(route.Position[crowdI] + Vector3.up * 3.2f -
                 route.SideFlat(crowdI) * 3.0f,
                 route.Position[route.IndexAt(880f)] + Vector3.up * 2.0f,
                 48f, "diag_minato_target_crowd.png");

            // Grounding check: frame ONE cyclist and ONE pedestrian from 9 m so a float or a
            // sink is unmistakable. Metrics have shipped floating props here before.
            var ridersRoot = FindDescendant(root.transform, "Boulevard Cyclists");
            if (ridersRoot != null && ridersRoot.childCount > 0)
            {
                var c = ridersRoot.GetChild(ridersRoot.childCount / 2);
                var cc = RendererCentre(c);
                Shot(cc + (c.right + Vector3.forward * 0.2f).normalized * 7.5f + Vector3.up * 1.1f,
                     cc + Vector3.down * 0.6f, 38f, "diag_minato_target_rider_contact.png");
            }
            var walkRoot = FindDescendant(root.transform, "Waterfront Pedestrians");
            if (walkRoot != null && walkRoot.childCount > 0)
            {
                var w = walkRoot.GetChild(walkRoot.childCount / 2);
                var wc = RendererCentre(w);
                Shot(wc + (w.right + Vector3.forward * 0.2f).normalized * 6.5f + Vector3.up * 1.0f,
                     wc + Vector3.down * 0.5f, 38f, "diag_minato_target_walker_contact.png");
            }

            // Crowd archetype contact frames. The seated archetype gets its own shot because its
            // origin convention (ground plane, feet dangling, authored ledge beneath) is the one
            // that can silently float, and it only exists on the beach.
            var sitter = FindCrowd(root.transform, "Crowd_06_Sit_Coral");
            if (sitter != null)
            {
                var sc = RendererCentre(sitter);
                Shot(sc + (sitter.right + Vector3.forward * 0.25f).normalized * 4.2f + Vector3.up * 0.7f,
                     sc + Vector3.down * 0.35f, 34f, "diag_minato_target_sit_contact.png");
            }
            // M2 market-district contact frame. The plaza market is only reviewable from 40-200 m
            // in the boulevard shots, which is far too coarse to prove that a cafe sitter landed
            // ON a chair and that the awning/parasol shells are neither floating nor sunk. This
            // shot puts the camera on the plaza itself.
            var cafeGroup = FindCrowd(root.transform, "Cafe Terraces");
            if (cafeGroup != null && cafeGroup.childCount > 0)
            {
                var set = cafeGroup.GetChild(cafeGroup.childCount / 2);
                var mcc = RendererCentre(set);
                Shot(mcc + new Vector3(6.5f, 2.6f, 6.5f), mcc + Vector3.down * 0.7f, 45f,
                     "diag_minato_target_market_contact.png");
            }

            var waver = FindCrowd(root.transform, "Crowd_02_Wave_Akane");            if (waver != null)
            {
                var vc = RendererCentre(waver);
                Shot(vc + (waver.right + Vector3.forward * 0.25f).normalized * 5.0f + Vector3.up * 0.9f,
                     vc + Vector3.down * 0.35f, 36f, "diag_minato_target_wave_contact.png");
            }

            // Cull-transition pair aimed at the same boulevard figure. The near image must show
            // the complete bike/rider; the far image may retire it, but neighbouring groups must
            // not exhibit a large synchronized pop.
            if (ridersRoot != null && ridersRoot.childCount > 0)
            {
                var targetCrowd = ridersRoot.GetChild(ridersRoot.childCount / 3);
                var tc = RendererCentre(targetCrowd);
                Vector3 axis = route.Tangent[route.IndexAt(900f)].normalized;
                Shot(tc - axis * 85f + Vector3.up * 4.0f, tc + Vector3.up * 0.2f, 42f,
                     "diag_minato_cull_near.png");
                Shot(tc - axis * 125f + Vector3.up * 5.0f, tc + Vector3.up * 0.2f, 42f,
                     "diag_minato_cull_far.png");
            }

            var lighthouse = FindDescendant(root.transform, "Minato_Lighthouse_Hero");
            if (lighthouse != null)
            {
                int landfallI = route.IndexAt(11200f);
                var fromLand = route.Position[landfallI] - lighthouse.position;
                fromLand.y = 0f;
                if (fromLand.sqrMagnitude < 1f) fromLand = Vector3.forward;
                var eye = lighthouse.position + fromLand.normalized * 300f + Vector3.up * 58f;
                var target = lighthouse.position + Vector3.up * 18f;
                Shot(eye, target, 35f, "diag_minato_target_lighthouse.png");
            }
            else
            {
                Debug.LogError("[minato] targeted lighthouse shot skipped: Minato_Lighthouse_Hero not found");
            }
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previousRegion))
            {
                regions.currentRegionId = previousRegion;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
        Debug.Log($"[minato] captured {Shots.Length} reference cameras plus four targeted art frames to {OutDir}");
    }

    private static Transform FindDescendant(Transform root, string exactName)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == exactName) return t;
        return null;
    }

    /// <summary>
    /// A mid-list crowd instance of the given archetype, by EXACT name. Picking the middle of
    /// the matches rather than the first avoids always framing the one figure at the start of
    /// the route, which is where placement bugs are least likely to show.
    /// </summary>
    private static Transform FindCrowd(Transform root, string exactName)
    {
        var hits = new System.Collections.Generic.List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == exactName) hits.Add(t);
        if (hits.Count == 0)
        {
            Debug.LogWarning($"[minato] crowd contact shot skipped: no {exactName} in scene");
            return null;
        }
        return hits[hits.Count / 2];
    }

    private static Transform FindDescendantContaining(Transform root, string token)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains(token)) return t;
        return null;
    }

    private static Vector3 RendererCentre(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return root.position;
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    private static void Shot(Vector3 eye, Vector3 target, float fov, string file)
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
        Debug.Log($"[minato] shot {file}  eye {eye}  fov {fov}");

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(tex);
    }
}
