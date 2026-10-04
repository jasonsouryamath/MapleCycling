using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes a north-up orthographic top-down picture of a region for the compact minimap
/// (RouteMapHud loads Resources/Minimap/&lt;regionId&gt;.png + &lt;regionId&gt;_bounds.json).
/// Run: -executeMethod MinimapBake.BakeMinato   (or BakeRegion(regionId) from code)
/// </summary>
public static class MinimapBake
{
    private const string OutDir = "Assets/Resources/Minimap";
        private const float MarginM = 450f;

    public static void BakeMinato() => BakeRegion(RegionCatalog.MinatoCoast, "Minato Coast Environment");

    public static void BakeRegion(string regionId, string regionRootName)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        var graph = RouteGraph.Load();
        if (graph == null) { Debug.LogError("[minimap] no route graph"); return; }

        Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = -lo;
        foreach (var def in graph.courses)
        {
            if (def.regionId != regionId) continue;
            var c = graph.BuildCourse(def.id);
            if (c == null) continue;
            foreach (var p in c.Position)
            {
                lo = Vector2.Min(lo, new Vector2(p.x, p.z));
                hi = Vector2.Max(hi, new Vector2(p.x, p.z));
            }
        }
        if (lo.x > hi.x) { Debug.LogError($"[minimap] no courses for region {regionId}"); return; }
        var centre = (lo + hi) * 0.5f;
        float size = Mathf.Max(hi.x - lo.x, hi.y - lo.y) + MarginM * 2f;

        // Only the target region may be visible (regions share one scene).
        GameObject root = null;
        foreach (var g in scene.GetRootGameObjects())
            if (g.name == regionRootName) root = g;
        bool wasActive = root != null && root.activeSelf;
        if (root != null) root.SetActive(true);

        // STYLISED map from a downward raycast grid (a camera bake is unusable: the ocean and
        // terrain shaders fade to a warm horizon colour with camera distance, so a top-down
        // render is flat haze). Sea navy, land shaded by height, sand-coloured coastline.
        Physics.SyncTransforms();
        const int N = 512;
        var height = new float[N, N];
        var land = new bool[N, N];
        for (int iy = 0; iy < N; iy++)
            for (int ix = 0; ix < N; ix++)
            {
                float x = centre.x - size * 0.5f + (ix + 0.5f) * size / N;
                float z = centre.y - size * 0.5f + (iy + 0.5f) * size / N;
                bool hit = Physics.Raycast(new Vector3(x, 3000f, z), Vector3.down, out var h, 6000f,
                                           ~0, QueryTriggerInteraction.Ignore);
                string n = hit ? h.collider.name.ToLowerInvariant() : "";
                bool sea = !hit || h.point.y < 1.5f || n.Contains("ocean") || n.Contains("sea");
                land[ix, iy] = !sea;
                height[ix, iy] = hit ? h.point.y : 0f;
            }
        // Terrain collision tiles have square edges, which drew rectangular "coastlines":
        // blur the land mask (separable box, radius 7, x2) and re-threshold -> rounded shores.
        var m = new float[N, N];
        for (int iy = 0; iy < N; iy++) for (int ix = 0; ix < N; ix++) m[ix, iy] = land[ix, iy] ? 1f : 0f;
        for (int pass = 0; pass < 4; pass++)
        {
            var o = new float[N, N];
            bool horiz = (pass & 1) == 0;
            for (int iy = 0; iy < N; iy++)
                for (int ix = 0; ix < N; ix++)
                {
                    float sum = 0f;
                    for (int k = -7; k <= 7; k++)
                        sum += horiz ? m[Mathf.Clamp(ix + k, 0, N - 1), iy] : m[ix, Mathf.Clamp(iy + k, 0, N - 1)];
                    o[ix, iy] = sum / 15f;
                }
            m = o;
        }
        for (int iy = 0; iy < N; iy++)
            for (int ix = 0; ix < N; ix++)
            {
                bool l = m[ix, iy] > 0.5f;
                // keep thin real features (the bridge deck, causeway) that the blur would erase
                land[ix, iy] = l || (land[ix, iy] && height[ix, iy] > 3f && m[ix, iy] > 0.08f);
            }
        var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
        var seaDeep = new Color(0.055f, 0.13f, 0.24f);
        var seaShallow = new Color(0.10f, 0.26f, 0.38f);
        var coast = new Color(0.78f, 0.74f, 0.60f);
        for (int iy = 0; iy < N; iy++)
            for (int ix = 0; ix < N; ix++)
            {
                bool nearOther = false, nearLand = false;
                for (int dy = -2; dy <= 2 && !nearOther; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int jx = Mathf.Clamp(ix + dx, 0, N - 1), jy = Mathf.Clamp(iy + dy, 0, N - 1);
                        if (land[jx, jy] != land[ix, iy]) { nearOther = true; break; }
                    }
                for (int dy = -6; dy <= 6 && !land[ix, iy] && !nearLand; dy += 3)
                    for (int dx = -6; dx <= 6; dx += 3)
                        if (land[Mathf.Clamp(ix + dx, 0, N - 1), Mathf.Clamp(iy + dy, 0, N - 1)]) { nearLand = true; break; }
                Color c;
                if (land[ix, iy])
                {
                    float t = Mathf.Clamp01(height[ix, iy] / 320f);
                    c = Color.Lerp(new Color(0.24f, 0.33f, 0.26f), new Color(0.50f, 0.50f, 0.44f), t);
                    // simple hillshade from the height gradient (light from the north-west)
                    float hx = height[Mathf.Min(ix + 1, N - 1), iy] - height[Mathf.Max(ix - 1, 0), iy];
                    float hz = height[ix, Mathf.Min(iy + 1, N - 1)] - height[ix, Mathf.Max(iy - 1, 0)];
                    c *= Mathf.Clamp(1f + (-hx + hz) * 0.012f, 0.75f, 1.2f);
                    if (nearOther) c = coast;
                }
                else c = nearLand ? seaShallow : seaDeep;
                c.a = 1f;
                tex.SetPixel(ix, iy, c);
            }
        tex.Apply();

        Directory.CreateDirectory(OutDir);
        File.WriteAllBytes($"{OutDir}/{regionId}.png", tex.EncodeToPNG());
        File.WriteAllText($"{OutDir}/{regionId}_bounds.json",
            JsonUtility.ToJson(new MinimapBounds { cx = centre.x, cz = centre.y, size = size }));

        Object.DestroyImmediate(tex);
        if (root != null) root.SetActive(wasActive);
        AssetDatabase.Refresh();
        Debug.Log($"[minimap] baked {regionId}: centre ({centre.x:0},{centre.y:0}) size {size:0} m -> {OutDir}/{regionId}.png");
    }
}
