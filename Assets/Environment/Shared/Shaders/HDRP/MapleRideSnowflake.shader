// Snowflake particle shader (WP-E weather snow, 2026-09-26).
//
// HDRP has no built-in particle shader for the legacy Shuriken system (HDRP/Unlit ignores vertex
// colour, so per-particle alpha fades and tints are lost). This one is a tiny transparent
// ForwardOnly pass that:
//  * draws a procedural soft flake (no texture needed): a bright core with a feathered edge, and
//    a softer, larger, dimmer disc for flakes close to the lens (depth-of-field bokeh) so the
//    near layer reads as out of focus the way it does in photographs of falling snow,
//  * lights the flake from the sun + sky ambient so snow in the shade of a cliff is blue-grey
//    and snow in sunlight glows, instead of every flake being the same flat white,
//  * applies the region fog so distant flakes dissolve into the air,
//  * respects particle vertex colour (alpha fade over lifetime).
Shader "MapleRide/HDRP/Snowflake"
{
    Properties
    {
        _Color     ("Tint", Color) = (1,1,1,0.9)
        _Softness  ("Edge Softness", Range(0.05,1)) = 0.55
        _NearBlurM ("Out-of-focus distance (m)", Float) = 2.2
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float  _Softness, _NearBlurM;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex MR_Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            float4 Frag(MRVaryings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float dist = length(i.positionRWS);
                float blur = 1.0 - saturate(dist / max(_NearBlurM, 0.1));      // 1 at the lens
                float soft = lerp(_Softness, 1.0, blur);
                float disc = 1.0 - smoothstep(1.0 - soft, 1.0, r);
                float core = 1.0 - smoothstep(0.0, 0.45, r);
                float a = saturate(disc * 0.75 + core * 0.35) * lerp(1.0, 0.35, blur);

                float3 sun = MR_SunColor();
                float3 amb = MR_Ambient(float3(0, 1, 0));
                // Flakes are translucent ice: mostly lit by the sky dome, with a forward-scatter
                // glow when the camera looks toward the sun.
                float3 v = normalize(-i.positionRWS);
                float fwd = pow(saturate(dot(-v, MR_SunDirection())), 6.0);
                float3 lit = amb * 1.15 + sun * (0.42 + fwd * 0.9);
                float3 col = MR_AuthoredCol(_Color.rgb) * lit;

                float4 vc = i.color;
                a *= _Color.a * vc.a;
                col *= vc.rgb;
                clip(a - 0.004);
                return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
