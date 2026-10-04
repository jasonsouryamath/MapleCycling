// Nagisa Bay (B5) - milestone 2: hero resort hotel, beachfront promenade, palms, beach props.
//
// Every asset is a Blender GLB from tools/blender/build_nagisa_*.py (one GLB, three nodes
// <stem>_LOD0/1/2). Materials are remapped here BY SLOT NAME ("NB_Stone", ...) onto the shared
// HDRP shaders: CelLit (textured + _NormalStrength) for solids, Foliage for fronds/shrubs,
// HDRP/Lit only for glass (incl. the emissive lamp globes) and pool water.
//
// Idempotent: everything lives under "Nagisa Resort" inside the region root, which Apply()
// rebuilds from scratch by exact name, so re-runs never stack duplicates.
//
// All placement numbers below are PROVISIONAL (illustrative tuning) and named.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- provisional tuning
    private const float PromenadeInnerM = 7.4f;     // seaward offset from the centreline
    private const float PromenadeOuterM = 12.4f;
    private const float PromLampSpacingM = 30f;
    private const float PromPalmSpacingM = 13f;
    private const float UmbrellaSpacingM = 11f;
    private const float LifeguardSpacingM = 380f;
    private const float PropShadowFrac = 0.022f;    // LOD1 cut ~ shadows off beyond ~150 m
    private const float SmallPropCull = 0.004f;     // loungers / umbrellas fade out early
    private const float HotelDriveHalfW = 7f;
    private const float HeroEmission = 0.22f;       // curtain-wall window glow (linear)

    private static readonly Dictionary<string, Material> _nb = new Dictionary<string, Material>();
    private static readonly Dictionary<string, (int n, long tris)> _instStats =
        new Dictionary<string, (int, long)>();

    static partial void BuildResort(Transform root)
    {
        _instStats.Clear();
        _pSpots.Clear(); _pWalks.Clear();
        PrepareNagisaTextures();
        BuildNbMaterials();

        var group = new GameObject("Nagisa Resort").transform;
        group.SetParent(root, false);

        var hotel = _route.Pads["hotel"];
        var boutique = _route.Pads["boutique"];

        // ---- hero: tower + podium share the pad frame (-z = sea, +z = road).
        var hotelT = new GameObject("Grand Shiokaze Resort").transform;
        hotelT.SetParent(group, false);
        hotelT.position = hotel.c;
        Place("Nagisa_B_HeroTower", hotelT, Vector3.zero, 0f, 1f, heroShadows: true);   // pass 2 hero (build_nagisa_tower2.py)
        Place("Nagisa_HotelPodium", hotelT, Vector3.zero, 0f, 1f, heroShadows: true);
        DressHotelDeck(hotelT);
        BuildHotelDrive(group, hotel.c);

        // pass 2: the second resort hotel replaces pass 1's boutique block on the boutique pad,
        // front (local -z) turned to the coast road; boutique hotels now live in the town's row A.
        var bt = new GameObject("Shiokaze Boutique").transform;
        bt.SetParent(group, false);
        bt.position = boutique.c;
        var h2 = Place("Nagisa_B_ResortHotel2", bt, Vector3.zero, 0f, 1f, heroShadows: true);
        if (h2 != null) h2.transform.rotation = Quaternion.Euler(0f, FaceRoadYaw(boutique.c), 0f);

        BuildPromenade2(group);   // pass 2 boardwalk promenade (Beach2.cs)
        BuildBeach(group);
        BuildBeach2(group);

        long total = 0;
        foreach (var kv in _instStats)
        {
            total += kv.Value.tris;
            Debug.Log($"[nagisa] resort asset {kv.Key}: {kv.Value.n} instances, " +
                      $"{kv.Value.tris:N0} LOD0 tris total");
        }
        Debug.Log($"[nagisa] resort: {total:N0} LOD0 tris across all instances (worst case, pre-LOD).");
    }

    // ================================================================= textures + materials

    private static void PrepareNagisaTextures()
    {
        if (!Directory.Exists(NagisaTex)) return;
        foreach (var f in Directory.GetFiles(NagisaTex, "*.png"))
        {
            string p = f.Replace('\\', '/');
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            bool changed = false;
            bool normal = p.EndsWith("_Normal.png"), rough = p.EndsWith("_Rough.png");
            bool alpha = p.Contains("Frond") || p.Contains("Hibiscus") || p.Contains("Bougainvillea") || p.Contains("Canopy");
            if (normal && ti.textureType != TextureImporterType.NormalMap)
            { ti.textureType = TextureImporterType.NormalMap; changed = true; }
            if (rough && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
            if (alpha && p.EndsWith("_Albedo.png") && !ti.alphaIsTransparency)
            { ti.alphaIsTransparency = true; changed = true; }
            if (ti.anisoLevel < 4) { ti.anisoLevel = 4; changed = true; }
            if (changed) ti.SaveAndReimport();
        }
    }

    private static Texture2D NbTex(string stem, string kind) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"{NagisaTex}/{stem}_{kind}.png");

    private static Material NbCel(string slot, string texStem, Color tint, float gloss, float spec,
                                  float rim = 0.14f, float normal = 0.8f, float cull = 2f)
    {
        var m = Cel("Nagisa_" + slot, tint, gloss, spec, rim,
                    texStem != null ? NbTex(texStem, "Albedo") : null,
                    texStem != null ? NbTex(texStem, "Normal") : null, normal, cull);
        SetF(m, "_TintVariation", 0.06f);
        SetF(m, "_WeatherAmount", 0f);
        return m;
    }

    private static Material NbFoliage(string slot, string texStem, Color tint, float wind)
    {
        var m = LoadOrCreate("Nagisa_" + slot, FoliageShader);
        m.SetColor("_Color", tint);
        m.SetTexture("_MainTex", NbTex(texStem, "Albedo"));
        SetF(m, "_Cutoff", 0.42f);
        SetIf(m, "_ShadeColor", new Color(0.36f, 0.48f, 0.40f, 1f));
        SetF(m, "_ShadeStrength", 0.45f);
        SetF(m, "_Translucency", 0.38f);
        SetIf(m, "_TransColor", new Color(0.78f, 0.92f, 0.38f, 1f));
        SetF(m, "_RimStrength", 0.12f);
        SetF(m, "_WindStrength", wind);
        SetF(m, "_SnowAccept", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material NbLit(string slot, Color baseColor, float smooth, float metallic,
                                  bool transparent, Texture2D albedo = null, Texture2D normal = null,
                                  Texture2D emission = null, float emissive = 0f)
    {
        var m = LoadOrCreate("Nagisa_" + slot, "HDRP/Lit");
        m.SetColor("_BaseColor", baseColor);
        m.SetTexture("_BaseColorMap", albedo);
        m.SetTexture("_NormalMap", normal);
        if (normal != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
        m.SetFloat("_NormalScale", 0.6f);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_SurfaceType", transparent ? 1f : 0f);
        m.SetFloat("_BlendMode", 0f);
        m.SetFloat("_DoubleSidedEnable", transparent ? 1f : 0f);
        m.SetFloat("_CullMode", transparent ? 0f : 2f);
        if (emission != null && emissive > 0f)
        {
            m.SetTexture("_EmissiveColorMap", emission);
            m.SetColor("_EmissiveColor", new Color(1f, 0.82f, 0.58f, 1f) * emissive);
            m.SetFloat("_UseEmissiveIntensity", 0f);
            m.EnableKeyword("_EMISSIVE_COLOR_MAP");
        }
        else
        {
            m.SetTexture("_EmissiveColorMap", null);
            m.SetColor("_EmissiveColor", Color.black);
            m.DisableKeyword("_EMISSIVE_COLOR_MAP");
        }
        UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Lamp globe: frosted glass on HDRP/Lit with an emissive colour (glass is the one
    /// HDRP/Lit use the E-rules allow), so no Unlit shader enters the region.</summary>
    private static Material NbGlow(string slot, Color c)
    {
        var m = LoadOrCreate("Nagisa_" + slot, "HDRP/Lit");
        if (m.shader == null || m.shader.name != "HDRP/Lit") m.shader = Shader.Find("HDRP/Lit");
        m.SetColor("_BaseColor", c);
        m.SetTexture("_BaseColorMap", null);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.55f);
        m.SetFloat("_SurfaceType", 0f);
        m.SetFloat("_UseEmissiveIntensity", 0f);
        m.SetTexture("_EmissiveColorMap", null);
        m.DisableKeyword("_EMISSIVE_COLOR_MAP");
        m.SetColor("_EmissiveColor", c * 2.2f);
        UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void BuildNbMaterials()
    {
        _nb.Clear();
        var white = Color.white;
        _nb["NB_Stone"] = NbCel("NB_Stone", "NB_Stone", new Color(0.74f, 0.69f, 0.60f), 0.18f, 0.10f, 0.10f, 1.0f);
        _nb["NB_DeckStone"] = NbCel("NB_DeckStone", "NB_DeckStone", new Color(0.86f, 0.84f, 0.80f), 0.14f, 0.08f);
        _nb["NB_Teak"] = NbCel("NB_Teak", "NB_Teak", white, 0.30f, 0.14f);
        _nb["NB_Bronze"] = NbCel("NB_Bronze", "NB_Bronze", white, 0.62f, 0.45f, 0.2f);
        _nb["NB_Plaster"] = NbCel("NB_Plaster", "NB_Plaster", new Color(0.78f, 0.75f, 0.69f), 0.10f, 0.05f);
        _nb["NB_TownWindows"] = NbCel("NB_TownWindows", "NB_TownWindows", white, 0.5f, 0.35f);
        _nb["NB_PoolTile"] = NbCel("NB_PoolTile", "NB_PoolTile", white, 0.45f, 0.30f);
        _nb["NB_Canvas"] = NbCel("NB_Canvas", "NB_Canvas", white, 0.08f, 0.04f, 0.18f, 0.6f, 0f);
        _nb["NB_Planter"] = NbCel("NB_Planter", "NB_Stone", new Color(0.82f, 0.78f, 0.72f), 0.12f, 0.06f);
        _nb["NB_Soil"] = NbCel("NB_Soil", null, new Color(0.28f, 0.22f, 0.16f), 0.05f, 0.02f);
        _nb["NB_HotelSign"] = NbCel("NB_HotelSign", "NB_HotelSign", white, 0.5f, 0.3f, 0.1f, 0.3f);
        _nb["NB_PalmBark"] = NbCel("NB_PalmBark", "NB_PalmBark", white, 0.08f, 0.04f, 0.12f, 1.0f);
        _nb["NB_Paint"] = NbCel("NB_Paint", null, new Color(0.86f, 0.22f, 0.18f), 0.35f, 0.2f);
        _nb["NB_PaintTeal"] = NbCel("NB_PaintTeal", null, new Color(0.18f, 0.62f, 0.62f), 0.35f, 0.2f);
        _nb["NB_Metal"] = NbCel("NB_Metal", null, new Color(0.62f, 0.64f, 0.66f), 0.6f, 0.45f);
        _nb["NB_PalmFrond"] = NbFoliage("NB_PalmFrond", "NB_PalmFrond", white, 0.35f);
        _nb["NB_FanFrond"] = NbFoliage("NB_FanFrond", "NB_FanFrond", white, 0.25f);
        _nb["NB_Hibiscus"] = NbFoliage("NB_Hibiscus", "NB_Hibiscus", white, 0.12f);
        _nb["NB_Bougainvillea"] = NbFoliage("NB_Bougainvillea", "NB_Bougainvillea", white, 0.12f);
        // glass / water only on HDRP/Lit
        _nb["NB_Glass"] = NbLit("NB_Glass", white, 0.88f, 0.25f, false,
                                NbTex("NB_Glass", "Albedo"), NbTex("NB_Glass", "Normal"),
                                NbTex("NB_Glass", "Emission"), HeroEmission);
        _nb["NB_BalconyGlass"] = NbLit("NB_BalconyGlass", new Color(0.62f, 0.82f, 0.80f, 0.32f),
                                       0.92f, 0f, true);
        _nb["NB_PoolWater"] = NbLit("NB_PoolWater", new Color(0.20f, 0.72f, 0.80f, 0.78f),
                                    0.96f, 0f, true);
        _nb["NB_Lamp"] = NbGlow("NB_Lamp", new Color(1f, 0.86f, 0.62f, 1f));
        // milestone 3 slots (town / marina / forest)
        _nb["NB_TownWall"] = NbCel("NB_TownWall", "NB_TownWindows", new Color(0.95f, 0.80f, 0.78f), 0.2f, 0.12f);
        _nb["NB_RoofTile"] = NbCel("NB_RoofTile", "NB_RoofTile", white, 0.16f, 0.08f);
        _nb["NB_Signs"] = NbCel("NB_Signs", "NB_Signs", white, 0.3f, 0.12f, 0.1f, 0.3f);
        _nb["NB_Gelcoat"] = NbCel("NB_Gelcoat", "NB_Gelcoat", new Color(0.94f, 0.94f, 0.93f), 0.7f, 0.5f, 0.2f, 0.5f);
        _nb["NB_Superstructure"] = NbCel("NB_Superstructure", "NB_Superstructure", white, 0.75f, 0.55f, 0.2f, 0.4f);
        _nb["NB_PontoonWood"] = NbCel("NB_PontoonWood", "NB_PontoonWood", white, 0.12f, 0.06f);
        _nb["NB_Concrete"] = NbCel("NB_Concrete", "NB_Plaster", new Color(0.70f, 0.69f, 0.66f), 0.08f, 0.04f);
        _nb["NB_Canopy"] = NbFoliage("NB_Canopy", "NB_Canopy", new Color(0.92f, 0.96f, 0.90f), 0.18f);
        // far-field (merged LOD2) variants: a lower alpha cutoff so the canopy keeps its coverage in
        // the small mips instead of thinning to bare trunks on the far hills; no wind at that range.
        foreach (var (slot, tint) in new[] { ("NB_Canopy", new Color(0.88f, 0.93f, 0.86f)), ("NB_PalmFrond", white), ("NB_FanFrond", white) })
        {
            var far = NbFoliage(slot + "_Far", slot, tint, 0f);
            SetF(far, "_Cutoff", 0.16f);
            _nb[slot + "_Far"] = far;
        }
        BuildNbPass2Materials();
    }

    // ---- pass 2 architecture kit slots (build_nagisa_arch / buildings / tower2 / street).
    private const float InteriorEmission = 0.10f;   // provisional: lit rooms read at dusk, subtle by day
    private const float SkyBarEmission = 0.32f;
    private const float ShopEmission = 0.16f;

    /// <summary>Pastel colourways (NB_Wall base, NB_Accent trim) - PROVISIONAL palette.</summary>
    private static readonly (string name, Color wall, Color accent)[] NbColourways =
    {
        ("Ivory",   new Color(0.97f, 0.95f, 0.90f), new Color(0.80f, 0.74f, 0.64f)),
        ("Coral",   new Color(0.98f, 0.80f, 0.72f), new Color(0.86f, 0.52f, 0.44f)),
        ("Mint",    new Color(0.80f, 0.93f, 0.86f), new Color(0.42f, 0.66f, 0.60f)),
        ("Sky",     new Color(0.80f, 0.89f, 0.96f), new Color(0.40f, 0.58f, 0.74f)),
        ("Butter",  new Color(0.99f, 0.92f, 0.70f), new Color(0.82f, 0.62f, 0.36f)),
        ("Blush",   new Color(0.97f, 0.84f, 0.86f), new Color(0.74f, 0.48f, 0.54f)),
        ("Sand",    new Color(0.90f, 0.84f, 0.74f), new Color(0.56f, 0.46f, 0.36f)),
        ("Lagoon",  new Color(0.72f, 0.90f, 0.90f), new Color(0.18f, 0.52f, 0.56f)),
    };
    private static readonly Dictionary<string, Material>[] _nbColourRemaps = new Dictionary<string, Material>[NbColourways.Length];

    private static readonly Color[] NbCarColours =
    {
        new Color(0.92f, 0.92f, 0.93f), new Color(0.12f, 0.13f, 0.15f), new Color(0.62f, 0.64f, 0.66f),
        new Color(0.70f, 0.16f, 0.14f), new Color(0.20f, 0.36f, 0.62f), new Color(0.88f, 0.84f, 0.70f),
        new Color(0.36f, 0.62f, 0.66f), new Color(0.95f, 0.78f, 0.30f),
    };
    private static readonly Dictionary<string, Material>[] _nbCarRemaps = new Dictionary<string, Material>[NbCarColours.Length];

    private static void BuildNbPass2Materials()
    {
        var white = Color.white;
        _nb["NB_Wall"] = NbCel("NB_Wall", "NB_Wall", NbColourways[0].wall, 0.12f, 0.06f, 0.10f, 0.9f);
        _nb["NB_Accent"] = NbCel("NB_Accent", "NB_Wall", NbColourways[0].accent, 0.12f, 0.06f, 0.10f, 0.9f);
        _nb["NB_WallBoard"] = NbCel("NB_WallBoard", "NB_WallBoard", white, 0.12f, 0.06f);
        _nb["NB_Trim"] = NbCel("NB_Trim", "NB_Trim", white, 0.35f, 0.22f, 0.12f, 0.8f);
        _nb["NB_Awning"] = NbCel("NB_Awning", "NB_Awning", white, 0.08f, 0.04f, 0.16f, 0.5f, 0f);
        _nb["NB_Signs2"] = NbCel("NB_Signs2", "NB_Signs2", white, 0.35f, 0.15f, 0.08f, 0.3f);
        _nb["NB_Signs3"] = NbCel("NB_Signs3", "NB_Signs3", white, 0.35f, 0.15f, 0.08f, 0.3f);   // NB2 pass 3 resort + shop names
        _nb["NB_Paving"] = NbCel("NB_Paving", "NB_Paving", white, 0.10f, 0.05f, 0.06f, 0.9f);
        _nb["NB_PlazaStone"] = NbCel("NB_PlazaStone", "NB_PlazaStone", white, 0.12f, 0.06f, 0.06f, 0.9f);
        _nb["NB_Kerb"] = NbCel("NB_Kerb", "NB_Kerb", white, 0.10f, 0.05f, 0.06f, 0.8f);
        _nb["NB_Lawn"] = NbCel("NB_Lawn", "NB_Lawn", white, 0.05f, 0.02f, 0.06f, 0.9f);
        _nb["NB_Groundcover"] = NbCel("NB_Groundcover", "NB_Groundcover", white, 0.06f, 0.03f, 0.08f, 1.0f);
        _nb["NB_Gravel"] = NbCel("NB_Gravel", "NB_Gravel", white, 0.06f, 0.03f, 0.06f, 1.0f);
        _nb["NB_Hedge"] = NbCel("NB_Hedge", "NB_Hedge", white, 0.06f, 0.03f, 0.14f, 1.0f);
        _nb["NB_Solar"] = NbCel("NB_Solar", "NB_Solar", white, 0.75f, 0.55f, 0.1f, 0.5f);
        _nb["NB_MetalRoof"] = NbCel("NB_MetalRoof", "NB_MetalRoof", white, 0.45f, 0.3f, 0.1f, 0.8f);
        _nb["NB_Asphalt2"] = NbCel("NB_Asphalt2", "NB_Asphalt2", white, 0.10f, 0.05f, 0.04f, 0.8f);
        _nb["NB_Terrazzo"] = NbCel("NB_Terrazzo", "NB_Terrazzo", white, 0.45f, 0.28f, 0.08f, 0.6f);
        _nb["NB_Boardwalk"] = NbCel("NB_Boardwalk", "NB_Boardwalk", white, 0.14f, 0.08f, 0.08f, 0.9f);
        _nb["NB_Thatch"] = NbCel("NB_Thatch", "NB_Thatch", white, 0.05f, 0.02f, 0.10f, 1.0f);
        _nb["NB_Net"] = NbCel("NB_Net", null, new Color(0.94f, 0.94f, 0.92f), 0.1f, 0.05f, 0.1f, 0.5f, 0f);
        _nb["NB_Rubber"] = NbCel("NB_Rubber", null, new Color(0.06f, 0.06f, 0.065f), 0.2f, 0.1f, 0.05f);
        _nb["NB_Chrome"] = NbCel("NB_Chrome", null, new Color(0.76f, 0.77f, 0.80f), 0.85f, 0.7f, 0.2f);
        _nb["NB_CarPaint"] = NbCel("NB_CarPaint", null, NbCarColours[0], 0.75f, 0.55f, 0.18f);
        // glass family on HDRP/Lit (the E-rule exception): tinted car glass, lit interiors
        // behind the recessed windows, the brighter sky-bar crown, and pool water.
        _nb["NB_CarGlass"] = NbLit("NB_CarGlass", new Color(0.10f, 0.13f, 0.16f, 1f), 0.9f, 0.1f, false);
        _nb["NB_Interior"] = NbLit("NB_Interior", white, 0.82f, 0.05f, false,
                                   NbTex("NB_Interior", "Albedo"), null, NbTex("NB_Interior", "Emission"), InteriorEmission);
        _nb["NB_ShopInterior"] = NbLit("NB_ShopInterior", white, 0.82f, 0.05f, false,
                                       NbTex("NB_ShopInterior", "Albedo"), null, NbTex("NB_ShopInterior", "Emission"), ShopEmission);
        _nb["NB_SkyGlass"] = NbLit("NB_SkyGlass", white, 0.86f, 0.05f, false,
                                   NbTex("NB_Interior", "Albedo"), null, NbTex("NB_Interior", "Emission"), SkyBarEmission);
        _nb["NB_Water"] = _nb["NB_PoolWater"];

        for (int i = 0; i < NbColourways.Length; i++)
        {
            var cw = NbColourways[i];
            _nbColourRemaps[i] = new Dictionary<string, Material>
            {
                ["NB_Wall"] = i == 0 ? _nb["NB_Wall"] : NbCel("NB_Wall_" + cw.name, "NB_Wall", cw.wall, 0.12f, 0.06f, 0.10f, 0.9f),
                ["NB_Accent"] = i == 0 ? _nb["NB_Accent"] : NbCel("NB_Accent_" + cw.name, "NB_Wall", cw.accent, 0.12f, 0.06f, 0.10f, 0.9f),
            };
        }
        for (int i = 0; i < NbCarColours.Length; i++)
            _nbCarRemaps[i] = new Dictionary<string, Material>
            {
                ["NB_CarPaint"] = i == 0 ? _nb["NB_CarPaint"] : NbCel("NB_CarPaint_" + i, null, NbCarColours[i], 0.75f, 0.55f, 0.18f),
            };
    }

    private static Dictionary<string, Material> NbColourway(int i) =>
        _nbColourRemaps[((i % NbColourways.Length) + NbColourways.Length) % NbColourways.Length];

    private static Dictionary<string, Material> NbCarColour(int i) =>
        _nbCarRemaps[((i % NbCarColours.Length) + NbCarColours.Length) % NbCarColours.Length];

    // ================================================================= instancing

    private static readonly Dictionary<string, GameObject> _glb = new Dictionary<string, GameObject>();

    private static GameObject Glb(string stem)
    {
        if (_glb.TryGetValue(stem, out var g) && g != null) return g;
        g = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{stem}.glb");
        if (g == null) Debug.LogError($"[nagisa] GLB missing: {ModelDir}/{stem}.glb");
        _glb[stem] = g;
        return g;
    }

    private static string SlotName(string n)
    {
        int cut = n.IndexOfAny(new[] { '.', ' ' });
        if (cut > 0) n = n.Substring(0, cut);
        return n.Replace("(Instance)", "").Trim();
    }

    private static int LodLevel(Transform t, Transform top)
    {
        for (var c = t; c != null && c != top; c = c.parent)
            for (int l = 0; l < 3; l++)
                if (c.name.EndsWith($"_LOD{l}")) return l;
        return 0;
    }

    /// <summary>Instance a Nagisa GLB with its LOD ladder, slot-remapped materials and
    /// distance-limited shadows. Local position/yaw are in <paramref name="parent"/> space.</summary>
    public static GameObject Place(string stem, Transform parent, Vector3 localPos, float yawDeg,
                                   float scale, bool heroShadows = false, float cull = 0.0015f,
                                   Dictionary<string, Material> remap = null)
    {
        var src = Glb(stem);
        if (src == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.name = stem;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        go.transform.localScale = Vector3.one * scale;

        var lods = new List<Renderer>[3];
        long lod0Tris = 0;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            int l = LodLevel(r.transform, go.transform.parent);
            (lods[l] ??= new List<Renderer>()).Add(r);
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string slot = SlotName(mats[i].name);
                if (remap != null && remap.TryGetValue(slot, out var rm)) mats[i] = rm;
                else if (_nb.TryGetValue(slot, out var m)) mats[i] = m;
                else Debug.LogWarning($"[nagisa] unmapped slot '{mats[i].name}' on {stem}");
            }
            r.sharedMaterials = mats;
            // shadows: hero buildings cast from LOD0+1; props only from LOD0 (~<150 m)
            bool cast = l == 0 || (heroShadows && l == 1);
            r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            if (l == 0)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) lod0Tris += mf.sharedMesh.triangles.LongLength / 3;
            }
        }

        var list = new List<LOD>();
        float[] cuts = heroShadows ? new[] { 0.10f, 0.03f, 0.0015f } : new[] { 0.060f, PropShadowFrac, cull };
        for (int l = 0; l < 3; l++)
            if (lods[l] != null) list.Add(new LOD(cuts[l], lods[l].ToArray()));
        if (list.Count > 1)
        {
            var last = list[list.Count - 1];
            list[list.Count - 1] = new LOD(heroShadows ? 0.0015f : cull, last.renderers);
            var group = go.GetComponent<LODGroup>();
            if (group == null) group = go.AddComponent<LODGroup>();
            group.SetLODs(list.ToArray());
            group.RecalculateBounds();
        }

        _instStats.TryGetValue(stem, out var s);
        _instStats[stem] = (s.n + 1, s.tris + lod0Tris);
        return go;
    }

    private static GameObject PlaceWorld(string stem, Transform parent, Vector3 world, float yaw,
                                         float scale = 1f, float cull = 0.0015f,
                                         Dictionary<string, Material> remap = null, bool hero = false)
    {
        var go = Place(stem, parent, Vector3.zero, 0f, scale, hero, cull, remap);
        if (go == null) return null;
        go.transform.position = world;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    // ================================================================= hotel dressing

    [Serializable] private class HotelAnchorsDto
    {
        public float deckY;
        public float driveEdgeZ;
    }

    private static List<float[]> ReadAnchorList(string json, string key)
    {
        // JsonUtility cannot read jagged arrays; parse "key": [[a, b(, c)], ...] by hand.
        var list = new List<float[]>();
        int k = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (k < 0) return list;
        int start = json.IndexOf('[', k), depth = 0, i = start;
        var cur = new List<float>(); var num = new System.Text.StringBuilder();
        for (; i < json.Length; i++)
        {
            char ch = json[i];
            if (ch == '[') { depth++; if (depth == 2) cur.Clear(); continue; }
            if (ch == ']' || ch == ',')
            {
                if (num.Length > 0)
                {
                    cur.Add(float.Parse(num.ToString(), System.Globalization.CultureInfo.InvariantCulture));
                    num.Clear();
                }
                if (ch == ']')
                {
                    if (depth == 2) list.Add(cur.ToArray());
                    depth--;
                    if (depth == 0) break;
                }
                continue;
            }
            if (char.IsDigit(ch) || ch == '-' || ch == '.' || ch == 'e' || ch == 'E' || ch == '+') num.Append(ch);
        }
        return list;
    }

    private static void DressHotelDeck(Transform hotelT)
    {
        string path = $"{ModelDir}/Nagisa_HotelAnchors.json";
        if (!File.Exists(path)) { Debug.LogWarning("[nagisa] hotel anchors missing"); return; }
        string json = File.ReadAllText(path);
        var a = JsonUtility.FromJson<HotelAnchorsDto>(json);
        float deck = a.deckY;
        var rng = new System.Random(5501);

        var deckT = new GameObject("Pool Deck Dressing").transform;
        deckT.SetParent(hotelT, false);
        int p = 0;
        foreach (var pl in ReadAnchorList(json, "planters"))
        {
            string stem = (p++ % 3 == 1) ? "Nagisa_FanPalm" : "Nagisa_CoconutPalm_A";
            Place(stem, deckT, new Vector3(pl[0], deck + 0.72f, pl[1]), (float)rng.NextDouble() * 360f,
                  stem == "Nagisa_FanPalm" ? 1.0f : 0.92f);
        }
        int n = 0;
        foreach (var l in ReadAnchorList(json, "loungers"))
        {
            // Sitter faces local -z (backrest at +z): row behind the pool faces the sea (yaw 0),
            // the east row faces the pool (-x, yaw 90).
            float yaw = Mathf.Approximately(l[2], 180f) ? 0f : l[2];
            Place("Nagisa_Lounger", deckT, new Vector3(l[0], deck, l[1]), yaw, 1f, false, SmallPropCull);
            if (n++ % 2 == 0)
            {
                var off = Quaternion.Euler(0, yaw, 0) * new Vector3(2.4f, 0f, 0.6f);
                Place("Nagisa_BeachUmbrella", deckT, new Vector3(l[0], deck, l[1]) + off, 0f, 1f, false, SmallPropCull);
            }
        }
        // drive-loop island palm
        var isl = ReadAnchorList(json, "driveIsland");
        Place("Nagisa_CoconutPalm_A", deckT, new Vector3(0f, 0.62f, 37.5f), 40f, 1.1f);
        _ = isl;

        // ---- pass 2: cabanas (skip the one under the corner planter), lounger sunbathers, guests at
        // the pool edge (deck spots get a walk-layer patch in Life2 so the ground probe keeps them up here),
        // taxis + bellhops under the porte-cochere.
        int cb = 0;
        foreach (var c in ReadAnchorList(json, "cabanas"))
        {
            if (cb++ == 0) continue;
            Place("Nagisa_S_Cabana", deckT, new Vector3(c[0] + 1.2f, deck, c[1]), 90f, 1f, false, SmallPropCull);
            _pSpots.Add(PSd(hotelT.TransformPoint(new Vector3(c[0] + 1.2f, deck, c[1])), Vector3.right, "PoolGuest",
                            MinatoCrowdActor.MotionKind.Sit));
        }
        int li = 0;
        foreach (var l in ReadAnchorList(json, "loungers"))
        {
            float yaw = Mathf.Approximately(l[2], 180f) ? 0f : l[2];
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;       // the sitter's facing (local -z)
            if (li++ % 3 != 2)
                _pSpots.Add(PSd(hotelT.TransformPoint(new Vector3(l[0], deck, l[1])), fwd, "Sunbather", MinatoCrowdActor.MotionKind.Sit, lounger: true));
            if (li % 4 == 1)
                _pSpots.Add(PSd(hotelT.TransformPoint(new Vector3(l[0] + 1.4f, deck, l[1] - 2.6f)), fwd,
                                "PoolGuest", rng.NextDouble() < 0.4 ? MinatoCrowdActor.MotionKind.Wave : MinatoCrowdActor.MotionKind.Idle));
        }
        var taxis = new (float x, float z, float yaw)[] { (-6.5f, 21.5f, 90f), (6.0f, 21.5f, 90f), (-16.5f, 33f, 0f) };
        int ti = 0;
        foreach (var (x, z, yaw) in taxis)
        {
            Place("Nagisa_S_CarTaxi", deckT, new Vector3(x, 0.02f, z), yaw, 1f, false, 0.004f);
            var side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            _pSpots.Add(PS(hotelT.TransformPoint(new Vector3(x, 0f, z) - side * 1.6f), side, ti == 0 ? "Bellhop" : "HotelGuest"));
            ti++;
        }
        foreach (float x in new[] { -3.2f, 2.8f })
            _pSpots.Add(PS(hotelT.TransformPoint(new Vector3(x, 0f, 18.4f)), Vector3.forward, "Bellhop"));
        for (int g = 0; g < 4; g++)
            _pSpots.Add(PS(hotelT.TransformPoint(new Vector3(-1.5f + g * 1.1f, 0f, 17.6f + (g & 1) * 0.8f)),
                           (g & 1) == 0 ? Vector3.right : Vector3.left, "HotelGuest"));
    }

    /// <summary>Paved driveway from the porte-cochère drive loop to the road edge.</summary>
    private static void BuildHotelDrive(Transform group, Vector3 pad)
    {
        float x = pad.x;
        int ri; _route.PlanDistance(x, pad.z + 100f, out ri);
        // walk to the sample nearest in x
        float bestDx = float.MaxValue; int best = ri;
        for (int i = Mathf.Max(0, ri - 40); i < Mathf.Min(_route.Count, ri + 40); i++)
        {
            float dx = Mathf.Abs(_route.Position[i].x - x);
            if (dx < bestDx) { bestDx = dx; best = i; }
        }
        var rp = _route.Position[best];
        float zRoad = rp.z - (RoadHalfWidth + ShoulderWidth) + 0.4f;   // tuck under the verge
        float zDrive = pad.z + 50f;                                     // inside the drive ring
        float y0 = pad.y + 0.05f, y1 = rp.y - 0.02f;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        const int segs = 12;
        for (int s = 0; s <= segs; s++)
        {
            float f = s / (float)segs;
            float z = Mathf.Lerp(zDrive, zRoad, f);
            float y = Mathf.Lerp(y0, y1, Mathf.SmoothStep(0f, 1f, f));
            y = Mathf.Max(y, _ground.Height(x, z) + 0.06f);
            v.Add(new Vector3(x - HotelDriveHalfW, y, z)); uv.Add(new Vector2(0f, z / 2.4f));
            v.Add(new Vector3(x + HotelDriveHalfW, y, z)); uv.Add(new Vector2(HotelDriveHalfW * 2f / 2.4f, z / 2.4f));
            if (s > 0)
            {
                int b = v.Count - 4;
                t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
        }
        FixWinding(v, t);
        AddMesh(group, "Hotel Driveway", Finish("Nagisa_HotelDriveway", v, uv, t), _nb["NB_DeckStone"], false);
    }

    /// <summary>Make every triangle face up (+y) so strips never render back-faced.</summary>
    private static void FixWinding(List<Vector3> v, List<int> t)
    {
        for (int i = 0; i < t.Count; i += 3)
        {
            var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (n.y < 0f) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
        }
    }

    // ================================================================= promenade + beach

    /// <summary>+1/-1: which SideFlat direction points to the sea at sample i.</summary>
    private static float SeaSign(int i)
    {
        var p = _route.Position[i]; var s = _route.SideFlat(i);
        float a = _ground.Coast(p.x + s.x * 25f, p.z + s.z * 25f);
        float b = _ground.Coast(p.x - s.x * 25f, p.z - s.z * 25f);
        return a < b ? 1f : -1f;
    }

    private static bool InPad(string pad, float x, float z, float margin)
    {
        if (!_route.Pads.TryGetValue(pad, out var p)) return false;
        return Mathf.Abs(x - p.c.x) < p.h.x + margin && Mathf.Abs(z - p.c.z) < p.h.y + margin;
    }

    private static bool InAnyPad(float x, float z, float margin) =>
        InPad("hotel", x, z, margin) || InPad("boutique", x, z, margin) || InPad("cafes", x, z, margin) ||
        InPad("town_a", x, z, margin) || InPad("town_b", x, z, margin) || InPad("town_c", x, z, margin) ||
        InPad("marina_quay", x, z, margin);

    private static bool NearDrive(float x) => Mathf.Abs(x - _route.Pads["hotel"].c.x) < HotelDriveHalfW + 3f;

    private static void BuildPromenade(Transform group)
    {
        var promT = new GameObject("Beach Promenade").transform;
        promT.SetParent(group, false);
        int i0 = _route.IndexAt(_route.BeachStartM), i1 = _route.IndexAt(_route.BeachEndM);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        bool open = false; float along = 0f; Vector3 prevMid = Vector3.zero;
        float lastLamp = -999f, lastPalm = -999f, lastBench = -999f;
        var rng = new System.Random(5502);
        for (int i = i0; i <= i1; i++)
        {
            var p = _route.Position[i];
            var sd = _route.SideFlat(i) * SeaSign(i);
            var a = p + sd * PromenadeInnerM; var b = p + sd * PromenadeOuterM;
            var mid = (a + b) * 0.5f;
            bool ok = _ground.Coast(b.x, b.z) > 3f && !NearDrive(mid.x) && !_route.OnBridge(i);
            if (open) along += Vector3.Distance(mid, prevMid);
            prevMid = mid;
            if (!ok) { open = false; continue; }
            a.y = _ground.Height(a.x, a.z) + 0.07f;
            b.y = _ground.Height(b.x, b.z) + 0.07f;
            v.Add(a); uv.Add(new Vector2(0f, along / 2.4f));
            v.Add(b); uv.Add(new Vector2((PromenadeOuterM - PromenadeInnerM) / 2.4f, along / 2.4f));
            if (open)
            {
                int k = v.Count - 4;
                t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            open = true;

            float dist = _route.Distance[i];
            var seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            if (dist - lastLamp >= PromLampSpacingM)
            {
                lastLamp = dist;
                var lp = p + sd * (PromenadeOuterM - 0.5f);
                lp.y = _ground.Height(lp.x, lp.z);
                PlaceWorld("Nagisa_PromenadeLamp", promT, lp, seaYaw);
            }
            if (dist - lastBench >= PromLampSpacingM * 2f && dist - lastLamp > 10f)
            {
                lastBench = dist;
                var bp = p + sd * (PromenadeOuterM - 1.0f);
                bp.y = _ground.Height(bp.x, bp.z);
                PlaceWorld("Nagisa_Bench", promT, bp, seaYaw + 180f);
            }
            if (dist - lastPalm >= PromPalmSpacingM)
            {
                lastPalm = dist;
                float off = PromenadeOuterM + 2.2f + (float)rng.NextDouble() * 1.5f;
                var pp = p + sd * off;
                if (CanPlace(pp.x, pp.z, 1f, out float py, 1.5f) && !InAnyPad(pp.x, pp.z, 2f))
                {
                    bool lean = rng.NextDouble() < 0.45;
                    float yaw = lean ? seaYaw + (float)(rng.NextDouble() * 40 - 20) : (float)rng.NextDouble() * 360f;
                    PlaceWorld(lean ? "Nagisa_CoconutPalm_B" : "Nagisa_CoconutPalm_A", promT,
                               new Vector3(pp.x, py - 0.1f, pp.z), yaw, 0.9f + (float)rng.NextDouble() * 0.25f);
                }
            }
            // flowering hedge on the landward verge (never inside the corridor)
            if (i % 3 == 0)
            {
                var hp = p - sd * (CorridorKeepOutM + 1.8f + (float)rng.NextDouble() * 1.5f);
                if (CanPlace(hp.x, hp.z, 0.5f, out float hy) && !InAnyPad(hp.x, hp.z, 1f))
                    PlaceWorld(rng.NextDouble() < 0.5 ? "Nagisa_Hibiscus" : "Nagisa_Bougainvillea", promT,
                               new Vector3(hp.x, hy - 0.05f, hp.z), (float)rng.NextDouble() * 360f,
                               0.9f + (float)rng.NextDouble() * 0.5f, SmallPropCull);
            }
            if (i % 5 == 2)
            {
                var fp = p - sd * (CorridorKeepOutM + 5f + (float)rng.NextDouble() * 4f);
                if (CanPlace(fp.x, fp.z, 2f, out float fy) && !InAnyPad(fp.x, fp.z, 2f))
                    PlaceWorld("Nagisa_FanPalm", promT, new Vector3(fp.x, fy - 0.1f, fp.z),
                               (float)rng.NextDouble() * 360f, 0.85f + (float)rng.NextDouble() * 0.4f);
            }
        }
        FixWinding(v, t);
        if (t.Count > 0)
            AddMesh(promT, "Promenade Paving", Finish("Nagisa_Promenade", v, uv, t), _nb["NB_DeckStone"], false);
    }

    private static void BuildBeach(Transform group)
    {
        var beachT = new GameObject("Beach Dressing").transform;
        beachT.SetParent(group, false);
        int i0 = _route.IndexAt(_route.BeachStartM), i1 = _route.IndexAt(_route.BeachEndM);
        var rng = new System.Random(5503);
        float lastUmb = -999f, lastGuard = -LifeguardSpacingM * 0.5f;
        for (int i = i0; i <= i1; i++)
        {
            float dist = _route.Distance[i];
            var p = _route.Position[i];
            var sd = _route.SideFlat(i) * SeaSign(i);
            float seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;

            // find the dry-sand band: walk seaward until the coast distance is inside [8, 26] m
            Vector3 sand = Vector3.zero; bool found = false;
            for (float o = PromenadeOuterM + 6f; o < 160f; o += 2f)
            {
                var q = p + sd * o;
                float c = _ground.Coast(q.x, q.z);
                if (c < 8f) break;
                if (c <= 26f) { sand = q; found = true; break; }
            }
            if (!found) continue;

            if (dist - lastGuard >= LifeguardSpacingM)
            {
                var g = sand + sd * 4f;
                if (!InAnyPad(g.x, g.z, 4f) && CanPlace(g.x, g.z, 2f, out float gy, 4f))
                {
                    lastGuard = dist;
                    PlaceWorld("Nagisa_LifeguardTower", beachT, new Vector3(g.x, gy - 0.05f, g.z), seaYaw + 180f);
                    continue;
                }
            }
            if (dist - lastUmb < UmbrellaSpacingM) continue;
            lastUmb = dist;
            if (rng.NextDouble() < 0.18) continue;          // gaps read as natural
            var u = sand + sd * (float)(rng.NextDouble() * 4 - 2);
            if (InAnyPad(u.x, u.z, 3f) || !CanPlace(u.x, u.z, 3f, out float uy, 5f)) continue;
            // loungers: backrest at local +z, so +z points landward (sitter faces the sea)
            float yaw = seaYaw + 180f + (float)(rng.NextDouble() * 16 - 8);
            PlaceWorld("Nagisa_BeachUmbrella", beachT, new Vector3(u.x, uy - 0.15f, u.z),
                       (float)rng.NextDouble() * 360f, 1f, SmallPropCull);
            var side = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            int count = rng.NextDouble() < 0.7 ? 2 : 1;
            for (int k = 0; k < count; k++)
            {
                var lp = u + side * (k == 0 ? -0.85f : 0.85f) + sd * 0.4f;
                lp.y = _ground.Height(lp.x, lp.z) + 0.01f;
                PlaceWorld("Nagisa_Lounger", beachT, lp, yaw, 1f, SmallPropCull);
            }
        }
    }
}
