Shader "MapleRide/MinatoOcean"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.10,0.37,0.45,1)
        _MidColor ("Mid Water Color", Color) = (0.035,0.19,0.31,1)
        _DeepColor ("Deep Color", Color) = (0.012,0.06,0.15,1)
        _SkyTint ("Sky Reflection Tint", Color) = (0.28,0.42,0.56,1)
        _SunColor ("Sun Glitter Color", Color) = (1,0.86,0.64,1)
        _FoamColor ("Foam Color", Color) = (0.78,0.88,0.88,1)

        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 4.5
        _FresnelBoost ("Fresnel Strength", Range(0,2)) = 0.58
        _GlitterPower ("Glitter Tightness", Range(4,512)) = 120
        _GlitterBoost ("Glitter Strength", Range(0,4)) = 1.3
        _SunPathWidth ("Sun Path Width", Range(0.02,0.8)) = 0.20
        _SunPathStrength ("Sun Path Strength", Range(0,3)) = 0.8

        _WaveScale ("Swell Scale", Range(0.01,1)) = 0.075
        _WaveSpeed ("Wave Speed", Range(0,3)) = 0.28
        _WaveStrength ("Swell Strength", Range(0,1)) = 0.32
        _ChopScale ("Chop Frequency", Range(2,24)) = 8.0
        _ChopStrength ("Chop Strength", Range(0,1)) = 0.17
        _DetailFadeStart ("Ripple Fade Start", Float) = 180
        _DetailFadeEnd ("Ripple Fade End", Float) = 2600

        _ShoreFadeStart ("Near Water Reach", Float) = 70
        _ShoreFadeEnd ("Deep Water Reach", Float) = 3200
        _MacroScale ("Macro Color Scale", Float) = 0.00055
        _MacroStrength ("Macro Color Strength", Range(0,1)) = 0.28
        _NormalDetailScale ("Normal Detail Scale", Float) = 0.075
        _NormalDetailStrength ("Normal Detail Strength", Range(0,1)) = 0.32
        _DepthNoiseScale ("Depth Tint Breakup Scale", Float) = 0.0018
        _DepthNoiseStrength ("Depth Tint Breakup", Range(0,0.25)) = 0.065
        _FoamAmount ("Whitecap Amount", Range(0,1)) = 0.08
        _AmbientWeight ("Ambient Weight", Range(0,2)) = 0.72
        _LightGain ("Light Gain", Range(0.1,4)) = 1.0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _ShallowColor, _MidColor, _DeepColor, _SkyTint, _SunColor, _FoamColor;
        float _FresnelPower, _FresnelBoost, _GlitterPower, _GlitterBoost;
        float _SunPathWidth, _SunPathStrength;
        float _WaveScale, _WaveSpeed, _WaveStrength, _ChopScale, _ChopStrength;
        float _DetailFadeStart, _DetailFadeEnd;
        float _ShoreFadeStart, _ShoreFadeEnd, _MacroScale, _MacroStrength;
        float _NormalDetailScale, _NormalDetailStrength;
        float _DepthNoiseScale, _DepthNoiseStrength;
        float _FoamAmount, _AmbientWeight, _LightGain;
    CBUFFER_END

    float MR_MinatoHash(float2 p)
    {
        p = frac(p * float2(0.1031, 0.1030));
        p += dot(p, p.yx + 33.33);
        return frac((p.x + p.y) * p.x);
    }

    float MR_MinatoNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(MR_MinatoHash(i), MR_MinatoHash(i + float2(1,0)), f.x),
                    lerp(MR_MinatoHash(i + float2(0,1)), MR_MinatoHash(i + 1.0), f.x), f.y);
    }

    // A finite-difference gradient gives the water a second, non-periodic normal layer.
    // Keeping this in world space prevents the regular plate topology from becoming visible
    // as scanline bands at a grazing camera angle.
    float2 MR_MinatoNoiseGradient(float2 p, float stepM)
    {
        float2 e = float2(max(0.02, stepM), 0.0);
        float x0 = MR_MinatoNoise(p - e.xy);
        float x1 = MR_MinatoNoise(p + e.xy);
        float y0 = MR_MinatoNoise(p - e.yx);
        float y1 = MR_MinatoNoise(p + e.yx);
        return float2(x1 - x0, y1 - y0) / (2.0 * e.xx);
    }

    float2 MR_MinatoWaveGradient(float2 p, float detail)
    {
        float t = _TimeParameters.x * _WaveSpeed;
        const float freq[5] = { 0.63, 1.00, 1.47, 2.31, 3.73 };
        const float ang[5] = { 0.21, 1.37, 2.74, 4.18, 5.31 };
        const float amp[5] = { 1.00, 0.68, 0.43, 0.22, 0.12 };
        float2 grad = 0;
        [unroll] for (int i = 0; i < 5; i++)
        {
            float2 d = float2(cos(ang[i]), sin(ang[i]));
            float k = _WaveScale * freq[i];
            float phase = dot(p, d) * k + t * (0.55 + 0.29 * i);
            grad += d * cos(phase) * k * amp[i];
        }

        float chopFade = detail * detail;
        chopFade *= chopFade;
        float2 c1 = normalize(float2(-0.81, 0.59));
        float2 c2 = normalize(float2(0.36, 0.93));
        float ck = _WaveScale * _ChopScale;
        grad += c1 * cos(dot(p, c1) * ck + t * 1.9) * ck * _ChopStrength * chopFade;
        grad += c2 * cos(dot(p, c2) * ck * 1.71 - t * 2.4) * ck * _ChopStrength * 0.55 * chopFade;
        return grad;
    }

    MRVaryings VertMinatoOcean(MRAttributes input)
    {
        UNITY_SETUP_INSTANCE_ID(input);
        float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
        float3 wp = GetAbsolutePositionWS(positionRWS);
        float t = _TimeParameters.x * _WaveSpeed;
        float swell = sin(dot(wp.xz, normalize(float2(0.91,0.42))) * _WaveScale * 0.36 + t) * 0.55
                    + sin(dot(wp.xz, normalize(float2(-0.34,0.94))) * _WaveScale * 0.51 - t * 1.23) * 0.31;
        positionRWS.y += swell * 0.08;

        MRVaryings output;
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        output.positionRWS = positionRWS;
        output.positionCS = TransformWorldToHClip(positionRWS);
        output.positionWS = GetAbsolutePositionWS(positionRWS);
        output.normalWS = float3(0,1,0);
        output.uv = input.uv;
        output.color = input.color;
        return output;
    }

    float4 ShadeMinatoOcean(MRVaryings input)
    {
        float3 cameraWS = GetAbsolutePositionWS(GetPrimaryCameraPosition());
        float distanceM = distance(input.positionWS, cameraWS);
        float detail = saturate(1.0 - (distanceM - _DetailFadeStart) /
                                max(1.0, _DetailFadeEnd - _DetailFadeStart));

        float2 gradient = MR_MinatoWaveGradient(input.positionWS.xz, detail);
        float2 detailA = MR_MinatoNoiseGradient(input.positionWS.xz * _NormalDetailScale,
                                                0.18) * 0.82;
        float2 detailB = MR_MinatoNoiseGradient(input.positionWS.xz * (_NormalDetailScale * 2.37) + 19.4,
                                                0.11) * 0.36;
        gradient += (detailA + detailB) * _NormalDetailStrength * detail;
        float3 normalWS = normalize(float3(-gradient.x * _WaveStrength, 1.0,
                                          -gradient.y * _WaveStrength));
        normalWS = normalize(lerp(float3(0,1,0), normalWS, detail));
        float3 viewWS = normalize(cameraWS - input.positionWS);
        float3 lightWS = MR_SunDirection();

        float depthT = saturate((distanceM - _ShoreFadeStart) /
                                max(1.0, _ShoreFadeEnd - _ShoreFadeStart));
        // Break up the broad distance tint very slightly so a shallow shelf does not resolve
        // into a single cyan strip when the camera is close to the waterline.
        float depthNoise = MR_MinatoNoise(input.positionWS.xz * _DepthNoiseScale + 47.2);
        depthT = saturate(depthT + (depthNoise - 0.5) * _DepthNoiseStrength * detail);
        depthT = depthT * depthT * (3.0 - 2.0 * depthT);
        float3 nearMid = lerp(MR_AuthoredCol(_ShallowColor.rgb),
                              MR_AuthoredCol(_MidColor.rgb), saturate(depthT * 1.65));
        float3 body = lerp(nearMid, MR_AuthoredCol(_DeepColor.rgb),
                           saturate(depthT * 1.55 - 0.55));

        float macroA = MR_MinatoNoise(input.positionWS.xz * _MacroScale);
        float macroB = MR_MinatoNoise(input.positionWS.xz * (_MacroScale * 2.17) + 31.7);
        float macro = (macroA * 0.68 + macroB * 0.32 - 0.5) * _MacroStrength;
        body *= 1.0 + macro;
        body = lerp(body, body * float3(0.72,0.92,1.08), saturate(macroB - 0.55) * 0.35);

        float t = _TimeParameters.x * _WaveSpeed;
        float swellA = sin(dot(input.positionWS.xz, normalize(float2(0.91,0.42))) * 0.027 + t);
        float swellB = sin(dot(input.positionWS.xz, normalize(float2(-0.34,0.94))) * 0.043 - t * 1.21);
        float swellC = sin(dot(input.positionWS.xz, normalize(float2(0.58,-0.82))) * 0.071 + t * 0.67);
        float swellValue = (swellA * 0.52 + swellB * 0.31 + swellC * 0.17);
        float swellBreak = 0.62 + MR_MinatoNoise(input.positionWS.xz * 0.0031 + 17.0) * 0.38;
        float resolvedSwell = swellValue * swellBreak * saturate(0.08 + detail * 0.92);
        body *= 1.0 + resolvedSwell * 0.055;
        body += MR_AuthoredCol(_SkyTint.rgb) * saturate(resolvedSwell) * 0.022;

        float facing = saturate(dot(normalWS, viewWS));
        float fresnel = pow(1.0 - facing, _FresnelPower) * _FresnelBoost;
        float3 color = lerp(body, MR_AuthoredCol(_SkyTint.rgb), saturate(fresnel));

        float3 halfVector = normalize(lightWS + viewWS);
        float pinGlitter = pow(saturate(dot(normalWS, halfVector)), _GlitterPower) * _GlitterBoost;
        float3 reflection = reflect(-lightWS, normalWS);
        float pathAlign = saturate(dot(reflection, viewWS));
        float broadPath = smoothstep(1.0 - _SunPathWidth, 1.0, pathAlign) * _SunPathStrength;
        broadPath *= saturate(0.18 + detail * 0.82);

        float steepness = saturate(length(gradient) * 1.7 - 0.24);
        float whitecapNoise = MR_MinatoNoise(input.positionWS.xz * 0.065 + _TimeParameters.x * 0.07);
        float authoredCrest = smoothstep(0.70, 0.94, swellValue * 0.5 + 0.5);
        float whitecap = max(smoothstep(0.74, 0.94, steepness * (0.68 + whitecapNoise * 0.48)),
                             authoredCrest * smoothstep(0.72, 0.96, whitecapNoise) * 0.36);
        whitecap *= _FoamAmount * detail;
        color = lerp(color, MR_AuthoredCol(_FoamColor.rgb), whitecap);

        // Sub-pixel breakup removes coherent horizontal quantisation in the grazing water
        // while remaining below the authored wave/foam contrast.
        float microBreakup = MR_MinatoNoise(input.positionWS.xz * 0.19 + 83.0) - 0.5;
        color += microBreakup * 0.010 * detail;

        float ndl = saturate(dot(normalWS, lightWS) * 0.5 + 0.5);
        float3 sun = MR_SunColor();
        color *= (lerp(0.58, 1.0, ndl) * sun +
                  MR_Ambient(float3(0,1,0)) * _AmbientWeight) * _LightGain;
        color += MR_AuthoredCol(_SunColor.rgb) * (pinGlitter + broadPath * 0.32) * sun;

        return float4(MR_ToRender(MR_ApplyFog(color, input.positionRWS)) *
                      GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode"="ForwardOnly" }
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex VertMinatoOcean
            #pragma fragment Frag
            #pragma multi_compile_instancing
            float4 Frag(MRVaryings input) : SV_Target { return ShadeMinatoOcean(input); }
            ENDHLSL
        }
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode"="DepthForwardOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex VertMinatoOcean
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            void FragDepth(MRVaryings input) { }
            ENDHLSL
        }
    }
    Fallback Off
}
