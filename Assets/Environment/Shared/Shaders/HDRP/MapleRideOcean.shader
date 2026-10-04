// HDRP replacement for MapleRide/SakuraWater (spec section 4.4: "HDRP Water System or
// dedicated HDRP ocean shader"). This is the dedicated-ocean route: the Built-in original
// already owned 100% of its own lighting, so porting it keeps the authored look exactly
// rather than restarting the art direction on HDRP's Water System, which is a much larger
// change and is better introduced with the section 12 ocean work.
//
// Property names and defaults are identical to the Built-in original so a material converts
// by shader swap with no loss. The Built-in shader stays on disk untouched (spec 4.1).
Shader "MapleRide/HDRP/Ocean"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.20,0.44,0.56,1)
        _DeepColor    ("Deep Color", Color) = (0.05,0.12,0.26,1)
        _SkyTint      ("Sky Reflection Tint", Color) = (0.85,0.72,0.78,1)
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 4.0
        _FresnelBoost ("Fresnel Strength", Range(0,2)) = 1.0

        _SunColor     ("Sun Glitter Color", Color) = (1,0.86,0.66,1)
        _GlitterPower ("Glitter Tightness", Range(4,512)) = 90
        _GlitterBoost ("Glitter Strength", Range(0,4)) = 1.6

        // GOLD HORIZON BAND FIX - OPT-IN, default 0 = byte-identical legacy behaviour.
        //
        // Beyond _DetailFadeEnd `detail` reaches 0 and the wave normal collapses to exactly
        // (0,1,0): a perfect mirror. A power-90 specular lobe on a perfect mirror is narrow in
        // ANGLE, but at grazing view the screen-to-angle mapping compresses enormously, so that
        // narrow lobe smears into a horizon-wide bar of _SunColor - the solid tan/gold strip and
        // the blown warm blob on the water. It is an artefact of asking for a mirror highlight
        // from waves the pixel cannot resolve.
        //
        // 1 fades glitter with the SAME distance curve that already fades the ripples (if the
        // waves are not resolvable, neither is their highlight) and additionally rolls it off as
        // the view goes grazing. Sakura Pass's lake leaves this at 0 and is unaffected.
        _GlitterFalloff ("Glitter Distance/Graze Falloff (0=legacy)", Range(0,1)) = 0
        _GlitterGrazeEnd ("Glitter Graze Fade End (dot(n,v))", Range(0.01,1)) = 0.22

        _WaveScale    ("Wave Scale", Range(0.02,4)) = 0.55
        _WaveSpeed    ("Wave Speed", Range(0,3)) = 0.35
        _WaveStrength ("Wave Normal Strength", Range(0,1)) = 0.30
        _DetailFadeStart ("Ripple Fade Start (m)", Float) = 140
        _DetailFadeEnd   ("Ripple Fade End (m)", Float) = 900

        _FoamColor    ("Foam Color", Color) = (1,1,1,1)
        _FoamAmount   ("Foam Amount", Range(0,1)) = 0.12

        // AMBIENT / GAIN (added for the Shiosai coast overhaul).
        //
        // Every other MapleRide HDRP shader lights its albedo with `sun * ndl + MR_Ambient(n)`.
        // This one was ported as `col *= lerp(0.55,1,ndl) * sun` - SUN ONLY, no ambient at all.
        // On Sakura Pass that is invisible because the lake is small, dark and read as a mirror.
        // On a 25 km coast it is fatal: the sea is the region's dominant colour mass and it was
        // rendering at roughly a sixth of its authored radiance, so ANY authored colour - navy,
        // cobalt or turquoise - collapsed to the same near-black plane. That, and not the palette,
        // is why repeatedly re-tinting the water never changed the picture.
        //
        // Both default to the pre-existing behaviour (no ambient, unity gain) so Sakura Pass's
        // water is byte-identical; only the coast material opts in.
        _AmbientWeight ("Ambient Weight", Range(0,2)) = 0.0
        _LightGain     ("Light Gain", Range(0.1,6)) = 1.0

        // DEPTH GRADIENT (added for the Shiosai coast overhaul, milestone M2).
        //
        // The original blended _DeepColor -> _ShallowColor by `facing` = dot(n, view). On a lake
        // that is a serviceable stand-in for depth. On an open coast it is not: from the saddle
        // and from the clifftop road almost every visible square metre of sea is at a grazing
        // angle, so `facing` pins to ~0 and the WHOLE bay resolves to _DeepColor. That - and not
        // the palette - is why the sea reads as one flat mid-blue slab however the two colours
        // are re-tinted. Every reference render instead shows a bright turquoise inshore band
        // grading to cobalt offshore, i.e. a gradient in DISTANCE, not in view angle.
        //
        // _DepthBlend is the opt-in weight between the two models. It defaults to 0, which is
        // byte-identical to the previous behaviour, so Sakura Pass's lake (which shares this
        // shader) is untouched; only the coast material opts in.
        _DepthBlend      ("Depth Gradient Weight", Range(0,1)) = 0.0
        _ShoreFadeStart  ("Turquoise Reach (m)", Float) = 90
        _ShoreFadeEnd    ("Cobalt Reach (m)", Float) = 1800
        _MidColor        ("Mid Water Color", Color) = (0.10,0.36,0.62,1)

        // MULTI-SCALE CHOP (ocean-realism milestone). OPT-IN, default 0 = byte-identical
        // legacy behaviour, so Sakura Pass's lake is untouched.
        //
        // The wave normal was three swell-scale trains and nothing else, so within ~200 m of
        // the eye - where a real sea shows centimetre chop catching the sun - the surface was a
        // smooth, almost mirror-flat sheet and the glitter collapsed into a few soft blobs.
        // These three extra octaves run 6-14x the swell frequency at a fraction of its
        // amplitude and are faded by the SAME `detail` term that fades the swell, so they only
        // ever exist where a pixel can resolve them and can never alias into moire.
        _ChopWeight   ("Micro Chop Weight (0=legacy)", Range(0,1)) = 0
        _ChopScale    ("Micro Chop Frequency x", Range(2,24)) = 8.0
        _ChopStrength ("Micro Chop Amplitude", Range(0,1)) = 0.22
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _ShallowColor, _DeepColor, _SkyTint, _SunColor, _FoamColor;
        float _FresnelPower, _FresnelBoost;
        float _GlitterPower, _GlitterBoost;
    float _GlitterFalloff, _GlitterGrazeEnd;
        float _WaveScale, _WaveSpeed, _WaveStrength;
        float _FoamAmount;
        float _DetailFadeStart, _DetailFadeEnd;
        float _AmbientWeight, _LightGain;
        float4 _MidColor;
        float _DepthBlend, _ShoreFadeStart, _ShoreFadeEnd;
        float _ChopWeight, _ChopScale, _ChopStrength;
    CBUFFER_END

    // Three rotated, non-harmonic wave trains. Two axis-aligned trains of the same frequency
    // interfere into a perfect lattice, which read as regular polka-dot foam across the whole
    // water body; off-axis directions with irrational frequency ratios keep the crests from
    // ever lining up.
    //
    // DEVIATION from the Built-in original: that version returned a TANGENT-space normal
    // (+Z = up) because surface shaders demand it. A hand-written HDRP pass has no tangent
    // frame, so the gradient is assembled directly in world space instead. The water plane is
    // horizontal, which makes the mapping exact rather than an approximation.
    float3 MR_RippleNormalWS(float2 p)
    {
        float t = _TimeParameters.x * _WaveSpeed;
        const float freq[3] = { 1.00, 1.63, 2.41 };
        const float ang[3]  = { 0.35, 1.97, 3.74 };
        const float amp[3]  = { 1.00, 0.55, 0.26 };

        float2 grad = float2(0, 0);
        for (int i = 0; i < 3; i++)
        {
            float2 d = float2(cos(ang[i]), sin(ang[i]));
            float k = _WaveScale * freq[i];
            grad += d * cos(dot(p, d) * k + t * (0.8 + 0.35 * i)) * k * amp[i];
        }
        float2 g = -grad * _WaveStrength;
        return normalize(float3(g.x, 1.0, g.y));
    }

    /// Swell + (opt-in) micro chop. See _ChopWeight. `detail` is the shader's existing
    /// "can this pixel resolve a wave?" term; the chop octaves are multiplied by it so they
    /// simply do not exist where they would alias.
    float3 MR_OceanNormalWS(float2 p, float detail)
    {
        float3 n = MR_RippleNormalWS(p);
        if (_ChopWeight <= 0.0001) return n;

        // CHOP MUST DIE MUCH FASTER THAN SWELL. `detail` is tuned for the swell wavelength;
        // reused raw it left chop alive out to ~900 m, where one pixel spans many chop
        // wavelengths and the sea moired into a visible crosshatch across the whole bay.
        // Biasing by ^8 collapses chop to nothing by roughly the swell's fade START.
        float chopDetail = detail * detail; chopDetail *= chopDetail; chopDetail *= chopDetail;
        if (chopDetail <= 0.0005) return n;

        float t = _TimeParameters.x * _WaveSpeed;
        const float freq[3] = { 1.00, 1.71, 2.63 };
        const float ang[3]  = { 2.60, 0.90, 4.80 };
        const float amp[3]  = { 1.00, 0.52, 0.27 };

        float2 grad = float2(0, 0);
        [unroll] for (int i = 0; i < 3; i++)
        {
            float2 d = float2(cos(ang[i]), sin(ang[i]));
            float k = _WaveScale * _ChopScale * freq[i];
            grad += d * cos(dot(p, d) * k + t * (2.1 + 0.7 * i)) * k * amp[i];
        }
        float2 g = -grad * _WaveStrength * _ChopStrength * chopDetail * _ChopWeight;
        // Perturb the swell normal rather than replacing it, so the long waves keep their shape.
        return normalize(float3(n.x + g.x, n.y, n.z + g.y));
    }

    MRVaryings VertOcean(MRAttributes input)
    {
        // Same gentle vertical swell as the Built-in vertex function, evaluated in absolute
        // world space so the swell does not travel with the camera in HDRP's camera-relative
        // world.
        UNITY_SETUP_INSTANCE_ID(input);
        float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
        float3 wp = GetAbsolutePositionWS(positionRWS);
        float t = _TimeParameters.x * _WaveSpeed;
        float wave = sin(wp.x * _WaveScale * 0.35 + t) * 0.5
                   + sin(wp.z * _WaveScale * 0.51 + t * 1.3) * 0.3;

        MRVaryings o;
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        positionRWS.y += wave * 0.06;
        // MRVaryings.positionRWS feeds MR_ApplyFog. Every custom vertex function must write it:
        // an unwritten interpolator produces garbage fog distance with no compiler diagnostic,
        // which rendered the sea milky-pale from one angle and pure black from another.
        o.positionRWS = positionRWS;
        o.positionCS = TransformWorldToHClip(positionRWS);
        o.positionWS = GetAbsolutePositionWS(positionRWS);
        o.normalWS = float3(0, 1, 0);
        o.uv = input.uv;
        o.color = input.color;
        return o;
    }

    float4 MR_ShadeOcean(MRVaryings i)
    {
        // Ripple detail decays with view distance: the normal is evaluated analytically from
        // world XZ, so at grazing angles a single pixel spans many wave periods and the
        // specular aliases into moire stripes across the horizon. There is no mip chain to
        // rescue it; the only fix is to stop asking for detail the pixel cannot resolve.
        float3 camWS = GetAbsolutePositionWS(GetPrimaryCameraPosition());
        float dist = distance(i.positionWS, camWS);
        float detail = saturate(1.0 - (dist - _DetailFadeStart)
                                / max(1.0, _DetailFadeEnd - _DetailFadeStart));

        float3 n = normalize(lerp(float3(0, 1, 0), MR_OceanNormalWS(i.positionWS.xz, detail), detail));
        float3 v = normalize(camWS - i.positionWS);
        float3 l = MR_SunDirection();

        // Foam picks out the ripple crests. In world space the crest term is the up component
        // of the perturbed normal, which is what the tangent-space .z meant in the original.
        float crest = saturate(n.y);
        float foam = smoothstep(1.0 - _FoamAmount, 1.0, 1.0 - crest) * detail;
        float3 albedo = lerp(MR_AuthoredCol(_ShallowColor.rgb), MR_AuthoredCol(_FoamColor.rgb), foam);

        float facing = saturate(dot(n, v));
        float fres = pow(1.0 - facing, _FresnelPower) * _FresnelBoost;

        // Two competing models for the body colour (see _DepthBlend in the property block):
        //   viewBody  - the original view-angle blend, kept for Sakura Pass's lake.
        //   depthBody - a three-stop gradient in DISTANCE: turquoise inshore, cobalt in the
        //               middle ground, deep blue offshore. This is the coast's model.
        float3 viewBody = lerp(MR_AuthoredCol(_DeepColor.rgb), albedo, facing);

        float depthT = saturate((dist - _ShoreFadeStart)
                                / max(1.0, _ShoreFadeEnd - _ShoreFadeStart));
        depthT = depthT * depthT * (3.0 - 2.0 * depthT);          // smoothstep, no hard edge
        float3 nearWater = lerp(MR_AuthoredCol(_MidColor.rgb), albedo, saturate(1.0 - depthT * 2.0));
        float3 farWater  = lerp(MR_AuthoredCol(_MidColor.rgb), MR_AuthoredCol(_DeepColor.rgb),
                                saturate(depthT * 2.0 - 1.0));
        float3 depthBody = depthT < 0.5 ? nearWater : farWater;
        // Foam still wins wherever a crest breaks, at any distance.
        depthBody = lerp(depthBody, MR_AuthoredCol(_FoamColor.rgb), foam);

        float3 body = lerp(viewBody, depthBody, _DepthBlend);
        float3 col = lerp(body, MR_AuthoredCol(_SkyTint.rgb), saturate(fres));

        float3 h = normalize(l + v);
        float glit = pow(saturate(dot(n, h)), _GlitterPower) * _GlitterBoost;

        // See _GlitterFalloff. `detail` is already the "can this pixel resolve a wave?" term,
        // so reusing it keeps the highlight exactly where real ripples still exist; the graze
        // term then kills what is left along the horizon line itself. lerp() against 1.0 means
        // _GlitterFalloff = 0 compiles out to the original expression.
        float grazeFade = smoothstep(0.0, _GlitterGrazeEnd, facing);
        glit *= lerp(1.0, detail * grazeFade, _GlitterFalloff);

        float ndl = saturate(dot(n, l) * 0.5 + 0.5);
        float3 sun = MR_SunColor();
        // Sun wrap term + (opt-in) ambient, then a gain. See the property block: without the
        // ambient the sea is lit by the sun alone and reads far darker than any other surface.
        float3 lighting = (lerp(0.55, 1.0, ndl) * sun
                          + MR_Ambient(float3(0, 1, 0)) * _AmbientWeight) * _LightGain;
        col *= lighting;
        // The original multiplied the glitter by `atten`. Cast shadows are not available to a
        // hand-written HDRP pass yet (documented PENDING gap); the term is fully lit for now.
        col += MR_AuthoredCol(_SunColor.rgb) * glit * sun;

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

            HLSLPROGRAM
            #pragma vertex VertOcean
            #pragma fragment Frag
            #pragma multi_compile_instancing
            float4 Frag(MRVaryings i) : SV_Target { return MR_ShadeOcean(i); }
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex VertOcean
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            void FragDepth(MRVaryings i) { }
            ENDHLSL
        }
    }

    Fallback Off
}
