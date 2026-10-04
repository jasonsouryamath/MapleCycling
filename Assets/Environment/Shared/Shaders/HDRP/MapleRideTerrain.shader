// HDRP replacement for MapleRide/SakuraTerrain (spec section 4.4).
//
// Property names, defaults and blend maths are deliberately IDENTICAL to the Built-in
// original, so converting a material is a shader swap that preserves every authored
// value. The Built-in shader is intentionally left on disk and untouched (spec 4.1).
//
// Three tiling PBR layers - grass, rock and scree - plus forest soil, blended by weights
// the Blender terrain builder baked into the mesh's extra UV channels (uv1.x = rock,
// uv1.y = scree, uv2.x = soil, uv2.y = petal, grass is the remainder). Weights travel
// through UV rather than vertex colour because glTF runs COLOR_0 through an sRGB
// conversion that would corrupt them.
//
// Everything is projected triplanar from world space. A single planar XZ projection is
// what made the grass visibly smear into vertical streaks down the cut slopes; triplanar
// costs three taps per layer but the valley walls are most of what the rider looks at.
//
// SHADOWS: closed. The term comes from MapleRideHDRPShadow.hlsl, sampled in the ForwardOnly
// pass; _ShadowEdge/_ShadowSoft/_ShadowAmbient shape it into a cel band.
//
// DE-TILING: see the [Header(De-tiling)] block. All three cures default to zero so an existing
// region material renders bit-for-bit as before until it opts in.
Shader "MapleRide/HDRP/Terrain"
{
    Properties
    {
        [Header(Grass)]
        _GrassTex       ("Grass Albedo", 2D) = "white" {}
        _GrassNormal    ("Grass Normal", 2D) = "bump" {}
        _GrassRough     ("Grass Roughness", 2D) = "gray" {}
        _GrassColor     ("Grass Tint", Color) = (1,1,1,1)
        _GrassScale     ("Grass Tiling (m per tile)", Float) = 6.0
        _TileOrigin     ("Tiling Origin (world offset, opt-in)", Vector) = (0,0,0,0)

        [Header(Rock)]
        _RockTex        ("Rock Albedo", 2D) = "white" {}
        _RockNormal     ("Rock Normal", 2D) = "bump" {}
        _RockRough      ("Rock Roughness", 2D) = "gray" {}
        _RockColor      ("Rock Tint", Color) = (1,1,1,1)
        _RockScale      ("Rock Tiling (m per tile)", Float) = 9.0

        [Header(Scree)]
        _ScreeTex       ("Scree Albedo", 2D) = "white" {}
        _ScreeNormal    ("Scree Normal", 2D) = "bump" {}
        _ScreeRough     ("Scree Roughness", 2D) = "gray" {}
        _ScreeColor     ("Scree Tint", Color) = (1,1,1,1)
        _ScreeScale     ("Scree Tiling (m per tile)", Float) = 4.0

        // Section 29: forest soil + leaf litter is the band between closed sward and bare
        // cliff. Without it every embankment read as one flat green sheet running straight
        // into rock. Weight arrives in UV2.x from the terrain builder.
        [Header(Forest soil and leaf litter)]
        _SoilTex        ("Soil Albedo", 2D) = "white" {}
        _SoilNormal     ("Soil Normal", 2D) = "bump" {}
        _SoilRough      ("Soil Roughness", 2D) = "gray" {}
        _SoilColor      ("Soil Tint", Color) = (1,1,1,1)
        _SoilScale      ("Soil Tiling (m per tile)", Float) = 5.0

        // Moss and petal accumulation are tint overlays rather than texture layers: both are
        // thin films over whatever ground is underneath, so a full triplanar layer each would
        // cost nine extra taps to say something a tint says just as well.
        [Header(Moss)]
        _MossColor      ("Moss Tint", Color) = (0.42,0.56,0.32,1)
        _MossStrength   ("Moss Strength", Range(0,1)) = 0.45
        _MossScale      ("Moss Patch Size (m)", Float) = 11.0

        [Header(Petal accumulation)]
        _PetalColor     ("Petal Tint", Color) = (0.97,0.80,0.86,1)
        _PetalStrength  ("Petal Strength", Range(0,1)) = 0.55
        _PetalScale     ("Petal Drift Size (m)", Float) = 3.5

        [Header(Blending)]
        _SlopeRockStart ("Slope Rock Start (deg)", Range(0,90)) = 34
        _SlopeRockEnd   ("Slope Rock End (deg)", Range(0,90)) = 58
        _MacroVariation ("Macro Variation", Range(0,1)) = 0.35
        _NormalStrength ("Normal Strength", Range(0,3)) = 1.0

        // DE-TILING (opening visual overhaul, milestone 2). A single tiling albedo at ~5 m per
        // tile quilts visibly across an open hillside - the start-line reverse shot showed a
        // regular diagonal corduroy running the full length of the slope, which is one of the
        // strongest remaining "Roblox" tells. Three cheap, independent cures, all defaulting to
        // OFF-ish values so no existing region changes unless its material opts in:
        //   _Detile*      - blend the grass albedo between two NON-HARMONIC tile scales under a
        //                   large low-frequency mask, so the repeat period stops being constant.
        //   _MesoVariation- a mid-scale tonal/hue break at the ~30 m band that sits between the
        //                   tile size and the existing 190 m macro break, which is exactly the
        //                   scale at which the eye locks onto a repeat.
        //   _DetailFade*  - fade tile-scale contrast toward the macro average with distance, so
        //                   the far slope stops aliasing into a moire stripe.
        [Header(De tiling)]
        _DetileAmount   ("Grass De-tile Amount", Range(0,1)) = 0.0
        _DetileRatio    ("Grass De-tile Second Scale", Range(1.2,5)) = 2.71
        _DetileBlendM   ("Grass De-tile Patch Size (m)", Float) = 42
        _MesoVariation  ("Meso Variation", Range(0,1)) = 0.0
        _MesoScaleM     ("Meso Patch Size (m)", Float) = 31
        _MesoHue        ("Meso Hue Shift", Range(0,1)) = 0.35
        _DetailFadeStart("Detail Fade Start (m)", Float) = 90
        _DetailFadeRange("Detail Fade Range (m)", Float) = 260
        _DetailFadeAmt  ("Detail Fade Amount", Range(0,1)) = 0.0

        [Header(Cel lighting)]
        _ShadeColor     ("Shade Tint", Color) = (0.40,0.46,0.70,1)
        _ShadeStrength  ("Shade Strength", Range(0,1)) = 0.70
        _RampSteps      ("Ramp Steps", Range(2,6)) = 4
        _RampSmooth     ("Ramp Softness", Range(0.002,0.35)) = 0.07
        _AmbientStrength("Ambient Strength", Range(0,2)) = 1.0
        _ShadowAmbient  ("Ambient Kept In Shadow", Range(0,1)) = 0.38
        _ShadowEdge     ("Shadow Step Midpoint", Range(0.05,0.95)) = 0.5
        _ShadowSoft     ("Shadow Step Softness", Range(0.01,0.45)) = 0.18
        _RimColor       ("Rim Color", Color) = (1,0.74,0.55,1)
        _RimPower       ("Rim Power", Range(0.5,10)) = 3.5
        _RimStrength    ("Rim Strength", Range(0,3)) = 0.45
        _SpecTint       ("Specular Tint", Color) = (1,0.93,0.85,1)
        _SpecStrength   ("Specular Strength", Range(0,2)) = 0.16

        [Header(Height grading)]
        _SnowColor      ("High Altitude Tint", Color) = (0.86,0.88,0.95,1)
        _HeightRange    ("Height Tint (start, end, amount, unused)", Vector) = (40,90,0.55,0)
        _SnowAccept     ("Snow Accept (global _MR_SnowCover)", Range(0,1)) = 0
        _SnowBias       ("Snow Slope Bias", Range(-0.5,0.5)) = 0.12

        // Face culling, exposed so geometry the CAMERA RIDES THROUGH (the Shiosai sea-arch
        // gate, tunnel shells, any rock ring) can be made double-sided. Defaults to Back (2),
        // so every existing terrain material is bit-for-bit unchanged.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    TEXTURE2D(_GrassTex);   TEXTURE2D(_GrassNormal);    TEXTURE2D(_GrassRough);
    TEXTURE2D(_RockTex);    TEXTURE2D(_RockNormal);     TEXTURE2D(_RockRough);
    TEXTURE2D(_ScreeTex);   TEXTURE2D(_ScreeNormal);    TEXTURE2D(_ScreeRough);
    TEXTURE2D(_SoilTex);    TEXTURE2D(_SoilNormal);     TEXTURE2D(_SoilRough);

    // Sixteen textures would need sixteen sampler states, which is exactly the D3D11
    // limit and leaves nothing for HDRP's own bindings. Every layer tiles the same way
    // (repeat, trilinear), so they all share one sampler declared against the grass
    // albedo. This is a pure resource decision; the filtering is unchanged.
    SAMPLER(sampler_GrassTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _GrassTex_ST, _GrassNormal_ST, _GrassRough_ST;
        float4 _RockTex_ST,  _RockNormal_ST,  _RockRough_ST;
        float4 _ScreeTex_ST, _ScreeNormal_ST, _ScreeRough_ST;
        float4 _SoilTex_ST,  _SoilNormal_ST,  _SoilRough_ST;
        float4 _GrassColor, _RockColor, _ScreeColor, _SoilColor;
        float  _GrassScale, _RockScale, _ScreeScale, _SoilScale;
        float4 _TileOrigin;
        float4 _MossColor, _PetalColor;
        float  _MossStrength, _PetalStrength, _MossScale, _PetalScale;
        float  _SlopeRockStart, _SlopeRockEnd, _MacroVariation, _NormalStrength;
        float  _DetileAmount, _DetileRatio, _DetileBlendM;
        float  _MesoVariation, _MesoScaleM, _MesoHue;
        float  _DetailFadeStart, _DetailFadeRange, _DetailFadeAmt;
        float4 _ShadeColor, _RimColor, _SpecTint, _SnowColor;
        float  _ShadeStrength, _RampSteps, _RampSmooth, _RimPower, _RimStrength, _SpecStrength;
        float  _AmbientStrength, _ShadowAmbient, _ShadowEdge, _ShadowSoft;
        float4 _HeightRange;
        float  _SnowAccept, _SnowBias;
    CBUFFER_END

    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideSnow.hlsl"

    // The terrain needs UV1 and UV2 (the baked splat weights), which the shared
    // MRAttributes does not carry, so this shader declares its own input struct. The
    // OUTPUT is still MRVaryings so the rest of the kit's conventions hold; the four
    // weights ride in the otherwise-unused .color interpolator.
    struct MRTerrainAttributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float2 uv         : TEXCOORD0;
        float2 uv1        : TEXCOORD1;
        float2 uv2        : TEXCOORD2;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    MRVaryings MR_VertTerrain(MRTerrainAttributes input)
    {
        MRVaryings o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
        o.positionCS = TransformWorldToHClip(positionRWS);
        // Absolute world space, exactly as MR_Vert does: every triplanar projection and
        // the altitude grading below are authored against absolute metres.
        o.positionWS = GetAbsolutePositionWS(positionRWS);
        // Camera-relative position, required by MR_ApplyFog. This struct member is shared with
        // MR_Vert; a custom vertex function that forgets it leaves the interpolator
        // uninitialised, and the resulting garbage fog distance killed every terrain pixel.
        o.positionRWS = positionRWS;
        o.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
        o.uv = input.uv;
        o.color = float4(input.uv1.x, input.uv1.y, input.uv2.x, input.uv2.y);
        return o;
    }

    // ------------------------------------------------------------------ triplanar

    // Weights for the three world-axis projections, sharpened so the transition band is
    // narrow enough not to wash out detail but wide enough to hide the seam.
    float3 MRT_TriWeights(float3 n)
    {
        float3 w = pow(abs(n), 5.0);
        return w / max(w.x + w.y + w.z, 1e-4);
    }

    float4 MRT_TriSample(TEXTURE2D_PARAM(tex, samp), float3 wp, float3 w, float scale)
    {
        float inv = 1.0 / max(scale, 0.001);
        float4 x = SAMPLE_TEXTURE2D(tex, samp, wp.zy * inv);
        float4 y = SAMPLE_TEXTURE2D(tex, samp, wp.xz * inv);
        float4 z = SAMPLE_TEXTURE2D(tex, samp, wp.xy * inv);
        return x * w.x + y * w.y + z * w.z;
    }

    // Tangent-space normal from a triplanar sample. The terrain's UV0 is a planar XZ
    // layout, so its tangent frame already lines up with the Y projection; the X and Z
    // projections are swizzled to match rather than doing a full world-space rebuild.
    float3 MRT_TriNormal(TEXTURE2D_PARAM(tex, samp), float3 wp, float3 w, float scale)
    {
        float inv = 1.0 / max(scale, 0.001);
        // UnpackNormal() is Built-in only; UnpackNormalMapRGorAG is the HDRP core
        // equivalent and handles both DXT5nm and plain RG normal maps.
        float3 x = UnpackNormalMapRGorAG(SAMPLE_TEXTURE2D(tex, samp, wp.zy * inv), 1.0);
        float3 y = UnpackNormalMapRGorAG(SAMPLE_TEXTURE2D(tex, samp, wp.xz * inv), 1.0);
        float3 z = UnpackNormalMapRGorAG(SAMPLE_TEXTURE2D(tex, samp, wp.xy * inv), 1.0);
        return normalize(x.zyx * w.x + y * w.y + z.xzy * w.z);
    }

    // The Built-in surface shader handed o.Normal to the framework in TANGENT space and
    // let it do the basis transform. A hand-written HDRP pass has no such step, so the
    // frame is rebuilt here from the terrain's own planar XZ UV layout: u runs along
    // world +X, v along world +Z, both projected onto the interpolated world normal.
    // On flat ground this collapses to the documented world = float3(t.x, t.z, t.y)
    // mapping, and unlike that shortcut it stays correct on the cut slopes, which are a
    // large part of what the rider actually sees.
    float3 MRT_TangentToWorld(float3 t, float3 wn)
    {
        float3 tangent = normalize(float3(1, 0, 0) - wn * wn.x);
        float3 bitangent = normalize(float3(0, 0, 1) - wn * wn.z);
        return normalize(tangent * t.x + bitangent * t.y + wn * t.z);
    }

    // ------------------------------------------------------------------ lighting

    float4 MRT_Shade(MRVaryings i, float sunShadow)
    {
        // OPT-IN tiling origin. Every triplanar projection below samples at ABSOLUTE world
        // position, so a region placed far from the world origin (Minato Coast sits at
        // X 9,000-20,090 / Z 4,146-11,944) produces tiling UVs in the thousands. float32
        // screen-space derivatives quantise at that magnitude, the GPU selects the top mip
        // and every layer collapses to its flat average colour. Subtracting a per-material
        // origin keeps the sampled coordinates small WITHOUT moving the geometry.
        // Defaults to 0, so SakuraPass/Shiosai/Taka are bit-for-bit unchanged.
        float3 wp = i.positionWS - _TileOrigin.xyz;
        float3 wn = normalize(i.normalWS);
        float3 tw = MRT_TriWeights(wn);

        // Baked weights, plus an automatic rock term wherever the ground is genuinely steep.
        float rock  = saturate(i.color.x);
        float scree = saturate(i.color.y);
        float soil  = saturate(i.color.z);
        float petal = saturate(i.color.w);

        float slopeDeg = degrees(acos(saturate(wn.y)));
        float slopeRock = smoothstep(_SlopeRockStart, _SlopeRockEnd, slopeDeg);
        rock = saturate(max(rock, slopeRock));

        // Exposed mineral surfaces win over litter, and grass takes whatever is left.
        scree = saturate(scree * (1.0 - rock));
        soil  = saturate(soil * (1.0 - rock - scree));
        float grass = saturate(1.0 - rock - scree - soil);

        // Albedo splats only: the roughness maps sampled below are linear data, not colour,
        // so they must NOT go through the authored-space bridge.
        //
        // GRASS IS DE-TILED. Sampling the grass albedo at two non-harmonic scales and crossfading
        // them under a large low-frequency mask destroys the constant repeat period that makes a
        // hillside read as one stamped texture. 2.71 is deliberately irrational-ish: an integer
        // ratio just produces a second, slower quilt in phase with the first. Costs three extra
        // taps, and only when _DetileAmount is non-zero on the material.
        float4 gA0 = MRT_TriSample(TEXTURE2D_ARGS(_GrassTex, sampler_GrassTex), wp, tw, _GrassScale);
        if (_DetileAmount > 0.001)
        {
            float mask = MR_ValueNoise(wp.xz, 1.0 / max(_DetileBlendM, 1.0));
            mask = smoothstep(0.30, 0.70, mask) * _DetileAmount;
            float4 gA1 = MRT_TriSample(TEXTURE2D_ARGS(_GrassTex, sampler_GrassTex), wp, tw,
                                       _GrassScale * _DetileRatio);
            gA0 = lerp(gA0, gA1, mask);
        }
        float4 gA = MR_Authored(gA0) * MR_AuthoredCol(_GrassColor);
        float4 rA = MR_Authored(MRT_TriSample(TEXTURE2D_ARGS(_RockTex,  sampler_GrassTex), wp, tw, _RockScale))  * MR_AuthoredCol(_RockColor);
        float4 sA = MR_Authored(MRT_TriSample(TEXTURE2D_ARGS(_ScreeTex, sampler_GrassTex), wp, tw, _ScreeScale)) * MR_AuthoredCol(_ScreeColor);
        float4 dA = MR_Authored(MRT_TriSample(TEXTURE2D_ARGS(_SoilTex,  sampler_GrassTex), wp, tw, _SoilScale))  * MR_AuthoredCol(_SoilColor);

        float3 albedo = gA.rgb * grass + rA.rgb * rock + sA.rgb * scree + dA.rgb * soil;

        float gR = MRT_TriSample(TEXTURE2D_ARGS(_GrassRough, sampler_GrassTex), wp, tw, _GrassScale).r;
        float rR = MRT_TriSample(TEXTURE2D_ARGS(_RockRough,  sampler_GrassTex), wp, tw, _RockScale).r;
        float sR = MRT_TriSample(TEXTURE2D_ARGS(_ScreeRough, sampler_GrassTex), wp, tw, _ScreeScale).r;
        float dR = MRT_TriSample(TEXTURE2D_ARGS(_SoilRough,  sampler_GrassTex), wp, tw, _SoilScale).r;
        float rough = gR * grass + rR * rock + sR * scree + dR * soil;

        float3 nrm = normalize(
            MRT_TriNormal(TEXTURE2D_ARGS(_GrassNormal, sampler_GrassTex), wp, tw, _GrassScale) * grass +
            MRT_TriNormal(TEXTURE2D_ARGS(_RockNormal,  sampler_GrassTex), wp, tw, _RockScale)  * rock +
            MRT_TriNormal(TEXTURE2D_ARGS(_ScreeNormal, sampler_GrassTex), wp, tw, _ScreeScale) * scree +
            MRT_TriNormal(TEXTURE2D_ARGS(_SoilNormal,  sampler_GrassTex), wp, tw, _SoilScale)  * soil);
        nrm.xy *= _NormalStrength;
        nrm = normalize(nrm);

        // A very large-scale tint break stops the whole valley reading as one flat colour.
        float3 macroCol = MR_Authored(MRT_TriSample(TEXTURE2D_ARGS(_GrassTex, sampler_GrassTex), wp, tw, 190.0)).rgb;
        float macro = macroCol.g;
        albedo *= lerp(1.0, 0.72 + 0.56 * macro, _MacroVariation);

        // MESO BREAK. Between the 5 m tile and the 190 m macro sits the ~30 m band the eye
        // actually uses to spot a repeat, and nothing was varying there. A cheap procedural
        // value-noise patch shifts both VALUE and HUE (toward warm yellow-green on the dry
        // crowns, cool blue-green in the hollows) so neighbouring tiles never match exactly.
        // No texture tap at all.
        if (_MesoVariation > 0.001)
        {
            float meso = MR_ValueNoise(wp.xz + 137.0, 1.0 / max(_MesoScaleM, 1.0));
            float meso2 = MR_ValueNoise(wp.zx - 61.0, 1.0 / max(_MesoScaleM * 2.9, 1.0));
            float m = (meso * 0.65 + meso2 * 0.35) - 0.5;
            float3 hue = lerp(float3(1.0, 1.0, 1.0),
                              float3(1.0 + m * 0.42, 1.0 + m * 0.10, 1.0 - m * 0.34), _MesoHue);
            albedo *= (1.0 + m * 0.46 * _MesoVariation) * lerp(1.0, hue, _MesoVariation);
        }

        // DISTANCE DETAIL FADE. Tile-scale contrast is what aliases into a moire stripe once a
        // tile is smaller than a pixel; blending toward the 190 m macro colour with distance
        // removes the stripe without touching the near ground the rider actually looks at.
        if (_DetailFadeAmt > 0.001)
        {
            float far = saturate((length(i.positionRWS) - _DetailFadeStart) / max(_DetailFadeRange, 1.0));
            albedo = lerp(albedo, macroCol * MR_AuthoredCol(_GrassColor.rgb), far * _DetailFadeAmt * grass);
        }

        // Moss: a damp film on sheltered ground and on the shaded footings of rock, so it
        // reads strongest low down and on near-vertical faces rather than on open lawn.
        // One extra tap, reusing the grass albedo's green channel as a patch mask.
        float mossPatch = MRT_TriSample(TEXTURE2D_ARGS(_GrassTex, sampler_GrassTex), wp, tw, _MossScale).g;
        float mossFit = saturate(rock * 0.65 + soil * 0.45 + grass * 0.15);
        float moss = saturate(mossPatch * 1.6 - 0.55) * mossFit * _MossStrength;
        albedo = lerp(albedo, albedo * 0.55 + MR_AuthoredCol(_MossColor.rgb) * 0.75, moss);
        rough = lerp(rough, 0.96, moss * 0.6);

        // Sakura petal accumulation: a dusting that settles on sheltered near-flat ground
        // and drifts. The weight already encodes slope and a wind mask, so all that is left
        // here is to break the edge up at close range.
        float petalBreak = MRT_TriSample(TEXTURE2D_ARGS(_ScreeTex, sampler_GrassTex), wp, tw, _PetalScale).r;
        float petalCover = saturate(petal * (0.55 + petalBreak * 0.9)) * _PetalStrength;
        albedo = lerp(albedo, MR_AuthoredCol(_PetalColor.rgb), petalCover);
        rough = lerp(rough, 0.88, petalCover);

        // Altitude grading: cool, pale and desaturated toward the tops.
        float t = saturate((wp.y - _HeightRange.x) / max(0.001, _HeightRange.y - _HeightRange.x));
        albedo = lerp(albedo, albedo * MR_AuthoredCol(_SnowColor.rgb) + MR_AuthoredCol(_SnowColor.rgb) * 0.25, t * _HeightRange.z);

        // Weather snow (no-op unless a snowy region is active and the material accepts it).
        float snowM = MR_SnowMask(i.positionWS, wn, _SnowAccept, _SnowBias);
        albedo = MR_SnowApply(albedo, snowM, i.positionWS);
        rough = lerp(rough, 0.92, snowM);

        float gloss = saturate(1.0 - rough);   // SurfaceOutput.Gloss
        float specMask = saturate(1.0 - rough); // SurfaceOutput.Specular

        float3 n = MRT_TangentToWorld(nrm, wn);
        if (snowM > 0.0) n = MR_SnowNormal(normalize(lerp(n, wn, snowM * 0.85)), i.positionWS, snowM);
        float3 l = MR_SunDirection();
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - wp);

        float ndl = dot(n, l) * 0.5 + 0.5;
        float ramp = MR_CelRamp(ndl, _RampSteps, _RampSmooth);

        // Built-in used smoothstep(_ShadowEdge +/- _ShadowSoft, atten) on the RAW
        // attenuation - never CelRamp a shadow term. There is no `atten` in a
        // hand-written HDRP pass (see the header PENDING note), so the surface is
        // treated as unoccluded; the controls stay wired for when the term returns.
        // CAST SHADOWS (was: hard-coded 1.0, i.e. always unoccluded). sunShadow is HDRP's
        // directional cascade attenuation, sampled in the ForwardOnly pass only.
        float shade = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft, saturate(sunShadow));
        float shadowed = ramp * shade;

        float3 lit = lerp(MR_AuthoredCol(_ShadeColor.rgb) * _ShadeStrength, float3(1, 1, 1), shadowed);
        if (snowM > 0.0) lit = lerp(lit, MR_SnowLit(dot(n, l), shade), snowM);

        // gloss carries the per-pixel roughness map, so wet rock glints and dry grass does not.
        float3 h = normalize(l + v);
        float spec = pow(saturate(dot(n, h)), gloss * 160.0 + 1.0);
        spec = smoothstep(0.4, 0.5, spec) * _SpecStrength * specMask * shadowed;

        float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength * saturate(ndl + 0.25);

        float3 sun = MR_SunColor();
        float3 col = albedo * lit * sun;
        // Shader-owned ambient (the Built-in version used `noambient` + ShadeSH9 for the
        // same reason), occluded by the same shadow term as the direct light. ShadeSH9
        // does not exist outside Built-in; MR_Ambient reconstructs the same trilight.
        col += albedo * MR_Ambient(n) * _AmbientStrength * lerp(_ShadowAmbient, 1.0, shade);
        col += MR_AuthoredCol(_SpecTint.rgb) * spec * sun;
        col += MR_AuthoredCol(_RimColor.rgb) * rim * lerp(_ShadowAmbient, 1.0, shade) * sun;
        if (snowM > 0.0) col += MR_SnowSparkle(i.positionWS, n, v, l, shade) * snowM * sun;

        // HDRP renders into a physically-exposed buffer; a hand-written pass must apply
        // the current exposure itself or it lands at a wildly different brightness than
        // every HDRP-lit surface around it.
        return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 400

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
            #pragma vertex MR_VertTerrain
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

            #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPShadow.hlsl"

            float4 Frag(MRVaryings i) : SV_Target
            {
                float shadow = MR_SunShadow(i.positionRWS, normalize(i.normalWS), i.positionCS.xy);
                return MRT_Shade(i, shadow);
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
            #pragma vertex MR_VertTerrain
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
            #pragma vertex MR_VertTerrain
            #pragma fragment FragShadow
            #pragma multi_compile_instancing

            void FragShadow(MRVaryings i) { }
            ENDHLSL
        }
    }

    Fallback Off
}
