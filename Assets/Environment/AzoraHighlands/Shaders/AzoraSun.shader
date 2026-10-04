// Azora sun disc + aureole (copilot, 2026-09-26, C1 "better sun").
//
// HDRP's GradientSky draws no sun, and the physically based sky (which does) rendered a night sky
// at this project's ~1 lux key light. So the sun is drawn here as one camera-facing quad that
// PLACES ITSELF: the vertex shader puts it on MR_SunDirection() just inside the camera's far clip,
// so it always sits exactly where the key light comes from and needs no follow script.
//  * Additive, ZWrite Off: it never occludes anything, and because it writes no depth the
//    SakuraPostFX aerial stage still sees "sky" behind it and leaves it unhazed.
//  * ZTest LEqual at ~0.985 x far: terrain, ranges and peaks in front of it hide it, so the sun
//    can drop behind a ridge.
//  * HDR output (not saturated like MR_ToRender): the disc core goes well above the bloom
//    threshold, so HDRP bloom gives it real photographic glare; the aureole is painted.
// The mesh must carry huge bounds (staged by AzoraHighlands.Sky.cs) so it is never culled.
Shader "MapleRide/HDRP/AzoraSun"
{
    Properties
    {
        _Color         ("Sun Color", Color) = (1.0,0.97,0.90,1)
        _HalfAngle     ("Quad Half Angle (deg)", Range(2,40)) = 18
        _DiscDeg       ("Disc Radius (deg)", Range(0.1,2)) = 0.36
        _DiscIntensity ("Disc Intensity (linear, HDR)", Range(0,40)) = 12
        _GlowNear      ("Aureole Near Strength", Range(0,4)) = 0.55
        _GlowNearDeg   ("Aureole Near Width (deg)", Range(0.1,10)) = 1.1
        _GlowFar       ("Aureole Far Strength", Range(0,2)) = 0.16
        _GlowFarDeg    ("Aureole Far Width (deg)", Range(0.5,30)) = 6.5
        _FarFraction   ("Distance (x far clip)", Range(0.5,0.999)) = 0.985
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float  _HalfAngle, _DiscDeg, _DiscIntensity;
        float  _GlowNear, _GlowNearDeg, _GlowFar, _GlowFarDeg, _FarFraction;
    CBUFFER_END

    MRVaryings VertSun(MRAttributes input)
    {
        MRVaryings o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        float3 sun = MR_SunDirection();
        float dist = _ProjectionParams.z * _FarFraction;
        float halfSize = dist * tan(radians(_HalfAngle));
        // Camera-relative: the eye is the origin, so the centre is simply sunDir * distance.
        float3 right = normalize(UNITY_MATRIX_V[0].xyz);
        float3 up = normalize(UNITY_MATRIX_V[1].xyz);
        float2 q = input.positionOS.xy;                       // quad corners at +-1
        float3 positionRWS = sun * dist + (right * q.x + up * q.y) * halfSize;
        o.positionCS = TransformWorldToHClip(positionRWS);
        o.positionWS = GetAbsolutePositionWS(positionRWS);
        o.positionRWS = positionRWS;
        o.normalWS = -sun;
        o.uv = q;
        // Fade the whole sprite out as the sun reaches the horizon (a sun below the horizon
        // would otherwise shine up through the ground's far edge).
        o.color = float4(1, 1, 1, smoothstep(-0.03, 0.04, sun.y));
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma vertex VertSun
            #pragma fragment Frag
            #pragma multi_compile_instancing

            float4 Frag(MRVaryings i) : SV_Target
            {
                float ang = length(i.uv) * _HalfAngle;          // degrees from the sun centre
                // Disc with a slightly darkened limb, then two exponential aureole lobes (the
                // narrow bright Mie forward peak, and a wide soft brightening of the sky).
                float disc = 1.0 - smoothstep(_DiscDeg * 0.82, _DiscDeg, ang);
                float limb = lerp(0.78, 1.0, saturate(1.0 - ang / max(_DiscDeg, 1e-3)));
                float glow = _GlowNear * exp(-ang / max(_GlowNearDeg, 1e-3))
                           + _GlowFar * exp(-ang / max(_GlowFarDeg, 1e-3));
                // Reach exactly zero at the quad edge so no square outline can ever show.
                glow *= 1.0 - smoothstep(_HalfAngle * 0.62, _HalfAngle * 0.98, ang);
                float3 c = SRGBToLinear(saturate(MR_AuthoredCol(_Color.rgb)));
                float3 col = c * (disc * limb * _DiscIntensity + glow) * i.color.a;
                return float4(col * GetCurrentExposureMultiplier(), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
