// HDRP replacement for MapleRide/SakuraCel (spec section 4.4).
//
// Property names, defaults and cel maths are deliberately IDENTICAL to the Built-in
// original, so converting a material is a shader swap that preserves every authored
// value. The Built-in shader is intentionally left on disk and untouched (spec 4.1).
//
// SHADOWS: closed. The first HDRP pass could not receive cast shadows - the Built-in version
// got its shadow term from the surface framework's `atten`, which does not exist in a
// hand-written HDRP pass. That term now comes from MapleRideHDRPShadow.hlsl, which samples
// HDRP's own directional cascade in the ForwardOnly pass exactly the way HDRP's Unlit
// "Shadow Matte" and HDRISky backplate do. _ShadowEdge/_ShadowSoft shape the result into a cel
// band; _DappleScale/_DappleStrength still break up the shadowed side.
Shader "MapleRide/HDRP/CelLitMacro"
{
    Properties
    {
        _Color            ("Base Color", Color) = (1,1,1,1)
        _MainTex          ("Albedo", 2D) = "white" {}
        // Neutral minimum for the albedo AFTER tint. Default (0,0,0) is a no-op, so every
        // environment material is byte-for-byte unchanged. A CHARACTER whose kit atlas is authored
        // near pure-black (Kuro's jersey/helmet/gloves are ~0.05 sRGB) collapses to a flat black
        // silhouette on its shadowed/backlit side, because every lighting term here is
        // albedo-multiplied. Lifting the floor a little gives matte black a real dark-grey value
        // that still catches diffuse + ambient and reads as FORM, matching the reference sheet.
        _MatteFloor       ("Matte Albedo Floor", Color) = (0,0,0,1)
        _ShadeColor       ("Shade Tint", Color) = (0.42,0.48,0.68,1)
        _ShadeStrength    ("Shade Strength", Range(0,1)) = 0.72
        _RampSteps        ("Ramp Steps", Range(2,6)) = 3
        _RampSmooth       ("Ramp Softness", Range(0.002,0.35)) = 0.05
        _RampOffset       ("Ramp Offset", Range(-0.5,0.5)) = 0.0
        _RimColor         ("Rim Color", Color) = (1,0.72,0.52,1)
        _RimPower         ("Rim Power", Range(0.5,10)) = 3.0
        _RimStrength      ("Rim Strength", Range(0,3)) = 0.85
        _SpecTint         ("Specular Tint", Color) = (1,0.93,0.85,1)
        _Gloss            ("Gloss", Range(0.01,1)) = 0.25
        _SpecStrength     ("Specular Strength", Range(0,2)) = 0.25
        _AmbientStrength  ("Ambient Strength", Range(0,2)) = 1.0
        _ShadowAmbient    ("Ambient Kept In Shadow", Range(0,1)) = 0.38
        // Highlight soft-clip for the CHARACTER only. Default 0 is a guaranteed no-op (the shade
        // blends the result in by this amount, so 0 returns the untouched colour). A rider's SKIN
        // is a mid-tone albedo (~0.65); under a bright region's sun+sky it drives the diffuse over
        // 1.0 and the legs/arms clip to flat WHITE, losing all form, while the shoulders catch a
        // hard specular blowout. Lifting this compresses everything above a fixed knee toward a
        // soft white point, so bright skin keeps a readable shaded gradient and the spec reads as
        // fabric sheen - without touching the near-black kit or any mid-tone (both sit below the
        // knee). It only ever runs on the materials that set it, so every environment/NPC material
        // is byte-for-byte unchanged.
        _HighlightRolloff ("Highlight Soft Clip (character)", Range(0,1)) = 0
        // Knee/white-point for the character highlight soft-clip. Defaults reproduce the original
        // hard-coded 0.55 / 1.35 constants, so every material that does not override them is
        // byte-for-byte unchanged. A CHARACTER under a bright HDRP region (Automatic exposure +
        // ACES + positive post-exposure) needs a LOWER knee and LOWER white point so its skin is
        // pulled below the clip point and keeps a shaded gradient instead of blowing to pure white.
        _HighlightKnee    ("Highlight Knee (character)", Range(0.1,1)) = 0.55
        _HighlightWhite   ("Highlight White Point (character)", Range(0.6,2)) = 1.35
        // ---- CHARACTER SELF-LIGHT RIG (default OFF => region lighting unchanged) --------------
        // This custom ForwardOnly cel pass lights from GLOBAL _MR_Sun*/_MR_Amb* uniforms, NOT
        // HDRP's light loop, so a real HDRP light on a Light Layer cannot touch it. The rider
        // instead carries its OWN view-relative key+fill+ambient here, so it reads the SAME in a
        // bright region (Sakura) and a dim cool one (Shiosai) - decoupled from the region grade.
        // _CharacterLight blends region->rig; 0 (default) is a guaranteed no-op for env/NPC mats.
        _CharacterLight   ("Character Self-Light Blend", Range(0,1)) = 0
        _CharKeyDir       ("Char Key Dir (viewRight, up, towardCam)", Vector) = (0.35,0.65,0.55,0)
        _CharKeyColor     ("Char Key Color", Color) = (1,1,1,1)
        _CharKeyIntensity ("Char Key Intensity", Range(0,3)) = 1.15
        _CharFillColor    ("Char Fill Color", Color) = (0.62,0.72,0.95,1)
        _CharFillIntensity("Char Fill Intensity", Range(0,2)) = 0.40
        _CharAmbient      ("Char Flat Ambient Floor", Range(0,1)) = 0.32
        // Always-on fresnel edge for silhouette separation against bright AND dark backgrounds.
        // Independent of any light direction; default strength 0 = no-op for every other material.
        _EdgeRimColor     ("Edge Rim Color (character)", Color) = (0.72,0.80,1.0,1)
        _EdgeRimStrength  ("Edge Rim Strength (character)", Range(0,2)) = 0
        _EdgeRimPower     ("Edge Rim Power (character)", Range(0.5,8)) = 3.0
        // ---- SKIN-AWARE HIGHLIGHT TAME (character only; default 0 => guaranteed no-op) ---------
        // Bright BARE SKIN (warm albedo ~0.85,0.62,0.55) drives the diffuse over 1.0 under the
        // char key + ACES/post-exposure and clips to flat WHITE on the thighs/calves, losing all
        // tan shading. This detects warm skin albedo AND a lit result that is actually blowing,
        // then pulls ONLY that toward a tan value. Neutral white (branding/socks) and the near-black
        // kit are excluded by the warm+bright albedo test, so the jersey art and socks are
        // untouched; the front face is protected by the lit-brightness knee (it is not blown).
        // _SkinTame 0 (default) makes every env/NPC material byte-for-byte unchanged.
        _SkinTame         ("Skin Highlight Tame (character)", Range(0,1)) = 0
        _SkinTameGain     ("Skin Tame Gain", Range(0.2,1)) = 0.6
        _SkinTameKnee     ("Skin Tame Lit Knee", Range(0.5,1.6)) = 0.9
        _SkinTameSharp    ("Skin Tame Sharpness", Range(0.5,8)) = 3.0
        // ---- PLAYER-ONLY WARM SKIN RETONE (character only; default 0 => guaranteed no-op) ------
        // The player kit atlas authors BARE SKIN as a PALE peach (~0.90,0.74,0.67, sat ~0.26) that
        // washes to flat grey-white on the thighs/calves under the bright character key + ACES/post
        // exposure. Unlike _SkinTame (which only scales the lit result down = grey), this DEEPENS
        // and SATURATES the warm skin ALBEDO toward a tan multiplier BEFORE lighting, so the legs
        // read as warm tan instead of near-white. Neutral white (branding/socks, r~=g~=b) and the
        // near-black kit are excluded by the warm+bright albedo test, so they stay exact. _SkinWarm
        // 0 (default) makes every env/NPC material byte-for-byte unchanged.
        _SkinWarm         ("Skin Warm Retone (character)", Range(0,1)) = 0
        _SkinWarmColor    ("Skin Warm Tan Tint (character)", Color) = (1,0.82,0.66,1)
        _ShadowEdge       ("Shadow Step Midpoint", Range(0.05,0.95)) = 0.5
        _ShadowSoft       ("Shadow Step Softness", Range(0.01,0.45)) = 0.18
        _DappleScale      ("Canopy Dapple Scale (per m)", Range(0.5,8)) = 2.6
        _DappleStrength   ("Canopy Dapple Strength", Range(0,1)) = 0.35
        _HeightTint       ("Height Tint Color", Color) = (1,1,1,1)
        _HeightRange      ("Height Tint (start, end, amount, unused)", Vector) = (0,1,0,0)
        _SnowColor        ("Snow Color", Color) = (1,1,1,1)
        _SnowRange        ("Snow (start, end, amount, unused)", Vector) = (0,1,0,0)
        _WeatherAmount    ("Weathering Master", Range(0,1)) = 0
        _MossColor        ("Moss / Damp Tint", Color) = (0.30,0.40,0.24,1)
        _MossAmount       ("Moss Amount", Range(0,1)) = 0.35
        _MossHeight       ("Moss Fades Out By (world m above base)", Float) = 2.2
        _MossBase         ("Moss Base Height (world m)", Float) = 0
        _GrimeAmount      ("Underside Grime", Range(0,1)) = 0.30
        _WearColor        ("Sun-bleach / Edge Wear Tint", Color) = (1,0.97,0.90,1)
        _WearAmount       ("Edge Wear Amount", Range(0,1)) = 0.25
        _DetailScale      ("Surface Detail Scale (per m)", Range(0.5,40)) = 9.0
        _DetailAmount     ("Surface Detail / Roughness Break", Range(0,1)) = 0.30
        _TintVariation    ("Per-Prop Colour Variation", Range(0,1)) = 0.18
        _TintVarScale     ("Colour Variation Scale (per m)", Range(0.002,0.2)) = 0.045
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        // Opt-in tangent-space normal map (OpenGL +Y, TextureImporter type Normal map). Default
        // strength 0 skips the sample entirely, so every existing material shades exactly as before.
        [Normal] _NormalMap ("Normal Map (opt-in)", 2D) = "bump" {}
        _NormalStrength   ("Normal Map Strength", Range(0,2)) = 0
        // E4 huddle idea 9: low-res world-space macro variation that breaks up visible tiling.
        _MacroTex    ("Macro Variation (linear grey, mean 0.5)", 2D) = "grey" {}
        _MacroScale  ("Macro Scale (repeats per metre)", Float) = 0.05
        _MacroAmount ("Macro Amount", Range(0,1)) = 0.6
        [Header(Weather Snow (global _MR_SnowCover))]
        _SnowAccept       ("Snow Accept (0 = never snowed on)", Range(0,1)) = 0
        _SnowBias         ("Snow Slope Bias (+ holds on steeper faces)", Range(-0.5,0.5)) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #define MR_WANT_TANGENT 1
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_MacroTex);
    SAMPLER(sampler_MacroTex);
    TEXTURE2D(_NormalMap);
    SAMPLER(sampler_NormalMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _Color;
        float4 _MatteFloor;
        float4 _ShadeColor;
        float  _ShadeStrength, _RampSteps, _RampSmooth, _RampOffset;
        float4 _RimColor;
        float  _RimPower, _RimStrength;
        float4 _SpecTint;
        float  _Gloss, _SpecStrength;
        float  _AmbientStrength, _ShadowAmbient, _ShadowEdge, _ShadowSoft;
        float  _HighlightRolloff, _HighlightKnee, _HighlightWhite;
        float  _CharacterLight;
        float4 _CharKeyDir, _CharKeyColor;
        float  _CharKeyIntensity;
        float4 _CharFillColor;
        float  _CharFillIntensity, _CharAmbient;
        float4 _EdgeRimColor;
        float  _EdgeRimStrength, _EdgeRimPower;
        float  _SkinTame, _SkinTameGain, _SkinTameKnee, _SkinTameSharp;
        float  _SkinWarm;
        float4 _SkinWarmColor;
        float  _DappleScale, _DappleStrength;
        float4 _HeightTint, _HeightRange, _SnowColor, _SnowRange;
        float  _WeatherAmount;
        float4 _MossColor;
        float  _MossAmount, _MossHeight, _MossBase, _GrimeAmount;
        float4 _WearColor;
        float  _WearAmount, _DetailScale, _DetailAmount, _TintVariation, _TintVarScale;
        float  _Cull;
        float  _NormalStrength;
        float  _MacroScale;
        float  _MacroAmount;
        float  _SnowAccept, _SnowBias;
    CBUFFER_END

    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideSnow.hlsl"

    // Identical accumulation order to the Built-in WeatherSurface(): surface detail ->
    // per-prop colour variation -> sun bleach -> moss -> underside grime.
    float3 MR_Weather(float3 albedo, float3 wp, float3 wn)
    {
        float3 an = abs(wn);
        an /= max(an.x + an.y + an.z, 1e-4);

        float fine = MR_TriNoise(wp, an, _DetailScale);
        float coarse = MR_TriNoise(wp, an, _DetailScale * 0.27);
        float detail = (fine * 0.62 + coarse * 0.38) * 2.0 - 1.0;
        albedo *= 1.0 + detail * _DetailAmount * 0.34;

        float3 var3 = float3(MR_TriNoise(wp + 11.3, an, _TintVarScale),
                             MR_TriNoise(wp + 37.1, an, _TintVarScale),
                             MR_TriNoise(wp + 71.7, an, _TintVarScale)) * 2.0 - 1.0;
        albedo *= 1.0 + var3 * _TintVariation * 0.30;

        float up = saturate(wn.y);
        float wear = up * up * saturate(0.45 + fine * 0.9) * _WearAmount;
        albedo = lerp(albedo, albedo * 0.55 + MR_AuthoredCol(_WearColor.rgb) * 0.62, wear);

        float low = 1.0 - saturate((wp.y - _MossBase) / max(0.05, _MossHeight));
        float patch = saturate(MR_TriNoise(wp, an, _DetailScale * 0.45) * 1.9 - 0.62);
        float moss = saturate(up * 0.75 + 0.25) * low * patch * _MossAmount;
        albedo = lerp(albedo, albedo * 0.42 + MR_AuthoredCol(_MossColor.rgb) * 0.80, moss);

        float down = saturate(-wn.y);
        albedo *= 1.0 - down * saturate(0.4 + coarse * 0.8) * _GrimeAmount * 0.45;

        return albedo;
    }

    float4 MR_Shade(MRVaryings i, bool frontFace, float sunShadow)
    {
        // Material Color properties arrive LINEAR under a Linear project (see MR_AuthoredCol);
        // the cel maths below is authored sRGB, so every tint is bridged once, here, before use.
        float3 tintBase   = MR_AuthoredCol(_Color.rgb);
        float3 tintHeight = MR_AuthoredCol(_HeightTint.rgb);
        float3 tintSnow   = MR_AuthoredCol(_SnowColor.rgb);
        float3 tintShade  = MR_AuthoredCol(_ShadeColor.rgb);
        float3 tintSpec   = MR_AuthoredCol(_SpecTint.rgb);
        float3 tintRim    = MR_AuthoredCol(_RimColor.rgb);

        float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
        float4 c = MR_Authored(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv));
        c.rgb *= tintBase;
        c.a *= _Color.a;

        // Matte floor (authored/sRGB space, same space as c after MR_Authored). Per-channel max
        // leaves whites, skin and mid-tones untouched and only lifts the near-black kit; default
        // (0,0,0) makes this a no-op for every environment material. Deliberately applied BEFORE
        // the macro variation below: a tunnel bore sits in near-zero ambient, where every lighting
        // term downstream is albedo-multiplied, so a floor applied AFTER the macro multiply (the
        // original order) would have clamped the darkened grain right back to a flat value and
        // erased it. Lifting first gives the macro pass a non-zero base so its grain still reads
        // once it reaches the tunnel's near-black ambient term.
        c.rgb = max(c.rgb, MR_AuthoredCol(_MatteFloor.rgb));

        // Macro variation (tunnel lining): world-space TRIPLANAR so it needs no second UV set and
        // runs continuously across tiles, segments and the bore/lining seam. Two octaves at
        // unrelated scales so the macro texture itself never reads as a repeat.
        {
            float3 an = abs(normalize(i.normalWS));
            float3 w = an / max(1e-4, an.x + an.y + an.z);
            float3 p = i.positionWS * _MacroScale;
            float m1 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, p.yz).r * w.x
                     + SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, p.xz).r * w.y
                     + SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, p.xy).r * w.z;
            float3 q = i.positionWS * (_MacroScale * 3.7) + 0.37;
            float m2 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, q.yz).r * w.x
                     + SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, q.xz).r * w.y
                     + SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, q.xy).r * w.z;
            float m = m1 * 0.65 + m2 * 0.35;
            c.rgb *= lerp(1.0, saturate(m * 2.0), _MacroAmount);
        }

        float t = saturate((i.positionWS.y - _HeightRange.x) / max(0.001, _HeightRange.y - _HeightRange.x));
        c.rgb = lerp(c.rgb, c.rgb * tintHeight, t * _HeightRange.z);

        float snow = smoothstep(_SnowRange.x, _SnowRange.y, i.positionWS.y) * _SnowRange.z;
        c.rgb = lerp(c.rgb, tintSnow, snow);

        // ---- PLAYER-ONLY WARM SKIN RETONE (default 0 => untouched). Deepen + saturate ONLY warm
        // bare-skin albedo toward a tan multiplier BEFORE lighting, so pale peach legs read as warm
        // tan instead of washing to grey-white under the bright character key. Neutral white
        // (branding/socks) and the near-black kit fail the warm+bright test and stay exact. --------
        if (_SkinWarm > 0.0)
        {
            float aLum    = dot(c.rgb, float3(0.299, 0.587, 0.114));
            float warmS   = saturate((c.rgb.r - c.rgb.b) * 6.0 - 0.12);  // r>>b => skin; ~0 neutral
            float isSkinA = warmS * saturate((aLum - 0.34) * 5.0);       // 0 for black kit / dim
            // Skin is a SOFT warm tone (sat ~0.25-0.40); yellow/orange kit and honey hair are
            // 0.6-0.9 and used to be retoned to mustard. Saturation gate excludes them.
            float aMax = max(c.r, max(c.g, c.b));
            float aSat = (aMax - min(c.r, min(c.g, c.b))) / max(aMax, 1e-4);
            isSkinA *= saturate((0.58 - aSat) * 6.0);
            c.rgb = lerp(c.rgb, c.rgb * MR_AuthoredCol(_SkinWarmColor.rgb), isSkinA * _SkinWarm);
        }

        float3 n = normalize(i.normalWS);
        // Opt-in baked surface detail (folds, seams, embossed logos). Same UV as the albedo.
        // UnpackNormalScale handles both BC5/DXT5nm (AG) and plain RGB (alpha 1) encodings.
        if (_NormalStrength > 0.0)
        {
            float3 tg = i.tangentWS.xyz;
            tg = normalize(tg - n * dot(tg, n) + 1e-6);
            float3 bt = cross(n, tg) * (i.tangentWS.w >= 0.0 ? 1.0 : -1.0);
            float3 nts = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalStrength);
            n = normalize(tg * nts.x + bt * nts.y + n * nts.z);
        }
        // Double-sided geometry (cards, banners, tunnel interiors with _Cull 0) needs the
        // normal flipped on back faces or the shaded side reads as a black hole.
        n = frontFace ? n : -n;

        if (_WeatherAmount > 0.001)
        {
            float3 weathered = MR_Weather(c.rgb, i.positionWS, n);
            c.rgb = lerp(c.rgb, weathered, _WeatherAmount);
        }

        // Weather snow (no-op unless a snowy region is active AND the material accepts snow).
        float3 geoN = normalize(i.normalWS) * (frontFace ? 1.0 : -1.0);
        float snowM = MR_SnowMask(i.positionWS, geoN, _SnowAccept, _SnowBias);
        if (snowM > 0.0)
        {
            c.rgb = MR_SnowApply(c.rgb, snowM, i.positionWS);
            n = normalize(lerp(n, geoN, snowM * 0.8));
        }

        float3 l = MR_SunDirection();
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);

        float ndl = dot(n, l) * 0.5 + 0.5;                    // wrapped diffuse, soft terminator
        float ramp = MR_CelRamp(saturate(ndl + _RampOffset), _RampSteps, _RampSmooth);

        // CAST SHADOWS. sunShadow is HDRP's directional shadow attenuation, sampled in the
        // ForwardOnly pass (MapleRideHDRPShadow.hlsl) and passed in so the depth/shadow passes
        // never pull the light-loop headers. 1 = lit, 0 = fully shadowed. _ShadowEdge/_ShadowSoft
        // shape the terminator so the hard shadow-map edge still reads as a cel band rather than
        // as a photoreal hard edge.
        float shadeBase = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft,
                                     saturate(sunShadow));
        float dapple = MR_ValueNoise(i.positionWS.xz, _DappleScale);
        float shade = saturate(shadeBase + (1.0 - shadeBase) * dapple * _DappleStrength);
        float shadowed = ramp * shade;

        float3 lit = lerp(tintShade * _ShadeStrength, float3(1, 1, 1), shadowed);
        if (snowM > 0.0) lit = lerp(lit, MR_SnowLit(dot(n, l), shade), snowM);

        float3 h = normalize(l + v);
        float spec = pow(saturate(dot(n, h)), _Gloss * 128.0 + 1.0);
        spec = smoothstep(0.35, 0.45, spec) * _SpecStrength * shadowed * (1.0 - snowM);

        float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
        rim *= _RimStrength * saturate(ndl + 0.25);

        float3 sun = MR_SunColor();
        float3 col = c.rgb * lit * sun;
        col += c.rgb * MR_Ambient(n) * _AmbientStrength * lerp(_ShadowAmbient, 1.0, shade);
        col += tintSpec * spec * sun;
        col += tintRim * rim * lerp(_ShadowAmbient, 1.0, shade) * sun;
        if (snowM > 0.0) col += MR_SnowSparkle(i.positionWS, n, v, l, shade) * snowM * sun;

        // ---- CHARACTER SELF-LIGHT RIG (default 0 => region-lit col is returned untouched) ------
        // Re-light the rider with a fixed VIEW-RELATIVE key+fill+ambient so its modelling is
        // identical in every region, then blend region->rig by _CharacterLight. Because the key is
        // camera-relative, the surface the camera actually sees (the rider's back on the chase cam)
        // is always modelled - which is exactly what a region backlight crushes to black.
        if (_CharacterLight > 0.0)
        {
            float3 camR   = normalize(UNITY_MATRIX_I_V._m00_m10_m20);
            float3 camU   = normalize(UNITY_MATRIX_I_V._m01_m11_m21);
            float3 camFwd = normalize(-UNITY_MATRIX_I_V._m02_m12_m22);   // camera -> scene
            float3 kL = normalize(camR * _CharKeyDir.x + camU * _CharKeyDir.y - camFwd * _CharKeyDir.z);

            float kndl  = dot(n, kL) * 0.5 + 0.5;                        // wrapped, soft terminator
            float kramp = MR_CelRamp(saturate(kndl + _RampOffset), _RampSteps, _RampSmooth);
            float3 klit = lerp(tintShade * _ShadeStrength, float3(1, 1, 1), kramp);

            float3 keyCol  = MR_AuthoredCol(_CharKeyColor.rgb) * _CharKeyIntensity;
            float3 fillCol = MR_AuthoredCol(_CharFillColor.rgb);
            float  kfill   = saturate(0.5 - 0.5 * dot(n, kL));           // opposite side, flat

            float3 charCol = c.rgb * klit * keyCol;
            charCol += c.rgb * fillCol * (_CharFillIntensity * kfill + _CharAmbient);

            float3 kh    = normalize(kL + v);
            float  kspec = pow(saturate(dot(n, kh)), _Gloss * 128.0 + 1.0);
            kspec = smoothstep(0.35, 0.45, kspec) * _SpecStrength * kramp;
            charCol += tintSpec * kspec * keyCol;

            col = lerp(col, charCol, _CharacterLight);
        }

        // ---- ALWAYS-ON EDGE RIM (default 0 => no-op). Pure view fresnel, light-independent, for
        // silhouette separation against both bright and dark backdrops. Kept subtle on purpose.
        if (_EdgeRimStrength > 0.0)
        {
            float edge = pow(1.0 - saturate(dot(n, v)), _EdgeRimPower);
            col += MR_AuthoredCol(_EdgeRimColor.rgb) * (edge * _EdgeRimStrength);
        }

        // ---- SKIN-AWARE HIGHLIGHT TAME (default 0 => untouched). Pull only warm bare skin that is
        // actually blowing (thighs/calves) down toward a tan value; leave neutral white branding/
        // socks and the near-black kit exact, and leave the (unblown) front face alone. ----------
        if (_SkinTame > 0.0)
        {
            float albLum = dot(c.rgb, float3(0.299, 0.587, 0.114));
            float warm   = saturate((c.rgb.r - c.rgb.b) * 6.0 - 0.12);   // r>>b => skin; ~0 for neutral
            float isSkin = warm * saturate((albLum - 0.34) * 5.0);       // 0 for black kit / dim
            float albMax = max(c.r, max(c.g, c.b));                      // same saturation gate
            isSkin *= saturate((0.58 - (albMax - min(c.r, min(c.g, c.b))) / max(albMax, 1e-4)) * 6.0);
            float colLum = dot(col, float3(0.299, 0.587, 0.114));
            float over   = saturate((colLum - _SkinTameKnee) * _SkinTameSharp); // only where lit skin blows
            col = lerp(col, col * _SkinTameGain, isSkin * over * _SkinTame);
        }

        // CHARACTER highlight soft-clip (default 0 => untouched). Identity below the knee, so the
        // near-black kit and every mid-tone are exact; only values that would clip (bright skin,
        // spec) are compressed toward a soft white point, restoring a shaded gradient instead of
        // flat white. Applied in the same pre-exposure linear space as the rest of `col`.
        if (_HighlightRolloff > 0.0)
        {
            // Knee: below this the kit + mid-tones are untouched. White point: the asymptotic soft
            // white the compressed range approaches. Both are material-driven (defaults 0.55/1.35)
            // so the character can be compressed harder than an environment prop under the same
            // bright key without changing any other material. Applied pre-exposure, so the target
            // is chosen against the ACES/post-exposure result, not the raw linear value.
            float knee = _HighlightKnee;
            float wp   = max(_HighlightWhite, knee + 1e-3);
            float3 over = max(col - knee, 0.0);
            float3 soft = min(col, knee) + (wp - knee) * over / (over + (wp - knee));
            col = lerp(col, soft, _HighlightRolloff);
        }

        // HDRP renders into a physically-exposed buffer; an unlit/custom pass must apply
        // the current exposure itself or it lands at a wildly different brightness than
        // every HDRP-lit surface around it. This is the standard HDRP idiom for
        // hand-written and emissive output.
        return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), c.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        // HDRP draws opaques through ForwardOnly when the material has no generated Lit
        // passes. DepthForwardOnly feeds the depth prepass, ShadowCaster keeps this
        // geometry casting into HDRP's shadow maps even though it cannot yet receive.
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
            // Shadow-quality variants HDRP's shadow algorithms branch on. Without these the
            // shadow headers compile against an undefined filter and every lookup returns 1.
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

            #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPShadow.hlsl"

            float4 Frag(MRVaryings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float shadow = MR_SunShadow(i.positionRWS, frontFace ? n : -n, i.positionCS.xy);
                return MR_Shade(i, frontFace, shadow);
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
