using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shunta Metro look: shared helpers. Procedural textures, HDRP/Lit material factory (display-referred
/// emissive: the look runs at a fixed camera EV of <see cref="EV"/>, so emissive "rel" 1 = a pixel value of 1),
/// and <see cref="MeshBag"/>, which merges many boxes into one mesh per material.
/// </summary>
public static class ShuntaLookKit
{
    /// <summary>Fixed camera EV100 the whole look is authored for (sky exposure = the same value).</summary>
    public const float EV = 10f;
    /// <summary>Nits that render as a pixel value of 1 at <see cref="EV"/> (1.2 * 2^EV).</summary>
    public static readonly float NitsPerRel = 1.2f * Mathf.Pow(2f, EV);
    /// <summary>Directional lux for a white surface to render at pixel value 1 (pi * 1.2 * 2^EV).</summary>
    public static readonly float LuxPerRel = Mathf.PI * NitsPerRel;

    public static Material Lit(string name, Color baseColor, float smoothness, float metallic = 0f)
    {
        var sh = Shader.Find("HDRP/Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
        // HDRP white-tint rule (see hdrp gotchas): keep base colour non-black
        m.SetColor("_BaseColor", baseColor); m.SetColor("_Color", baseColor);
        m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", metallic);
        return m;
    }

    public static void SetEmissive(Material m, Color rgb, float rel)
    {
        var hdr = new Color(rgb.r, rgb.g, rgb.b, 1f) * (rel * NitsPerRel);
        hdr.a = 1f;
        m.SetColor("_EmissiveColor", hdr); m.SetColor("_EmissionColor", hdr);
        m.SetFloat("_UseEmissiveIntensity", 0f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

    public static void SetMaskMap(Material m, Texture2D mask, float smoothMin, float smoothMax)
    {
        m.SetTexture("_MaskMap", mask); m.EnableKeyword("_MASKMAP");
        m.SetFloat("_SmoothnessRemapMin", smoothMin); m.SetFloat("_SmoothnessRemapMax", smoothMax);
        m.SetFloat("_Metallic", 0f); m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 1f);
        m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
    }

    public static void SetBaseMap(Material m, Texture2D t, Color tint)
    {
        m.SetTexture("_BaseColorMap", t); m.SetTexture("_MainTex", t);
        m.SetColor("_BaseColor", tint); m.SetColor("_Color", tint);
    }

    public static void SetEmissiveMap(Material m, Texture2D t, Color rgb, float rel)
    {
        m.SetTexture("_EmissiveColorMap", t); m.EnableKeyword("_EMISSIVE_COLOR_MAP");
        SetEmissive(m, rgb, rel);
    }

    // ------------------------------------------------------------------ textures

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    static float ValueNoise(float x, float y, int cells, int seed)
    {
        x *= cells; y *= cells;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int x0 = ((xi % cells) + cells) % cells, x1 = (x0 + 1) % cells, y0 = ((yi % cells) + cells) % cells, y1 = (y0 + 1) % cells;
        float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
        float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    /// <summary>Road albedo (asphalt grain, edge lines, dashed centre line) and mask (A = smoothness with puddle blotches). One tile = one road width.</summary>
    public static void MakeRoadTextures(out Texture2D albedo, out Texture2D mask)
    {
        const int S = 256;
        albedo = new Texture2D(S, S, TextureFormat.RGBA32, true, false) { name = "ShuntaRoadAlbedo", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, hideFlags = HideFlags.DontSave };
        mask = new Texture2D(S, S, TextureFormat.RGBA32, true, true) { name = "ShuntaRoadMask", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, hideFlags = HideFlags.DontSave };
        var a = new Color32[S * S]; var m = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float grain = Hash(x, y, 7);
                float blot = ValueNoise(u, v, 6, 3) * 0.6f + ValueNoise(u, v, 16, 5) * 0.4f;
                float g = 52f + grain * 18f + blot * 14f;
                float r = g, gg = g, b = g * 1.06f;
                float smooth = Mathf.Clamp01(0.30f + blot * 0.9f - 0.2f + (grain - 0.5f) * 0.25f);
                float line = 0f; Color lc = Color.white;
                if (Mathf.Abs(u - 0.035f) < 0.011f || Mathf.Abs(u - 0.965f) < 0.011f) { line = 1f; }
                else if (Mathf.Abs(u - 0.5f) < 0.011f && (v % 1f) < 0.5f) { line = 1f; lc = new Color(1f, 0.85f, 0.45f); }
                if (line > 0f) { r = Mathf.Lerp(r, lc.r * 200f, 0.85f); gg = Mathf.Lerp(gg, lc.g * 200f, 0.85f); b = Mathf.Lerp(b, lc.b * 200f, 0.85f); smooth *= 0.6f; }
                a[y * S + x] = new Color32((byte)r, (byte)gg, (byte)b, 255);
                m[y * S + x] = new Color32(0, 255, 0, (byte)(Mathf.Clamp01(smooth) * 255f));
            }
        albedo.SetPixels32(a); albedo.Apply(true); mask.SetPixels32(m); mask.Apply(true);
    }

    /// <summary>Window grid emissive map: 8 columns x 16 floors, one cell per window, random lit pattern.</summary>
    public static Texture2D MakeWindowTexture(int seed)
    {
        const int cols = 8, rows = 16, cell = 8, W = cols * cell, H = rows * cell;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "ShuntaWindows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[W * H];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float h = Hash(c, r, seed);
                byte v = h < 0.52f ? (byte)0 : (byte)Mathf.Lerp(90f, 255f, (h - 0.52f) / 0.48f);
                for (int y = 1; y < cell - 1; y++)
                    for (int x = 1; x < cell - 1; x++)
                        px[(r * cell + y) * W + c * cell + x] = new Color32(v, v, v, 255);
            }
        t.SetPixels32(px); t.Apply(true);
        return t;
    }

    /// <summary>Fine tower facade emission map: 16 columns x 16 floors per tile (use tileU 16 m, tileV 48 m => 1 m x 3 m bays).
    /// Tall panes with dark mullions, per-floor lit/dark bias (offices working late), and warm / cool / white tints
    /// carried in the texture itself (apply with a white emissive tint).</summary>
    public static Texture2D MakeWindowTextureFine(int seed)
    {
        const int cols = 16, rows = 16, cell = 8, W = cols * cell, H = rows * cell;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "ShuntaWindowsFine", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[W * H];
        for (int r = 0; r < rows; r++)
        {
            float floorBias = Hash(99, r, seed);                       // some floors mostly lit, some mostly dark
            for (int c = 0; c < cols; c++)
            {
                float h = Hash(c, r, seed);
                float litChance = Mathf.Lerp(0.22f, 0.78f, floorBias);
                if (h > litChance) continue;                            // dark pane: stays black = no emission
                float k = Hash(c + 31, r + 7, seed);
                Color tint = k < 0.55f ? new Color(1f, 0.78f, 0.45f)    // warm interior
                           : k < 0.85f ? new Color(0.72f, 0.86f, 1f)    // cool office
                           : new Color(1f, 0.96f, 0.9f);                // white
                float bright = Mathf.Lerp(0.45f, 1f, Hash(c + 5, r + 91, seed));
                for (int y = 2; y < cell - 1; y++)
                    for (int x = 1; x < cell - 1; x++)
                    {
                        float fade = 1f - 0.25f * (y - 2) / (float)(cell - 4);          // light pools toward the ceiling
                        px[(r * cell + y) * W + c * cell + x] = new Color(tint.r * bright * fade, tint.g * bright * fade, tint.b * bright * fade, 1f);
                    }
            }
        }
        t.SetPixels32(px); t.Apply(true);
        return t;
    }

    // ------------------------------------------------------------------ PBR texture set (Assets/Textures/ShuntaMetro)
    // Every factory returns null / false when a texture is missing so callers keep the procedural look.
    // MR_SHUNTA_TEX=0 forces the fallback (before/after comparisons).

    static ShuntaTextureSet _set;
    public static bool TexturesOn
    {
        get
        {
            if (System.Environment.GetEnvironmentVariable("MR_SHUNTA_TEX") == "0") return false;
            if (_set == null) _set = Resources.Load<ShuntaTextureSet>("ShuntaMetro/ShuntaTextureSet");
            return _set != null;
        }
    }
    public static Texture2D Tex(string n) => TexturesOn ? _set.Get(n) : null;

    static void SetNormal(Material m, Texture2D n, float scale)
    {
        if (n == null) return;
        m.SetTexture("_NormalMap", n); m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        m.SetFloat("_NormalScale", scale);
    }

    static void SetTiling(Material m, float u, float v)
    {
        m.SetTextureScale("_BaseColorMap", new Vector2(u, v));
        m.SetVector("_BaseColorMap_ST", new Vector4(u, v, 0, 0));
    }

    /// <summary>Puddle ripple detail (HDRP detail map, masked by the mask map's B channel). tileM = metres per ripple tile.</summary>
    static void SetRipples(Material m, float metresPerUvU, float metresPerUvV, float tileM, float scale)
    {
        var d = Tex("Road_Ripple_Detail"); if (d == null) return;
        m.SetTexture("_DetailMap", d); m.EnableKeyword("_DETAIL_MAP");
        m.SetFloat("_LinkDetailsWithBase", 0f);
        m.SetVector("_DetailMap_ST", new Vector4(metresPerUvU / tileM, metresPerUvV / tileM, 0, 0));
        m.SetFloat("_DetailAlbedoScale", 0f); m.SetFloat("_DetailNormalScale", scale); m.SetFloat("_DetailSmoothnessScale", 0f);
    }

    public static string RoadKind(ShuntaZone z, float wet)
    {
        switch (z.surface)
        {
            case "wet_asphalt": return "Wet";
            case "tunnel_concrete": return "Tunnel";
            case "expressway": return "Expressway";
            case "bridge_deck": return "Bridge";
        }
        return wet >= 0.75f ? "Wet" : "Dry";
    }

    /// <summary>Road ribbon UVs: u = 0..1 across (9 m), v = distance / roadWidth. One texture tile = 9 m x 18 m, so v is scaled by 0.5.</summary>
    public static bool ApplyRoadTextures(Material m, ShuntaZone z, float wet, float roadWidth)
    {
        string kind = RoadKind(z, wet);
        var a = Tex("Road_" + kind + "_Albedo"); var k = Tex("Road_" + kind + "_Mask");
        if (a == null || k == null) return false;
        float t = kind == "Wet" ? 1.05f : Mathf.Lerp(1.15f, 1.05f, wet);     // 2026-10-04: black asphalt      // 2026-10-04: lighter asphalt so streetlight pools and lane paint read
        SetBaseMap(m, a, new Color(t, t, t * 1.03f, 1f));
        SetNormal(m, Tex("Road_" + kind + "_Normal"), 1f);
        // 2026-10-04: caps lowered (was wet .85-.99, dry .55-.95) - roads read as glass; rain zones stay visibly wet but not mirrors.
        // puddle-only shine: the mask carries a low base smoothness and small high-smoothness puddles, so the range must stay wide
        if (kind == "Wet") SetMaskMap(m, k, 0.04f, 0.62f);
        else SetMaskMap(m, k, 0.04f, 0.60f);          // dry asphalt can never reach the SSR floor (.80): only puddles mirror
        SetTiling(m, 1f, 0.5f);
        SetRipples(m, roadWidth, roadWidth, 1.3f, kind == "Wet" ? 1f : 0.6f);
        return true;
    }

    public static readonly string[] FacadeStyles = { "Concrete", "Tile", "Metal", "Glass" };

    /// <summary>Facade tile = 16 m x 32 m (8 x 16 windows), emission follows the zone's neon driver via the caller's registration.</summary>
    public static Material Facade(string name, string style, Color emitTint, float rel)
    {
        var a = Tex("Facade_" + style + "_Albedo"); var e = Tex("Facade_" + style + "_Emission"); var k = Tex("Facade_" + style + "_Mask");
        if (a == null || e == null || k == null) return null;
        var m = Lit(name, Color.white, 0.4f);
        SetBaseMap(m, a, new Color(1.05f, 1.05f, 1.12f, 1f));       // was .8: facades read near-black at night
        SetNormal(m, Tex("Facade_" + style + "_Normal"), 1f);
        SetMaskMap(m, k, 0.1f, 0.85f);
        SetEmissiveMap(m, e, emitTint, rel);
        return m;
    }

    /// <summary>Atlas material for neon signs: dark housings (albedo) + saturated tubes (emission).</summary>
    public static Material SignAtlas(string name, Color emitTint, float rel)
    {
        var a = Tex("Sign_Atlas_Albedo"); var e = Tex("Sign_Atlas_Emission");
        if (a == null || e == null) return null;
        var m = Lit(name, Color.white, 0.55f);
        SetBaseMap(m, a, Color.white);
        SetEmissiveMap(m, e, emitTint, rel);
        return m;
    }

    /// <summary>UV rect of a sign cell. Vertical blades: 8 x 2 cells of 256x1024 (left half). Horizontal boards: 2 x 8 cells of 1024x256 (right half).</summary>
    public static Rect SignRect(bool vertical, int idx)
    {
        const float inset = 3f / 4096f;
        if (vertical)
        {
            idx &= 15; int c = idx % 8, r = idx / 8;
            return new Rect(c * 256f / 4096f + inset, 1f - (r + 1) * 0.5f + inset, 256f / 4096f - 2 * inset, 0.5f - 2 * inset);
        }
        idx &= 15; int hc = idx % 2, hr = idx / 2;
        return new Rect((2048f + hc * 1024f) / 4096f + inset, 1f - (hr + 1) * 0.125f + inset, 1024f / 4096f - 2 * inset, 0.125f - 2 * inset);
    }

    /// <summary>UV rect of one of the 100 unique neon signs in Sign100_Atlas_* (10 x 10 cells of 512 x 256; n = 0..99, row 0 = top). Regenerate with tools/textures/ShuntaMetro/make_shunta_signs100.py.</summary>
    public static Rect SignRect100(int n)
    {
        const float inset = 2f / 5120f;
        n = Mathf.Clamp(n, 0, 99); int c = n % 10, r = n / 10;
        return new Rect(c * 0.1f + inset, 1f - (r + 1) * 0.1f + inset, 0.1f - 2 * inset, 0.1f - 2 * inset);
    }

    public static Material GroundMaterial(Color tint)
    {
        var a = Tex("Ground_Wet_Albedo"); var k = Tex("Ground_Wet_Mask");
        if (a == null || k == null) return null;
        var m = Lit("Ground", Color.white, 0.2f);
        SetBaseMap(m, a, tint);
        SetNormal(m, Tex("Ground_Wet_Normal"), 1f);
        SetMaskMap(m, k, 0.08f, 0.60f);       // 2026-10-04: was .95 (whole ground read as water)
        SetRipples(m, 40f, 40f, 1.6f, 1f);      // ground boxes are tiled at 40 m per UV unit
        return m;
    }

    public static Material SidewalkMaterial()
    {
        var a = Tex("Sidewalk_Brick_Albedo"); var k = Tex("Sidewalk_Brick_Mask");
        if (a == null || k == null) return null;
        var m = Lit("SidewalkBrick", Color.white, 0.5f);
        SetBaseMap(m, a, new Color(0.9f, 0.9f, 0.95f, 1f)); SetNormal(m, Tex("Sidewalk_Brick_Normal"), 1f); SetMaskMap(m, k, 0.08f, 0.55f);
        return m;
    }

    public static Material TactileMaterial()
    {
        var a = Tex("Tactile_Albedo"); var k = Tex("Tactile_Mask");
        if (a == null || k == null) return null;
        var m = Lit("TactileYellow", Color.white, 0.4f);
        SetBaseMap(m, a, Color.white); SetNormal(m, Tex("Tactile_Normal"), 1f); SetMaskMap(m, k, 0.1f, 0.7f);
        return m;
    }

    /// <summary>Alpha-clipped zebra crossing decal, 9 m x 4.4 m. Under the paint the RGB is asphalt grey, so a failed clip degrades gracefully.</summary>
    public static Material CrosswalkMaterial()
    {
        var a = Tex("Road_Crosswalk_Albedo");
        if (a == null) return null;
        var m = Lit("Crosswalk", Color.white, 0.45f);
        SetBaseMap(m, a, new Color(0.95f, 0.95f, 0.98f, 1f));
        m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = 2450;
        return m;
    }

    // ------------------------------------------------------------------ mesh bag

    public sealed class MeshBag
    {
        sealed class Part { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>(); }
        readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();
        public int BoxCount;

        Part P(Material m) { if (!parts.TryGetValue(m, out var p)) parts[m] = p = new Part(); return p; }

        static readonly Vector3[] FN = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
        static readonly Vector3[] FU = { Vector3.right, Vector3.left, Vector3.back, Vector3.forward, Vector3.right, Vector3.right };
        static readonly Vector3[] FV = { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.back, Vector3.forward };

        /// <summary>Oriented box. <paramref name="tile"/> = metres per UV tile (0 = unit UVs).</summary>
        public void Box(Material mat, Vector3 center, Quaternion rot, Vector3 size, float tileU = 0f, float tileV = 0f, Vector2 uvOff = default, bool skipBottom = false, Rect atlas = default)
        {
            var p = P(mat); BoxCount++;
            Vector3 h = size * 0.5f;
            for (int f = 0; f < 6; f++)
            {
                if (skipBottom && f == 5) continue;
                Vector3 n = FN[f], u = FU[f], v = FV[f];
                float hn = Mathf.Abs(Vector3.Dot(n, h)), hu = Mathf.Abs(Vector3.Dot(u, h)), hv = Mathf.Abs(Vector3.Dot(v, h));
                int b = p.v.Count;
                Vector3[] c = { -u * hu - v * hv, -u * hu + v * hv, u * hu + v * hv, u * hu - v * hv };
                Vector2[] uvs = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    p.v.Add(center + rot * (n * hn + c[i])); p.n.Add(rot * n);
                    float uu = (i == 0 || i == 1) ? 0f : 2f * hu, vv = (i == 0 || i == 3) ? 0f : 2f * hv;
                    if (tileU > 0f) uvs[i] = new Vector2(uu / tileU + uvOff.x, vv / tileV + uvOff.y); else if (atlas.width > 0f) uvs[i] = new Vector2(atlas.x + (uu > 0 ? atlas.width : 0f), atlas.y + (vv > 0 ? atlas.height : 0f)); else uvs[i] = new Vector2(uu > 0 ? 1 : 0, vv > 0 ? 1 : 0);
                    p.uv.Add(uvs[i]);
                }
                // u x v = n in Unity's left-handed view puts u on the viewer's LEFT, so wind the other way
                p.t.Add(b); p.t.Add(b + 2); p.t.Add(b + 1); p.t.Add(b); p.t.Add(b + 3); p.t.Add(b + 2);
            }
        }

        /// <summary>Thin beam between two points.</summary>
        public void Beam(Material mat, Vector3 a, Vector3 b, float thickness, float thickness2 = -1f)
        {
            var d = b - a; if (d.sqrMagnitude < 1e-6f) return;
            var rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up);
            Box(mat, (a + b) * 0.5f, rot, new Vector3(thickness, thickness2 < 0f ? thickness : thickness2, d.magnitude));
        }

        /// <summary>Raw triangle (clockwise = front), smooth normal supplied.</summary>
        public void Tri(Material mat, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            var p = P(mat); int i = p.v.Count;
            p.v.Add(a); p.v.Add(b); p.v.Add(c); p.n.Add(na); p.n.Add(nb); p.n.Add(nc);
            p.uv.Add(Vector2.zero); p.uv.Add(Vector2.right); p.uv.Add(Vector2.up);
            p.t.Add(i); p.t.Add(i + 1); p.t.Add(i + 2);
        }

        /// <summary>Lathe (surface of revolution) about +Y from a (radius,height) profile; faces outward.</summary>
        public void Lathe(Material mat, Vector3 origin, float[] radius, float[] height, int seg)
        {
            for (int i = 0; i < radius.Length - 1; i++)
            {
                float dr = radius[i + 1] - radius[i], dh = height[i + 1] - height[i];
                float len = Mathf.Max(Mathf.Sqrt(dr * dr + dh * dh), 1e-4f);
                float ny = -dr / len, nr = dh / len;
                for (int s = 0; s < seg; s++)
                {
                    float a0 = s * Mathf.PI * 2f / seg, a1 = (s + 1) * Mathf.PI * 2f / seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    Vector3 p00 = origin + d0 * radius[i] + Vector3.up * height[i], p10 = origin + d1 * radius[i] + Vector3.up * height[i];
                    Vector3 p01 = origin + d0 * radius[i + 1] + Vector3.up * height[i + 1], p11 = origin + d1 * radius[i + 1] + Vector3.up * height[i + 1];
                    Vector3 n0 = (d0 * nr + Vector3.up * ny).normalized, n1 = (d1 * nr + Vector3.up * ny).normalized;
                    // outward is clockwise seen from outside: bottom-left, top-left, top-right, bottom-right with u = tangent direction
                    Tri(mat, p00, p01, p11, n0, n0, n1);
                    Tri(mat, p00, p11, p10, n0, n1, n1);
                }
            }
        }

        public List<GameObject> Flush(Transform parent, string namePrefix)
        {
            var res = new List<GameObject>();
            foreach (var kv in parts)
            {
                var p = kv.Value; if (p.v.Count == 0) continue;
                var mesh = new Mesh { name = namePrefix + "_" + kv.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(p.v); mesh.SetNormals(p.n); mesh.SetUVs(0, p.uv); mesh.SetTriangles(p.t, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();   // normal-mapped materials need tangents
                var go = new GameObject(namePrefix + " " + kv.Key.name) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                res.Add(go);
            }
            parts.Clear();
            return res;
        }
    }
}
