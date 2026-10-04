// Terrain surface for Sakura Pass (Built-in Render Pipeline).
//
// Three tiling PBR layers - grass, rock and scree - blended by weights the Blender terrain
// builder baked into the mesh's second UV channel (uv1.x = rock, uv1.y = scree, grass is the
// remainder). Weights travel through UV rather than vertex colour because glTF runs COLOR_0
// through an sRGB conversion that would corrupt them.
//
// Everything is projected triplanar from world space. A single planar XZ projection is what
// made the grass visibly smear into vertical streaks down the cut slopes; triplanar costs
// three taps per layer but the valley walls are most of what the rider actually looks at.
Shader "MapleRide/SakuraTerrain"
{
    Properties
    {
        [Header(Grass)]
        _GrassTex       ("Grass Albedo", 2D) = "white" {}
        _GrassNormal    ("Grass Normal", 2D) = "bump" {}
        _GrassRough     ("Grass Roughness", 2D) = "gray" {}
        _GrassColor     ("Grass Tint", Color) = (1,1,1,1)
        _GrassScale     ("Grass Tiling (m per tile)", Float) = 6.0

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

        [Header(Cel lighting)]
        _ShadeColor     ("Shade Tint", Color) = (0.40,0.46,0.70,1)
        _ShadeStrength  ("Shade Strength", Range(0,1)) = 0.70
        _RampSteps      ("Ramp Steps", Range(2,6)) = 4
        _RampSmooth     ("Ramp Softness", Range(0.002,0.35)) = 0.07
        // Section 24 fix, ported from SakuraCel - this shader carried the identical pair of
        // defects. (1) A surface shader adds ambient in ForwardBase UNATTENUATED, so under this
        // project's high-key Trilight ambient a shadowed pixel is lifted straight back to the
        // lit value and cast shadows are rendered but invisible; the shader therefore takes
        // ownership of ambient via 'noambient' on the #pragma and occludes it with the shadow
        // term. (2) CelRamp(atten) returns (band + soft) / steps, so a FULLY occluded pixel
        // (atten 0.22) came back as 0.33, not 0, and the shade floor never applied. A shadow
        // term must use a clean step on the raw attenuation. All values PROVISIONAL.
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

        // Face culling, exposed so that geometry the CAMERA RIDES THROUGH (the Shiosai sea-arch
        // gate, tunnel shells, any rock ring) can be made double-sided. Defaults to Back, so
        // every existing terrain material is bit-for-bit unchanged.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 400
        Cull [_Cull]

        CGPROGRAM
        // exclude_path:deferred - the project defaults to deferred, which cannot run a custom
        // lighting model. Forcing forward keeps the cel ramp.
        #pragma surface surf SakuraTerrain fullforwardshadows exclude_path:deferred noambient vertex:vert
        #pragma target 3.5

        sampler2D _GrassTex, _GrassNormal, _GrassRough;
        sampler2D _RockTex,  _RockNormal,  _RockRough;
        sampler2D _ScreeTex, _ScreeNormal, _ScreeRough;
        sampler2D _SoilTex,  _SoilNormal,  _SoilRough;
        fixed4 _GrassColor, _RockColor, _ScreeColor, _SoilColor;
        float _GrassScale, _RockScale, _ScreeScale, _SoilScale;

        fixed4 _MossColor, _PetalColor;
        half _MossStrength, _PetalStrength;
        float _MossScale, _PetalScale;

        half _SlopeRockStart, _SlopeRockEnd, _MacroVariation, _NormalStrength;

        fixed4 _ShadeColor, _RimColor, _SpecTint, _SnowColor;
        half _ShadeStrength, _RampSteps, _RampSmooth, _RimPower, _RimStrength, _SpecStrength;
        half _AmbientStrength, _ShadowAmbient, _ShadowEdge, _ShadowSoft;
        float4 _HeightRange;

        struct Input
        {
            float2 uv_GrassTex;     // UV0 - the planar XZ layout, kept for compatibility
            float2 uv2_RockTex;     // UV1 - (rock, scree) splat weights baked in Blender
            float2 splat2;          // UV2 - (soil, petal); see vert(), below
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            INTERNAL_DATA
        };

        // Surface shaders only expose the first two UV sets through the uv_/uv2_ convention, so
        // the third set - which carries the soil and petal weights - has to be forwarded by
        // hand. Weights travel through UV rather than vertex colour because glTF runs COLOR_0
        // through an sRGB conversion that would corrupt them.
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.splat2 = v.texcoord2.xy;
        }

        // ------------------------------------------------------------------ triplanar

        // Weights for the three world-axis projections, sharpened so the transition band is
        // narrow enough not to wash out detail but wide enough to hide the seam.
        half3 TriWeights(half3 n)
        {
            half3 w = pow(abs(n), 5.0);
            return w / max(w.x + w.y + w.z, 1e-4);
        }

        fixed4 TriSample(sampler2D tex, float3 wp, half3 w, float scale)
        {
            float inv = 1.0 / max(scale, 0.001);
            fixed4 x = tex2D(tex, wp.zy * inv);
            fixed4 y = tex2D(tex, wp.xz * inv);
            fixed4 z = tex2D(tex, wp.xy * inv);
            return x * w.x + y * w.y + z * w.z;
        }

        // Tangent-space normal from a triplanar sample. The terrain's UV0 is a planar XZ
        // layout, so its tangent frame already lines up with the Y projection; the X and Z
        // projections are swizzled to match rather than doing a full world-space rebuild.
        half3 TriNormal(sampler2D tex, float3 wp, half3 w, float scale)
        {
            float inv = 1.0 / max(scale, 0.001);
            half3 x = UnpackNormal(tex2D(tex, wp.zy * inv));
            half3 y = UnpackNormal(tex2D(tex, wp.xz * inv));
            half3 z = UnpackNormal(tex2D(tex, wp.xy * inv));
            return normalize(x.zyx * w.x + y * w.y + z.xzy * w.z);
        }

        // ------------------------------------------------------------------ lighting

        half CelRamp(half value)
        {
            half steps = max(2.0, _RampSteps);
            half scaled = saturate(value) * steps;
            half band = floor(scaled);
            half f = scaled - band;
            half soft = smoothstep(0.5 - _RampSmooth, 0.5 + _RampSmooth, f);
            return saturate((band + soft) / steps);
        }

        half4 LightingSakuraTerrain(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half3 n = normalize(s.Normal);
            half3 v = normalize(viewDir);
            half ndl = dot(n, lightDir) * 0.5 + 0.5;
            half ramp = CelRamp(ndl);
            // Clean step on the RAW attenuation - never CelRamp a shadow term (see header).
            half shade = smoothstep(_ShadowEdge - _ShadowSoft, _ShadowEdge + _ShadowSoft, atten);
            half shadowed = ramp * shade;

            half3 lit = lerp(_ShadeColor.rgb * _ShadeStrength, half3(1,1,1), shadowed);

            // s.Gloss carries the per-pixel roughness map, so wet rock glints and dry grass does not.
            half3 h = normalize(lightDir + v);
            half spec = pow(saturate(dot(n, h)), s.Gloss * 160.0 + 1.0);
            spec = smoothstep(0.4, 0.5, spec) * _SpecStrength * s.Specular * shadowed;

            half rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength * saturate(ndl + 0.25);

            half3 col = s.Albedo * lit * _LightColor0.rgb;
            // Additive lights contribute attenuated diffuse only - see SakuraCel.shader for why
            // re-adding the shade floor, ambient and rim per light paints a screen-space slab.
            #ifdef UNITY_PASS_FORWARDADD
                return half4(s.Albedo * ramp * atten * _LightColor0.rgb, s.Alpha);
            #endif
            // Shader-owned ambient, occluded by the same shadow term as the direct light.
            col += s.Albedo * ShadeSH9(half4(n, 1)) * _AmbientStrength
                 * lerp(_ShadowAmbient, 1.0, shade);
            col += _SpecTint.rgb * spec * _LightColor0.rgb;
            col += _RimColor.rgb * rim * lerp(_ShadowAmbient, 1.0, shade) * _LightColor0.rgb;
            return half4(col, s.Alpha);
        }

        // ------------------------------------------------------------------ surface

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 wp = IN.worldPos;
            half3 wn = normalize(WorldNormalVector(IN, half3(0,0,1)));
            half3 tw = TriWeights(wn);

            // Baked weights, plus an automatic rock term wherever the ground is genuinely steep.
            half rock  = saturate(IN.uv2_RockTex.x);
            half scree = saturate(IN.uv2_RockTex.y);
            half soil  = saturate(IN.splat2.x);
            half petal = saturate(IN.splat2.y);

            half slopeDeg = degrees(acos(saturate(wn.y)));
            half slopeRock = smoothstep(_SlopeRockStart, _SlopeRockEnd, slopeDeg);
            rock = saturate(max(rock, slopeRock));

            // Exposed mineral surfaces win over litter, and grass takes whatever is left.
            scree = saturate(scree * (1.0 - rock));
            soil  = saturate(soil * (1.0 - rock - scree));
            half grass = saturate(1.0 - rock - scree - soil);

            fixed4 gA = TriSample(_GrassTex, wp, tw, _GrassScale) * _GrassColor;
            fixed4 rA = TriSample(_RockTex,  wp, tw, _RockScale)  * _RockColor;
            fixed4 sA = TriSample(_ScreeTex, wp, tw, _ScreeScale) * _ScreeColor;
            fixed4 dA = TriSample(_SoilTex,  wp, tw, _SoilScale)  * _SoilColor;

            fixed3 albedo = gA.rgb * grass + rA.rgb * rock + sA.rgb * scree + dA.rgb * soil;

            half gR = TriSample(_GrassRough, wp, tw, _GrassScale).r;
            half rR = TriSample(_RockRough,  wp, tw, _RockScale).r;
            half sR = TriSample(_ScreeRough, wp, tw, _ScreeScale).r;
            half dR = TriSample(_SoilRough,  wp, tw, _SoilScale).r;
            half rough = gR * grass + rR * rock + sR * scree + dR * soil;

            half3 nrm = normalize(
                TriNormal(_GrassNormal, wp, tw, _GrassScale) * grass +
                TriNormal(_RockNormal,  wp, tw, _RockScale)  * rock +
                TriNormal(_ScreeNormal, wp, tw, _ScreeScale) * scree +
                TriNormal(_SoilNormal,  wp, tw, _SoilScale)  * soil);
            nrm.xy *= _NormalStrength;

            // A very large-scale tint break stops the whole valley reading as one flat colour.
            half macro = TriSample(_GrassTex, wp, tw, 190.0).g;
            albedo *= lerp(1.0, 0.72 + 0.56 * macro, _MacroVariation);

            // Moss: a damp film on sheltered ground and on the shaded footings of rock, so it
            // reads strongest low down and on near-vertical faces rather than on open lawn.
            // One extra tap, reusing the grass albedo's green channel as a patch mask.
            half mossPatch = TriSample(_GrassTex, wp, tw, _MossScale).g;
            half mossFit = saturate(rock * 0.65 + soil * 0.45 + grass * 0.15);
            half moss = saturate(mossPatch * 1.6 - 0.55) * mossFit * _MossStrength;
            albedo = lerp(albedo, albedo * 0.55 + _MossColor.rgb * 0.75, moss);
            rough = lerp(rough, 0.96, moss * 0.6);

            // Sakura petal accumulation: a dusting that settles on sheltered near-flat ground
            // and drifts. The weight already encodes slope and a wind mask, so all that is left
            // here is to break the edge up at close range.
            half petalBreak = TriSample(_ScreeTex, wp, tw, _PetalScale).r;
            half petalCover = saturate(petal * (0.55 + petalBreak * 0.9)) * _PetalStrength;
            albedo = lerp(albedo, _PetalColor.rgb, petalCover);
            rough = lerp(rough, 0.88, petalCover);

            // Altitude grading: cool, pale and desaturated toward the tops.
            half t = saturate((wp.y - _HeightRange.x) / max(0.001, _HeightRange.y - _HeightRange.x));
            albedo = lerp(albedo, albedo * _SnowColor.rgb + _SnowColor.rgb * 0.25, t * _HeightRange.z);

            o.Albedo = albedo;
            o.Normal = normalize(nrm);
            o.Gloss = saturate(1.0 - rough);
            o.Specular = saturate(1.0 - rough);
            o.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
