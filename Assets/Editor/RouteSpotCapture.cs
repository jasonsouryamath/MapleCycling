using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Chase-camera stills at arbitrary arc-length marks on any segment of the route graph.
///
/// SakuraBenchmark only knows the five marks on the climb, which is the wrong tool for a defect
/// reported somewhere else on the network. Segment and distances come in through the
/// environment so a new area can be inspected without another script.
///
///   MR_SPOT_SEG   segment id (default "s1", the Kawabe lakeshore return)
///   MR_SPOT_D     comma-separated arc lengths in metres
///   MR_SPOT_TAG   filename tag
/// </summary>
public static class RouteSpotCapture
{
    const string OutDir = "../good_graphics/spot";

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        SakuraSceneDefaults.Fix();
        Directory.CreateDirectory(OutDir);

        string segId = Env("MR_SPOT_SEG", "s1");
        string tag = Env("MR_SPOT_TAG", "spot");
        string dlist = Env("MR_SPOT_D", "200,400,684,900,1179,1400,1635");

        // Isolation switch. A streaky grey smear on a steep cut slope is either shadow acne or
        // the terrain material itself; killing the shadow pass tells the two apart in one frame
        // instead of by argument.
        if (Env("MR_SPOT_NOSHADOW", "0") == "1")
        {
            QualitySettings.shadows = ShadowQuality.Disable;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None))
                l.shadows = LightShadows.None;
            Debug.Log("[spot] shadows DISABLED for this pass");
        }

        // Terrain layer isolation. The grey smears survived MR_SPOT_NOSHADOW, so they come from
        // the material. Flat-tinting each splat layer says which one paints the slope in a
        // single frame: red = rock, blue = scree, green = grass.
        Material terrainMat = null;
        Color keepRock = Color.white, keepScree = Color.white, keepGrass = Color.white;
        Color keepSoil = Color.white;
        float keepNormal = 0f, keepMacro = 0f;
        bool restoreFlatN = false;
        float keepStart = 0f, keepEnd = 0f;
        string terrainMode = Env("MR_SPOT_TERRAIN", "");
        if (terrainMode != "")
        {
            terrainMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Environment/SakuraPass/Materials/SakuraPass_Terrain.mat");
            if (terrainMat != null)
            {
                keepRock = terrainMat.GetColor("_RockColor");
                keepScree = terrainMat.GetColor("_ScreeColor");
                keepGrass = terrainMat.GetColor("_GrassColor");
                keepSoil = terrainMat.HasProperty("_SoilColor")
                    ? terrainMat.GetColor("_SoilColor") : Color.white;
                keepStart = terrainMat.GetFloat("_SlopeRockStart");
                keepEnd = terrainMat.GetFloat("_SlopeRockEnd");
                if (terrainMode == "layers")
                {
                    terrainMat.SetColor("_RockColor", new Color(1f, 0f, 0f, 1f));
                    terrainMat.SetColor("_ScreeColor", new Color(0f, 0f, 1f, 1f));
                    terrainMat.SetColor("_GrassColor", new Color(0f, 1f, 0f, 1f));
                    if (terrainMat.HasProperty("_SoilColor"))
                        terrainMat.SetColor("_SoilColor", new Color(1f, 1f, 0f, 1f));
                }
                else if (terrainMode == "norock")
                {
                    terrainMat.SetFloat("_SlopeRockStart", 88f);
                    terrainMat.SetFloat("_SlopeRockEnd", 90f);
                }
                else if (terrainMode == "soil" && terrainMat.HasProperty("_SoilColor"))
                {
                    // Does the third UV set survive the glTF round trip? Flat-tinting only the
                    // soil layer answers that in one frame: any magenta at all means the weights
                    // arrived, and where it lands says whether the band is placed sensibly.
                    terrainMat.SetColor("_SoilColor", new Color(1f, 0f, 1f, 1f));
                }
                else if (terrainMode == "flatn")
                {
                    // Kills the normal map and the macro tint break. Anything still streaking
                    // after this is coming from the albedo tap or the mesh, not from shading.
                    keepNormal = terrainMat.GetFloat("_NormalStrength");
                    keepMacro = terrainMat.GetFloat("_MacroVariation");
                    terrainMat.SetFloat("_NormalStrength", 0f);
                    terrainMat.SetFloat("_MacroVariation", 0f);
                    restoreFlatN = true;
                }
                Debug.Log($"[spot] terrain isolation mode '{terrainMode}' applied");
            }
        }

        var graph = RouteGraph.Load();

        // Occluder isolation: hide renderers whose GameObject name matches exactly, so a
        // suspect mesh can be removed from one frame without editing the scene.
        var hidden = new List<Renderer>();
        string hideName = Env("MR_SPOT_HIDE", "");
        if (hideName != "")
        {
            var names = new HashSet<string>();
            foreach (var n in hideName.Split(',')) if (n.Trim().Length > 0) names.Add(n.Trim());
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && names.Contains(r.gameObject.name)) { r.enabled = false; hidden.Add(r); }
            Debug.Log($"[spot] hid {hidden.Count} renderer(s) matching '{hideName}'");
        }

        // Screen-space overlay isolation: an Overlay Canvas composites into whatever render
        // target is active, so a HUD panel shows up inside a Camera.Render() capture as a
        // screen-aligned rectangle that survives camera roll.
        var hiddenCanvases = new List<Canvas>();
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            Debug.Log($"[ui] canvas '{c.name}' mode={c.renderMode} enabled={c.enabled} sortOrder={c.sortingOrder} cam={(c.worldCamera != null ? c.worldCamera.name : "<none>")}");
            if (Env("MR_SPOT_NOUI", "0") == "1" && c.enabled) { c.enabled = false; hiddenCanvases.Add(c); }
        }
        if (hiddenCanvases.Count > 0) Debug.Log($"[ui] disabled {hiddenCanvases.Count} canvas(es)");

        var seg = graph != null ? graph.Segment(segId) : null;        if (seg == null) { Debug.LogError($"[spot] no segment '{segId}'"); EditorApplication.Exit(1); return; }
        Debug.Log($"[spot] segment '{segId}' length {seg.Length:F1} m, {seg.Count} samples");

        var camGo = new GameObject("SpotCam");
        var cam = camGo.AddComponent<Camera>();
        cam.allowHDR = true;
        // Post-FX isolation: capturing the raw forward render says in one frame whether a suspect
        // tint/seam comes from the grade (aerial perspective, bloom) or from the scene itself.
        bool noFx = Env("MR_SPOT_NOFX", "0") == "1";
        if (!noFx) camGo.AddComponent<SakuraPostFX>();
        else Debug.Log("[spot] SakuraPostFX disabled for this capture");
        cam.fieldOfView = 55f;
        // Near-camera card isolation: pushing the near plane out clips any translucent quad
        // sitting right in front of the lens, which no bounds pick or occluder hide can reveal.
        float nearClip = 0.05f;
        if (float.TryParse(Env("MR_SPOT_NEAR", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float nOverride))
        {
            nearClip = nOverride;
            Debug.Log($"[spot] near clip overridden to {nearClip:F2} m");
        }
        cam.nearClipPlane = nearClip;
        cam.farClipPlane = 9000f;
        cam.renderingPath = RenderingPath.Forward;

        // Sky isolation: clearing to magenta instead of the skybox says in one frame whether a
        // suspect flat region is actual geometry or a hole with nothing behind it.
        if (Env("MR_SPOT_NOSKY", "0") == "1")
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
            Debug.Log("[spot] skybox replaced with magenta clear");
        }

        // Empty-scene isolation: culling everything leaves only the skybox. A rectangle that
        // still shows up there cannot come from any authored object, material or light.
        if (Env("MR_SPOT_EMPTY", "0") == "1")
        {
            cam.cullingMask = 0;
            Debug.Log("[spot] culling mask cleared - skybox only");
        }

        // Resolution / MSAA are overridable: a screen-space rectangle artefact that tracks the
        // pixel grid rather than the world is a render-target resolve bug, and changing either
        // of these separates that from anything authored in the scene.
        int rw = 1600, rh = 900, aa = 4;
        if (int.TryParse(Env("MR_SPOT_W", ""), out int wOv) && wOv > 0) rw = wOv;
        if (int.TryParse(Env("MR_SPOT_H", ""), out int hOv) && hOv > 0) rh = hOv;
        if (int.TryParse(Env("MR_SPOT_AA", ""), out int aaOv) && aaOv > 0) aa = aaOv;
        Debug.Log($"[spot] target {rw}x{rh} msaa={aa}");
        var rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = aa };
        var shot = new Texture2D(rw, rh, TextureFormat.RGB24, false);

        foreach (var piece in dlist.Split(','))
        {
            if (!float.TryParse(piece.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float m))
                continue;
            m = Mathf.Clamp(m, 0f, seg.Length);

            int a = seg.IndexAt(m);
            int b = Mathf.Min(a + 1, seg.Count - 1);
            float span = seg.distance[b] - seg.distance[a];
            float f = span > 1e-4f ? (m - seg.distance[a]) / span : 0f;

            Vector3 p = Vector3.Lerp(seg.position[a], seg.position[b], f);
            Vector3 tan = Vector3.Slerp(seg.tangent[a], seg.tangent[b], f).normalized;

            // The player's own chase framing: behind and slightly above, level (no bank roll).
            cam.transform.position = p - tan * 7f + Vector3.up * 3.2f;
            cam.transform.rotation = Quaternion.LookRotation(
                (p + tan * 18f + Vector3.up * 0.8f) - cam.transform.position, Vector3.up);

            // Roll override: rotating about the view axis separates a world-space silhouette
            // (rotates with the scene) from a screen-space artefact (stays axis-aligned).
            if (float.TryParse(Env("MR_SPOT_ROLL", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float roll))
                cam.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, roll);

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, rw, rh), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes($"{OutDir}/{tag}_{segId}_{Mathf.RoundToInt(m)}.png", shot.EncodeToPNG());
            Debug.Log($"[spot] {segId} @ {m:F0} m  pos={p:F1} -> {tag}_{segId}_{Mathf.RoundToInt(m)}.png");

            string pick = Env("MR_SPOT_PICK", "");
            if (pick.Length > 0) PickAlongRay(cam, rt, pick);

            // Containment probe: a screen-wide tint with a straight edge usually means the camera
            // is *inside* a translucent mesh, which a forward ray pick cannot reveal.
            if (Env("MR_SPOT_INSIDE", "0") == "1") ReportContaining(cam.transform.position);

            // Proximity probe: list every renderer whose bounds lie within R metres of the lens,
            // sorted by distance, with render queue - the cheapest way to find a near-camera card.
            if (float.TryParse(Env("MR_SPOT_NEARLIST", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                ReportNearby(cam.transform.position, radius);

            // Screen-AABB match: an artefact that stays axis-aligned under camera roll is the
            // projected bounding box of some renderer, so rank every renderer by how closely
            // its screen-space AABB matches the rectangle measured off the PNG.
            string rect = Env("MR_SPOT_RECT", "");
            if (rect.Length > 0) MatchScreenRect(cam, rect, rw, rh);
        }

        Object.DestroyImmediate(camGo);
        foreach (var r in hidden) r.enabled = true;
        foreach (var c in hiddenCanvases) c.enabled = true;
        if (terrainMat != null)
        {
            terrainMat.SetColor("_RockColor", keepRock);
            terrainMat.SetColor("_ScreeColor", keepScree);
            terrainMat.SetColor("_GrassColor", keepGrass);
            if (terrainMat.HasProperty("_SoilColor")) terrainMat.SetColor("_SoilColor", keepSoil);
            if (restoreFlatN)
            {
                terrainMat.SetFloat("_NormalStrength", keepNormal);
                terrainMat.SetFloat("_MacroVariation", keepMacro);
            }
            terrainMat.SetFloat("_SlopeRockStart", keepStart);
            terrainMat.SetFloat("_SlopeRockEnd", keepEnd);
            AssetDatabase.SaveAssets();
            Debug.Log("[spot] terrain material restored");
        }
        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(0);
    }

    /// Rank renderers by how closely their projected screen-space bounding box matches a
    /// rectangle measured off a capture. Arguments are image-space x0,y0,x1,y1 (top-left origin).
    static void MatchScreenRect(Camera cam, string rect, int rw, int rh)
    {
        string[] f = rect.Split(',');
        if (f.Length < 4) { Debug.LogError("[rect] need x0,y0,x1,y1"); return; }
        float tx0 = float.Parse(f[0], CultureInfo.InvariantCulture);
        float ty0 = float.Parse(f[1], CultureInfo.InvariantCulture);
        float tx1 = float.Parse(f[2], CultureInfo.InvariantCulture);
        float ty1 = float.Parse(f[3], CultureInfo.InvariantCulture);
        Debug.Log($"[rect] target image rect x[{tx0},{tx1}] y[{ty0},{ty1}]");

        var scored = new List<(float err, string line)>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            var b = r.bounds;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            bool anyFront = false;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = cam.WorldToScreenPoint(c);
                if (sp.z <= 0f) continue;
                anyFront = true;
                // Screen space is bottom-left origin; convert to image space for comparison.
                float ix = sp.x, iy = rh - sp.y;
                x0 = Mathf.Min(x0, ix); x1 = Mathf.Max(x1, ix);
                y0 = Mathf.Min(y0, iy); y1 = Mathf.Max(y1, iy);
            }
            if (!anyFront) continue;
            x0 = Mathf.Clamp(x0, 0, rw); x1 = Mathf.Clamp(x1, 0, rw);
            y0 = Mathf.Clamp(y0, 0, rh); y1 = Mathf.Clamp(y1, 0, rh);
            float err = Mathf.Abs(x0 - tx0) + Mathf.Abs(x1 - tx1) + Mathf.Abs(y0 - ty0) + Mathf.Abs(y1 - ty1);
            var mat = r.sharedMaterial;
            scored.Add((err, $"[rect] err={err,8:F0}  x[{x0,6:F0},{x1,6:F0}] y[{y0,6:F0},{y1,6:F0}]  {Path(r.transform)}   [{(mat != null ? mat.name + " / " + (mat.shader != null ? mat.shader.name : "?") : "<none>")}]"));
        }
        scored.Sort((a, b) => a.err.CompareTo(b.err));
        for (int i = 0; i < Mathf.Min(15, scored.Count); i++) Debug.Log(scored[i].line);
    }

    /// List every renderer sitting close to the lens, nearest first, with its render queue.
    /// A large translucent quad parked in front of the camera tints the whole frame with a
    /// hard screen-aligned edge, and shows up here as a queue >= 3000 mesh at ~0 m.
    static void ReportNearby(Vector3 camPos, float radius)
    {
        var near = new List<(float d, string name, string mat, int queue, Vector3 size)>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(r.bounds.ClosestPoint(camPos), camPos);
            if (d > radius) continue;
            var mat = r.sharedMaterial;
            near.Add((d, Path(r.transform),
                mat != null ? mat.name + " / " + (mat.shader != null ? mat.shader.name : "?") : "<none>",
                mat != null ? mat.renderQueue : -1, r.bounds.size));
        }
        near.Sort((a, b) => a.d.CompareTo(b.d));
        Debug.Log($"[near] {near.Count} renderer(s) within {radius:F1} m of {camPos:F1}");
        foreach (var n in near)
            Debug.Log($"[near] {n.d,7:F2} m  q={n.queue,5}  size={n.size:F1}  {n.name}   [{n.mat}]");
    }

    /// Identify what a screen pixel is looking at. Most staged environment meshes have no
    /// collider, so a physics raycast alone is useless here - we test renderer bounds instead
    /// and report every candidate sorted by distance along the ray.
    static void PickAlongRay(Camera cam, RenderTexture rt, string pick)
    {
        string[] xy = pick.Split(',');
        if (xy.Length < 2) return;
        if (!float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float px)) return;
        if (!float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float py)) return;

        cam.targetTexture = rt;
        Ray ray = cam.ScreenPointToRay(new Vector3(px, py, 0f));
        cam.targetTexture = null;
        Debug.Log($"[pick] ray o={ray.origin:F1} d={ray.direction:F3}");

        var hits = new List<(float d, string name, string mat)>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!r.bounds.IntersectRay(ray, out float d)) continue;
            var mat = r.sharedMaterial;
            hits.Add((d, Path(r.transform), mat != null ? mat.name + " / " + (mat.shader != null ? mat.shader.name : "?") : "<none>"));
        }
        hits.Sort((a, b) => a.d.CompareTo(b.d));
        foreach (var h in hits) Debug.Log($"[pick] {h.d,9:F1} m  {h.name}   [{h.mat}]");
        if (hits.Count == 0) Debug.Log("[pick] nothing intersected");
    }

    static void ReportContaining(Vector3 p)
    {
        Debug.Log($"[inside] camera at {p:F2}");
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!r.bounds.Contains(p)) continue;
            var mat = r.sharedMaterial;
            Debug.Log($"[inside] {Path(r.transform)}  size={r.bounds.size:F1}  " +
                      $"[{(mat != null ? mat.name + " / " + (mat.shader != null ? mat.shader.name : "?") : "<none>")}]");
        }
    }

    static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    static string Env(string k, string fallback)
    {
        var v = System.Environment.GetEnvironmentVariable(k);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }
}
