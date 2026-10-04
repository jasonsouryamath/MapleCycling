using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Single source of truth for which shader a MapleRide surface role should actually use.
///
/// Why this exists
/// ---------------
/// The region generators (Shiosai Coast, Sakura Pass, Maple City, ...) do not merely create
/// materials once - every build they call LoadOrCreate, which re-assigns
/// `mat.shader = Shader.Find(name)` against a hard-coded Built-in shader name. That means an
/// asset-level HDRP conversion pass is silently undone the next time the region is regenerated,
/// and because the diagnostics capture regenerates the region before it renders, the conversion
/// was being reverted in the same run that was supposed to prove it. The symptom was terrain and
/// sky vanishing from the capture (under HDRP a material on a Built-in shader draws with no
/// light-loop data rather than drawing magenta), while every shader still reported
/// supported=True and the material census reported the *asset* correctly converted.
///
/// Resolving the name here instead of at each call site means the generators stay correct no
/// matter which pipeline is active, and it satisfies the rebuild spec's safety rule (SS4.1) that
/// no Built-in shader is deleted: if the project is rolled back to Built-in from the checkpoint,
/// Resolve falls through to the original name automatically.
///
/// A replacement is only used when it actually exists *and* compiles. A missing or broken HDRP
/// shader falls back to the Built-in one, because a catalogued wrong-pipeline surface is easier
/// to find than a silently invisible one.
/// </summary>
public static class MapleRideShaderNames
{
    private static readonly (string builtIn, string hdrp)[] Map =
    {
        ("MapleRide/SakuraCel",           "MapleRide/HDRP/CelLit"),
        ("MapleRide/SakuraTerrain",       "MapleRide/HDRP/Terrain"),
        ("MapleRide/SakuraWater",         "MapleRide/HDRP/Ocean"),
        ("MapleRide/SakuraFoliage",       "MapleRide/HDRP/Foliage"),
        ("MapleRide/SakuraSky",           "MapleRide/HDRP/Sky"),
        ("MapleRide/ShiosaiShallows",     "MapleRide/HDRP/Shallows"),

        // No dedicated HDRP port yet. The architecture shader is a cel-lit surface with the same
        // authored property set, so it routes to CelLit rather than HDRP/Lit: HDRP/Lit is
        // physically lit in lux and this project's lights are authored at intensity ~1-3, which
        // renders those surfaces black. Tracked as a deliberate deviation in SC_MIGRATION_AUDIT.
        ("MapleRide/ShiosaiArchitecture", "MapleRide/HDRP/CelLit"),

        // Sakura Pass's own road/facade shaders. SakuraRoadPass already hard-coded the HDRP
        // names, but SakuraPassEnvironment still asked for the Built-in road shader, so a
        // region rebuild put the carriageway back onto a pipeline-incompatible shader.
        ("MapleRide/SakuraRoad",          "MapleRide/HDRP/Road"),
        ("MapleRide/MapleCityFacade",     "MapleRide/HDRP/CityFacade"),

        // Stock Built-in shaders the region builders reach for directly (petal particles,
        // tunnel sodium lamps). They have no HDRP pass at all, so under HDRP they draw with
        // the error (magenta) shader. HDRP/Unlit is the like-for-like replacement; its
        // property names differ, which is what SetBaseColor/SetBaseTexture below absorb.
        ("Particles/Standard Unlit",      "HDRP/Unlit"),
        ("Legacy Shaders/Particles/Alpha Blended Premultiply", "HDRP/Unlit"),
        ("Sprites/Default",               "HDRP/Unlit"),
        ("Unlit/Color",                   "HDRP/Unlit"),
        ("Unlit/Texture",                 "HDRP/Unlit"),
    };

    // Albedo colour/texture property names across every shader family this project mixes:
    // Built-in and the MapleRide cel shaders (_Color/_MainTex), HDRP/Unlit
    // (_UnlitColor/_UnlitColorMap) and HDRP/Lit (_BaseColor/_BaseColorMap).
    private static readonly string[] ColorProps = { "_Color", "_UnlitColor", "_BaseColor" };
    private static readonly string[] TextureProps = { "_MainTex", "_UnlitColorMap", "_BaseColorMap" };

    /// <summary>Writes an albedo colour to whichever colour property the material's shader declares.</summary>
    public static void SetBaseColor(Material mat, Color color)
    {
        if (mat == null) return;
        foreach (var p in ColorProps) if (mat.HasProperty(p)) mat.SetColor(p, color);
    }

    /// <summary>Writes an albedo texture to whichever texture property the material's shader declares.</summary>
    public static void SetBaseTexture(Material mat, Texture tex)
    {
        if (mat == null || tex == null) return;
        foreach (var p in TextureProps) if (mat.HasProperty(p)) mat.SetTexture(p, tex);
    }

    /// <summary>True when an SRP (HDRP here) is driving rendering rather than Built-in.</summary>
    public static bool HdrpActive => GraphicsSettings.currentRenderPipeline != null;

    /// <summary>
    /// Returns the shader name that should actually be assigned for <paramref name="builtInName"/>
    /// under the currently active render pipeline.
    /// </summary>
    public static string Resolve(string builtInName)
    {
        if (!HdrpActive) return builtInName;

        foreach (var entry in Map)
        {
            if (entry.builtIn != builtInName) continue;

            var replacement = Shader.Find(entry.hdrp);
            if (replacement != null && replacement.isSupported) return entry.hdrp;

            Debug.LogWarning($"[mr-shader] HDRP replacement '{entry.hdrp}' for '{builtInName}' is " +
                             (replacement == null ? "missing" : "not supported on this platform") +
                             " - falling back to the Built-in shader, which will not light correctly under HDRP.");
            return builtInName;
        }

        return builtInName;
    }

    /// <summary>Resolve and load in one step. Never returns null if the Built-in shader exists.</summary>
    public static Shader Find(string builtInName)
    {
        string resolved = Resolve(builtInName);
        return Shader.Find(resolved) ?? Shader.Find(builtInName);
    }
}
