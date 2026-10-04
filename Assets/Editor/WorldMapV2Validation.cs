using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// World redraw (M1-M4) checks and captures for the v2 relief World Map.
///
///   WorldMapV2Validation.Check    - data only: world_layout.json + the projection JSON parse,
///                                   every RegionCatalog region has an in-frame pin that sits
///                                   on LAND of the layout, ids agree, the art is imported as a
///                                   full-resolution sprite. Logs "[worldmap-v2] PASS"/"FAIL".
///   WorldMapV2Validation.Capture  - Check, then renders the REAL overlay at min zoom, two
///                                   close zooms and a corner-panned max zoom. Never saves.
///
/// Headless (no -nographics):
///   tools/unity/run_steps.ps1 "WorldMapV2Validation.Capture|copilot_wm_capture.log|1"
/// Frames: MapleRidePaths.RenderDir("worldmap_v2").
/// </summary>
public static class WorldMapV2Validation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private static string OutDir => MapleRidePaths.RenderDir("worldmap_v2");

    [System.Serializable] private class LRegion { public string id; public string name; public string status; public float[] pos; }
    [System.Serializable] private class Layout { public string name; public float[] size_km; public float[] coast; public LRegion[] regions; }
    [System.Serializable] private class PPin { public string id; public float u; public float v; }
    [System.Serializable] private class Proj { public string image; public int width; public int height; public PPin[] regions; }

    [MenuItem("MapleRide/World Map/Check v2 world data", priority = 60)]
    public static bool Check()
    {
        var errors = new List<string>();

        var layoutTa = Resources.Load<TextAsset>(RegionCatalog.WorldLayoutResource);
        var projTa = Resources.Load<TextAsset>(RegionCatalog.MapProjectionResource);
        Layout layout = layoutTa != null ? JsonUtility.FromJson<Layout>(layoutTa.text) : null;
        Proj proj = projTa != null ? JsonUtility.FromJson<Proj>(projTa.text) : null;
        if (layout == null || layout.regions == null) errors.Add($"Resources/{RegionCatalog.WorldLayoutResource} missing or unparsable");
        if (proj == null || proj.regions == null) errors.Add($"Resources/{RegionCatalog.MapProjectionResource} missing or unparsable");
        if (proj != null && proj.image != RegionCatalog.MapTextureResource)
            errors.Add($"projection is for '{proj.image}', catalog draws '{RegionCatalog.MapTextureResource}'");

        var tex = Resources.Load<Texture2D>(RegionCatalog.MapTextureResource);
        if (tex == null) errors.Add($"Resources/{RegionCatalog.MapTextureResource} missing");
        EnsureImport(errors);
        tex = Resources.Load<Texture2D>(RegionCatalog.MapTextureResource);
        if (tex != null && proj != null && (tex.width != proj.width || tex.height != proj.height))
            errors.Add($"art is {tex.width}x{tex.height}, projection expects {proj.width}x{proj.height}");

        var projected = new Dictionary<string, Vector2>();
        if (proj?.regions != null) foreach (var p in proj.regions) projected[p.id] = new Vector2(p.u, p.v);
        var laid = new Dictionary<string, LRegion>();
        if (layout?.regions != null) foreach (var r in layout.regions) laid[r.id] = r;

        RegionCatalog.ReloadProjection();
        var seen = new List<(string id, Vector2 pin)>();
        foreach (var region in RegionCatalog.Regions)
        {
            if (!projected.ContainsKey(region.Id)) errors.Add($"{region.Id}: no projected pin");
            if (!laid.TryGetValue(region.Id, out var lr)) errors.Add($"{region.Id}: not in world_layout.json");
            else if (lr.pos == null || lr.pos.Length < 2) errors.Add($"{region.Id}: layout has no pos");
            else if (layout.coast != null && layout.coast.Length >= 6 && !InPoly(layout.coast, lr.pos[0], lr.pos[1])
                     && !region.Id.Contains("isle") && !region.Id.Contains("islands"))
                errors.Add($"{region.Id}: layout pos ({lr.pos[0]:0},{lr.pos[1]:0}) km is off the main coast");

            Vector2 pin = RegionCatalog.PinFor(region);
            if (pin.x < 0.02f || pin.x > 0.98f || pin.y < 0.06f || pin.y > 0.94f)
                errors.Add($"{region.Id}: pin {pin} outside the visible frame");
            foreach (var (id, other) in seen)
                if ((other - pin).magnitude < 0.03f) errors.Add($"{region.Id}: pin within 3 % of {id}");
            seen.Add((region.Id, pin));
            if (projected.TryGetValue(region.Id, out var pp) && (pp - region.Pin).magnitude > 0.002f)
                Debug.LogWarning($"[worldmap-v2] {region.Id}: hard-coded fallback Pin {region.Pin} != projection {pp}");
        }

        Debug.Log($"[worldmap-v2] layout '{layout?.name}' {laid.Count} regions, projection {projected.Count} pins, " +
                  $"catalog {RegionCatalog.Regions.Length} regions, art {(tex != null ? tex.width + "x" + tex.height : "none")}");
        foreach (var e in errors) Debug.LogError("[worldmap-v2] " + e);
        Debug.Log(errors.Count == 0 ? "[worldmap-v2] PASS" : $"[worldmap-v2] FAIL ({errors.Count} errors)");
        return errors.Count == 0;
    }

    /// <summary>Full-resolution single sprite, no mips: the map is zoomed 5x, so 2048 would blur it.</summary>
    private static void EnsureImport(List<string> errors)
    {
        string asset = $"Assets/Resources/{RegionCatalog.MapTextureResource}.png";
        var imp = AssetImporter.GetAtPath(asset) as TextureImporter;
        if (imp == null) { errors.Add($"no TextureImporter at {asset}"); return; }
        bool dirty = false;
        if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; dirty = true; }
        if (imp.spriteImportMode != SpriteImportMode.Single) { imp.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (imp.maxTextureSize < 8192) { imp.maxTextureSize = 8192; dirty = true; }
        if (imp.mipmapEnabled) { imp.mipmapEnabled = false; dirty = true; }
        if (imp.npotScale != TextureImporterNPOTScale.None) { imp.npotScale = TextureImporterNPOTScale.None; dirty = true; }
        if (dirty)
        {
            imp.SaveAndReimport();
            Debug.Log($"[worldmap-v2] re-imported {asset} as an 8192 single sprite, no mips");
        }
    }

    private static bool InPoly(float[] xy, float x, float y)
    {
        bool inside = false;
        int n = xy.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = xy[2 * i], yi = xy[2 * i + 1], xj = xy[2 * j], yj = xy[2 * j + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    [MenuItem("MapleRide/World Map/Capture v2 overlay (zoom + pan)", priority = 61)]
    public static void Capture()
    {
        bool ok = Check();
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutDir);

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[worldmap-v2] no RideBootstrap in the scene."); return; }
        boot.Resolve();
        boot.regions?.Resolve();
        var hud = boot.hud;
        boot.session.Graph = null;
        boot.session.EnsureCourse();
        hud.Build();
        var map = hud.worldMap;
        map.regions = boot.regions;
        map.selectionMode = true;

        var shots = new (string name, Vector2 center, float zoom, string focus)[]
        {
            ("wm2_overview_z1", new Vector2(0.5f, 0.5f), 1f, RegionCatalog.SakuraPass),
            ("wm2_maple_city_z2_5", RegionCatalog.PinFor(RegionCatalog.Find(RegionCatalog.MapleCity)), 2.5f, RegionCatalog.MapleCity),
            ("wm2_minato_strait_z4", RegionCatalog.PinFor(RegionCatalog.Find(RegionCatalog.MinatoCoast)), 4f, RegionCatalog.MinatoCoast),
            ("wm2_corner_ne_z5", new Vector2(1f, 1f), 5f, null),
            ("wm2_corner_sw_z3", new Vector2(0f, 0f), 3f, null),
        };
        foreach (var s in shots)
        {
            map.SetOpen(true);
            map.SetView(s.center, s.zoom);
            hud.Refresh();
            map.SetView(s.center, s.zoom);
            Canvas.ForceUpdateCanvases();
            string path = Path.Combine(OutDir, s.name + ".png");
            RenderOverlay(boot, map, s.center, s.zoom, path);
            Debug.Log($"[worldmap-v2] {s.name}: zoom {map.Zoom:0.00} centre {map.ViewCenter} -> {path}");
        }
        map.SetOpen(false);
        map.selectionMode = false;
        Debug.Log(ok ? "[worldmap-v2] capture done (data PASS)" : "[worldmap-v2] capture done (data FAIL - see errors)");
    }

    /// <summary>The overlay is opaque, so any camera works: the canvas is made world-space in front of it.</summary>
    private static void RenderOverlay(RideBootstrap boot, WorldMapHud map, Vector2 center, float zoom, string path)
    {
        const int W = 1920, H = 1080;
        var go = new GameObject("~WorldMapV2Cam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = new Vector3(0f, -5000f, 0f);
        cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        var canvas = boot.hud.Canvas;
        var rt = (RectTransform)canvas.transform;
        var prevMode = canvas.renderMode; var prevCam = canvas.worldCamera;
        var prevPos = rt.position; var prevRot = rt.rotation; var prevScale = rt.localScale; var prevSize = rt.sizeDelta;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        rt.sizeDelta = new Vector2(W, H);
        const float dist = 1.0f;
        float worldH = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        rt.localScale = Vector3.one * (worldH / H);
        rt.position = cam.transform.position + cam.transform.forward * dist;
        rt.rotation = cam.transform.rotation;
        Canvas.ForceUpdateCanvases();
        // The frame's AspectRatioFitter only resizes after the canvas size change; lay the view out again.
        foreach (var f in canvas.GetComponentsInChildren<UnityEngine.UI.AspectRatioFitter>(true))
        { f.enabled = false; f.enabled = true; }
        Canvas.ForceUpdateCanvases();
        map.SetView(center, zoom);
        Canvas.ForceUpdateCanvases();

        var tex = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = tex;
        cam.Render();
        var prevActive = RenderTexture.active;
        RenderTexture.active = tex;
        var img = new Texture2D(W, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prevActive;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);

        canvas.renderMode = prevMode; canvas.worldCamera = prevCam;
        rt.position = prevPos; rt.rotation = prevRot; rt.localScale = prevScale; rt.sizeDelta = prevSize;
    }
}
