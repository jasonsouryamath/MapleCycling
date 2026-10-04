using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Shunta Metro night sky tooling.
///   ShuntaMetroSky.GenerateTextures : (re)bakes Assets/Textures/ShuntaMetro/Sky/ShuntaStars_4096x2048.png (sRGB-encoded star
///                                     map: ~9,000 magnitude-distributed stars + ~30k faint band stars, B-V tints, Milky Way
///                                     concentration, dust lanes, patchy density) and ShuntaMilkyWay_1024x512.exr (smooth HDR
///                                     band glow, no 8-bit banding), then forces the import settings.
///   ShuntaMetroSky.ApplyToPlayable  : idempotent; adds 'Shunta Night Sky' under 'Shunta Metro Environment' in the playable scene.
///   ShuntaMetroSky.ApplyToOverview  : same for Assets/Scenes/ShuntaMetro.unity.
///   ShuntaMetroSky.ApplyAll         : textures (if missing) + both scenes.
/// </summary>
public static class ShuntaMetroSky
{
    public const string StarPath = "Assets/Textures/ShuntaMetro/Sky/ShuntaStars_4096x2048.png";
    public const string GlowPath = "Assets/Textures/ShuntaMetro/Sky/ShuntaMilkyWay_1024x512.exr";
    public const string ShaderPath = "Assets/Ride/ShuntaMetro/Sky/ShuntaNightSky.shader";
    const string RootName = "Shunta Night Sky";
    const int SW = 4096, SH = 2048, GW = 1024, GH = 512;

    // galactic frame
    static readonly Vector3 Pole = new Vector3(0.38f, 0.62f, 0.69f).normalized;
    static Vector3 ex, ey;

    // ------------------------------------------------------------------ noise
    static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    static float VN(Vector3 p)
    {
        int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
        float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float a = Mathf.Lerp(Mathf.Lerp(Hash(ix, iy, iz), Hash(ix + 1, iy, iz), fx), Mathf.Lerp(Hash(ix, iy + 1, iz), Hash(ix + 1, iy + 1, iz), fx), fy);
        float b = Mathf.Lerp(Mathf.Lerp(Hash(ix, iy, iz + 1), Hash(ix + 1, iy, iz + 1), fx), Mathf.Lerp(Hash(ix, iy + 1, iz + 1), Hash(ix + 1, iy + 1, iz + 1), fx), fy);
        return Mathf.Lerp(a, b, fz);
    }
    static float FBM(Vector3 p, int oct = 4)
    {
        float a = 0.5f, s = 0f;
        for (int i = 0; i < oct; i++) { s += a * VN(p); p = p * 2.03f + new Vector3(17.1f, 17.1f, 17.1f); a *= 0.5f; }
        return s;
    }
    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

    // ------------------------------------------------------------------ Milky Way model
    static void Frame()
    {
        ex = Vector3.Cross(Pole, Vector3.up).normalized; ey = Vector3.Cross(Pole, ex);
    }
    struct Band { public float glow, lane, bulge; }
    static Band Sample(Vector3 d)
    {
        float b = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d, Pole), -1f, 1f));
        float l = Mathf.Atan2(Vector3.Dot(d, ey), Vector3.Dot(d, ex));
        float g = Mathf.Exp(-(l * l) / (2f * 0.95f * 0.95f));          // galactic-centre bulge at l = 0
        float sigma = Mathf.Lerp(0.15f, 0.27f, g);
        float prof = Mathf.Exp(-(b * b) / (2f * sigma * sigma));
        float clump = 0.5f + 1.0f * FBM(d * 3.4f);
        float b0 = 0.05f * Mathf.Sin(l * 2f + 1.3f) + 0.05f * (FBM(d * 2.2f + new Vector3(5, 5, 5), 3) - 0.5f);
        float lane = Mathf.Exp(-((b - b0) * (b - b0)) / (2f * 0.03f * 0.03f)) * (0.45f + 0.55f * Smooth(0.3f, 0.7f, FBM(d * 7f + new Vector3(9, 1, 3), 3))) * (0.45f + 0.55f * g);
        lane = Mathf.Clamp01(lane * 0.95f);
        // secondary rifts
        float rift = Mathf.Exp(-((b + 0.11f) * (b + 0.11f)) / (2f * 0.02f * 0.02f)) * Smooth(0.5f, 0.75f, FBM(d * 9f + new Vector3(2, 8, 4), 3)) * 0.5f;
        lane = Mathf.Clamp01(lane + rift);
        return new Band { glow = prof * (1f + 1.5f * g) * clump * (1f - 0.88f * lane), lane = lane, bulge = g };
    }

    // ------------------------------------------------------------------ colour
    static Color BvToColor(float bv)
    {
        bv = Mathf.Clamp(bv, -0.3f, 1.9f);
        float T = 4600f * (1f / (0.92f * bv + 1.7f) + 1f / (0.92f * bv + 0.62f));
        float t = T / 100f, r, g, bl;
        if (t <= 66f) { r = 255f; g = 99.4708f * Mathf.Log(t) - 161.1196f; } else { r = 329.6987f * Mathf.Pow(t - 60f, -0.1332f); g = 288.1222f * Mathf.Pow(t - 60f, -0.0755f); }
        if (t >= 66f) bl = 255f; else if (t <= 19f) bl = 0f; else bl = 138.5177f * Mathf.Log(t - 10f) - 305.0448f;
        var c = new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(bl / 255f));
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); c = new Color(c.r / m, c.g / m, c.b / m);
        return Color.Lerp(Color.white, c, 0.6f);   // the eye reads little colour: keep the tint subtle
    }
    static float Gauss(System.Random rng) { double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble(); return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)); }
    static byte Enc(float lin)
    {
        lin = Mathf.Clamp01(lin);
        float s = lin <= 0.0031308f ? 12.92f * lin : 1.055f * Mathf.Pow(lin, 1f / 2.4f) - 0.055f;
        return (byte)Mathf.Clamp(Mathf.RoundToInt(s * 255f), 0, 255);
    }

    static Vector3 DirFromUv(float u, float v)   // matches the shader: u = atan2(x, z)/2pi + .5 ; v = asin(y)/pi + .5
    {
        float lon = (u - 0.5f) * 2f * Mathf.PI, lat = (v - 0.5f) * Mathf.PI;
        return new Vector3(Mathf.Sin(lon) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(lon) * Mathf.Cos(lat));
    }

    // ------------------------------------------------------------------ bake
    [MenuItem("MapleRide/Shunta Metro/Generate Sky Textures")]
    public static void GenerateTextures()
    {
        Frame();
        Directory.CreateDirectory(Path.GetDirectoryName(StarPath));
        BakeStars();
        BakeGlow();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        ForceImportSettings();
        Debug.Log("[shunta-sky] textures baked: " + StarPath + " , " + GlowPath);
    }

    static void BakeStars()
    {
        var buf = new float[SW * SH * 3];
        // faint, never-black base starlight
        for (int i = 0; i < buf.Length; i += 3) { buf[i] = 0.0010f; buf[i + 1] = 0.0013f; buf[i + 2] = 0.0019f; }
        var rng = new System.Random(20261003);
        int bright = 0, faint = 0, tries = 0;
        const int Bright = 9000, Faint = 30000;
        float cMin = Mathf.Pow(10f, 0.5f * (-1f - 6.5f));
        while (bright < Bright && tries++ < 4000000)
        {
            var d = RandomDir(rng);
            var bs = Sample(d);
            float patch = 0.35f + 1.15f * Smooth(0.38f, 0.64f, FBM(d * 1.35f + new Vector3(77, 3, 12), 3));   // constellation-scale density variation
            float w = (0.30f + 1.9f * bs.glow * 0.55f) * patch * (1f - 0.8f * bs.lane);
            if ((float)rng.NextDouble() * 2.6f > w) continue;
            // magnitude: cumulative counts N(<m) ~ 10^(0.5 m)
            float uu = (float)rng.NextDouble();
            float mag = 6.5f + 2f * Mathf.Log10(uu * (1f - cMin) + cMin);
            float v = Mathf.Min(1f, Mathf.Pow(10f, -0.4f * (mag - 1.5f) * 0.6f));
            float r = (float)rng.NextDouble(); float bv = r < 0.60f ? 0.65f + 0.35f * Gauss(rng) : r < 0.85f ? 0.0f + 0.2f * Gauss(rng) : 1.4f + 0.25f * Gauss(rng);
            Splat(buf, d, v, BvToColor(bv), 0.62f + 0.2f * Mathf.Max(0f, 3f - mag), mag < 2.4f ? 0.07f * (2.4f - mag) : 0f);
            bright++;
        }
        tries = 0;
        while (faint < Faint && tries++ < 8000000)
        {
            var d = RandomDir(rng);
            var bs = Sample(d);
            float w = (0.03f + 1.6f * bs.glow * 0.55f) * (1f - 0.7f * bs.lane);
            if ((float)rng.NextDouble() * 1.6f > w) continue;
            float v = Mathf.Lerp(0.010f, 0.030f, (float)rng.NextDouble());
            float bv = 0.7f + 0.4f * Gauss(rng);
            Splat(buf, d, v, BvToColor(bv), 0.68f, 0f);
            faint++;
        }
        var px = new Color32[SW * SH];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(Enc(buf[i * 3]), Enc(buf[i * 3 + 1]), Enc(buf[i * 3 + 2]), 255);
        var tex = new Texture2D(SW, SH, TextureFormat.RGBA32, false, false);
        tex.SetPixels32(px);
        File.WriteAllBytes(StarPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        Debug.Log($"[shunta-sky] star map: {bright} stars + {faint} faint band stars");
    }

    static Vector3 RandomDir(System.Random rng)
    {
        double z = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(1 - z * z);
        return new Vector3((float)(r * Math.Sin(a)), (float)z, (float)(r * Math.Cos(a)));
    }

    static void Splat(float[] buf, Vector3 d, float amp, Color col, float sigma, float haloAmp)
    {
        float u = Mathf.Atan2(d.x, d.z) / (2f * Mathf.PI) + 0.5f, v = Mathf.Asin(d.y) / Mathf.PI + 0.5f;
        float px = u * SW, py = v * SH;
        float cosLat = Mathf.Max(0.06f, Mathf.Sqrt(1f - d.y * d.y));
        float sx = Mathf.Min(sigma / cosLat, 14f), sy = sigma;
        float hs = 2.8f;
        float reach = haloAmp > 0f ? hs * 3f : 3.2f * sigma;
        int rx = Mathf.CeilToInt(reach / cosLat), ry = Mathf.CeilToInt(reach);
        rx = Mathf.Min(rx, 40);
        int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
        for (int y = cy - ry; y <= cy + ry; y++)
        {
            if (y < 0 || y >= SH) continue;
            float dy = (y + 0.5f - py);
            for (int x = cx - rx; x <= cx + rx; x++)
            {
                float dx = (x + 0.5f - px);
                float g = Mathf.Exp(-(dx * dx) / (2f * sx * sx) - (dy * dy) / (2f * sy * sy));
                if (haloAmp > 0f) g += haloAmp * Mathf.Exp(-(dx * dx) / (2f * hs * hs / (cosLat * cosLat)) - (dy * dy) / (2f * hs * hs));
                int xx = ((x % SW) + SW) % SW;
                int i = (y * SW + xx) * 3;
                float a = amp * g;
                buf[i] += col.r * a; buf[i + 1] += col.g * a; buf[i + 2] += col.b * a;
            }
        }
    }

    static void BakeGlow()
    {
        var tex = new Texture2D(GW, GH, TextureFormat.RGBAFloat, false, true);
        var px = new Color[GW * GH];
        var blue = new Color(0.62f, 0.76f, 1.0f); var warm = new Color(1.0f, 0.80f, 0.56f);
        for (int y = 0; y < GH; y++)
            for (int x = 0; x < GW; x++)
            {
                var d = DirFromUv((x + 0.5f) / GW, (y + 0.5f) / GH);
                var bs = Sample(d);
                var c = Color.Lerp(blue, warm, Mathf.Clamp01(bs.bulge * 0.85f)) * (0.075f * bs.glow);
                // dust lanes stay faintly reddish rather than empty
                c += new Color(0.0030f, 0.0036f, 0.0052f);
                px[y * GW + x] = new Color(c.r, c.g, c.b, 1f);
            }
        tex.SetPixels(px); tex.Apply(false);
        File.WriteAllBytes(GlowPath, tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static void ForceImportSettings()
    {
        var ti = AssetImporter.GetAtPath(StarPath) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true; ti.streamingMipmaps = false; ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4; ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.isReadable = false;
            ti.SaveAndReimport();
        }
        var gi = AssetImporter.GetAtPath(GlowPath) as TextureImporter;
        if (gi != null)
        {
            gi.textureType = TextureImporterType.Default; gi.sRGBTexture = false; gi.mipmapEnabled = true;
            gi.wrapModeU = TextureWrapMode.Repeat; gi.wrapModeV = TextureWrapMode.Clamp; gi.filterMode = FilterMode.Bilinear;
            gi.npotScale = TextureImporterNPOTScale.None; gi.textureCompression = TextureImporterCompression.Uncompressed; gi.isReadable = false;
            gi.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ scene install
    static ShuntaNightSky Install()
    {
        if (!File.Exists(StarPath) || !File.Exists(GlowPath)) GenerateTextures();
        var rb = UnityEngine.Object.FindFirstObjectByType<ShuntaRouteBuilder>(FindObjectsInactive.Include);
        if (rb == null) { Fail("no ShuntaRouteBuilder in scene"); return null; }
        var prev = GameObject.Find(RootName); while (prev != null) { UnityEngine.Object.DestroyImmediate(prev); prev = GameObject.Find(RootName); }
        var root = new GameObject(RootName);
        var env = GameObject.Find(ShuntaMetroIntegration.EnvRoot);
        if (env != null) root.transform.SetParent(env.transform, true);
        var sky = root.AddComponent<ShuntaNightSky>();
        sky.shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        sky.starMap = AssetDatabase.LoadAssetAtPath<Texture2D>(StarPath);
        sky.glowMap = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowPath);
        if (sky.shader == null || sky.starMap == null || sky.glowMap == null) { Fail($"missing asset shader={sky.shader} star={sky.starMap} glow={sky.glowMap}"); return null; }
        sky.enabled = false; sky.enabled = true;   // rebuild with assigned assets
        var d = UnityEngine.Object.FindFirstObjectByType<ShuntaLookDriver>(FindObjectsInactive.Include);
        if (d != null) d.ApplyKm(d.previewKm);
        return sky;
    }

    static void ApplyScene(string path, string tag)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var sky = Install();
        if (sky == null) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[shunta-sky] APPLY OK ({tag}). {sky.LastReport}");
    }

    [MenuItem("MapleRide/Shunta Metro/Apply Night Sky To Playable")]
    public static void ApplyToPlayable() { ApplyScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro), "playable"); }

    [MenuItem("MapleRide/Shunta Metro/Apply Night Sky To Overview")]
    public static void ApplyToOverview() { ApplyScene("Assets/Scenes/ShuntaMetro.unity", "overview"); }

    public static void ApplyAll() { ApplyToPlayable(); ApplyToOverview(); }

    static void Fail(string msg)
    {
        Debug.LogError("[shunta-sky] FAILED: " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}
