#ifndef MAPLERIDE_HDRP_COMMON_INCLUDED
#define MAPLERIDE_HDRP_COMMON_INCLUDED

// Shared core for the hand-written MapleRide HDRP shaders (spec section 4.4).
//
// Why hand-written and not Shader Graph: the acceptance requirement for
// MapleRide/HDRP/CelLit is "stable two/three-band light, shadow, specular and rim
// controls" with the SAME property names as the Built-in MapleRide/SakuraCel, so an
// existing material converts by swapping its shader and keeps every authored value.
// A .shadergraph is a generated JSON node document; it cannot be reviewed as the
// lighting contract, and it cannot guarantee property-name parity. This include keeps
// the cel maths identical to the Built-in original so the art direction does not drift
// across the pipeline change.
//
// Why the sun arrives through globals: HDRP does not populate _WorldSpaceLightPos0 /
// _LightColor0, and its light-loop data is only reachable from the generated HDRP
// shader passes. MapleRideSunBinder pushes the brightest directional light and the
// trilight ambient into these globals every frame, in the editor and at runtime,
// WITHOUT modifying the scene (see MapleRideSunBinder.cs). The defaults below are a
// plausible late-afternoon sun so a material is never black if the binder is absent.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

// ---------------------------------------------------------------------------------
// Colour space bridge.
//
// This kit's cel maths, light colours and ambient bands were all authored against a
// GAMMA project, where a shader worked directly in sRGB numbers and its output was
// displayed verbatim. HDRP forces LINEAR, which changes BOTH ends of that contract:
//
//   * sRGB textures are now linearised on sample, so albedo arrives darker and less
//     saturated than the Built-in maths expects.
//   * the shader's result is treated as linear and sRGB-encoded for display, so a
//     value that used to display as 0.5 now displays as ~0.73.
//
// Applying neither correction is what washed the whole coast out to pale blue while
// every material, texture and tint was individually correct. These two helpers keep
// the cel maths in the space it was authored in and convert only at the boundaries,
// so the validated Built-in look is preserved exactly under a Linear pipeline.
//
// MR_Authored: linear sample -> authored (sRGB) space, applied to every sRGB texture.
// MR_ToRender: authored result -> linear, applied once to the final colour.
// ---------------------------------------------------------------------------------
// MR_ToRender clamps first: the Gamma pipeline this kit was authored against clamped at
// display, so a cel highlight that computed to 1.4 simply showed as white. Feeding that 1.4
// through a 2.4-power sRGB->Linear curve instead amplifies it to ~2.4 and blows a far larger
// region to pure white (the road, the guardrail and every tree crown went flat white).
// Clamping in authored space reproduces the original behaviour exactly.
float3 MR_Authored(float3 linearSample) { return LinearToSRGB(linearSample); }
float4 MR_Authored(float4 linearSample) { return float4(LinearToSRGB(linearSample.rgb), linearSample.a); }
float3 MR_ToRender(float3 authored)     { return SRGBToLinear(saturate(authored)); }

// MR_AuthoredCol: the SAME bridge, but for material Color PROPERTIES rather than texture
// samples - and it is every bit as load-bearing.
//
// Under a Linear project Unity converts a shader property declared as `Color` to linear space
// before uploading it, so `_Color`, `_ShadeColor`, `_RimColor` and friends arrive here already
// linearised while all the cel maths around them is still authored sRGB. Bridging the textures
// but not the tints leaves the two halves of the same expression in different spaces.
//
// The error is invisible on bright tints and catastrophic on dark ones, which is why it hid for
// so long: an authored 0.92 arrives as 0.83 (a 10% shift nobody can see), but an authored 0.19
// arrives as 0.030 - 6.3x too dark. That is exactly why Shiosai_DistantTrees, authored at
// (0.10, 0.19, 0.15), rendered as a black treeline while every bright material looked correct.
float3 MR_AuthoredCol(float3 linearColor) { return LinearToSRGB(linearColor); }
float4 MR_AuthoredCol(float4 linearColor) { return float4(LinearToSRGB(linearColor.rgb), linearColor.a); }

// ---------------------------------------------------------------------------------
// Distance fog, reconstructed from the scene's Built-in RenderSettings.
//
// The coast was authored with RenderSettings.fog = ExponentialSquared in a WARM cream
// (0.82, 0.76, 0.67). That fog is a large part of the validated look: it supplies the
// aerial perspective on the headlands and mountains AND it is what warms the distance
// back up against a cool blue trilight ambient. HDRP's own fog is disabled (its stock
// Physically Based Sky whitened the world), and a hand-written pass gets no fog for
// free - so without this the coast rendered flat, cold and blue with no depth at all.
//
// Fed by MapleRideSunBinder from RenderSettings; applied in AUTHORED space, i.e. after
// the cel maths and before MR_ToRender, exactly where the Built-in pipeline applied it.
// ---------------------------------------------------------------------------------
float4 _MR_FogColor;
// x = enabled, y = mode (1=Linear, 2=Exponential, 3=ExponentialSquared),
// z = density, w = unused. _MR_FogRange = (start, end, 0, 0).
float4 _MR_FogParams;
float4 _MR_FogRange;

float3 MR_ApplyFog(float3 authored, float3 positionRWS)
{
    if (_MR_FogParams.x < 0.5) return authored;

    float dist = length(positionRWS);
    float mode = _MR_FogParams.y;
    float density = _MR_FogParams.z;

    float f;
    if (mode < 1.5)
    {
        // Linear: 1 at the start distance, 0 at the end distance.
        f = saturate((_MR_FogRange.y - dist) / max(_MR_FogRange.y - _MR_FogRange.x, 1e-4));
    }
    else if (mode < 2.5)
    {
        f = exp2(-density * dist * 1.4426950408); // exp(-d*x)
    }
    else
    {
        float dd = density * dist;
        f = exp2(-dd * dd * 1.4426950408); // exp(-(d*x)^2)
    }

    return lerp(_MR_FogColor.rgb, authored, saturate(f));
}



// Set by MapleRideSunBinder. xyz = unit vector pointing TOWARD the sun; w unused.
float4 _MR_SunDir;
float4 _MR_SunColor;
float4 _MR_AmbSky;
float4 _MR_AmbEquator;
float4 _MR_AmbGround;

float3 MR_SunDirection()
{
    float3 d = _MR_SunDir.xyz;
    // Guard against an unbound global (all zeros): fall back to a high afternoon sun
    // rather than returning a degenerate normalize() and rendering the world black.
    return (dot(d, d) < 1e-6) ? normalize(float3(0.32, 0.78, -0.54)) : normalize(d);
}

float3 MR_SunColor()
{
    float3 c = _MR_SunColor.rgb;
    return (dot(c, c) < 1e-8) ? float3(1.0, 0.95, 0.86) : c;
}

// Trilight ambient, matching the Built-in RenderSettings model the kit was lit against.
// ShadeSH9 is not available outside the Built-in pipeline, and HDRP's ambient probe is
// not bound in a hand-written pass, so the three authored colours are reconstructed
// directly from the world normal's up component.
float3 MR_Ambient(float3 n)
{
    float3 sky = _MR_AmbSky.rgb;
    float3 eq  = _MR_AmbEquator.rgb;
    float3 gnd = _MR_AmbGround.rgb;
    if (dot(sky + eq + gnd, float3(1.0, 1.0, 1.0)) < 1e-6)
    {
        sky = float3(0.55, 0.62, 0.74);
        eq  = float3(0.47, 0.47, 0.47);
        gnd = float3(0.24, 0.22, 0.20);
    }
    float up = n.y;
    return (up > 0.0) ? lerp(eq, sky, saturate(up)) : lerp(eq, gnd, saturate(-up));
}

// Quantises 0..1 into N soft bands. Identical maths to the Built-in SakuraCel CelRamp,
// so converted materials keep their authored banding exactly.
float MR_CelRamp(float value, float steps, float softness)
{
    steps = max(2.0, steps);
    float scaled = saturate(value) * steps;
    float band = floor(scaled);
    float f = scaled - band;
    float soft = smoothstep(0.5 - softness, 0.5 + softness, f);
    return saturate((band + soft) / steps);
}

float MR_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float MR_ValueNoise(float2 worldXZ, float scale)
{
    float2 p = worldXZ * scale;
    float2 i = floor(p);
    float2 f = frac(p);
    float a = MR_Hash21(i);
    float b = MR_Hash21(i + float2(1.0, 0.0));
    float c = MR_Hash21(i + float2(0.0, 1.0));
    float d = MR_Hash21(i + float2(1.0, 1.0));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float MR_TriNoise(float3 wp, float3 an, float scale)
{
    float x = MR_ValueNoise(wp.zy, scale);
    float y = MR_ValueNoise(wp.xz, scale);
    float z = MR_ValueNoise(wp.xy, scale);
    return x * an.x + y * an.y + z * an.z;
}

// Shared vertex plumbing for the hand-written passes.
struct MRAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
#ifdef MR_WANT_TANGENT
    float4 tangentOS  : TANGENT;
#endif
    float2 uv         : TEXCOORD0;
    float4 color      : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct MRVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    // Camera-RELATIVE world position. HDRP renders camera-relative, so this IS the view
    // vector and length(positionRWS) is the true view distance. Deriving distance from
    // the absolute position minus _WorldSpaceCameraPos is not safe here: with camera-
    // relative rendering that global is not the absolute camera origin, which silently
    // produced a near-zero fog factor and no visible aerial perspective at all.
    float3 positionRWS : TEXCOORD4;
    float3 normalWS   : TEXCOORD1;
    float2 uv         : TEXCOORD2;
    float4 color      : TEXCOORD3;
#ifdef MR_WANT_TANGENT
    // Opt-in (CelLit defines MR_WANT_TANGENT): world tangent + bitangent sign, for normal maps.
    float4 tangentWS  : TEXCOORD5;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

MRVaryings MR_Vert(MRAttributes input)
{
    MRVaryings o;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(positionRWS);
    // HDRP works in camera-relative world space; add the camera origin back so every
    // world-space effect in this kit (height tints, snow lines, triplanar weathering,
    // canopy dapple) keeps sampling the ABSOLUTE world position it was authored
    // against. Skipping this makes every such effect swim with the camera.
    o.positionWS = GetAbsolutePositionWS(positionRWS);
    o.positionRWS = positionRWS;
    o.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
#ifdef MR_WANT_TANGENT
    o.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz),
                         input.tangentOS.w * GetOddNegativeScale());
#endif
    o.uv = input.uv;
    o.color = input.color;
    return o;
}

#endif // MAPLERIDE_HDRP_COMMON_INCLUDED
