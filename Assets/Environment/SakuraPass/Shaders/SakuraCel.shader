// Cel / anime lighting for the Sakura Pass environment kit (Built-in Render Pipeline).
// Banded diffuse ramp + rim light + soft specular, with real shadow casting/receiving and fog.
Shader "MapleRide/SakuraCel"
{
    Properties
    {
        _Color            ("Base Color", Color) = (1,1,1,1)
        _MainTex          ("Albedo", 2D) = "white" {}
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
        // --- Part B (section 24) shadow response -------------------------------------------
        // Measured, not guessed: contrast_atten.png shows crisp blossom-shaped canopy dapple on
        // the carriageway while contrast_beauty.png (same camera, same frame) shows none of it.
        // Cause: a surface shader adds ambient in the ForwardBase pass UNATTENUATED, so under
        // this project's deliberately high-key Trilight ambient the shadowed term is lifted
        // straight back to the lit term and the dapple is rendered but invisible. The shader
        // therefore takes ownership of ambient (see 'noambient' on the #pragma below) and
        // occludes it with the same shadow term as the direct light.
        // Both values are PROVISIONAL tuning knobs, not spec requirements.
        _AmbientStrength  ("Ambient Strength", Range(0,2)) = 1.0
        _ShadowAmbient    ("Ambient Kept In Shadow", Range(0,1)) = 0.38
        _ShadowEdge       ("Shadow Step Midpoint", Range(0.05,0.95)) = 0.5
        // Section 24 polish: the cast-shadow step needs its own softness. Sharing _RampSmooth
        // (tuned for the N-dot-L cel bands) made canopy dapple read as hard black decals.
        // PROVISIONAL tuning value.
        _ShadowSoft       ("Shadow Step Softness", Range(0.01,0.45)) = 0.18
        // Canopy dapple: the built-in shadow map gives one continuous atten value per pixel, so
        // a tree's cast shadow is a single coherent soft-edged shape with a flat dark interior -
        // real leaf-canopy shadow is interleaved light/dark speckle instead. A small-scale value
        // noise sampled on world XZ punches scattered "light leaks" back through the shadow
        // interior (never darkens the lit side, never touches the soft penumbra edge shape).
        // PROVISIONAL tuning knobs.
        _DappleScale      ("Canopy Dapple Scale (per m)", Range(0.5,8)) = 2.6
        _DappleStrength   ("Canopy Dapple Strength", Range(0,1)) = 0.35
        _HeightTint       ("Height Tint Color", Color) = (1,1,1,1)
        _HeightRange      ("Height Tint (start, end, amount, unused)", Vector) = (0,1,0,0)
        // Snow cap. Splitting snow from rock with two material slots quantises the boundary to
        // whole faces, which on distant geometry reads as a row of rectangular "comb teeth"
        // (a face on the hero volcano is ~12 px on screen). Blending by world height in the
        // shader instead gives a sub-face-accurate, softenable line. Amount 0 = disabled.
        _SnowColor        ("Snow Color", Color) = (1,1,1,1)
        _SnowRange        ("Snow (start, end, amount, unused)", Vector) = (0,1,0,0)
        // --- Part B (section 30) prop weathering ---------------------------------------------
        // Section 30 asks every near-road hard-surface prop for normal detail, roughness
        // variation, edge wear, dirt/moss and subtle colour variation, and explicitly asks for
        // "modular reusable sets instead of dozens of unique one-off assets". Every prop in this
        // kit already shares this one shader through CelMaterial(), so the whole requirement is
        // met here rather than by re-authoring stone walls, lanterns, torii, guardrails, chevrons
        // and signage individually.
        //
        // _WeatherAmount is the master switch and defaults to 0, so every existing surface that
        // does not opt in renders bit-identically to before.
        //
        // KNOWN LIMITATION - normal detail is done as a shading-gradient term, not a detail
        // normal map. See the comment in surf(): this shader must never write o.Normal, because
        // doing so forces Unity's tangent-space normal path and these procedurally generated
        // meshes carry no reliable tangent basis (it blew out every lit surface to near-white
        // once already). The weathering terms therefore work in albedo and in the world-normal
        // gradient, which under a banded cel ramp reads the same way at gameplay distance.
        // All values below are PROVISIONAL tuning.
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
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull [_Cull]

        CGPROGRAM
        // exclude_path:deferred keeps the custom ramp lighting intact under the project's deferred default.
        // 'noambient' is load-bearing: it suppresses the framework's unattenuated ambient add so
        // the lighting function can apply a shadow-occluded ambient instead. Removing it silently
        // flattens every shadow in the scene back out again.
        #pragma surface surf SakuraCel fullforwardshadows exclude_path:deferred noambient
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _ShadeColor;
        half _ShadeStrength;
        half _RampSteps;
        half _RampSmooth;
        half _RampOffset;
        fixed4 _RimColor;
        half _RimPower;
        half _RimStrength;
        fixed4 _SpecTint;
        half _Gloss;
        half _SpecStrength;
        half _AmbientStrength;
        half _ShadowAmbient;
        half _ShadowEdge;
        half _ShadowSoft;
        half _DappleScale;
        half _DappleStrength;
        fixed4 _HeightTint;
        float4 _HeightRange;
        fixed4 _SnowColor;
        float4 _SnowRange;
        half _WeatherAmount;
        fixed4 _MossColor;
        half _MossAmount, _MossHeight, _MossBase, _GrimeAmount;
        fixed4 _WearColor;
        half _WearAmount, _DetailScale, _DetailAmount, _TintVariation, _TintVarScale;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;   // read-only: surf() never writes o.Normal, see below
            float3 viewDir;
        };

        // Custom surface output that carries world position through to the lighting function -
        // the stock SurfaceOutput has no such field, and the canopy dapple noise needs a stable
        // world-space coordinate to sample (screen-space or UV-space noise would swim as the
        // camera moves and would not line up between the road and the shadow caster).
        struct SurfaceOutputCel
        {
            fixed3 Albedo;
            fixed3 Normal;
            fixed3 Emission;   // unused by LightingSakuraCel, but Unity's generated surface-shader
                               // wrapper always emits "c.rgb += o.Emission;" regardless of custom
                               // lighting model, so a custom output struct must still declare it
                               // (its absence was a genuine compile error: "invalid subscript
                               // 'Emission'" - the shader silently fell back to garbage/undefined
                               // per-pixel output, which is what actually washed every
                               // SakuraCel-lit surface out near-white).
            fixed Alpha;
            float3 WorldPos;
        };

        // Quantises a 0..1 value into N soft bands so lighting reads as painted cel shading.
        half CelRamp(half value)
        {
            half steps = max(2.0, _RampSteps);
            half scaled = saturate(value) * steps;
            half band = floor(scaled);
            half f = scaled - band;
            half soft = smoothstep(0.5 - _RampSmooth, 0.5 + _RampSmooth, f);
            return saturate((band + soft) / steps);
        }

        // Cheap value-noise hash, tiled on world XZ. Deliberately non-axis-aligned frequencies
        // are not needed here (single octave, no interference risk like the water's foam), but
        // the smooth bilinear interpolation keeps the speckle organic instead of a hard mosaic.
        half Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        half CanopyDappleNoise(float2 worldXZ, half scale)
        {
            float2 p = worldXZ * scale;
            float2 i = floor(p);
            float2 f = frac(p);
            half a = Hash21(i);
            half b = Hash21(i + float2(1.0, 0.0));
            half c = Hash21(i + float2(0.0, 1.0));
            half d = Hash21(i + float2(1.0, 1.0));
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        // Triplanar value noise, built from the existing 2D noise on the three world planes.
        // Props are arbitrary hard-surface geometry with no usable UV layout, so world-space
        // projection is the only way to get consistent grain across a wall, a post and a cap.
        half TriNoise(float3 wp, half3 an, half scale)
        {
            half x = CanopyDappleNoise(wp.zy, scale);
            half y = CanopyDappleNoise(wp.xz, scale);
            half z = CanopyDappleNoise(wp.xy, scale);
            return x * an.x + y * an.y + z * an.z;
        }

        // Section 30 weathering. Four effects, in the order they physically accumulate:
        // surface detail -> per-prop colour variation -> sun bleach on up-facing wear edges ->
        // moss in the damp, sheltered, low, up-facing places -> grime on undersides.
        fixed3 WeatherSurface(fixed3 albedo, float3 wp, half3 wn)
        {
            half3 an = abs(wn);
            an /= max(an.x + an.y + an.z, 1e-4);

            // Surface detail / roughness break. A fine triplanar mottle plus a coarser one an
            // octave down, so the prop stops being a perfectly uniform flat fill without
            // reading as noise. This is the honest stand-in for a detail normal map.
            half fine = TriNoise(wp, an, _DetailScale);
            half coarse = TriNoise(wp, an, _DetailScale * 0.27);
            half detail = (fine * 0.62 + coarse * 0.38) * 2.0 - 1.0;
            albedo *= 1.0 + detail * _DetailAmount * 0.34;

            // Subtle per-prop colour variation. Hashing the object origin would be the obvious
            // route, but this kit's staged glTF meshes carry world-space vertices on identity
            // transforms, so their object origins are all (0,0,0) and would hash identically.
            // A very low frequency world-space tint achieves the same "no two props match" read
            // and works for both staged landmarks and scattered instances.
            half3 var3 = half3(TriNoise(wp + 11.3, an, _TintVarScale),
                               TriNoise(wp + 37.1, an, _TintVarScale),
                               TriNoise(wp + 71.7, an, _TintVarScale)) * 2.0 - 1.0;
            albedo *= 1.0 + var3 * _TintVariation * 0.30;

            // Sun bleach / edge wear: up-facing surfaces and exposed arrises lose pigment. The
            // up-facing term is modulated by the detail noise so the wear follows the grain
            // instead of forming a clean horizontal band.
            half up = saturate(wn.y);
            half wear = up * up * saturate(0.45 + fine * 0.9) * _WearAmount;
            albedo = lerp(albedo, albedo * 0.55 + _WearColor.rgb * 0.62, wear);

            // Moss / damp: low, up-facing, sheltered. Broken up by its own patch mask so it
            // drifts across a stone base rather than coating it evenly.
            half low = 1.0 - saturate((wp.y - _MossBase) / max(0.05, _MossHeight));
            half patch = saturate(TriNoise(wp, an, _DetailScale * 0.45) * 1.9 - 0.62);
            half moss = saturate(up * 0.75 + 0.25) * low * patch * _MossAmount;
            albedo = lerp(albedo, albedo * 0.42 + _MossColor.rgb * 0.80, moss);

            // Grime on undersides and in the shadowed rebates that catch road dirt.
            half down = saturate(-wn.y);
            albedo *= 1.0 - down * saturate(0.4 + coarse * 0.8) * _GrimeAmount * 0.45;

            return albedo;
        }

        half4 LightingSakuraCel(SurfaceOutputCel s, half3 lightDir, half3 viewDir, half atten)
        {
            half3 n = normalize(s.Normal);
            half3 v = normalize(viewDir);
            half ndl = dot(n, lightDir) * 0.5 + 0.5;          // wrapped diffuse keeps terminators soft
            half ramp = CelRamp(saturate(ndl + _RampOffset));
            // The shadow term must NOT go through CelRamp. CelRamp quantises into N bands as
            // (band + soft) / steps, so at _RampSteps=3 a fully-occluded pixel (atten = 1 - 0.78
            // = 0.22) lands just above the first band edge and returns 0.333 instead of 0 - the
            // shade floor then never applies and dapple reads as a faint smudge. A clean step on
            // the raw attenuation gives a true sunlit / in-shadow decision.
            half shadeBase = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft, atten);
            // Canopy dapple speckle: punches scattered light back through the shadow interior.
            // Additive on top of shadeBase and clamped at 1, so it can only brighten pixels that
            // are already inside the shadow (shadeBase < 1) - the lit side and the soft penumbra
            // edge shape are untouched, only the flat dark interior gets broken up.
            half dapple = CanopyDappleNoise(s.WorldPos.xz, _DappleScale);
            half shade = saturate(shadeBase + (1.0 - shadeBase) * dapple * _DappleStrength);
            half shadowed = ramp * shade;

            // Shaded side keeps a cool tint instead of collapsing to black.
            half3 lit = lerp(_ShadeColor.rgb * _ShadeStrength, half3(1,1,1), shadowed);

            half3 h = normalize(lightDir + v);
            half spec = pow(saturate(dot(n, h)), _Gloss * 128.0 + 1.0);
            spec = smoothstep(0.35, 0.45, spec) * _SpecStrength * shadowed;

            half rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
            rim *= _RimStrength * saturate(ndl + 0.25);

            half3 col = s.Albedo * lit * _LightColor0.rgb;

            // ForwardAdd runs this function once per per-pixel additive light. Unity draws that
            // pass across the light's screen-space scissor rectangle, so every term that does
            // not vanish as `atten` goes to zero is emitted again over that whole rectangle -
            // which is what painted a hard, axis-aligned, washed-out slab across the climb once
            // the tunnel lamps came into range. The shade floor, the shader-owned ambient and
            // the rim are all base-pass decoration and must not be re-added per light; an
            // additive light contributes attenuated diffuse only.
            #ifdef UNITY_PASS_FORWARDADD
                return half4(s.Albedo * ramp * atten * _LightColor0.rgb, s.Alpha);
            #endif

            // Shader-owned ambient (see 'noambient'). Occluding it with the shadow term is what
            // lets canopy dapple survive to the screen instead of being lifted back to flat.
            half3 amb = ShadeSH9(half4(n, 1)) * _AmbientStrength * lerp(_ShadowAmbient, 1.0, shade);
            col += s.Albedo * amb;

            // Spec and rim are shaped decoration, but leaving them unattenuated re-lifts the
            // shaded side; the rim in particular was reading as a permanent wrap-around glow.
            col += _SpecTint.rgb * spec * _LightColor0.rgb;
            col += _RimColor.rgb * rim * lerp(_ShadowAmbient, 1.0, shade) * _LightColor0.rgb;

            return half4(col, s.Alpha);
        }

        void surf(Input IN, inout SurfaceOutputCel o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            // Optional world-height gradient: lets one material read as wet valley floor -> dry ridge.
            half t = saturate((IN.worldPos.y - _HeightRange.x) / max(0.001, _HeightRange.y - _HeightRange.x));
            c.rgb = lerp(c.rgb, c.rgb * _HeightTint.rgb, t * _HeightRange.z);

            // Snow goes on after the height tint so the cap is not darkened by it.
            half s = smoothstep(_SnowRange.x, _SnowRange.y, IN.worldPos.y) * _SnowRange.z;
            c.rgb = lerp(c.rgb, _SnowColor.rgb, s);

            // Section 30 prop weathering. Skipped entirely unless the material opts in, so
            // terrain-adjacent and backdrop surfaces pay nothing and look unchanged.
            if (_WeatherAmount > 0.001)
            {
                fixed3 weathered = WeatherSurface(c.rgb, IN.worldPos, normalize(IN.worldNormal));
                c.rgb = lerp(c.rgb, weathered, _WeatherAmount);
            }

            o.Albedo = c.rgb;
            // Deliberately NOT writing o.Normal: doing so (even to a constant) forces Unity's
            // surface-shader compiler into tangent-space normal mode, which requires a valid
            // per-vertex tangent basis. This kit's procedurally generated env meshes don't
            // reliably carry that, and turning it on produced garbage per-pixel normals that
            // washed every SakuraCel-lit surface out near-white (QA climb-mark regression).
            // Leaving Normal untouched keeps the framework's direct world-space vertex normal
            // path, matching this shader's original (correct) behaviour.
            o.Alpha = c.a;
            o.WorldPos = IN.worldPos;
            o.Emission = half3(0, 0, 0);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
