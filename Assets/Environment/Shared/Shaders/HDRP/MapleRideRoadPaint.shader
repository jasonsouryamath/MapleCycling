// MapleRide/HDRP/RoadPaint - line markings (区画線).
//
// WHY THIS EXISTS
// The markings GLB was staged with NO override material, so the stripes kept the glTF
// importer's Shader Graphs/glTF-pbrMetallicRoughness. That shader survives under HDRP, which
// is exactly why the problem was invisible in a shader census: it renders, but it renders a
// near-white metallic-rough surface with no diffuse tint of its own, so under the pass's blue
// sky ambient the edge lines came out BLUE - the "simplistic blue edge lines" in the user's
// screenshot. Paint is one of the most colour-critical surfaces on the road; it needs to be
// authored, not inherited from an importer default.
//
// What this adds over a flat colour:
//   * matte diffuse response (traffic paint is not a mirror; specular is nearly killed)
//   * longitudinal wear so the line thins and greys where traffic crosses it
//   * grimy paint edges, which is what stops a stripe reading as a decal
//   * a faint retroreflective bead sparkle at grazing angles
//
// UV CONTRACT (build_road.py::_stripe): u = 0..1 ACROSS the stripe, v = arc metres * 0.5.
// _MetresPerV converts back, so wear is sized in real metres regardless of stripe width.
Shader "MapleRide/HDRP/RoadPaint"
{
    Properties
    {
        _Color          ("Paint Color", Color) = (0.92,0.92,0.89,1)
        _WornColor      ("Worn-Through Color", Color) = (0.42,0.41,0.40,1)
        _GrimeColor     ("Edge Grime Color", Color) = (0.55,0.53,0.50,1)

        _WearAmount     ("Wear Amount", Range(0,1)) = 0.35
        _WearScale      ("Wear Scale (per m)", Float) = 0.45
        _EdgeGrime      ("Edge Grime", Range(0,1)) = 0.40
        _MetresPerV     ("Metres Per V Unit", Float) = 2.0

        _BeadSparkle    ("Retroreflective Bead Sparkle", Range(0,2)) = 0.35
        _Gloss          ("Gloss", Range(0.01,1)) = 0.12
        _SpecStrength   ("Specular Strength", Range(0,2)) = 0.06

        _ShadeColor     ("Shade Tint", Color) = (0.72,0.70,0.74,1)
        _ShadeStrength  ("Shade Strength", Range(0,1)) = 0.55
        _AmbientStrength("Ambient Strength", Range(0,2)) = 1.0
        _ShadowAmbient  ("Ambient Kept In Shadow", Range(0,1)) = 0.38
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Color, _WornColor, _GrimeColor, _ShadeColor;
        float  _WearAmount, _WearScale, _EdgeGrime, _MetresPerV;
        float  _BeadSparkle, _Gloss, _SpecStrength;
        float  _ShadeStrength, _AmbientStrength, _ShadowAmbient;
        float  _Cull;
    CBUFFER_END

    float4 MR_ShadePaint(MRVaryings i, bool frontFace)
    {
        float3 paint = MR_AuthoredCol(_Color.rgb);
        float3 worn  = MR_AuthoredCol(_WornColor.rgb);
        float3 grime = MR_AuthoredCol(_GrimeColor.rgb);
        float3 tintShade = MR_AuthoredCol(_ShadeColor.rgb);

        float along = i.uv.y * _MetresPerV;           // metres along the stripe
        float across = saturate(i.uv.x);              // 0..1 across it

        // Wear: two octaves along the line, so it thins in runs of a few metres rather than
        // flickering per pixel. Wear is strongest in the middle of the stripe, where tyres
        // actually track over it, and the paint survives at the edges.
        float w = MR_ValueNoise(float2(along, 0.5), _WearScale);
        w += MR_ValueNoise(float2(along + 53.1, 3.7), _WearScale * 3.3) * 0.5;
        w /= 1.5;
        float centreBias = 1.0 - abs(across - 0.5) * 1.4;
        float wear = saturate((w - 0.42) * 2.3) * saturate(centreBias) * _WearAmount;
        float3 c = lerp(paint, worn, wear);

        // Edge grime: dirt and rubber collect against the raised paint edge.
        float edge = saturate(1.0 - abs(across - 0.5) * 2.0);      // 1 at centre, 0 at edges
        float dirt = (1.0 - smoothstep(0.0, 0.22, edge)) * _EdgeGrime;
        c = lerp(c, grime, dirt * 0.75);

        float3 n = normalize(i.normalWS);
        n = frontFace ? n : -n;
        float3 l = MR_SunDirection();
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);

        // Matte, smooth (NOT cel-banded) response: banded paint reads as a sticker.
        float ndl = saturate(dot(n, l) * 0.5 + 0.5);
        float3 lit = lerp(tintShade * _ShadeStrength, float3(1, 1, 1), ndl);

        float3 h = normalize(l + v);
        float spec = pow(saturate(dot(n, h)), _Gloss * 160.0 + 8.0) * _SpecStrength * ndl;

        // Glass beads: high-frequency sparkle that only shows up at a grazing angle, which is
        // exactly how a rider sees a line 20 m ahead.
        float beads = MR_ValueNoise(float2(along * 6.0, across * 40.0), 3.0);
        float graze = pow(1.0 - saturate(dot(n, v)), 3.0);
        float sparkle = saturate(beads * 1.6 - 0.75) * graze * _BeadSparkle * (1.0 - wear);

        float3 sun = MR_SunColor();
        float3 col = c * lit * sun;
        col += c * MR_Ambient(n) * _AmbientStrength * _ShadowAmbient;
        col += spec * sun;
        col += c * sparkle * sun;

        return float4(MR_ToRender(MR_ApplyFog(col, i.positionRWS)) * GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+1" }

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
                return MR_ShadePaint(i, frontFace);
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
