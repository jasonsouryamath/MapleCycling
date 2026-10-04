// Nagisa-only extension: leaf color floor and neutral sky bounce; shared Foliage untouched.
// HDRP replacement for MapleRide/SakuraFoliage (spec section 4.4).
//
// Foliage variant of the cel shader: alpha cutout, two-sided, subsurface translucency and
// wind sway. Used for sakura blossom cards, leaves, ferns and grass so they read as lit
// volumes, not flat stickers.
//
// Property names, defaults and cel maths are deliberately IDENTICAL to the Built-in
// original, so converting a material is a shader swap that preserves every authored
// value. The Built-in shader is intentionally left on disk and untouched (spec 4.1).
//
// SHADOWS: closed (opening visual overhaul, milestone 2). The first HDRP pass could not
// receive cast shadows - the Built-in version got its shadow term from the surface framework's
// `atten`, which does not exist in a hand-written HDRP pass - so every bush, fern, grass tuft
// and blossom card rendered at full key even when it stood inside a tree's shadow. That is a
// large part of what read as "stickers glued onto the hillside". The term now comes from
// MapleRideHDRPShadow.hlsl, sampled in the ForwardOnly pass ONLY (DepthForwardOnly and
// ShadowCaster have no light-loop constants bound and pass 1.0), exactly as CelLit and Terrain
// already do. _ShadowEdge/_ShadowSoft shape the hard cascade edge back into a cel band.
Shader "MapleRide/HDRP/NagisaCoastalFoliage"
{
    Properties
    {
        _CoastalMatteFloor ("Leaf Albedo Floor", Color) = (0.15,0.25,0.10,1)
        _CoastalAmbient ("Sky Bounce", Range(0,1)) = 0.45
        _Color         ("Base Color", Color) = (1,1,1,1)
        _MainTex       ("Albedo (A = cutout)", 2D) = "white" {}
        _Cutoff        ("Alpha Cutoff", Range(0,1)) = 0.4
        _ShadeColor    ("Shade Tint", Color) = (0.48,0.42,0.62,1)
        _ShadeStrength ("Shade Strength", Range(0,1)) = 0.70
        _RampSteps     ("Ramp Steps", Range(2,6)) = 3
        _RampSmooth    ("Ramp Softness", Range(0.002,0.35)) = 0.09
        _Translucency  ("Backlight Translucency", Range(0,2)) = 0.9
        _TransColor    ("Translucency Color", Color) = (1,0.62,0.76,1)
        _RimColor      ("Rim Color", Color) = (1,0.8,0.86,1)
        _RimPower      ("Rim Power", Range(0.5,10)) = 2.5
        _RimStrength   ("Rim Strength", Range(0,3)) = 0.6
        _EdgeOnFade    ("Edge-on Sliver Fade (0 = legacy)", Range(0,1)) = 0
        _WindStrength  ("Wind Strength", Range(0,1)) = 0.12
        _WindSpeed     ("Wind Speed", Range(0,5)) = 1.1
        _WindScale     ("Wind Spatial Scale", Range(0.01,1)) = 0.12

        // Cel shaping for the cast-shadow term. Softer than CelLit's default: a leaf card is
        // small, and a hard step across one puts a visible black notch in the canopy.
        _ShadowEdge    ("Shadow Step Midpoint", Range(0.05,0.95)) = 0.5
        _ShadowSoft    ("Shadow Step Softness", Range(0.01,0.45)) = 0.22
        _SnowAccept    ("Snow Accept (global _MR_SnowCover)", Range(0,1)) = 0
        _SnowBias      ("Snow Slope Bias", Range(-0.5,0.5)) = -0.05
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _CoastalMatteFloor;
        float _CoastalAmbient;
        float4 _MainTex_ST;
        float4 _Color;
        float  _Cutoff;
        float4 _ShadeColor;
        float  _ShadeStrength, _RampSteps, _RampSmooth;
        float  _Translucency;
        float4 _TransColor;
        float4 _RimColor;
        float  _RimPower, _RimStrength;
        float  _EdgeOnFade;
        float  _WindStrength, _WindSpeed, _WindScale;
        float  _ShadowEdge, _ShadowSoft;
        float  _SnowAccept, _SnowBias;
    CBUFFER_END

    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideSnow.hlsl"
        float4 _MapleWind;   // global, WeatherDirector

    // Sways verts more the higher they sit above the object pivot, so trunks stay planted.
    //
    // Two HDRP deviations, both required rather than cosmetic:
    //  - TransformObjectToWorld returns CAMERA-RELATIVE world space, so the sway phase is
    //    taken from GetAbsolutePositionWS(). Using the relative position would make the
    //    whole canopy re-phase every time the rider moves, which reads as a shimmer.
    //  - _Time.y does not exist outside Built-in; HDRP's equivalent is _TimeParameters.x.
    MRVaryings MR_VertFoliage(MRAttributes input)
    {
        MRVaryings o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);

        float3 wp = GetAbsolutePositionWS(TransformObjectToWorld(input.positionOS.xyz));
        float phase = (wp.x + wp.z) * _WindScale + _TimeParameters.x * _WindSpeed;
        float sway = (sin(phase) + sin(phase * 2.31 + 1.7) * 0.5) * 0.5;
        float mask = saturate(input.positionOS.y * 0.35);

        // WEATHER (2026-09-25): bend + sway ALONG the gameplay wind (_MapleWind, set globally by
        // WeatherDirector: xy = world XZ wind velocity, z = speed m/s, w = gust m/s), so foliage
        // agrees with the wind the rider feels. No weather running (z == 0) -> the old look.
        bool live = _MapleWind.z > 0.01;
        float2 wdir = live ? normalize(_MapleWind.xy) : normalize(float2(1.0, 0.6));
        float k = live ? 0.5 + saturate((_MapleWind.z + _MapleWind.w) / 8.0) * 1.5 : 1.0;
        float bend = live ? 0.35 * saturate(_MapleWind.z / 8.0) : 0.0;
        float amt = (sway + bend) * _WindStrength * mask * k;
        float3 positionOS = input.positionOS.xyz
                          + TransformWorldToObjectDir(float3(wdir.x, 0.0, wdir.y) * amt, false);

        float3 positionRWS = TransformObjectToWorld(positionOS);
        o.positionCS = TransformWorldToHClip(positionRWS);
        o.positionWS = GetAbsolutePositionWS(positionRWS);
        // See MapleRideTerrain: MR_ApplyFog reads this and a custom vertex function must
        // populate it explicitly.
        o.positionRWS = positionRWS;
        o.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
        o.uv = input.uv;
        o.color = input.color;
        return o;
    }

    float4 MRF_Sample(MRVaryings i)
    {
        float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
        return MR_Authored(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv)) * MR_AuthoredCol(_Color);
    }
    // EDGE-ON SLIVER FIX (QA 2026-09-30, opt-in via _EdgeOnFade, default 0 = old look).
    // A card seen edge-on has dot(n,v) ~ 0, which drove the rim term to full strength and added
    // it WITHOUT albedo -> bright thin crosshatch lines across canopies. Facing is taken from the
    // GEOMETRIC normal (screen derivatives) because crown vertex normals are often bent.
    float MRF_Facing(MRVaryings i)
    {
        float3 gn = normalize(cross(ddx(i.positionWS), ddy(i.positionWS)));
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);
        float f = abs(dot(gn, v));
        return any(isnan(f)) ? 1.0 : f;
    }
    // Extra cutoff that removes near-edge-on slivers. Used identically by the forward and depth
    // passes so the depth prepass never disagrees with colour. Not used by ShadowCaster.
    float MRF_EdgeCut(MRVaryings i)
    {
        if (_EdgeOnFade <= 0.0) return 0.0;
        return _EdgeOnFade * (1.0 - smoothstep(0.08, 0.26, MRF_Facing(i))) * 1.2;
    }


    // sunShadow: HDRP directional cascade attenuation (1 = lit, 0 = fully shadowed), sampled in
    // the ForwardOnly pass and passed in, so the depth and shadow passes never pull the
    // light-loop headers into their translation unit.
    float4 MRF_Shade(MRVaryings i, float sunShadow)
    {
        float4 c = MRF_Sample(i);
        clip(c.a - _Cutoff - MRF_EdgeCut(i));
        c.rgb = max(c.rgb, MR_AuthoredCol(_CoastalMatteFloor.rgb));

        // Weather snow on the top of the canopy. Cards are double sided, so the up test takes the
        // better-facing side (a flat card holds snow whichever way it was authored).
        float3 gn = normalize(i.normalWS);
        float snowM = MR_SnowMask(i.positionWS, float3(gn.x, abs(gn.y), gn.z), _SnowAccept, _SnowBias);
        c.rgb = MR_SnowApply(c.rgb, snowM, i.positionWS);

        float3 n = normalize(i.normalWS);
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);
        // Foliage cards are double sided; flip the normal toward the viewer so backfaces still light.
        n *= sign(dot(n, v) + 0.0001);

        float3 l = MR_SunDirection();
        float ndl = dot(n, l) * 0.5 + 0.5;

        // Built-in: CelRamp(ndl) * CelRamp(atten). `atten` is now real again - it is HDRP's
        // directional shadow attenuation, shaped into a cel band by _ShadowEdge/_ShadowSoft so
        // the raw cascade edge does not read as a photoreal hard line across a leaf card.
        float shade = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft,
                                 saturate(sunShadow));
        float shadowed = MR_CelRamp(ndl, _RampSteps, _RampSmooth) * shade;
        float3 lit = lerp(MR_AuthoredCol(_ShadeColor.rgb) * _ShadeStrength, float3(1, 1, 1), shadowed);
        if (snowM > 0.0) lit = lerp(lit, MR_SnowLit(dot(n, l), shade), snowM);

        // Light bleeding through the canopy from behind - the signature sakura backlight.
        float back = pow(saturate(dot(v, -l)), 2.0) * _Translucency;

        float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength;
        float edgeFacing = MRF_Facing(i);
        rim  *= lerp(1.0, smoothstep(0.2, 0.7, edgeFacing), _EdgeOnFade);
        back *= lerp(1.0, smoothstep(0.1, 0.45, edgeFacing), _EdgeOnFade);

        float3 sun = MR_SunColor();
        float3 col = c.rgb * lit * sun;
        // The Built-in shader multiplied the translucency by atten: a leaf in full shade is not
        // backlit by a sun it cannot see. Restored now that the term exists.
        col += c.rgb * MR_AuthoredCol(_TransColor.rgb) * back * shade * sun;
        col += MR_AuthoredCol(_RimColor.rgb) * rim * sun;
        col += c.rgb * MR_Ambient(n) * _CoastalAmbient;

        // This Nagisa variant restores restrained sky bounce above the inherited shade
        // floor. The base shared shader retains its original gamma-port lighting contract.

        // HDRP renders into a physically-exposed buffer; a hand-written pass must apply
        // the current exposure itself or it lands at a wildly different brightness than
        // every HDRP-lit surface around it.
        return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), c.a);
    }
    ENDHLSL

    SubShader
    {
        // Source queue/render type preserved: cutout foliage must sort with AlphaTest.
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex MR_VertFoliage
            #pragma fragment Frag
            #pragma multi_compile_instancing
            // Shadow-quality variants HDRP's shadow algorithms branch on. Without these the
            // shadow headers compile against an undefined filter and every lookup returns 1.
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

            // INSIDE THE PASS ONLY. DepthForwardOnly and ShadowCaster have no light-loop
            // constants bound; pulling these headers into their translation unit would either
            // fail to compile or bind garbage.
            #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPShadow.hlsl"

            float4 Frag(MRVaryings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                // Foliage is two-sided (Cull Off), so the normal handed to the shadow
                // normal-bias must be the one actually facing the light's side of the card, or
                // backfaces self-shadow into black speckle.
                float3 n = normalize(i.normalWS);
                float shadow = MR_SunShadow(i.positionRWS, frontFace ? n : -n, i.positionCS.xy);
                return MRF_Shade(i, shadow);
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
            #pragma vertex MR_VertFoliage
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            // The prepass must run the same cutout as the forward pass and the same wind
            // sway, or the depth buffer describes solid rectangles where the cards are.
            void FragDepth(MRVaryings i)
            {
                clip(MRF_Sample(i).a - _Cutoff - MRF_EdgeCut(i));
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex MR_VertFoliage
            #pragma fragment FragShadow
            #pragma multi_compile_instancing

            // Equivalent of the original's `addshadow`: a cutout shadow, not a card shadow.
            void FragShadow(MRVaryings i)
            {
                clip(MRF_Sample(i).a - _Cutoff);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
