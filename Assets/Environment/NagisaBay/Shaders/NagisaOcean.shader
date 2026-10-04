// NB1 - Nagisa Bay photoreal ocean + crashing surf. Copy-and-extend of MapleRide/HDRP/Ocean (that shader is
// untouched). Wave/surf maths lives in NagisaOceanCommon.hlsl and is mirrored by NagisaSurf.cs.
//   * Gerstner swell trains (SE swell + 5 shorter trains) + a set-based breaker model driven by a baked
//     depth / coast-distance field, so every beach in the bay gets refracted breakers that shoal, curl and collapse.
//   * Colour: Beer-Lambert absorption through the real water column over sand / reef -> turquoise to cobalt to navy.
//   * Fresnel sky reflection, GGX sun glint, crest subsurface glow, foam (breaker lip, bore trail, whitecaps,
//     shoreline lace), two scrolling detail normal maps at irrational scales fading out with distance.
Shader "MapleRide/HDRP/NagisaOcean"
{
    Properties
    {
        [NoScaleOffset] _NagisaField ("Depth/coast field (RGBAHalf)", 2D) = "black" {}
        _FieldRect ("Field rect x0,z0,cell", Vector) = (0,0,6,0)
        _FieldDim ("Field dim nx,nz", Vector) = (1,1,0,0)
        [NoScaleOffset] _NormA ("Detail normal A", 2D) = "bump" {}
        [NoScaleOffset] _NormB ("Detail normal B", 2D) = "bump" {}
        [NoScaleOffset] _FoamTex ("Lace foam", 2D) = "white" {}

        _ShallowTint ("Shallow Scatter (turquoise)", Color) = (0.08,0.62,0.66,1)
        _DeepColor ("Deep Scatter (navy)", Color) = (0.015,0.09,0.24,1)
        _SandColor ("Seabed Sand", Color) = (0.86,0.78,0.60,1)
        _ReefColor ("Reef / Rock Seabed", Color) = (0.10,0.15,0.14,1)
        _SkyZenith ("Sky Reflection Zenith", Color) = (0.38,0.60,0.88,1)
        _SkyHorizon ("Sky Reflection Horizon", Color) = (0.80,0.88,0.94,1)
        _SunColor ("Sun Glint Color", Color) = (1,0.95,0.82,1)
        _FoamColor ("Foam Color", Color) = (0.97,0.99,0.99,1)
        _SssColor ("Crest Subsurface Color", Color) = (0.25,0.85,0.62,1)
        _WetSandColor ("Wet Sand (swash only)", Color) = (0.30,0.25,0.19,1)

        _SurfHeight ("Offshore Breaker Height (m)", Range(0.3,3)) = 1.3
        _SwashHeight ("Swash Run-up per m of wave", Range(0,1)) = 0.34
        _ChopAmp ("Swell/Chop Amplitude x", Range(0,3)) = 1.0
        _WhitecapAmount ("Whitecaps", Range(0,1)) = 0.35
        _AbsorbScale ("Water Absorption x", Range(0.2,3)) = 1.0
        _AmbientWeight ("Ambient Weight", Range(0,2)) = 0.95
        _SunWeight ("Sun Weight", Range(0,2)) = 1.0
        _SpecGain ("Sun Glint Gain", Range(0,4)) = 0.35
        _SkyGain ("Sky Reflection Gain", Range(0,2)) = 1.0
        _DetailStrength ("Detail Normal Strength", Range(0,2)) = 1.0
        _FoamGain ("Foam Gain", Range(0,2)) = 1.0
        _SwashAlpha ("Swash overlay opacity", Range(0,1)) = 0.85
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "NagisaOceanCommon.hlsl"

    struct NSAttr
    {
        float4 positionOS : POSITION;
        float4 color : COLOR;          // r = displacement envelope (0 on the far plate)
        float2 uv1 : TEXCOORD1;        // stitch half-edge vector for LOD-boundary vertices
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct NSVary
    {
        float4 positionCS : SV_POSITION;
        float3 positionRWS : TEXCOORD0;
        float4 p0y : TEXCOORD1;        // xy = lattice xz, z = displaced absolute y, w = envelope
        float2 grad : TEXCOORD2;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    NSVary VertNS(NSAttr input)
    {
        NSVary o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        float3 prws = TransformObjectToWorld(input.positionOS.xyz);
        float3 wp = GetAbsolutePositionWS(prws);
        float t = NS_Time();
        float env = input.color.r;

        float3 d = float3(0, 0, 0);
        float2 grad = float2(0, 0);
        if (env > 0.0001)
        {
            if (dot(input.uv1, input.uv1) > 1e-6)
            {
                NSDisp a = NS_Displace(wp.xz + input.uv1, t, env);
                NSDisp b = NS_Displace(wp.xz - input.uv1, t, env);
                d = 0.5 * (a.d + b.d);
                grad = 0.5 * (a.grad + b.grad);
            }
            else
            {
                NSDisp a = NS_Displace(wp.xz, t, env);
                d = a.d; grad = a.grad;
            }
        }
        float3 absPos = wp + d;
        o.positionRWS = GetCameraRelativePositionWS(absPos);
        o.positionCS = TransformWorldToHClip(o.positionRWS);
        o.p0y = float4(wp.xz, absPos.y, env);
        o.grad = clamp(grad, -4.0, 4.0);
        return o;
    }

    float2 NS_Rot(float2 p, float a)
    {
        float c = cos(a), s = sin(a);
        return float2(p.x * c - p.y * s, p.x * s + p.y * c);
    }

    float4 FragNS(NSVary i) : SV_Target
    {
        float t = NS_Time();
        float dist = max(length(i.positionRWS), 0.01);
        float3 v = -i.positionRWS / dist;
        float2 p0 = i.p0y.xy;
        float surfY = i.p0y.z;
        float2 hs = NS_HS(p0);
        float depthW = smoothstep(0.2, 6.0, max(-hs.x, 0.0));

        NSSurf sf = NS_Surf(p0, t, hs);

        // ---- normal: vertex surf gradient + analytic trains + two scrolling detail maps
        float2 gTr = NS_TrainGrad(p0, t, depthW, dist);
        float fadeA = 1.0 - smoothstep(25.0, 240.0, dist);
        float fadeB = 1.0 - smoothstep(100.0, 1100.0, dist);
        float macro = 0.65 + 0.7 * NS_Noise(p0 * 0.0093);
        float2 uvA = NS_Rot(p0, 0.61) * 0.137 + t * float2(0.021, 0.013);
        float2 uvB = NS_Rot(p0, -0.47) * 0.0437 + t * float2(-0.012, 0.017);
        float3 nA = SAMPLE_TEXTURE2D(_NormA, sampler_NormA, uvA).xyz * 2.0 - 1.0;
        float3 nB = SAMPLE_TEXTURE2D(_NormB, sampler_NormB, uvB).xyz * 2.0 - 1.0;
        float2 gDet = (-nA.xy / max(nA.z, 0.25)) * (0.20 * fadeA) + (-nB.xy / max(nB.z, 0.25)) * (0.17 * fadeB * macro);
        float2 g = i.grad + gTr + gDet * _DetailStrength;
        float3 n = normalize(float3(-g.x, 1.0, -g.y));

        float3 l = MR_SunDirection();
        float3 sun = MR_SunColor();
        float3 amb = MR_Ambient(float3(0, 1, 0));

        // ---- body colour: absorption through the real water column
        float Dw = max(surfY - hs.x, 0.0);
        float3 absorb = float3(0.46, 0.115, 0.075) * _AbsorbScale;
        float3 T = exp(-absorb * Dw);
        float reefN = NS_Noise(p0 * 0.031) * 0.6 + NS_Noise(p0 * 0.09) * 0.4;
        float reef = smoothstep(0.55, 0.75, reefN) * smoothstep(1.5, 3.5, Dw) * (1.0 - smoothstep(10.0, 16.0, Dw));
        float3 bottom = lerp(MR_AuthoredCol(_SandColor.rgb), MR_AuthoredCol(_ReefColor.rgb), reef);
        float3 scat = lerp(MR_AuthoredCol(_DeepColor.rgb), MR_AuthoredCol(_ShallowTint.rgb), exp(-Dw * 0.16));
        float3 shallowGlow = MR_AuthoredCol(_ShallowTint.rgb) * (1.0 - exp(-Dw * 2.0)) * exp(-Dw * 0.22) * 0.5;   // turquoise volume scatter so the shallows are not grey
        float3 body = bottom * T * 0.82 + scat * (1.0 - T * 0.7) + shallowGlow;

        float ndl = saturate(dot(n, l) * 0.5 + 0.5);
        float3 lightB = (amb * _AmbientWeight + sun * (0.45 + 0.55 * ndl) * _SunWeight) * 0.72;
        float3 col = body * lightB;

        // ---- crest subsurface glow
        float hT = saturate(sf.eta / (0.7 * _SurfHeight) + sf.steep * 0.6);
        float3 sssDir = normalize(l + n * 0.4);
        float sss = hT * pow(saturate(dot(v, -sssDir)), 3.0);
        col += MR_AuthoredCol(_SssColor.rgb) * (sss * 1.3) * (amb * 0.5 + sun * 0.6);

        // ---- fresnel sky reflection
        float nv = saturate(dot(n, v));
        float F = 0.02 + 0.98 * pow(1.0 - nv, 5.0);
        float3 r = reflect(-v, n);
        r.y = max(r.y, 0.02);
        float skyScale = lerp(0.35, 1.0, saturate(dot(amb, float3(0.333, 0.333, 0.333)) / 0.62));
        float3 sky = lerp(MR_AuthoredCol(_SkyHorizon.rgb), MR_AuthoredCol(_SkyZenith.rgb), pow(saturate(r.y), 0.5)) * skyScale * _SkyGain;
        col = lerp(col, sky, saturate(F));

        // ---- sun glint (GGX, wider lobe with distance)
        float3 hv = normalize(l + v);
        float nh = saturate(dot(n, hv));
        float rough = lerp(0.045, 0.30, smoothstep(40.0, 1800.0, dist));
        float a2 = rough * rough;
        float dd = nh * nh * (a2 - 1.0) + 1.0;
        float Dg = a2 / (NS_PI * dd * dd);
        float spec = Dg * (0.02 + 0.98 * pow(1.0 - saturate(dot(hv, v)), 5.0)) * saturate(dot(n, l)) * 0.25 * _SpecGain;
        spec = min(spec, 6.0);

        // ---- foam
        float lace = NS_Lace(p0, t, float2(0.7, 0.7));
        float lipF = saturate(sf.lip * (0.75 + 0.55 * lace));
        float tf = saturate(sf.trail * 1.15 - (1.0 - lace) * 0.9);
        tf = smoothstep(0.08, 0.55, tf);
        float fr = (1.0 - smoothstep(0.02, 0.30, Dw)) * saturate(lace * 1.25) * 0.8;
        float th; float2 tpush;
        NS_Trains(p0, t, depthW, 1.0, th, tpush);
        float thn = th / max(0.647 * _ChopAmp * max(depthW, 0.05), 1e-3);
        float cap = smoothstep(0.55, 0.95, thn) * _WhitecapAmount * lace * (1.0 - smoothstep(250.0, 900.0, dist));
        float foam = saturate(max(max(lipF, tf), max(fr, cap)) * _FoamGain);
        col += MR_AuthoredCol(_SunColor.rgb) * sun * spec * (1.0 - foam);

        float3 foamLit = MR_AuthoredCol(_FoamColor.rgb) * (amb * 0.9 + sun * (0.35 + 0.65 * saturate(dot(n, l))));
        col = lerp(col, foamLit, foam * (0.50 + 0.42 * lace));

        return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+10" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex VertNS
            #pragma fragment FragNS
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex VertNS
            #pragma fragment FragDepthNS
            #pragma multi_compile_instancing
            void FragDepthNS(NSVary i) { }
            ENDHLSL
        }
    }

    Fallback Off
}
