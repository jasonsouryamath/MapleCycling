// NB1 - swash overlay for the Nagisa Bay beaches: the foamy tongue that runs up the sand and pulls back, plus the
// dark wet-sand band that dries. A transparent sheet hugging the terrain (built by NagisaBayEnvironment.Surf.cs),
// driven by the SAME wave-set schedule as MapleRide/HDRP/NagisaOcean so each bore that collapses at the shoreline
// is followed by its run-up here.
Shader "MapleRide/HDRP/NagisaOceanSwash"
{
    Properties
    {
        [NoScaleOffset] _NagisaField ("Depth/coast field (RGBAHalf)", 2D) = "black" {}
        _FieldRect ("Field rect x0,z0,cell", Vector) = (0,0,6,0)
        _FieldDim ("Field dim nx,nz", Vector) = (1,1,0,0)
        [NoScaleOffset] _NormA ("Detail normal A (unused)", 2D) = "bump" {}
        [NoScaleOffset] _NormB ("Detail normal B (unused)", 2D) = "bump" {}
        [NoScaleOffset] _FoamTex ("Lace foam", 2D) = "white" {}

        _ShallowTint ("Shallow Scatter (unused)", Color) = (0.08,0.62,0.66,1)
        _DeepColor ("Deep Scatter (unused)", Color) = (0.015,0.09,0.24,1)
        _SandColor ("Seabed Sand (unused)", Color) = (0.86,0.78,0.60,1)
        _ReefColor ("Reef (unused)", Color) = (0.10,0.15,0.14,1)
        _SkyZenith ("Sky Reflection Zenith", Color) = (0.38,0.60,0.88,1)
        _SkyHorizon ("Sky Reflection Horizon", Color) = (0.80,0.88,0.94,1)
        _SunColor ("Sun Color", Color) = (1,0.95,0.82,1)
        _FoamColor ("Foam Color", Color) = (0.97,0.99,0.99,1)
        _SssColor ("(unused)", Color) = (0.25,0.85,0.62,1)
        _WetSandColor ("Wet Sand", Color) = (0.30,0.25,0.19,1)

        _SurfHeight ("Offshore Breaker Height (m)", Range(0.3,3)) = 1.3
        _SwashHeight ("Swash Run-up per m of wave", Range(0,1)) = 0.34
        _ChopAmp ("(unused)", Range(0,3)) = 1.0
        _WhitecapAmount ("(unused)", Range(0,1)) = 0.35
        _AbsorbScale ("(unused)", Range(0.2,3)) = 1.0
        _AmbientWeight ("Ambient Weight", Range(0,2)) = 0.95
        _SunWeight ("Sun Weight", Range(0,2)) = 1.0
        _SpecGain ("(unused)", Range(0,4)) = 0.35
        _SkyGain ("Sky Reflection Gain", Range(0,2)) = 1.0
        _DetailStrength ("(unused)", Range(0,2)) = 1.0
        _FoamGain ("Foam Gain", Range(0,2)) = 1.0
        _SwashAlpha ("Swash overlay opacity", Range(0,1)) = 0.85
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "NagisaOceanCommon.hlsl"

    static const float NS_LIFT = 0.035;      // overlay height above the ground (must match the stage)

    struct SwAttr
    {
        float4 positionOS : POSITION;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct SwVary
    {
        float4 positionCS : SV_POSITION;
        float3 positionRWS : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    SwVary VertSw(SwAttr input)
    {
        SwVary o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        o.positionRWS = TransformObjectToWorld(input.positionOS.xyz);
        o.positionCS = TransformWorldToHClip(o.positionRWS);
        o.positionWS = GetAbsolutePositionWS(o.positionRWS);
        return o;
    }

    float4 NS_Over(float4 below, float4 above)
    {
        float a = above.a + below.a * (1.0 - above.a);
        float3 c = (above.rgb * above.a + below.rgb * below.a * (1.0 - above.a)) / max(a, 1e-4);
        return float4(c, a);
    }

    float4 FragSw(SwVary i) : SV_Target
    {
        float t = NS_Time();
        float2 p = i.positionWS.xz;
        float h = i.positionWS.y - NS_LIFT;
        float2 hs = NS_HS(p);
        float dist = max(length(i.positionRWS), 0.01);
        float3 v = -i.positionRWS / dist;

        float s = max(-hs.y, 0.0);
        float tau = 2.0 * sqrt(s) / NS_C0;
        float offT = (NS_Noise(p * 0.022) - 0.5) * 3.0;
        float ampV = 0.80 + 0.40 * NS_Noise(p * 0.013 + 17.3);
        float u = t + tau - offT;
        int kmin = (int)floor((u - 170.0) / NS_C);

        float wetH = 0.06;            // permanent damp band just above the waterline
        float foamW = 0.0;
        float filmA = 0.0;
        float lace = NS_Lace(p, t, float2(-0.7, -0.7));

        [loop] for (int ks = 0; ks < 4; ks++)
        {
            int k = kmin + ks;
            float st; int n; float sp; float hk;
            NS_SetParams(k, st, n, sp, hk);
            for (int j = 0; j < 5; j++)
            {
                if (j >= n) break;
                float psi = u - (st + (float)j * sp);
                if (psi < 0.0 || psi > 110.0) continue;
                float Hn = _SurfHeight * NS_WaveH(k, j, n, hk) * ampV;
                float hmax = _SwashHeight * Hn;
                float Tsw = 6.5 + 1.5 * Hn;
                float q = psi / Tsw;
                float parab = q < 1.0 ? 4.0 * q * (1.0 - q) * hmax : 0.0;
                float wetN = psi <= 0.5 * Tsw ? parab : max(parab, exp(-(psi - 0.5 * Tsw) / 32.0) * hmax);
                wetH = max(wetH, wetN);

                float dW = parab - h;                      // water depth above the sand under the tongue
                if (dW > 0.0)
                {
                    float edge = 1.0 - smoothstep(0.0, 0.05 + 0.05 * hmax, dW);
                    float fill = exp(-dW / (0.22 * hmax + 0.03)) * (q < 0.5 ? 0.85 : 0.65);
                    foamW = max(foamW, saturate(edge + fill * (0.35 + lace * 0.9)));
                    filmA = max(filmA, smoothstep(0.0, 0.03, dW));
                }
            }
        }

        float wet = 1.0 - smoothstep(wetH - 0.05, wetH + 0.015, h);
        wet *= smoothstep(-0.06, 0.0, h);
        wet *= 1.0 - smoothstep(35.0, 70.0, hs.y);        // never far inland

        float3 l = MR_SunDirection();
        float3 sun = MR_SunColor();
        float3 amb = MR_Ambient(float3(0, 1, 0));
        float3 lightC = amb * _AmbientWeight + sun * (0.4 + 0.6 * saturate(l.y)) * _SunWeight;
        float skyScale = lerp(0.35, 1.0, saturate(dot(amb, float3(0.333, 0.333, 0.333)) / 0.62));

        float nv = saturate(v.y);
        float F = 0.03 + 0.97 * pow(1.0 - nv, 5.0);
        float3 sky = lerp(MR_AuthoredCol(_SkyHorizon.rgb), MR_AuthoredCol(_SkyZenith.rgb), 0.35) * skyScale * _SkyGain;

        float3 wetCol = MR_AuthoredCol(_WetSandColor.rgb) * lightC;
        wetCol = lerp(wetCol, sky, saturate(F * 0.8 + 0.06));
        float4 c = float4(wetCol, wet * 0.62 * _SwashAlpha);

        float3 filmCol = lerp(wetCol * 0.9, sky, saturate(F + 0.12));
        c = NS_Over(c, float4(filmCol, filmA * 0.45 * _SwashAlpha));

        float foam = saturate(foamW * _FoamGain);
        float3 foamCol = MR_AuthoredCol(_FoamColor.rgb) * (amb * 0.9 + sun * (0.35 + 0.65 * saturate(l.y)));
        c = NS_Over(c, float4(foamCol, foam * 0.92 * _SwashAlpha));

        if (c.a < 0.003) discard;
        return float4(MR_ToRender(MR_ApplyFog(c.rgb, i.positionRWS)) * GetCurrentExposureMultiplier(), c.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-90" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VertSw
            #pragma fragment FragSw
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    Fallback Off
}
