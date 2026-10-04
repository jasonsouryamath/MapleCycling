// Part B section 26: the road is a HERO ASSET - it occupies a huge share of the screen, so it
// gets its own shader rather than sharing the generic environment cel material.
//
// The lighting half is deliberately identical to MapleRide/SakuraCel (same banded ramp, same
// shader-owned shadow-occluded ambient from section 24) so the road sits in the same light as
// everything around it. What this adds is the section-26 layer stack:
//
//     Base Asphalt  +  Macro Colour Variation  +  Detail Normal
//                   +  Roughness Variation     +  Resurfacing Patches
//                   +  Wheel-Track Darkening   +  Shoulder Dirt / Petal Mask
//
// Repetition is broken WITHOUT a second albedo asset: the base texture is re-sampled at a very
// large, non-integer-related scale and used as a low-frequency luminance modulator. That is the
// standard macro-variation trick and it costs one extra sample instead of a new 4K texture.
//
// Cross-road position is recovered from UV: build_road.py sweeps the carriageway with
// u = (cumulative metres across the cross-section) * ASPHALT_TILE, so metresAcross = u / 0.22.
// _UvPerMetre MUST track ASPHALT_TILE in build_road.py or every mask below slides sideways.
Shader "MapleRide/SakuraRoad"
{
    Properties
    {
        _Color            ("Base Color", Color) = (1,1,1,1)
        _MainTex          ("Albedo", 2D) = "white" {}
        _DetailNormal     ("Detail Normal", 2D) = "bump" {}
        _DetailTile       ("Detail Normal Tile", Float) = 7.0
        _DetailStrength   ("Detail Normal Strength", Range(0,2)) = 0.55

        // --- section 26 layer stack (ALL PROVISIONAL tuning) ---
        _MacroScale       ("Macro Variation Scale", Float) = 0.031
        _MacroAmount      ("Macro Variation Amount", Range(0,1)) = 0.30
        _PatchScale       ("Resurfacing Patch Scale", Float) = 0.013
        _PatchAmount      ("Resurfacing Patch Amount", Range(0,1)) = 0.22
        _PatchColor       ("Resurfacing Patch Tint", Color) = (0.86,0.84,0.86,1)
        _RoughVariation   ("Roughness Variation", Range(0,1)) = 0.45

        _UvPerMetre       ("UV Per Metre (= ASPHALT_TILE)", Float) = 0.22
        // Measured u-metre of the carriageway centreline. build_road.py derives u from the
        // cumulative arc of the FULL cross-section (skirt|shoulder|lanes|shoulder|skirt), so
        // u = 0 is the outer skirt, not the road edge. Half-span = 5.7824 m of profile arc.
        _UvCentreM        ("Carriageway Centre In UV Metres", Float) = 5.7824
        _RoadWidthM       ("Carriageway Width (m)", Float) = 7.0
        _TrackOffsetM     ("Wheel Track Offset From Lane Centre (m)", Float) = 0.80
        // QA fix #3: 0.16 over a 0.55 m band read as barely visible at gameplay camera distance.
        // Widened and darkened so the worn-wheel-track cue actually reads without turning into a
        // solid black stripe (macro-noise modulation below still breaks it up into a worn look).
        _TrackWidthM      ("Wheel Track Width (m)", Float) = 0.70
        _TrackDarken      ("Wheel Track Darkening", Range(0,1)) = 0.30

        _ShoulderWidthM   ("Shoulder Band Width (m)", Float) = 0.75
        _ShoulderColor    ("Shoulder Dirt / Petal Tint", Color) = (0.62,0.50,0.50,1)
        _ShoulderAmount   ("Shoulder Amount", Range(0,1)) = 0.55

        // --- section 28: transitional shoulder (ALL PROVISIONAL) ---
        // Measured from the road edge outward. The carriageway half-width is 3.5 m and the
        // swept shoulder runs to 4.05 m, so this band covers the chip seal and the start of
        // the buried skirt - the last asphalt-side surface before the terrain takes over.
        _VergeWidthM      ("Verge Transition Width (m)", Float) = 1.45
        _VergeGravelColor ("Verge Gravel Tint", Color) = (0.60,0.56,0.53,1)
        _VergeSoilColor   ("Verge Soil Tint", Color) = (0.40,0.33,0.27,1)
        _VergeAmount      ("Verge Amount", Range(0,1)) = 0.80
        _VergeBreakup     ("Verge Edge Breakup", Range(0,1)) = 0.42

        // --- cel lighting (mirrors SakuraCel) ---
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
        _AmbientStrength  ("Ambient Strength", Range(0,2)) = 1.0
        _ShadowAmbient    ("Ambient Kept In Shadow", Range(0,1)) = 0.38
        _ShadowEdge       ("Shadow Step Midpoint", Range(0.05,0.95)) = 0.5
        // Section 24 polish: dedicated cast-shadow softness (see SakuraCel). PROVISIONAL.
        _ShadowSoft       ("Shadow Step Softness", Range(0.01,0.45)) = 0.20
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull [_Cull]

        CGPROGRAM
        // 'noambient' is load-bearing - see the section-24 note in SakuraCel.shader. Without it
        // the framework adds ambient unattenuated and canopy dapple on the road vanishes.
        #pragma surface surf SakuraCel fullforwardshadows exclude_path:deferred noambient
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _DetailNormal;
        half _DetailTile, _DetailStrength;
        fixed4 _Color;
        half _MacroScale, _MacroAmount;
        half _PatchScale, _PatchAmount;
        fixed4 _PatchColor;
        half _RoughVariation;
        half _UvPerMetre, _RoadWidthM, _UvCentreM;
        half _TrackOffsetM, _TrackWidthM, _TrackDarken;
        half _ShoulderWidthM, _ShoulderAmount;
        fixed4 _ShoulderColor;
        half _VergeWidthM, _VergeAmount, _VergeBreakup;
        fixed4 _VergeGravelColor, _VergeSoilColor;
        fixed4 _ShadeColor;
        half _ShadeStrength, _RampSteps, _RampSmooth, _RampOffset;
        fixed4 _RimColor;
        half _RimPower, _RimStrength;
        fixed4 _SpecTint;
        half _Gloss, _SpecStrength;
        half _AmbientStrength, _ShadowAmbient, _ShadowEdge, _ShadowSoft;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 viewDir;
        };

        half CelRamp(half value)
        {
            half steps = max(2.0, _RampSteps);
            half scaled = saturate(value) * steps;
            half band = floor(scaled);
            half f = scaled - band;
            half soft = smoothstep(0.5 - _RampSmooth, 0.5 + _RampSmooth, f);
            return saturate((band + soft) / steps);
        }

        half4 LightingSakuraCel(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half3 n = normalize(s.Normal);
            half3 v = normalize(viewDir);
            half ndl = dot(n, lightDir) * 0.5 + 0.5;
            half ramp = CelRamp(saturate(ndl + _RampOffset));
            // Never CelRamp the shadow term - see SakuraCel.shader for why that erased the dapple.
            half shade = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft, atten);
            half shadowed = ramp * shade;

            half3 lit = lerp(_ShadeColor.rgb * _ShadeStrength, half3(1,1,1), shadowed);

            // s.Specular carries the per-pixel roughness variation written in surf(), so wet-look
            // sheen breaks up along the carriageway instead of being one uniform sheet.
            half3 h = normalize(lightDir + v);
            half spec = pow(saturate(dot(n, h)), s.Specular * 128.0 + 1.0);
            spec = smoothstep(0.35, 0.45, spec) * _SpecStrength * s.Gloss * shadowed;

            half rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
            rim *= _RimStrength * saturate(ndl + 0.25);

            half3 col = s.Albedo * lit * _LightColor0.rgb;
            // Additive lights contribute attenuated diffuse only - see SakuraCel.shader for why
            // re-adding the shade floor, ambient and rim per light paints a screen-space slab.
            #ifdef UNITY_PASS_FORWARDADD
                return half4(s.Albedo * ramp * atten * _LightColor0.rgb, s.Alpha);
            #endif
            half3 amb = ShadeSH9(half4(n, 1)) * _AmbientStrength * lerp(_ShadowAmbient, 1.0, shade);
            col += s.Albedo * amb;
            col += _SpecTint.rgb * spec * _LightColor0.rgb;
            col += _RimColor.rgb * rim * lerp(_ShadowAmbient, 1.0, shade) * _LightColor0.rgb;
            return half4(col, s.Alpha);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 uv = IN.uv_MainTex;
            fixed3 c = tex2D(_MainTex, uv).rgb * _Color.rgb;

            // --- macro colour variation: the base texture re-read at a large, deliberately
            // non-harmonic scale. Breaks the "one obvious repeating tile" read section 26 calls out.
            half macro = tex2D(_MainTex, uv * _MacroScale).g;
            c *= lerp(1.0, 0.72 + macro * 0.72, _MacroAmount);

            // --- resurfacing patches: a second, even larger read, thresholded into soft slabs.
            half patch = smoothstep(0.44, 0.60, tex2D(_MainTex, uv * _PatchScale).r);
            c = lerp(c, c * _PatchColor.rgb, patch * _PatchAmount);

            // --- cross-road masks.
            //
            // CORRECTION (section 28): xm is NOT 0 at the carriageway edge. build_road.py sweeps
            // the profile [outer skirt | shoulder | lanes | shoulder | outer skirt] and derives u
            // from cumulative profile arc length, so u = 0 sits at the OUTER SKIRT, 2.28 m
            // outboard of the carriageway. The original `centre = _RoadWidthM * 0.5` therefore
            // put the mask origin 2.28 m off, which shifted every wheel track sideways and
            // smeared the shoulder tint across the whole right-hand lane (visible as an
            // asymmetric lighter half-road in the section 26 benchmark captures).
            //
            // _UvCentreM is the measured u-metre of the carriageway centreline and MUST be
            // recomputed if the cross-section in build_road.py changes.
            half xm = uv.x / max(0.0001, _UvPerMetre);
            half fromCentre = abs(xm - _UvCentreM);
            half edge = _RoadWidthM * 0.5;          // carriageway half-width, 3.5 m
            half fromEdge = fromCentre - edge;      // <0 on the lanes, >0 on shoulder/verge
            half laneCentre = _RoadWidthM * 0.25;   // centre of each lane

            // Wheel tracks: two darkened bands per lane, at +/- _TrackOffsetM about the lane centre.
            half track = 0;
            track = max(track, 1.0 - saturate(abs(fromCentre - (laneCentre - _TrackOffsetM)) / _TrackWidthM));
            track = max(track, 1.0 - saturate(abs(fromCentre - (laneCentre + _TrackOffsetM)) / _TrackWidthM));
            // Modulated by macro noise so the tracks are worn, not two perfect stripes.
            c *= 1.0 - track * _TrackDarken * (0.6 + macro * 0.8);

            // Shoulder dirt / accumulated petals: the last _ShoulderWidthM of carriageway before
            // the edge line, noise-broken.
            half shoulder = saturate((fromEdge + _ShoulderWidthM) / max(0.01, _ShoulderWidthM));
            shoulder *= saturate(0.35 + macro * 1.3);
            c = lerp(c, _ShoulderColor.rgb, shoulder * _ShoulderAmount);

            // --- section 28: transitional shoulder ------------------------------------------
            // The single lerp above is a clean, constant-width tint, which is precisely the
            // "asphalt ends -> grass begins" line section 28 forbids. Two stops are added
            // OUTBOARD of the carriageway edge: a gravel/chip band, then a damp-soil band,
            // both with their boundary chewed up by a second, higher-frequency noise read so
            // no straight edge survives anywhere across the transition.
            half grit = tex2D(_MainTex, uv * (_MacroScale * 9.3) + half2(0.37, 0.71)).g;
            half outer = saturate(fromEdge / max(0.01, _VergeWidthM));
            // Chew the boundary: offset the ramp by noise, then re-sharpen it.
            outer = saturate((outer - 0.5) * 2.2 + 0.5 + (grit - 0.5) * _VergeBreakup * 2.0);
            half gravelBand = smoothstep(0.15, 0.62, outer);
            half soilBand = smoothstep(0.55, 1.00, outer);
            c = lerp(c, _VergeGravelColor.rgb, gravelBand * _VergeAmount * (0.55 + grit * 0.9));
            c = lerp(c, _VergeSoilColor.rgb, soilBand * _VergeAmount * (0.45 + (1.0 - grit) * 0.9));
            shoulder = max(shoulder, outer);

            o.Albedo = c;
            o.Alpha = 1;

            // --- micro detail normal at a completely different tile rate. This is the single
            // biggest cue that the surface has aggregate rather than being a flat painted plane.
            half3 dn = UnpackNormal(tex2D(_DetailNormal, uv * _DetailTile));
            o.Normal = normalize(half3(dn.xy * _DetailStrength, 1.0));

            // --- roughness variation, carried to the lighting function. Damp in the wheel tracks
            // (polished by traffic) and rough on the shoulder (dirt and petals).
            o.Specular = saturate(_Gloss * lerp(1.0, 0.45 + macro, _RoughVariation) + track * 0.25);
            o.Gloss = saturate(1.0 - shoulder * 0.8);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
