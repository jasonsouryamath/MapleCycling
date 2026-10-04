#ifndef MAPLERIDE_SNOW_INCLUDED
#define MAPLERIDE_SNOW_INCLUDED

// WEATHER SNOW COVER (2026-09-26, WP-E). Shared by MapleRideCelLit / Terrain / Foliage.
//
// Globals are written by WeatherSnowCover (Assets/Ride/Weather/WeatherSnowCover.cs). With no
// component alive _MR_SnowCover is 0 and every function here is a no-op, so every other region
// renders exactly as before. Materials additionally opt in per material with _SnowAccept (default
// 0), so characters, riders and bikes never grow a white cap even while a snowy region is active.
//
// The look is built from the cues that make snow read as snow in photographs, not as white paint:
//  * albedo ~0.9 but never clipped, with a faint cool sheen,
//  * SKY-LIT BLUE SHADOWS (open shade on snow is lit by the blue sky dome, not by the sun),
//  * a soft, wide terminator (light scatters inside the snowpack),
//  * view-dependent sparkle from ice crystals facing the sun,
//  * irregular drift edges driven by two noise scales, thinning on steep faces so rock and
//    timber still show through.

float  _MR_SnowCover;    // 0..1 overall cover
float4 _MR_SnowParams;   // x = altitude where cover starts, y = altitude of full cover (y <= x: no altitude term),
                         // z = sparkle strength, w = drift noise strength
float4 _MR_SnowShadow;   // rgb = sky-lit shadow tint (authored sRGB), a = shadow floor
float4 _MR_SnowAlbedo;   // rgb = lit snow albedo (authored sRGB), a = cool sheen strength

float3 MRS_Albedo()
{
    float3 a = _MR_SnowAlbedo.rgb;
    return dot(a, a) < 1e-6 ? float3(0.90, 0.92, 0.95) : a;
}

float3 MRS_Shadow()
{
    float3 s = _MR_SnowShadow.rgb;
    return dot(s, s) < 1e-6 ? float3(0.58, 0.68, 0.86) : s;
}

// Coverage 0..1. geoN = geometric world normal (before any normal map). bias > 0 holds snow on
// steeper faces (terrain, roofs), bias < 0 sheds it sooner (foliage cards, thin props).
float MR_SnowMask(float3 wp, float3 geoN, float accept, float bias)
{
    float cover = saturate(_MR_SnowCover) * accept;
    if (cover <= 0.001) return 0.0;

    float alt = 1.0;
    if (_MR_SnowParams.y > _MR_SnowParams.x + 0.5)
        alt = smoothstep(_MR_SnowParams.x, _MR_SnowParams.y,
                         wp.y + (MR_ValueNoise(wp.xz, 0.004) - 0.5) * (_MR_SnowParams.y - _MR_SnowParams.x) * 0.6);
    cover *= alt;
    if (cover <= 0.001) return 0.0;

    float drift = max(0.0, _MR_SnowParams.w);
    drift = drift < 1e-4 ? 1.0 : drift;
    // High-frequency terms fade with distance, or they alias into shimmering speckle on a far
    // mountainside (value noise has no mips).
    float dist = length(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - wp);
    float near1 = 1.0 - smoothstep(40.0, 120.0, dist);
    float near2 = 1.0 - smoothstep(300.0, 900.0, dist);
    float near3 = 1.0 - smoothstep(1500.0, 4000.0, dist);
    float n1 = (MR_ValueNoise(wp.xz, 0.85) - 0.5) * near1;          // ~1 m clumps
    float n2 = (MR_ValueNoise(wp.xz + 17.3, 0.11) - 0.5) * near2;   // ~9 m drifts
    float n3 = (MR_ValueNoise(wp.xz + 41.9, 0.021) - 0.5) * near3;  // ~50 m wind-scoured patches
    float n4 = MR_ValueNoise(wp.xz + 5.1, 0.0025) - 0.5;            // ~400 m, for mountainsides

    // Up-facing threshold: full cover buries everything below ~70 degrees of slope.
    float thresh = lerp(0.97, 0.18, cover) - bias
                 + (n1 * 0.16 + n2 * 0.26 + n3 * 0.20 + n4 * 0.18) * drift;
    return smoothstep(thresh - 0.07, thresh + 0.07, geoN.y);
}

// Replace the surface albedo under the snow. A thin rim of damp, darker ground at the drift edge
// (melt line) is what makes the transition read as a real edge rather than a blend.
float3 MR_SnowApply(float3 albedo, float mask, float3 wp)
{
    if (mask <= 0.0) return albedo;
    float gd = length(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - wp);
    float grain = lerp(0.5, MR_ValueNoise(wp.xz, 3.1) * 0.5 + MR_ValueNoise(wp.xz + 7.7, 0.6) * 0.5,
                       1.0 - smoothstep(25.0, 80.0, gd));
    // broad, soft tonal undulation (wind-packed vs fresh powder) so a snowfield is never one flat fill
    float broad = MR_ValueNoise(wp.xz + 91.3, 0.045) * 0.6 + MR_ValueNoise(wp.xz + 12.9, 0.008) * 0.4;
    float3 snow = MRS_Albedo() * (0.955 + grain * 0.05 + (broad - 0.5) * 0.07);
    float edge = saturate(1.0 - abs(mask - 0.35) * 5.0) * 0.25;
    float3 under = albedo * (1.0 - edge);
    return lerp(under, snow, smoothstep(0.15, 0.75, mask));
}

// Lighting multiplier for the snow part: soft wrapped terminator, cool sky-lit shade.
// ndlRaw = dot(n, l) in -1..1; shade = cast-shadow term (1 lit). Returns the `lit` factor that
// replaces the cel ramp's lit factor where the surface is snow.
float3 MR_SnowLit(float ndlRaw, float shade)
{
    float wrap = saturate((ndlRaw + 0.45) / 1.45);
    float direct = wrap * shade;
    float3 sh = MRS_Shadow();
    return lerp(sh * max(0.55, _MR_SnowShadow.a > 0 ? _MR_SnowShadow.a : 0.78), float3(1, 1, 1), direct);
}

// Ice-crystal glints: sparse cells that flash when their random facet lines up between the sun
// and the eye. Coordinates are wrapped so large world positions keep float precision.
float MR_SnowSparkle(float3 wp, float3 n, float3 v, float3 l, float shade)
{
    float s = _MR_SnowParams.z;
    if (s <= 0.0) return 0.0;
    float3 w = wp - floor(wp / 64.0) * 64.0;
    float2 cell = floor(w.xz * 38.0 + w.y * 11.0);
    float r = MR_Hash21(cell);
    float3 facet = normalize(n + (float3(MR_Hash21(cell + 3.1), 1.4, MR_Hash21(cell + 9.7)) - float3(0.5, 0.0, 0.5)) * 1.3);
    float3 h = normalize(l + v);
    float glint = pow(saturate(dot(facet, h)), 380.0) * step(0.72, r);
    float dist = length(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - wp);
    float fade = 1.0 - smoothstep(14.0, 60.0, dist);
    return glint * fade * shade * s;
}

// Wind-rippled snow surface (sastrugi): perturbs the normal so low sun rakes across the pack.
float3 MR_SnowNormal(float3 n, float3 wp, float mask)
{
    if (mask <= 0.0) return n;
    float e = 0.35;
    float h0 = MR_ValueNoise(wp.xz, 0.9) + MR_ValueNoise(wp.xz * 1.0 + 5.0, 3.3) * 0.25;
    float hx = MR_ValueNoise(wp.xz + float2(e, 0), 0.9) + MR_ValueNoise(wp.xz + float2(e, 0) + 5.0, 3.3) * 0.25;
    float hz = MR_ValueNoise(wp.xz + float2(0, e), 0.9) + MR_ValueNoise(wp.xz + float2(0, e) + 5.0, 3.3) * 0.25;
    float3 g = float3(hx - h0, 0.0, hz - h0) * (0.55 / e);
    float fd = length(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - wp);
    return normalize(n - g * (mask * 0.7 * (1.0 - smoothstep(40.0, 140.0, fd))));
}

#endif
