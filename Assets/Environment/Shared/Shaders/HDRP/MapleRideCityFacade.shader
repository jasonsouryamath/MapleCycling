// HDRP replacement for MapleRide/MapleCityFacade.
//
// WHY THIS SHADER EXISTS AND WHY MapleRide/HDRP/CelLit IS NOT A VALID TARGET FOR IT:
// the Built-in original's header spells out both reasons, and both survive the pipeline change.
//
//   * Maple City bakes several hundred buildings into ONE combined mesh (three renderers draw
//     the whole city), so per-building tint is carried in VERTEX COLOUR and nowhere else.
//     CelLit ignores vertex colour, so routing the facades there returns the entire skyline as
//     a single flat texture colour - a failure that compiles, renders and looks wrong.
//   * A flat building wall has one normal, so a cel ramp collapses it to ONE solid colour and
//     the skyline reads as stacked cardboard. Walls need CONTINUOUS light response.
//
// So this is the continuous-lighting sibling of the cel kit: same globals (MapleRideSunBinder),
// same authored-space colour bridge, same fog, same three HDRP passes - but a smooth N.L
// response and a vertex-colour tint. Property names, defaults and ranges are IDENTICAL to the
// Built-in original so a material converts by shader swap with no loss. The Built-in shader is
// left on disk untouched, matching the rest of the migration.
Shader "MapleRide/HDRP/CityFacade"
{
    Properties
    {
        _Color        ("Albedo Tint", Color) = (1,1,1,1)
        _MainTex      ("Albedo", 2D) = "white" {}
        _BumpMap      ("Normal Map", 2D) = "bump" {}
        _BumpStrength ("Normal Strength", Range(0,2)) = 0.45
        _Metallic     ("Metallic", Range(0,1)) = 0
        _Glossiness   ("Smoothness", Range(0,1)) = 0.2
        _Occlusion    ("Occlusion", Range(0,1)) = 1
        _VertexTint   ("Vertex Colour Tint", Range(0,1)) = 1
        // Not on the Built-in original: HDRP hand-written passes get no ambient probe, so the
        // trilight reconstruction needs the same master control every other shader in this kit
        // exposes. Default 1 == the Built-in ambient contribution, i.e. no look change.
        _AmbientStrength ("Ambient Strength", Range(0,2)) = 1.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_BumpMap);
    SAMPLER(sampler_BumpMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _Color;
        float  _BumpStrength, _Metallic, _Glossiness, _Occlusion, _VertexTint;
        float  _AmbientStrength;
        float  _Cull;
    CBUFFER_END

    // Tangent frame from screen-space derivatives.
    //
    // The shared MRAttributes struct carries POSITION/NORMAL/TEXCOORD0/COLOR and no TANGENT.
    // Adding one there would change the vertex contract of every shader in the kit for the sake
    // of one material, so the basis is derived per-pixel instead (standard cotangent-frame
    // construction). The city mesh is flat-walled and uniformly UV'd, which is the case this
    // approximation handles exactly.
    float3 MR_PerturbNormal(float3 n, float3 dpdx, float3 dpdy, float2 duvdx, float2 duvdy, float3 tn)
    {
        float3 dp2perp = cross(dpdy, n);
        float3 dp1perp = cross(n, dpdx);
        float3 T = dp2perp * duvdx.x + dp1perp * duvdy.x;
        float3 B = dp2perp * duvdx.y + dp1perp * duvdy.y;
        float invmax = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-8));
        return normalize(T * (invmax * tn.x) + B * (invmax * tn.y) + n * tn.z);
    }

    float4 MR_ShadeFacade(MRVaryings i, bool frontFace)
    {
        float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;

        float4 c = MR_Authored(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv));
        c.rgb *= MR_AuthoredCol(_Color.rgb);

        // Identical to the Built-in surf(): lerp from white rather than a bare multiply, so a
        // mesh authored with NO vertex colours (Unity hands back opaque black) does not render
        // as a black wall. Vertex colours are raw mesh data and are NOT colour-space converted
        // by Unity, so they need no bridge - they mean the same number in both pipelines.
        c.rgb *= lerp(float3(1, 1, 1), i.color.rgb, _VertexTint);
        c.a *= _Color.a;

        float3 n = normalize(i.normalWS);
        n = frontFace ? n : -n;

        // MapleCity_Facade_Normal.png is imported as a DEFAULT texture with sRGBTexture=1, not
        // as a Unity normal map. Under the old Gamma project its bytes were sampled verbatim;
        // under HDRP's mandatory Linear space the same sample is sRGB-decoded, which bends every
        // normal toward +Z and flattens the brick/panel relief. MR_Authored undoes exactly that
        // decode, so the vectors are the ones the texture actually stores.
        float4 nsample = MR_Authored(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv));
        float3 tn;
        tn.xy = (nsample.xy * 2.0 - 1.0) * _BumpStrength;   // matches `o.Normal.xy *= _BumpStrength`
        tn.z = sqrt(saturate(1.0 - saturate(dot(tn.xy, tn.xy))));
        n = MR_PerturbNormal(n, ddx(i.positionWS), ddy(i.positionWS), ddx(uv), ddy(uv), tn);

        float3 l = MR_SunDirection();
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);
        float3 sun = MR_SunColor();

        // CONTINUOUS diffuse - the entire point of this shader. No MR_CelRamp here.
        float ndl = saturate(dot(n, l));

        // Specular: Blinn-Phong standing in for the Standard BRDF. Dielectrics keep a neutral
        // 0.04 highlight and metals tint theirs by albedo, which is the one Standard behaviour
        // the facades actually rely on (_Metallic is 0 on the shipped material, so this is
        // effectively the dielectric path).
        float3 h = normalize(l + v);
        float power = exp2(_Glossiness * 10.0) + 1.0;
        float spec = pow(saturate(dot(n, h)), power) * ndl * (_Glossiness * 0.8 + 0.2);
        float3 specCol = lerp(float3(0.04, 0.04, 0.04), c.rgb, _Metallic);
        float3 diffuseAlbedo = c.rgb * (1.0 - _Metallic * 0.85);

        float3 col = diffuseAlbedo * sun * ndl;
        col += diffuseAlbedo * MR_Ambient(n) * _AmbientStrength * _Occlusion;
        col += specCol * spec * sun;

        // Exposure: HDRP composites into a physically-exposed buffer, so a hand-written pass
        // must apply the current exposure itself or it lands at a different brightness than
        // every HDRP-lit surface beside it.
        return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), c.a);
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

            float4 Frag(MRVaryings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                return MR_ShadeFacade(i, frontFace);
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

        // The Built-in original asked for `addshadow`; this preserves the city casting into
        // HDRP's shadow maps.
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
