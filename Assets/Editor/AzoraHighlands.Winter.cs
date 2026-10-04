using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// WP-E winter staging (copilot session 1). Azora is a snowbound Swiss winter:
//  * a WeatherSnowCover on the region root drives the shared shaders' snow term while Azora is
//    the active region and blends the WeatherDirector to AlpineSnowfall in play mode;
//  * every environment material under the root opts in to snow (_SnowAccept), EXCEPT emissive,
//    glass, water, paint and anything belonging to a character or bike.
// Runs LAST in Apply so it covers geometry from every work package (A-D) without them doing
// anything.
public static partial class AzoraHighlandsEnvironment
{
    // =================================================================== winter landscape (WP-A)

    /// <summary>
    /// Snow-capped Alpine ranges and roadside plough banks. WP-H1 (2026-09-26) replaced the 14
    /// isolated radial horns - which read as spikes on a white plain - with layered massifs:
    /// foothill ridges 1.5-4 km out and a main range of long, multi-summit massifs 4-9 km out,
    /// built in AzoraHighlands.Ranges.cs and shaded by MapleRide/HDRP/AzoraAlpineRange (baked
    /// snow on the gentle slopes, dark rock on the faces, aerial perspective by distance).
    /// </summary>
    private static void BuildWinterLandscape(Transform root, AzoraRoute route)
    {
        BuildAlpinePeaks(root, route);
        BuildPloughBanks(root, route);
    }

    private static void BuildAlpinePeaks(Transform root, AzoraRoute route)
    {
        var group = new GameObject("Azora Alpine Peaks").transform;
        group.SetParent(root, false);
        // The pre-H1 horn meshes are no longer referenced; drop their assets.
        for (int i = 0; i < 64; i++)
        {
            string stale = $"{MeshDir}/AzoraA_Peak_{i}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(stale) != null) AssetDatabase.DeleteAsset(stale);
        }
        BuildAlpineMassifs(group, route);
    }

    /// <summary>
    /// Plough banks: the rounded ridge of snow a plough leaves just beyond the shoulder on both
    /// sides of a cleared road, with a lumpy crest. The single strongest "the road has been
    /// cleared, it snowed last night" cue, and it frames the dark asphalt.
    /// </summary>
    private static void BuildPloughBanks(Transform root, AzoraRoute route)
    {
        var group = new GameObject("Azora Plough Banks").transform;
        group.SetParent(root, false);
        var mat = CelMaterial("Azora_Snow_Bank", new Color(0.90f, 0.92f, 0.95f), gloss: 0.05f, spec: 0.02f, rim: 0.12f,
                              shade: new Color(0.60f, 0.70f, 0.88f));
        const int SpansPerTile = 700;
        const int Prof = 6;
        float inner = RoadHalfWidth + ShoulderWidth * 0.55f;
        // WP-G: the plough leaves the island-bridge mouth open. Side = the side the island lies on.
        int bridgeSide = 0; float bridgeD = -1e9f;
        if (_bridgeStation >= 0 && LakeValid(_island))
        {
            var bp = route.Position[_bridgeStation]; var bs = route.SideFlat(_bridgeStation);
            bridgeSide = (_island.centre.x - bp.x) * bs.x + (_island.centre.y - bp.z) * bs.z >= 0f ? 1 : -1;
            bridgeD = route.Distance[_bridgeStation];
        }
        for (int t0 = 0; t0 < route.Count - 1; t0 += SpansPerTile)
        {
            int t1 = Mathf.Min(t0 + SpansPerTile, route.Count - 1);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int start = verts.Count, rows = 0;
                for (int i = t0; i <= t1; i++)
                {
                    float d = route.Distance[i];
                    bool village = InVillage(d) || (sign == bridgeSide && Mathf.Abs(d - bridgeD) < 9f);
                    float hgt = village ? 0.10f : 0.34f + (Mathf.PerlinNoise(d * 0.09f, sign * 3.3f) - 0.5f) * 0.30f
                                                        + (Mathf.PerlinNoise(d * 0.61f, sign * 7.1f) - 0.5f) * 0.10f;
                    float wid = village ? 0.45f : 1.15f + (Mathf.PerlinNoise(d * 0.05f, sign * 1.7f) - 0.5f) * 0.5f;
                    for (int k = 0; k <= Prof; k++)
                    {
                        float a = k / (float)Prof;
                        float off = inner + a * wid;
                        Foot(route, i, sign * off, out var f, out _);
                        // half-sine profile, steeper on the road side (the plough blade cut)
                        float prof = Mathf.Sin(Mathf.Pow(a, 0.75f) * Mathf.PI);
                        verts.Add(f + Vector3.up * (hgt * prof - 0.04f));
                        uvs.Add(new Vector2(a, d * 0.25f));
                    }
                    rows++;
                }
                for (int rI = 0; rI < rows - 1; rI++)
                for (int k = 0; k < Prof; k++)
                {
                    int a = start + rI * (Prof + 1) + k, b = a + Prof + 1;
                    if (sign > 0) { tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1); }
                    else { tris.Add(a); tris.Add(a + 1); tris.Add(b); tris.Add(a + 1); tris.Add(b + 1); tris.Add(b); }
                }
            }
            var go = AddMesh(group, $"Banks {t0}", Finish($"AzoraA_Banks_{t0}", verts.ToArray(), uvs.ToArray(), tris), mat, collider: false);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        mat.SetFloat("_Cull", 0f);
    }

    // =================================================================== snow staging (WP-E)

    private static void ApplyWinter(Transform root)
    {
        var sc = root.GetComponent<WeatherSnowCover>() ?? root.gameObject.AddComponent<WeatherSnowCover>();
        sc.cover = 1f;
        sc.snowLineStartY = 0f;
        sc.snowLineFullY = 0f;
        sc.sparkle = 1f;
        sc.driftNoise = 1f;
        // Pre-exposure headroom: CelLit/Terrain clamp to 1.0 BEFORE exposure, and lit snow is
        // albedo x (sun + snow-bounce ambient ~1.7), so an albedo near 0.9 clipped every sunlit
        // snowfield to one flat white. ~0.56 keeps the whole sun-to-shade gradient in range.
        sc.albedo = new Color(0.56f, 0.575f, 0.60f);
        sc.shadowTint = new Color(0.60f, 0.70f, 0.88f);
        sc.shadowFloor = 0.80f;
        // Clear skies (user 2026-09-26): SnowClearing keeps full snow cover with only a few
        // sparkling flakes, instead of the flurries that filled the clear sky with snowfall.
        sc.weatherPreset = "SnowClearing";
        EditorUtility.SetDirty(sc);

        var done = new HashSet<Material>();
        int accepted = 0, refused = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            bool character = IsCharacter(r.transform, root);
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !m.HasProperty("_SnowAccept") || !done.Add(m)) continue;
                string n = m.name.ToLowerInvariant();
                bool refuse = character || n.Contains("glow") || n.Contains("glass") || n.Contains("lantern") ||
                              n.Contains("water") || n.Contains("paint") || n.Contains("marking") || n.Contains("flag") ||
                              n.Contains("window") || n.Contains("emiss") || n.Contains("neon") || n.Contains("sky") ||
                              n.Contains("asphalt") || n.Contains("tarmac") || n.Contains("road");
                m.SetFloat("_SnowAccept", refuse ? 0f : 1f);
                if (m.HasProperty("_SnowBias"))
                {
                    float bias = n.Contains("peak") ? -0.22f
                               : n.Contains("cliff") || n.Contains("crag") ? -0.22f
                               : m.shader != null && m.shader.name.Contains("Terrain") ? 0.0f
                               : n.Contains("needle") || n.Contains("pine") || n.Contains("spruce") || n.Contains("fir") ? 0.04f
                               : n.Contains("shrub") || n.Contains("heather") || n.Contains("gorse") || n.Contains("cushion") ? 0.45f
                               : n.Contains("roof") ? 0.22f
                               : n.Contains("bark") || n.Contains("trunk") ? -0.35f
                               : 0f;
                    m.SetFloat("_SnowBias", bias);
                }
                WinterRetint(m, n);
                EditorUtility.SetDirty(m);
                if (refuse) refused++; else accepted++;
            }
        }
        // Distant ranges (RidgeHaze, no snow term): repaint as snow-covered ranges in cool haze.
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var m = r.sharedMaterial;
            if (m == null || m.shader == null || !m.shader.name.Contains("RidgeHaze") || !done.Add(m)) continue;
            m.SetColor("_Color", new Color(0.60f, 0.66f, 0.76f));
            m.SetColor("_CrestColor", new Color(0.90f, 0.93f, 0.97f));
            if (m.HasProperty("_HazeColor")) m.SetColor("_HazeColor", new Color(0.80f, 0.85f, 0.91f));
            EditorUtility.SetDirty(m);
        }
        Debug.Log($"[azora] winter: WeatherSnowCover on root; {accepted} materials take snow, {refused} refused.");
    }

    /// <summary>Summer greens go to winter: grass and tufts to dry straw, shrubs to dark evergreen.</summary>
    private static void WinterRetint(Material m, string n)
    {
        if (!m.HasProperty("_Color")) return;
        if (n.Contains("grass") || n.Contains("tuft") || n.Contains("flower") || n.Contains("cushion"))
            m.SetColor("_Color", new Color(0.62f, 0.55f, 0.40f, m.GetColor("_Color").a));
        else if (n.Contains("shrub") || n.Contains("gorse") || n.Contains("heather"))
            m.SetColor("_Color", new Color(0.22f, 0.28f, 0.22f, m.GetColor("_Color").a));
    }

    private static bool IsCharacter(Transform t, Transform root)
    {
        for (var p = t; p != null && p != root; p = p.parent)
        {
            if (p.GetComponent<MinatoCrowdActor>() != null || p.GetComponent<Animator>() != null) return true;
            string n = p.name;
            if (n.StartsWith("Crowd_") || n.Contains("Rider") || n.Contains("Kuro") || n.Contains("Person") ||
                n.Contains("Pedestrian") || n.Contains("Townsfolk") || n.Contains("Walker") || n.Contains("Bicycle"))
                return true;
        }
        return t.GetComponent<SkinnedMeshRenderer>() != null;
    }
}
