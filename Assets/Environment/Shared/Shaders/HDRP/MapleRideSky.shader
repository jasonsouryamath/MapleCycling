// HDRP replacement for MapleRide/SakuraSky (spec section 4.4).
//
// Painted anime sunset backdrop: zenith -> horizon -> ground gradient with a sun disc,
// horizon bloom and soft procedural cloud banding. Applied to a sky DOME mesh, not as a
// Unity skybox material, so it stays an ordinary geometry shader here.
//
// Property names, defaults and gradient maths are deliberately IDENTICAL to the Built-in
// original, so converting a material is a shader swap that preserves every authored
// value. The Built-in shader is intentionally left on disk and untouched (spec 4.1).
//
// Note on _SunDirection: this stays an AUTHORED property and is NOT replaced with
// MR_SunDirection(). The painted disc and glow were art-directed against this vector and
// the scene's actual directional light does not necessarily agree with it; swapping it
// would move the sun in the painting and break the look the port is meant to preserve.
Shader "MapleRide/HDRP/Sky"
{
    Properties
    {
        _ZenithColor   ("Zenith Color", Color) = (0.14,0.17,0.38,1)
        _MidColor      ("Mid Sky Color", Color) = (0.42,0.38,0.58,1)
        _HorizonColor  ("Horizon Color", Color) = (0.98,0.66,0.52,1)
        _GroundColor   ("Ground Color", Color) = (0.16,0.18,0.26,1)
        _HorizonSharp  ("Horizon Falloff", Range(0.5,12)) = 3.2
        _MidPoint      ("Mid Sky Blend Height", Range(0.02,1)) = 0.34

        _SunColor      ("Sun Color", Color) = (1,0.82,0.60,1)
        _SunDirection  ("Sun Direction (xyz)", Vector) = (0.38,0.30,0.87,0)
        _SunSize       ("Sun Size", Range(0.001,0.3)) = 0.045
        _SunSoftness   ("Sun Edge Softness", Range(0.0001,0.2)) = 0.012
        _SunGlow       ("Sun Glow Strength", Range(0,4)) = 1.4
        _SunGlowPower  ("Sun Glow Falloff", Range(1,256)) = 18

        _CloudColor    ("Cloud Color", Color) = (1,0.74,0.68,1)
        _CloudStrength ("Cloud Strength", Range(0,1)) = 0.35
        _CloudScale    ("Cloud Scale", Range(0.5,12)) = 3.5
        _CloudHeight   ("Cloud Band Height", Range(0,1)) = 0.18
        _CloudSpread   ("Cloud Band Spread", Range(0.01,1)) = 0.30

        _Exposure      ("Exposure", Range(0,4)) = 1.0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    // This shader samples no textures; the whole sky is procedural.

    CBUFFER_START(UnityPerMaterial)
        float4 _ZenithColor, _MidColor, _HorizonColor, _GroundColor;
        float  _HorizonSharp, _MidPoint;
        float4 _SunColor;
        float4 _SunDirection;
        float  _SunSize, _SunSoftness, _SunGlow, _SunGlowPower;
        float4 _CloudColor;
        float  _CloudStrength, _CloudScale, _CloudHeight, _CloudSpread;
        float  _Exposure;
    CBUFFER_END

    // The Built-in version sampled the gradient on the raw OBJECT-space vertex position,
    // which for a dome centred on its own pivot is the view direction. That is kept here
    // rather than recomputing a camera-to-fragment vector, so a dome authored at any
    // scale or rotation produces the byte-identical gradient it did before. The direction
    // rides in the otherwise-unused .color interpolator of MRVaryings.
    MRVaryings MR_VertSky(MRAttributes input)
    {
        MRVaryings o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
        o.positionCS = TransformWorldToHClip(positionRWS);
        o.positionWS = GetAbsolutePositionWS(positionRWS);
        // The sky deliberately takes no fog, but the member is still part of the shared
        // MRVaryings contract and must never be left uninitialised.
        o.positionRWS = positionRWS;
        o.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
        o.uv = input.uv;
        o.color = float4(input.positionOS.xyz, 1.0);
        return o;
    }

    // Clouds are sampled on the 3D view direction rather than an (atan2, height) chart.
    // The chart had a wrap seam at +-pi and, because the band was several times tighter
    // vertically than horizontally, its noise degenerated into constant-altitude stripes
    // that read as concentric rings across the whole sky.
    //
    // These are 3D; MapleRideHDRPCommon's MR_Hash21/MR_ValueNoise are 2D, so the original
    // 3D pair is carried over verbatim under an MRSky_ prefix rather than approximated.
    float MRSky_Hash(float3 p)
    {
        return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
    }

    float MRSky_ValueNoise(float3 p)
    {
        float3 i = floor(p);
        float3 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        float a = lerp(lerp(MRSky_Hash(i + float3(0, 0, 0)), MRSky_Hash(i + float3(1, 0, 0)), f.x),
                       lerp(MRSky_Hash(i + float3(0, 1, 0)), MRSky_Hash(i + float3(1, 1, 0)), f.x), f.y);
        float b = lerp(lerp(MRSky_Hash(i + float3(0, 0, 1)), MRSky_Hash(i + float3(1, 0, 1)), f.x),
                       lerp(MRSky_Hash(i + float3(0, 1, 1)), MRSky_Hash(i + float3(1, 1, 1)), f.x), f.y);
        return lerp(a, b, f.z);
    }

    float MRSky_Fbm(float3 p)
    {
        float sum = 0.0, amp = 0.5;
        for (int i = 0; i < 4; i++) { sum += MRSky_ValueNoise(p) * amp; p *= 2.07; amp *= 0.5; }
        return sum;
    }

    float4 MRSky_Shade(MRVaryings i)
    {
        float3 d = normalize(i.color.xyz);
        float h = d.y;

        // --- vertical gradient -------------------------------------------------
        float up = saturate(pow(saturate(h), 1.0 / _HorizonSharp));
        float3 sky = lerp(MR_AuthoredCol(_HorizonColor.rgb), MR_AuthoredCol(_MidColor.rgb), saturate(up / _MidPoint));
        sky = lerp(sky, MR_AuthoredCol(_ZenithColor.rgb), saturate((up - _MidPoint) / max(0.001, 1.0 - _MidPoint)));

        // Below the horizon fades to a haze-matched ground tone so distant terrain seats cleanly.
        float3 below = lerp(MR_AuthoredCol(_HorizonColor.rgb), MR_AuthoredCol(_GroundColor.rgb), saturate(-h * _HorizonSharp));
        float3 col = lerp(below, sky, saturate(h * 40.0 + 0.5));

        // --- sun disc + atmospheric glow --------------------------------------
        float3 sunDir = normalize(_SunDirection.xyz);
        float sd = dot(d, sunDir);
        float glow = pow(saturate(sd), _SunGlowPower) * _SunGlow;
        col += MR_AuthoredCol(_SunColor.rgb) * glow;

        float ang = acos(clamp(sd, -1.0, 1.0));
        float disc = 1.0 - smoothstep(_SunSize, _SunSize + _SunSoftness, ang);
        col = lerp(col, MR_AuthoredCol(_SunColor.rgb) * 1.6, disc);

        // --- drifting cloud band ----------------------------------------------
        float band = exp(-pow((h - _CloudHeight) / _CloudSpread, 2.0));
        float3 cp = d * _CloudScale;
        cp.y *= 2.6;                  // flatten the puffs without stratifying them
        // _Time.y is Built-in only; _TimeParameters.x is HDRP's equivalent seconds value.
        cp.x += _TimeParameters.x * 0.012;
        float clouds = saturate(MRSky_Fbm(cp) * 1.7 - 0.52);
        col = lerp(col, MR_AuthoredCol(_CloudColor.rgb), clouds * band * _CloudStrength);

        // _Exposure is the authored painterly gain and is kept exactly as before;
        // GetCurrentExposureMultiplier() is then applied on top because HDRP renders into
        // a physically-exposed buffer and an unlit pass must exposure-match it itself.
        return float4(MR_ToRender(col) * _Exposure * GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        // Cull Off and ZWrite Off are preserved from the source: the dome is viewed from
        // the inside and must never occlude the scene.
        //
        // DEVIATION: the source queue was "Background" (1000). HDRP's opaque draw range
        // starts at 1900, so anything tagged Background is never submitted and the dome
        // would simply not render. "Geometry-1" (1999) is the closest queue that HDRP
        // actually draws while still sorting ahead of all ordinary geometry, which -
        // combined with ZWrite Off - gives the same "behind everything" result.
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Background"
            "Queue" = "Geometry-1"
            "PreviewType" = "Skybox"
            "IgnoreProjector" = "True"
        }

        // DEVIATION: only ForwardOnly. The source is a single-pass, ZWrite-Off, shadow-free
        // backdrop; a DepthForwardOnly pass with ZWrite Off would write nothing, and a
        // ShadowCaster would have the sky dome cast a shadow over the entire valley.
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex MR_VertSky
            #pragma fragment Frag
            #pragma multi_compile_instancing

            float4 Frag(MRVaryings i) : SV_Target
            {
                return MRSky_Shade(i);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
