using UnityEngine;

/// <summary>
/// NAGISA BAY (B5) - runtime look constants for the tropical resort beach town.
///
/// Lives in its own file so the shared <see cref="RegionDirector"/> only needs ONE additive line
/// per lookup (AmbienceFor / SkyFor) instead of a whole new struct block in a shared file.
///
/// Look: bright TROPICAL LATE AFTERNOON. A high warm-white sun from the WNW over the bay, a
/// clean saturated sky, soft cool fill, low haze. Distinct from Minato (golden hour port) and
/// Shiosai (near-white northern daylight). The HDRP volume "Nagisa Sky Volume" under the region
/// root owns sky / fog / exposure / grade, so the SakuraPostFX grade is neutralised here exactly
/// the way MinatoAmbience does it. ALL PROVISIONAL art tuning.
/// </summary>
public static class NagisaBayLook
{
    public const string RegionId = "nagisa_bay";
    public const string CourseId = "nagisa_bay_loop";
    public const string EnvironmentRoot = "Nagisa Bay Environment";

    public static readonly RegionDirector.Ambience Ambience = new RegionDirector.Ambience
    {
        fog = new Color(0.86f, 0.92f, 0.97f, 1f), fogDensity = 0.000022f,
        // FIX (copilot, fix-implementer pass): ambientSky was (0.62, 0.78, 1.00) - the most
        // saturated / maxed-blue sky of any region (every other region tops out ~0.85-0.95 on
        // blue; compare MinatoAmbience's proven (0.60, 0.72, 0.95)). Under Trilight ambient a
        // near-horizontal upward normal (road, terrain) samples almost PURE ambientSky, so that
        // over-saturated blue was completely overwhelming the road/ground albedo into a flat
        // pale-blue wash while vertical surfaces (buildings/trees, mostly ambientEquator) still
        // read correctly - exactly the reported "washed-out blue road/ground" bug. Brought in
        // line with Minato's calibrated values; equator/ground left untouched.
        ambientSky = new Color(0.58f, 0.72f, 0.90f, 1f),
        ambientEquator = new Color(0.86f, 0.88f, 0.82f, 1f),
        ambientGround = new Color(0.46f, 0.44f, 0.36f, 1f),
        ambientIntensity = 0.80f,
        keyEuler = new Vector3(46f, 112f, 0f),
        keyColor = new Color(1f, 0.93f, 0.80f, 1f), keyIntensity = 1.30f,
        fillEuler = new Vector3(30f, 292f, 0f),
        fillColor = new Color(0.60f, 0.74f, 1f, 1f), fillIntensity = 0.38f,
        keyShadows = true, keyShadowStrength = 0.72f, shadowDistance = 150f,
        bloomThreshold = 1.45f, bloomIntensity = 0.00f,
        exposure = 1.00f, saturation = 1.00f, contrast = 1.00f,
        lift = new Color(0f, 0f, 0f, 0f),
        gain = new Color(1f, 1f, 1f, 0f),
        vignette = 0.00f, vignetteSoftness = 0.88f,
        dofFocusDistance = 240f, dofFocusRange = 2400f, dofStrength = 0.12f,
        dofFalloff = 1.20f, dofIterations = 2,
        aerialStart = 1400f, aerialRange = 16000f,
        aerialDesaturation = 0.12f, aerialFlatten = 0.08f,
        aerialTint = new Color(0.80f, 0.90f, 1.00f, 1f), aerialTintAmount = 0.10f,
        mistBaseY = -4f, mistTopY = 30f, mistStrength = 0.02f, mistStart = 2600f,
        mistColor = new Color(0.88f, 0.94f, 1.00f, 1f),
    };
}
