// MapleRide/HDRP/Road - the carriageway is a HERO SURFACE.
//
// WHY THIS EXISTS
// The Built-in MapleRide/SakuraRoad carried the whole road look (aggregate, macro variation,
// wheel tracks, shoulder and verge transition). The HDRP migration had no road entry in its
// conversion table, so SakuraPass_Asphalt was swapped onto the generic MapleRide/HDRP/CelLit -
// a flat tinted cel surface. That is exactly the "flat purple/grey strip" in the user's
// screenshot: every road-specific layer was dropped on the floor.
//
// This shader is the HDRP replacement. It keeps the Built-in property NAMES so the existing
// authored values transfer, ports the section-26 layer stack, and adds the cues that make a
// surface read as engineered roadway rather than a painted plane:
//
//   base asphalt -> macro lay-down variation -> resurfacing patches -> longitudinal paving
//   joint -> wheel-track polish (darker AND glossier) -> edge ravelling -> shoulder grit ->
//   verge transition -> aggregate normal -> view-dependent sheen
//
// ROAD SPACE IS RECOVERED FROM UV, NOT FROM WORLD XZ.
// build_road.py sweeps the carriageway with u = (cumulative metres across the cross-section)
// * ASPHALT_TILE and v = (arc metres) * ASPHALT_TILE. So:
//     metres across the profile = uv.x / _UvPerMetre      (u = 0 is the OUTER SKIRT)
//     metres along the route    = uv.y / _UvPerMetre
// _UvPerMetre MUST equal ASPHALT_TILE in build_road.py and _UvCentreM MUST equal the measured
// u-metre of the carriageway centreline (half of the full profile arc), or every mask below
// slides sideways. Both are pushed from SakuraRoadPass.cs, which owns the canonical values.
//
// PHOTOREALISM vs THE KIT'S CEL LOOK
// _CelAmount blends between a smooth wrapped-lambert response (0) and the kit's banded cel
// ramp (1). The road sits at a low value: hard N.L bands on a 7 m surface that fills half the
// frame is the single strongest "toy" cue, while the trees and props around it keep their
// banding. ALL PROVISIONAL tuning - see SakuraRoadPass.cs for the values actually pushed.
//
// KNOWN GAP (shared with MapleRide/HDRP/CelLit): a hand-written HDRP pass cannot reach HDRP's
// shadow data, so this surface does not RECEIVE cast shadows. It still casts. The shade
// controls stay wired so the look returns unchanged when the term arrives.
Shader "MapleRide/HDRP/Road"
{
    Properties
    {
        _Color            ("Base Color", Color) = (1,1,1,1)
        _MainTex          ("Albedo", 2D) = "white" {}
        _DetailNormal     ("Detail Normal", 2D) = "bump" {}
        _DetailTile       ("Detail Normal Tile", Float) = 7.0
        _DetailStrength   ("Detail Normal Strength", Range(0,2)) = 0.55

        // --- section 26 layer stack (ALL PROVISIONAL) ---
        _MacroScale       ("Macro Variation Scale (per m)", Float) = 0.031
        _MacroAmount      ("Macro Variation Amount", Range(0,1)) = 0.30
        _PatchScale       ("Resurfacing Patch Scale (per m)", Float) = 0.013
        _PatchAmount      ("Resurfacing Patch Amount", Range(0,1)) = 0.22
        _PatchColor       ("Resurfacing Patch Tint", Color) = (0.86,0.84,0.86,1)
        _RoughVariation   ("Roughness Variation", Range(0,1)) = 0.45

        _UvPerMetre       ("UV Per Metre (= ASPHALT_TILE)", Float) = 0.22
        _UvCentreM        ("Carriageway Centre In UV Metres", Float) = 5.7824
        _RoadWidthM       ("Carriageway Width (m)", Float) = 7.0

        _TrackOffsetM     ("Wheel Track Offset From Lane Centre (m)", Float) = 0.80
        _TrackWidthM      ("Wheel Track Width (m)", Float) = 0.70
        _TrackDarken      ("Wheel Track Darkening", Range(0,1)) = 0.30
        _TrackPolish      ("Wheel Track Polish (gloss gain)", Range(0,1)) = 0.55

        // --- engineered-roadway cues (ALL PROVISIONAL) ---
        _JointOffsetM     ("Longitudinal Paving Joint From Centre (m)", Float) = 0.0
        _JointWidthM      ("Paving Joint Width (m)", Float) = 0.05
        _JointDarken      ("Paving Joint Darkening", Range(0,1)) = 0.35
        _CrackScale       ("Crack / Ravel Noise Scale (per m)", Float) = 0.55
        _EdgeRavelM       ("Edge Ravelling Band (m)", Float) = 0.45
        _EdgeRavelAmount  ("Edge Ravelling Amount", Range(0,1)) = 0.55

        _ShoulderWidthM   ("Shoulder Band Width (m)", Float) = 0.75
        _ShoulderColor    ("Shoulder Dirt / Petal Tint", Color) = (0.62,0.50,0.50,1)
        _ShoulderAmount   ("Shoulder Amount", Range(0,1)) = 0.55

        _VergeWidthM      ("Verge Transition Width (m)", Float) = 1.45
        _VergeGravelColor ("Verge Gravel Tint", Color) = (0.60,0.56,0.53,1)
        _VergeSoilColor   ("Verge Soil Tint", Color) = (0.40,0.33,0.27,1)
        _VergeAmount      ("Verge Amount", Range(0,1)) = 0.80
        _VergeBreakup     ("Verge Edge Breakup", Range(0,1)) = 0.42

        // --- lighting ---
        _CelAmount        ("Cel Banding Amount", Range(0,1)) = 0.25
        _ShadeColor       ("Shade Tint", Color) = (0.72,0.66,0.74,1)
        _ShadeStrength    ("Shade Strength", Range(0,1)) = 0.55
        _RampSteps        ("Ramp Steps", Range(2,6)) = 3
        _RampSmooth       ("Ramp Softness", Range(0.002,0.35)) = 0.06
        _RampOffset       ("Ramp Offset", Range(-0.5,0.5)) = 0.0
        _RimColor         ("Rim Color", Color) = (1,0.72,0.52,1)
        _RimPower         ("Rim Power", Range(0.5,10)) = 3.0
        _RimStrength      ("Rim Strength", Range(0,3)) = 0.25
        _SpecTint         ("Specular Tint", Color) = (1,0.93,0.85,1)
        _Gloss            ("Gloss", Range(0.01,1)) = 0.25
        _SpecStrength     ("Specular Strength", Range(0,2)) = 0.25
        _SheenStrength    ("Grazing Sheen Strength", Range(0,2)) = 0.45
        _AmbientStrength  ("Ambient Strength", Range(0,2)) = 1.0
        _ShadowAmbient    ("Ambient Kept In Shadow", Range(0,1)) = 0.38
        _ShadowEdge       ("Shadow Step Midpoint", Range(0.05,0.95)) = 0.5
        _ShadowSoft       ("Shadow Step Softness", Range(0.01,0.45)) = 0.20
        _DappleScale      ("Canopy Dapple Scale (per m)", Range(0.5,8)) = 2.6
        _DappleStrength   ("Canopy Dapple Strength", Range(0,1)) = 0.25
        // WET PATCHES (Minato redesign 2026-09-25): mirror-smooth puddle films that pick up the
        // sky and a hard sun glint. Default 0 = byte-for-byte the previous road everywhere else.
        _WetAmount        ("Wet Patch Amount", Range(0,1)) = 0
        _WetScale         ("Wet Patch Scale", Float) = 0.045
        _WetSkyColor      ("Wet Sky Reflection", Color) = (0.62,0.72,0.86,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
    TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);

    CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _Color;
        float  _DetailTile, _DetailStrength;
        float  _MacroScale, _MacroAmount, _PatchScale, _PatchAmount;
        float4 _PatchColor;
        float  _RoughVariation;
        float  _UvPerMetre, _UvCentreM, _RoadWidthM;
        float  _TrackOffsetM, _TrackWidthM, _TrackDarken, _TrackPolish;
        float  _JointOffsetM, _JointWidthM, _JointDarken;
        float  _CrackScale, _EdgeRavelM, _EdgeRavelAmount;
        float  _ShoulderWidthM, _ShoulderAmount;
        float4 _ShoulderColor;
        float  _VergeWidthM, _VergeAmount, _VergeBreakup;
        float4 _VergeGravelColor, _VergeSoilColor;
        float  _CelAmount;
        float4 _ShadeColor;
        float  _ShadeStrength, _RampSteps, _RampSmooth, _RampOffset;
        float4 _RimColor;
        float  _RimPower, _RimStrength;
        float4 _SpecTint;
        float  _Gloss, _SpecStrength, _SheenStrength;
        float  _AmbientStrength, _ShadowAmbient, _ShadowEdge, _ShadowSoft;
        float  _DappleScale, _DappleStrength;
        float  _WetAmount, _WetScale;
        float4 _WetSkyColor;
        float  _Cull;
    CBUFFER_END

    // Smooth 2D fbm on ROAD SPACE (metres across, metres along) rather than on world XZ:
    // the masks below must stay glued to the carriageway through every bank and switchback.
    float MR_RoadFbm(float2 rm, float scale)
    {
        float n = MR_ValueNoise(rm, scale);
        n += MR_ValueNoise(rm + 31.7, scale * 2.17) * 0.5;
        n += MR_ValueNoise(rm + 71.3, scale * 4.61) * 0.25;
        return n / 1.75;
    }

    float4 MR_ShadeRoad(MRVaryings i, bool frontFace, float sunShadow)
    {
        float3 tintBase    = MR_AuthoredCol(_Color.rgb);
        float3 tintShade   = MR_AuthoredCol(_ShadeColor.rgb);
        float3 tintSpec    = MR_AuthoredCol(_SpecTint.rgb);
        float3 tintRim     = MR_AuthoredCol(_RimColor.rgb);
        float3 tintPatch   = MR_AuthoredCol(_PatchColor.rgb);
        float3 tintShoulder= MR_AuthoredCol(_ShoulderColor.rgb);
        float3 tintGravel  = MR_AuthoredCol(_VergeGravelColor.rgb);
        float3 tintSoil    = MR_AuthoredCol(_VergeSoilColor.rgb);

        float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
        float3 c = MR_Authored(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv)).rgb * tintBase;

        // ---- road space -------------------------------------------------------------
        float inv = 1.0 / max(1e-4, _UvPerMetre);
        float xm = i.uv.x * inv;                 // metres across the swept profile
        float ym = i.uv.y * inv;                 // metres along the route
        float2 rm = float2(xm, ym);
        float fromCentre = abs(xm - _UvCentreM);
        float edge = _RoadWidthM * 0.5;
        float fromEdge = fromCentre - edge;      // <0 on the lanes, >0 outboard
        float laneCentre = _RoadWidthM * 0.25;

        // ---- macro lay-down variation ------------------------------------------------
        // Paving is laid in mats by a finisher; the mats never match exactly. A large, soft
        // luminance field in road space is what breaks the 4.5 m albedo tile without a second
        // texture, and it is oriented ALONG the road, like real paving.
        float macro = MR_RoadFbm(float2(rm.x * 0.35, rm.y), _MacroScale);
        c *= lerp(1.0, 0.74 + macro * 0.66, _MacroAmount);

        // ---- resurfacing patches ------------------------------------------------------
        // Soft slabs with a slightly darker, fresher binder. Thresholded so most of the road
        // has none - a road that is patched everywhere reads as noise, not as maintenance.
        float patchN = MR_RoadFbm(float2(rm.x * 0.5, rm.y * 0.22), _PatchScale * 6.0);
        float patch = smoothstep(0.52, 0.66, patchN);
        c = lerp(c, c * tintPatch, patch * _PatchAmount);
        // The seam around a patch is the thing that actually reads as a patch.
        float seam = smoothstep(0.50, 0.53, patchN) - smoothstep(0.545, 0.575, patchN);
        c *= 1.0 - saturate(seam) * 0.45 * _PatchAmount;

        // ---- longitudinal paving joint -------------------------------------------------
        // The cold joint between two paving passes. Offset 0 puts it on the centreline (where
        // the yellow line hides it), so the default is disabled and SakuraRoadPass pushes a
        // real offset. Wanders slightly - a machine-straight line is the giveaway.
        float jointWander = (MR_RoadFbm(float2(0.0, rm.y), 0.05) - 0.5) * 0.10;
        float joint = 0.0;
        if (_JointOffsetM > 0.01)
        {
            float dj = abs(fromCentre - (_JointOffsetM + jointWander));
            joint = 1.0 - saturate(dj / max(0.005, _JointWidthM));
            joint *= saturate(0.55 + macro * 0.9);
            c *= 1.0 - joint * _JointDarken;
        }

        // ---- wheel tracks ---------------------------------------------------------------
        // Two polished bands per lane. Traffic both DARKENS (embedded rubber, fines) and
        // POLISHES (aggregate worn smooth) the surface; the polish is applied to gloss below,
        // which is what sells them at a grazing rider's-eye angle.
        float track = 0.0;
        track = max(track, 1.0 - saturate(abs(fromCentre - (laneCentre - _TrackOffsetM)) / max(0.01, _TrackWidthM)));
        track = max(track, 1.0 - saturate(abs(fromCentre - (laneCentre + _TrackOffsetM)) / max(0.01, _TrackWidthM)));
        track *= step(fromCentre, edge);          // tracks exist on the carriageway only
        c *= 1.0 - track * _TrackDarken * (0.6 + macro * 0.8);

        // ---- edge ravelling --------------------------------------------------------------
        // Real asphalt does not end on a drawn line: the last half metre loses fines, cracks and
        // is dusted with grit washed off the verge. This is a high-frequency break-up applied
        // only inside the outermost _EdgeRavelM of carriageway.
        float ravelN = MR_RoadFbm(float2(rm.x * 1.6, rm.y * 0.9), _CrackScale);
        float ravelBand = saturate((fromEdge + _EdgeRavelM) / max(0.02, _EdgeRavelM)) * step(fromCentre, edge);
        float ravel = ravelBand * ravelBand * saturate(ravelN * 1.8 - 0.45) * _EdgeRavelAmount;
        c = lerp(c, c * 0.72 + tintGravel * 0.30, ravel);

        // ---- shoulder dirt / petal accumulation -------------------------------------------
        float shoulder = saturate((fromEdge + _ShoulderWidthM) / max(0.01, _ShoulderWidthM));
        shoulder *= saturate(0.35 + macro * 1.3);
        c = lerp(c, tintShoulder, shoulder * _ShoulderAmount);

        // ---- verge transition (section 28) -------------------------------------------------
        float grit = MR_RoadFbm(rm * 2.3, _MacroScale * 9.3);
        float outer = saturate(fromEdge / max(0.01, _VergeWidthM));
        outer = saturate((outer - 0.5) * 2.2 + 0.5 + (grit - 0.5) * _VergeBreakup * 2.0);
        float gravelBand = smoothstep(0.15, 0.62, outer);
        float soilBand = smoothstep(0.55, 1.00, outer);
        c = lerp(c, tintGravel, gravelBand * _VergeAmount * (0.55 + grit * 0.9));
        c = lerp(c, tintSoil, soilBand * _VergeAmount * (0.45 + (1.0 - grit) * 0.9));
        shoulder = max(shoulder, outer);

        // ---- wet patches -------------------------------------------------------------------
        // Standing water collects in low, polished areas: biased into the wheel tracks and
        // kept on the carriageway. Water darkens the binder underneath it.
        float wetN = MR_RoadFbm(float2(rm.x * 0.6, rm.y * 0.35), _WetScale);
        float wet = smoothstep(0.56, 0.66, wetN + track * 0.06) * _WetAmount;
        wet *= step(fromCentre, edge + 0.2);
        c = lerp(c, c * 0.52, wet);

        // ---- normal --------------------------------------------------------------------
        // No tangent is plumbed through MRVaryings, so the tangent frame is rebuilt from screen
        // derivatives of world position and UV. This is the standard derivative-frame trick; it
        // is exact enough for a near-planar swept surface and avoids re-authoring the GLB.
        float3 n = normalize(i.normalWS);
        n = frontFace ? n : -n;

        float3 dpx = ddx(i.positionWS);
        float3 dpy = ddy(i.positionWS);
        float2 dux = ddx(uv);
        float2 duy = ddy(uv);
        float det = dux.x * duy.y - duy.x * dux.y;
        if (abs(det) > 1e-12)
        {
            float3 T = (dpx * duy.y - dpy * dux.y) / det;
            T = normalize(T - n * dot(n, T));
            float3 B = normalize(cross(n, T));

            // Unpack the tangent-space normal WITHOUT UnpackNormalmapRGorAG: that helper lives
            // in Packing.hlsl, which the HDRP ShaderVariables chain does not pull in here, and
            // referencing it fails the whole shader to the error shader. Multiplying r by a
            // decodes both possible encodings of a NormalMap-imported texture:
            //   BC5      r = x, g = y, a = 1  -> r * a = x
            //   DXT5nm   r = 1, g = y, a = x  -> r * a = x
            float4 pn = SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv * _DetailTile);
            float3 nt;
            nt.xy = float2(pn.r * pn.a, pn.g) * 2.0 - 1.0;
            nt.z = sqrt(saturate(1.0 - dot(nt.xy, nt.xy)));
            // Aggregate is coarse in the open surface and polished flat in the wheel tracks.
            float bump = _DetailStrength * lerp(1.0, 0.45, track * _TrackPolish);
            nt.xy *= bump;
            float3 ng = n;
            n = normalize(T * nt.x + B * nt.y + n * nt.z);
            n = normalize(lerp(n, ng, wet));    // a water film is flat
        }

        // ---- lighting --------------------------------------------------------------------
        float3 l = MR_SunDirection();
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);

        float ndl = dot(n, l) * 0.5 + 0.5;
        float smoothD = saturate(ndl + _RampOffset);
        float banded = MR_CelRamp(smoothD, _RampSteps, _RampSmooth);
        // Photoreal road: mostly smooth response, a touch of the kit's banding so it still
        // belongs in the same world as the cel-shaded props around it.
        float ramp = lerp(smoothD, banded, _CelAmount);

        // CAST SHADOWS (was: hard-coded 1.0). Tarmac is the surface where tree and guardrail
        // shadows do the most work, so this is the single most visible consumer of the term.
        float shadeBase = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft,
                                     saturate(sunShadow));
        float dapple = MR_ValueNoise(i.positionWS.xz, _DappleScale);
        float shade = saturate(shadeBase + (1.0 - shadeBase) * dapple * _DappleStrength);
        float shadowed = ramp * shade;

        float3 lit = lerp(tintShade * _ShadeStrength, float3(1, 1, 1), shadowed);

        // Gloss: polished in the tracks, killed on the shoulder and verge.
        float gloss = saturate(_Gloss * lerp(1.0, 0.55 + macro * 0.9, _RoughVariation));
        gloss = saturate(gloss + track * _TrackPolish * 0.35);
        gloss *= saturate(1.0 - shoulder * 0.85);
        gloss = lerp(gloss, 0.97, wet);

        // Soft lobe rather than the cel shader's hard smoothstep: a stepped highlight on a
        // surface this large is the strongest remaining "toy" cue.
        float3 h = normalize(l + v);
        float spec = pow(saturate(dot(n, h)), gloss * 220.0 + 6.0) * _SpecStrength * shadowed;

        // Grazing sheen. Asphalt is strongly forward-scattering: at a 1.5 m rider's eye the
        // road ahead brightens toward the horizon. This is the cue that makes a flat ribbon
        // read as a real surface receding into the distance.
        float graze = pow(1.0 - saturate(dot(n, v)), 4.0);
        float sheen = graze * _SheenStrength * gloss * saturate(dot(n, l) * 0.5 + 0.6);

        float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength * saturate(ndl + 0.25);

        float3 sun = MR_SunColor();
        float3 col = c * lit * sun;
        col += c * MR_Ambient(n) * _AmbientStrength * lerp(_ShadowAmbient, 1.0, shade);
        col += tintSpec * spec * sun;
        col += tintSpec * sheen * sun * 0.35;
        col += tintRim * rim * lerp(_ShadowAmbient, 1.0, shade) * sun;

        if (_WetAmount > 0.0)
        {
            // Sky reflection by Fresnel (strong at a rider's grazing view) + a mirror sun glint.
            float fres = 0.04 + 0.96 * pow(1.0 - saturate(dot(n, v)), 5.0);
            col = lerp(col, MR_AuthoredCol(_WetSkyColor.rgb) * _AmbientStrength, saturate(fres * wet * 0.85));
            float glint = pow(saturate(dot(n, h)), 1400.0) * 9.0 * wet * shadowed;
            col += sun * glint;
        }

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
                return MR_ShadeRoad(i, frontFace, shadow);
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
