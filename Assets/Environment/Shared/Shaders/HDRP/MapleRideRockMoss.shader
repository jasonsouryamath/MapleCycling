// Self-contained rock shader for the Nagisa Bay headland rock scatter (NB-ROCKS,
// 2026-09-30). Copies the MapleRideCelLit/FoliageNormal pattern (own MRAttributes/
// MRVaryings via MR_Vert from MapleRideHDRPCommon.hlsl, cel ramp + HDRP shadow term)
// but is an INDEPENDENT shader/material so it never touches MapleRideCelLit.shader,
// MapleRideHDRPCommon.hlsl or the existing world-height `_MossAmount` mechanic already
// built into CelLit (Nagisa_Ground and every other weathered prop keep using that).
//
// This shader instead reads a HAND-AUTHORED vertex-colour mask baked per rock in
// build_nagisa_rocks_scatter.py (heavier in crevices/shaded undersides, bare on
// exposed high points/edges) and blends a moss tint/roughness break by that mask -
// deterministic per-rock art direction, not a runtime world-height rule.
Shader "MapleRide/HDRP/RockMoss"
{
    Properties
    {
        _Color          ("Base Rock Color", Color) = (0.50,0.49,0.47,1)
        _MossColor      ("Moss Color", Color) = (0.28,0.38,0.20,1)
        _MossMaskGamma  ("Moss Mask Contrast", Range(0.25,4)) = 1.3
        _MossRoughness  ("Moss Roughness Break", Range(0,1)) = 0.35
        _ShadeColor     ("Shade Tint", Color) = (0.28,0.30,0.31,1)
        _ShadeStrength  ("Shade Strength", Range(0,1)) = 0.70
        _RampSteps      ("Ramp Steps", Range(2,6)) = 3
        _RampSmooth     ("Ramp Softness", Range(0.002,0.35)) = 0.06
        _RimColor       ("Rim Color", Color) = (0.86,0.90,0.95,1)
        _RimPower       ("Rim Power", Range(0.5,10)) = 3.2
        _RimStrength    ("Rim Strength", Range(0,2)) = 0.35
        _SpecStrength   ("Specular Strength", Range(0,2)) = 0.12
        _Gloss          ("Gloss", Range(0.01,1)) = 0.18
        _AmbientStrength("Ambient Strength", Range(0,2)) = 1.0
        _ShadowAmbient  ("Ambient Kept In Shadow", Range(0,1)) = 0.40
        _ShadowEdge     ("Shadow Step Midpoint", Range(0.05,0.95)) = 0.5
        _ShadowSoft     ("Shadow Step Softness", Range(0.01,0.45)) = 0.18
        _DetailScale    ("Surface Detail Scale (per m)", Range(0.5,40)) = 7.0
        _DetailAmount   ("Surface Detail / Roughness Break", Range(0,1)) = 0.22
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Header(Weather Snow (global _MR_SnowCover))]
        _SnowAccept     ("Snow Accept (0 = never snowed on)", Range(0,1)) = 0.6
        _SnowBias       ("Snow Slope Bias", Range(-0.5,0.5)) = 0.1
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float4 _MossColor;
        float  _MossMaskGamma, _MossRoughness;
        float4 _ShadeColor;
        float  _ShadeStrength, _RampSteps, _RampSmooth;
        float4 _RimColor;
        float  _RimPower, _RimStrength;
        float  _SpecStrength, _Gloss;
        float  _AmbientStrength, _ShadowAmbient, _ShadowEdge, _ShadowSoft;
        float  _DetailScale, _DetailAmount;
        float  _Cull;
        float  _SnowAccept, _SnowBias;
    CBUFFER_END

    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideSnow.hlsl"

    float4 MR_Shade(MRVaryings i, bool frontFace, float sunShadow)
    {
        float3 tintBase  = MR_AuthoredCol(_Color.rgb);
        float3 tintMoss  = MR_AuthoredCol(_MossColor.rgb);
        float3 tintShade = MR_AuthoredCol(_ShadeColor.rgb);
        float3 tintRim   = MR_AuthoredCol(_RimColor.rgb);

        float3 n = normalize(i.normalWS);
        n = frontFace ? n : -n;

        // Hand-authored moss mask: COLOR_0.r baked per vertex in Blender (0 = bare rock,
        // 1 = full moss), heavier in crevices/undersides. Contrast-shaped so the painted
        // gradient reads as distinct moss patches rather than a flat wash.
        float mossMask = saturate(pow(saturate(i.color.r), _MossMaskGamma));

        // Fine triplanar breakup keeps both the bare rock and the moss patches from
        // reading as flat colour cards at headland viewing distance.
        float3 an = abs(n);
        an /= max(an.x + an.y + an.z, 1e-4);
        float detail = MR_TriNoise(i.positionWS, an, _DetailScale) * 2.0 - 1.0;

        float3 rockAlbedo = tintBase * (1.0 + detail * _DetailAmount * 0.30);
        float3 mossAlbedo = tintMoss * (1.0 + detail * _DetailAmount * _MossRoughness);
        float3 c = lerp(rockAlbedo, mossAlbedo, mossMask);

        // Weather snow (no-op unless a snowy region is active AND the material accepts it).
        float snowM = MR_SnowMask(i.positionWS, n, _SnowAccept, _SnowBias);
        if (snowM > 0.0) c = MR_SnowApply(c, snowM, i.positionWS);

        float3 l = MR_SunDirection();
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);

        float ndl = dot(n, l) * 0.5 + 0.5;
        float ramp = MR_CelRamp(ndl, _RampSteps, _RampSmooth);

        float shadeBase = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft,
                                     saturate(sunShadow));
        float shadowed = ramp * shadeBase;

        float3 lit = lerp(tintShade * _ShadeStrength, float3(1, 1, 1), shadowed);
        if (snowM > 0.0) lit = lerp(lit, MR_SnowLit(dot(n, l), shadeBase), snowM);

        float3 h = normalize(l + v);
        // Moss is matte; only the bare-rock fraction of the surface carries specular.
        float spec = pow(saturate(dot(n, h)), _Gloss * 128.0 + 1.0);
        spec = smoothstep(0.35, 0.45, spec) * _SpecStrength * shadowed * (1.0 - mossMask) * (1.0 - snowM);

        float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength * saturate(ndl + 0.25);

        float3 sun = MR_SunColor();
        float3 col = c * lit * sun;
        col += c * MR_Ambient(n) * _AmbientStrength * lerp(_ShadowAmbient, 1.0, shadeBase);
        col += float3(1, 1, 1) * spec * sun;
        col += tintRim * rim * lerp(_ShadowAmbient, 1.0, shadeBase) * sun;
        if (snowM > 0.0) col += MR_SnowSparkle(i.positionWS, n, v, l, shadeBase) * snowM * sun;

        return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex MR_Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

            #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPShadow.hlsl"

            float4 Frag(MRVaryings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float shadow = MR_SunShadow(i.positionRWS, frontFace ? n : -n, i.positionCS.xy);
                return MR_Shade(i, frontFace, shadow);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex MR_Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            void FragDepth(MRVaryings i) { }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex MR_Vert
            #pragma fragment FragShadow
            #pragma multi_compile_instancing

            void FragShadow(MRVaryings i) { }
            ENDHLSL
        }
    }

    Fallback Off
}
