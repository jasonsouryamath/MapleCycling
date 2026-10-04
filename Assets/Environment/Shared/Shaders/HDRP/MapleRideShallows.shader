// HDRP shallow-water layer (spec section 4.4: "HDRP shallow-water layer integrated with main
// ocean - turquoise depth gradient, foam, shoreline blend, no z-fighting").
//
// The shelf is an additive piece of geometry laid over the inshore strip of the ocean plane.
// It never writes depth, so it cannot z-fight the sea.
//
// WHAT CHANGED IN THE OCEAN-REALISM MILESTONE, AND WHY
// ---------------------------------------------------
// The shelf used to carry a gradient in VERTEX ALPHA that was a pure geometric ramp: 1 at the
// waterline, 0 at a fixed 85 m seaward, squared. That is a stripe of constant width painted
// along the coast - it has no idea where the water is actually shallow. The art-director
// reference (ShiosaiCoast_06.png) shows the opposite: the turquoise follows the SAND, so it
// pools wide across a shelf and pinches to nothing where a headland drops away steeply.
//
// The builder now sweeps the shelf over the cross-section's real SEA FLOOR nodes and bakes the
// true water depth into VERTEX COLOUR:
//     color.r = depth / _DepthScale metres (0 at the waterline)
//     color.a = shelf presence (Seaness x outer-edge fade), so the shelf still collapses to
//               nothing in the inland chapters exactly as before.
// Everything below - body colour, surf, transparency - is then a function of DEPTH, which is
// what makes the band follow the coastline instead of tracing it.
//
// Legacy behaviour is still reachable: _DepthDriven = 0 restores the old vertex-alpha ramp
// byte-for-byte, so any other region that ever picks this shader up is unaffected.
Shader "MapleRide/HDRP/Shallows"
{
    Properties
    {
        _ShoreColor  ("Shore Colour", Color)      = (0.36,0.86,0.84,1)
        _ShelfColor  ("Shelf Colour", Color)      = (0.08,0.55,0.66,1)
        _FoamColor   ("Foam Colour", Color)       = (1,1,1,1)
        _FoamWidth   ("Foam Width", Range(0,1))   = 0.18
        _RippleScale ("Ripple Scale", Float)      = 0.35
        _RippleSpeed ("Ripple Speed", Float)      = 0.45
        _Opacity     ("Opacity", Range(0,1))      = 0.85

        // ---- depth-driven model (0 = legacy vertex-alpha ramp) ----------------------------
        _DepthDriven ("Depth Driven (0=legacy)", Range(0,1)) = 0
        // Metres of depth encoded by a full vertex-colour red channel.
        _DepthScale  ("Depth Encode Scale (m)", Float) = 20
        // Water over bright sand: the pale aqua right at the waterline.
        _SandColor   ("Sand Shallow Colour", Color) = (0.60,0.92,0.90,1)
        // Depth at which the bright turquoise peaks, metres.
        _TurquoiseDepth ("Turquoise Depth (m)", Float) = 1.6
        // Depth at which the shelf has become the open sea's teal, metres.
        _ShelfDepth  ("Shelf Depth (m)", Float) = 7.0
        // Depth at which the shelf is fully transparent (hands over to the ocean), metres.
        _FadeDepth   ("Handover Depth (m)", Float) = 13.0

        // ---- surf ---------------------------------------------------------------------------
        // Depth below which broken water / surf can appear, metres.
        _SurfDepth   ("Surf Depth (m)", Float) = 1.25
        _SurfSpeed   ("Surf Speed", Float) = 0.55
        _SurfAmount  ("Surf Strength", Range(0,1)) = 0.85

        // ---- sparkle ------------------------------------------------------------------------
        _SunColor    ("Sun Glitter Colour", Color) = (1,0.97,0.88,1)
        _GlitterPower("Glitter Tightness", Range(4,512)) = 140
        _GlitterBoost("Glitter Strength", Range(0,4)) = 0.9
        // Multi-scale wave normal strength on the shelf.
        _WaveStrength("Wave Normal Strength", Range(0,1)) = 0.22
        _WaveScale   ("Wave Scale", Float) = 0.55
        _DetailFadeStart ("Ripple Fade Start (m)", Float) = 160
        _DetailFadeEnd   ("Ripple Fade End (m)", Float) = 1400
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _ShoreColor, _ShelfColor, _FoamColor;
        float _FoamWidth, _RippleScale, _RippleSpeed, _Opacity;
        float _DepthDriven, _DepthScale;
        float4 _SandColor;
        float _TurquoiseDepth, _ShelfDepth, _FadeDepth;
        float _SurfDepth, _SurfSpeed, _SurfAmount;
        float4 _SunColor;
        float _GlitterPower, _GlitterBoost;
        float _WaveStrength, _WaveScale;
        float _DetailFadeStart, _DetailFadeEnd;
    CBUFFER_END

    // MULTI-SCALE wave normal. Six rotated, non-harmonic trains in two octave groups: three
    // swell-scale trains that survive to the horizon and three chop-scale trains that only
    // exist where the pixel can still resolve them. Two axis-aligned trains of one frequency
    // interfere into a lattice (visible as regular polka dots); off-axis directions with
    // irrational frequency ratios never line up.
    float3 MR_ShelfNormalWS(float2 p, float detail)
    {
        float t = _TimeParameters.x * _RippleSpeed;
        const float freq[6] = { 1.00, 1.63, 2.41, 5.90, 9.37, 14.30 };
        const float ang[6]  = { 0.35, 1.97, 3.74, 2.60, 0.90, 4.80 };
        const float amp[6]  = { 1.00, 0.55, 0.26, 0.085, 0.045, 0.022 };

        float2 grad = float2(0, 0);
        [unroll] for (int i = 0; i < 6; i++)
        {
            // The chop octaves (i >= 3) are the ones that alias; fade them, not the swell.
            float w = amp[i] * (i < 3 ? 1.0 : detail);
            float2 d = float2(cos(ang[i]), sin(ang[i]));
            float k = _WaveScale * freq[i];
            grad += d * cos(dot(p, d) * k + t * (0.8 + 0.35 * i)) * k * w;
        }
        float2 g = -grad * _WaveStrength;
        return normalize(float3(g.x, 1.0, g.y));
    }
    ENDHLSL

    SubShader
    {
        // Transparent, queued just ahead of the default transparent band so props standing in
        // the water (sea stacks, the harbour mole) still sort in front of it.
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "Queue" = "Transparent-100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            // HDRP draws transparent materials through the ForwardOnly pass as well; the queue
            // tag is what puts it in the transparent phase.
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            ZWrite Off
            Cull Off                // the rider can look at the shelf edge-on from the clifftop road
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex MR_Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            float4 Frag(MRVaryings i) : SV_Target
            {
                float3 camWS = GetAbsolutePositionWS(GetPrimaryCameraPosition());
                float viewDist = distance(i.positionWS, camWS);
                float detail = saturate(1.0 - (viewDist - _DetailFadeStart)
                                        / max(1.0, _DetailFadeEnd - _DetailFadeStart));

                // ---- LEGACY MODEL: geometric ramp in vertex alpha ---------------------------
                float shore = saturate(i.color.a);
                float t0 = _TimeParameters.x * _RippleSpeed;
                float w = sin(i.positionWS.x * _RippleScale + t0)
                        + sin(i.positionWS.z * _RippleScale * 0.77 - t0 * 1.3);
                float legacyShore = saturate(shore + w * 0.035);
                float3 legacyCol = lerp(MR_AuthoredCol(_ShelfColor.rgb),
                                        MR_AuthoredCol(_ShoreColor.rgb),
                                        legacyShore * legacyShore);
                float legacyFoam = smoothstep(1.0 - _FoamWidth, 1.0, legacyShore);
                legacyCol = lerp(legacyCol, MR_AuthoredCol(_FoamColor.rgb), legacyFoam * 0.75);
                float legacyA = max(legacyShore * _Opacity, legacyFoam);

                // ---- DEPTH MODEL -----------------------------------------------------------
                // color.r carries true water depth in metres / _DepthScale; color.a carries
                // shelf presence (Seaness x outer fade), which still collapses the shelf inland.
                float depth = i.color.r * _DepthScale;
                float present = saturate(i.color.a);

                // Three-stop body gradient by DEPTH: pale aqua over bare sand at the waterline,
                // bright turquoise over the shallow shelf, the open sea's teal at the handover.
                float tT = saturate(depth / max(0.05, _TurquoiseDepth));
                float tS = saturate((depth - _TurquoiseDepth)
                                    / max(0.05, _ShelfDepth - _TurquoiseDepth));
                float3 body = lerp(MR_AuthoredCol(_SandColor.rgb), MR_AuthoredCol(_ShoreColor.rgb), tT);
                body = lerp(body, MR_AuthoredCol(_ShelfColor.rgb), tS * tS * (3.0 - 2.0 * tS));

                // SURF. Shallow water breaks, and it breaks in MOVING BANDS, not in a stripe:
                // two crossing swells push the break line in and out, so where the beach is flat
                // the foam sheet runs wide and where the shelf drops away it pinches out.
                float ts = _TimeParameters.x * _SurfSpeed;
                float swell = sin(i.positionWS.x * 0.055 + ts * 1.7)
                            + 0.7 * sin(i.positionWS.z * 0.041 - ts * 1.25)
                            + 0.45 * sin((i.positionWS.x + i.positionWS.z) * 0.021 + ts * 0.8);
                float breakDepth = _SurfDepth * (1.0 + 0.35 * swell);
                float surf = saturate(1.0 - depth / max(0.05, breakDepth));
                surf = surf * surf * _SurfAmount;
                // A crisp wet-sand line right at the waterline, always present.
                surf = max(surf, smoothstep(0.30, 0.02, depth) * 0.9);
                body = lerp(body, MR_AuthoredCol(_FoamColor.rgb), saturate(surf));

                // Transparency: opaque inshore, handing over to the ocean plane by _FadeDepth.
                float fade = 1.0 - smoothstep(_ShelfDepth, _FadeDepth, depth);
                float depthA = max(saturate(fade) * _Opacity, saturate(surf)) * present;

                // ---- SPARKLE ----------------------------------------------------------------
                float3 n = MR_ShelfNormalWS(i.positionWS.xz, detail);
                float3 v = normalize(camWS - i.positionWS);
                float3 l = MR_SunDirection();
                float3 h = normalize(l + v);
                // Glitter only where waves are still resolvable, and not on the foam (broken
                // water is diffuse, not specular) - otherwise the surf line turns into a
                // continuous blown highlight.
                float glit = pow(saturate(dot(n, h)), _GlitterPower) * _GlitterBoost
                             * detail * (1.0 - saturate(surf));

                float3 col = lerp(legacyCol, body, _DepthDriven);
                float a = lerp(legacyA, depthA, _DepthDriven);

                // DEVIATION: the Built-in original lit this with `unity_AmbientSky`, which HDRP
                // does not populate. MR_Ambient sampled straight up is the same quantity - the
                // sky band of the authored trilight ambient - so the shelf keeps its brightness
                // relationship to the beach and ocean either side of it.
                float3 ambientSky = MR_Ambient(float3(0, 1, 0));
                float3 lit = col * ambientSky * 1.6 + col * 0.35;
                lit += MR_AuthoredCol(_SunColor.rgb) * glit * MR_SunColor() * _DepthDriven;

                return float4(MR_ToRender(MR_ApplyFog(lit, i.positionRWS)) * GetCurrentExposureMultiplier(), a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
