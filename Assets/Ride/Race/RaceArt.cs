using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Sprites, textures and runtime materials for the race feature. The PNGs are drawn by
/// tools/ui/make_race_icons.py into Assets/Resources/Race/ and cached here on first use.
/// </summary>
public static class RaceArt
{
    public const string Dir = "Race/";

    private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

    public static Texture2D Tex(string name)
    {
        if (_tex.TryGetValue(name, out var t) && t != null) return t;
        t = Resources.Load<Texture2D>(Dir + name);
        if (t == null) Debug.LogWarning("[race] missing texture Resources/" + Dir + name +
                                        " (run python tools/ui/make_race_icons.py)");
        _tex[name] = t;
        return t;
    }

    public static Sprite Sprite(string name)
    {
        if (_sprites.TryGetValue(name, out var s) && s != null) return s;
        var t = Tex(name);
        s = t == null ? HudSprites.White
            : UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        _sprites[name] = s;
        return s;
    }

    public static Sprite Icon(RaceType t) => Sprite("race_icon_" + RaceMath.IconName(t));
    public static Sprite InsigniaByIndex(int i) => Sprite($"insignia_{Mathf.Clamp(i, 0, 16):00}");
    public static Sprite Insignia(int level) => InsigniaByIndex(RaceMath.InsigniaIndex(level));
    public static Sprite Glow => Sprite("race_glow");
    public static Sprite Sparkle => Sprite("race_sparkle");

    /// <summary>A rider's greeting portrait (NpcPortraitBake), else the generic silhouette.</summary>
    public static Texture Portrait(string riderName)
    {
        var p = string.IsNullOrEmpty(riderName) ? null
            : Resources.Load<Texture2D>(NpcGreeting.PortraitResourceDir + riderName);
        return p != null ? (Texture)p : NpcGreetingCard.Silhouette;
    }

    // ------------------------------------------------------------------ materials

    /// <summary>HDRP/Unlit transparent (alpha or additive). Same setup WeatherAtmosphere uses.</summary>
    public static Material Unlit(string name, Texture tex, Color color, bool additive)
    {
        var sh = Shader.Find("HDRP/Unlit");
        if (sh == null) return null;
        var m = new Material(sh) { name = name };
        m.SetFloat("_SurfaceType", 1f);
        m.SetFloat("_BlendMode", additive ? 1f : 0f);
        m.SetFloat("_DoubleSidedEnable", 1f);
        m.SetFloat("_CullMode", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_EnableFogOnTransparent", 0f);
        if (tex != null) m.SetTexture("_UnlitColorMap", tex);
        m.SetColor("_UnlitColor", color);
        HDMaterial.ValidateMaterial(m);
        return m;
    }

    /// <summary>The project's character/prop shader, so arches light like the world around them.</summary>
    public static Material CelLit(string name, Texture tex, Color color)
    {
        var sh = Shader.Find("MapleRide/HDRP/CelLit");
        if (sh == null) sh = Shader.Find("HDRP/Lit");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_MainTex") && tex != null) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_BaseColorMap") && tex != null) m.SetTexture("_BaseColorMap", tex);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        return m;
    }

    /// <summary>A flat quad (1 x 1, centred, facing +Z) with UVs, for rings, beams and boards.</summary>
    public static Mesh Quad
    {
        get
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "~RaceQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                                     new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds();
            return _quad;
        }
    }
    private static Mesh _quad;

    public static GameObject QuadObject(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = Quad;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }
}
