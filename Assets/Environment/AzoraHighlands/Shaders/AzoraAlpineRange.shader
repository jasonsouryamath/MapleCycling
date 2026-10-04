// Azora WP-H1 (copilot, 2026-09-26): snow-and-rock Alpine massifs with their own aerial perspective.
//
// Used by two kinds of geometry built in Assets/Editor/AzoraHighlands.Ranges.cs:
//
//  * STATIC massifs (foothills 1.5-4 km and the main range 4-9 km). Ordinary world geometry, lit
//    by the key light with a soft wrap, snow/rock baked per vertex (COLOR.r = snow, .g = rock tone,
//    .b = cavity), then hazed toward the horizon by true camera distance plus valley fill.
//
//  * The FAR CHAIN (_Compress = 1). A ring of ranges authored at its NOMINAL 10-22 km distance but
//    kept centred on the camera by AzoraRangeFollow. Nothing beyond the ride camera's 9 km far clip
//    can render, so the vertex shader slides every vertex ALONG ITS OWN CAMERA RAY into a thin
//    band just inside the far clip (_GeoNear.._GeoFar x far clip). Moving a point along the ray
//    through the eye leaves its screen position unchanged, so the chain keeps its true angular
//    size and silhouette, while the haze below still uses the nominal distance. The depth band
//    also puts the chain behind every static layer, so nearer ranges always occlude it.
//
// No ShadowCaster pass on purpose (kilometre-scale meshes would drag the cascades out).
Shader "MapleRide/HDRP/AzoraAlpineRange"
{
    Properties
    {
        _RockLit      ("Rock Lit", Color) = (0.46,0.47,0.50,1)
        _RockShade    ("Rock Shade", Color) = (0.19,0.23,0.31,1)
        _SnowLit      ("Snow Lit", Color) = (0.95,0.96,0.98,1)
        _SnowShade    ("Snow Shade", Color) = (0.60,0.69,0.84,1)
        _Wrap         ("Light Wrap", Range(0,1)) = 0.30
        _HazeColor    ("Haze / Horizon Color", Color) = (0.76,0.84,0.92,1)

        [Header(Distance haze)]
        _HazeStart    ("Haze Start (m)", Float) = 800
        _HazeFull     ("Haze Full (m)", Float) = 12000
        _HazeMin      ("Haze At Start", Range(0,1)) = 0.0
        _HazeMax      ("Haze At Full", Range(0,1)) = 0.65

        [Header(Valley fill)]
        _BaseY        ("Valley Base World Y", Float) = 700
        _ValleyHeight ("Valley Fill Height (m)", Float) = 600
        _ValleyFill   ("Valley Fill Amount", Range(0,1)) = 0.35

        [Header(Tone)]
        _MacroNoise   ("Macro Tonal Noise", Range(0,1)) = 0.10
        _MacroScale   ("Macro Noise Size (m)", Float) = 1400
        _SceneFog     ("Apply Scene Fog (0/1)", Float) = 0

        [Header(Far chain depth compression)]
        _Compress     ("Compress Along View Ray (0/1)", Float) = 0
        _NominalNear  ("Nominal Near (m)", Float) = 10000
        _NominalFar   ("Nominal Far (m)", Float) = 22000
        _GeoNear      ("Drawn Near (x far clip)", Float) = 0.80
        _GeoFar       ("Drawn Far (x far clip)", Float) = 0.96

        [Header(Static layers soft far knee)]
        _SoftFar      ("Soft Far Knee (0/1)", Float) = 0
        _KneeStart    ("Knee Start (x far clip)", Float) = 0.60
        _KneeEnd      ("Knee Asymptote (x far clip)", Float) = 0.80
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _RockLit, _RockShade, _SnowLit, _SnowShade, _HazeColor;
        float  _Wrap;
        float  _HazeStart, _HazeFull, _HazeMin, _HazeMax;
        float  _BaseY, _ValleyHeight, _ValleyFill;
        float  _MacroNoise, _MacroScale, _SceneFog;
        float  _Compress, _NominalNear, _NominalFar, _GeoNear, _GeoFar;
        float  _SoftFar, _KneeStart, _KneeEnd;
    CBUFFER_END

    // Far clip of the camera about to render; pushed per camera by AzoraRangeFollow.
    float _AzoraRangeFar;

    MRVaryings AlpineVert(MRAttributes input)
    {
        MRVaryings o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        float3 rws = TransformObjectToWorld(input.positionOS.xyz);
        // Shading (haze, valley fill) always uses the NOMINAL position.
        o.positionRWS = rws;
        o.positionWS = GetAbsolutePositionWS(rws);
        float3 drawn = rws;
        float farClip = _AzoraRangeFar > 1.0 ? _AzoraRangeFar : 9000.0;
        float d = max(length(rws), 1.0);
        if (_Compress > 0.5)
        {
            float t = saturate((d - _NominalNear) / max(_NominalFar - _NominalNear, 1.0));
            float g = lerp(_GeoNear, _GeoFar, t) * farClip;
            drawn = rws * (g / d);
        }
        else if (_SoftFar > 0.5)
        {
            // Unchanged up to the knee, then eased toward an asymptote inside the far clip (and in
            // front of the far chain's band). Monotonic, so static layers keep their depth order.
            float k0 = _KneeStart * farClip, k1 = max(_KneeEnd * farClip, k0 + 1.0);
            if (d > k0)
            {
                float g = k0 + (k1 - k0) * (1.0 - exp(-(d - k0) / (k1 - k0)));
                drawn = rws * (g / d);
            }
        }
        o.positionCS = TransformWorldToHClip(drawn);
        o.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
        o.uv = input.uv;
        o.color = input.color;
        return o;
    }

    float3 AlpineShade(MRVaryings i)
    {
        float3 v = normalize(-i.positionRWS);
        float3 n = i.normalWS;
        float nl = length(n);
        n = nl > 1e-5 ? n / nl : float3(0.0, 1.0, 0.0);
        if (dot(n, v) < 0.0) n = -n;

        float ndl = dot(n, MR_SunDirection());
        float lit = saturate((ndl + _Wrap) / (1.0 + _Wrap));
        lit = lit * lit * (3.0 - 2.0 * lit);

        float snow = saturate(i.color.r);
        float tone = saturate(i.color.g);
        float cav  = saturate(i.color.b);

        float3 rock  = lerp(MR_AuthoredCol(_RockShade.rgb), MR_AuthoredCol(_RockLit.rgb), lit) * (0.80 + 0.40 * tone);
        float3 snowC = lerp(MR_AuthoredCol(_SnowShade.rgb), MR_AuthoredCol(_SnowLit.rgb), lit);
        float3 base  = lerp(rock, snowC, snow) * lerp(0.80, 1.0, cav);

        float macro = saturate(MR_ValueNoise(i.positionWS.xz, 1.0 / max(_MacroScale, 1.0)));
        base *= 1.0 + (macro - 0.5) * 2.0 * saturate(_MacroNoise);

        // --- aerial perspective (nominal distance) -----------------------------------------
        float dist = length(i.positionRWS);
        float t = saturate((dist - _HazeStart) / max(_HazeFull - _HazeStart, 1.0));
        float haze = lerp(saturate(_HazeMin), saturate(_HazeMax), t * t * (3.0 - 2.0 * t));
        float above = max(0.0, i.positionWS.y - _BaseY);
        float valley = (1.0 - saturate(above / max(_ValleyHeight, 1.0))) * saturate(_ValleyFill);
        haze = saturate(haze + (1.0 - haze) * valley);

        float3 col = lerp(max(base, 0.0), MR_AuthoredCol(_HazeColor.rgb), haze);
        if (_SceneFog > 0.5) col = MR_ApplyFog(col, i.positionRWS);
        return col;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex AlpineVert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            float4 Frag(MRVaryings i) : SV_Target
            {
                return float4(MR_ToRender(AlpineShade(i)) * GetCurrentExposureMultiplier(), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex AlpineVert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            void FragDepth(MRVaryings i) { }
            ENDHLSL
        }
    }

    Fallback Off
}
