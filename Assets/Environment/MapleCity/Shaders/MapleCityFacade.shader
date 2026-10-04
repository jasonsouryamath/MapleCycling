// MAPLE CITY facade material.
//
// Deliberately separate from both MapleRide/SakuraCel and MapleRide/ShiosaiArchitecture:
//
//   * SakuraCel is a three-band cel ramp. A flat building wall has one normal, so a cel ramp
//     collapses it to ONE solid colour and a whole skyline reads as stacked cardboard. Walls
//     need continuous light response, which is the same conclusion ShiosaiArchitecture reached.
//
//   * ShiosaiArchitecture is that continuous material, and would otherwise be reusable - except
//     that its Input struct is { float2 uv_MainTex; } and it therefore IGNORES VERTEX COLOUR.
//     Maple City bakes several hundred buildings into ONE combined mesh (three renderers draw
//     the entire city), so the ONLY channel left to carry per-building tint is vertex colour.
//     Painted through ShiosaiArchitecture the whole skyline would come back as a single flat
//     texture colour - a silent failure that compiles, renders, and looks wrong.
//
// So: ShiosaiArchitecture plus a COLOR input, and nothing else. Every tuning knob keeps the
// coast shader's name and range so the two stay swappable.
Shader "MapleRide/MapleCityFacade"
{
    Properties
    {
        _Color ("Albedo Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpStrength ("Normal Strength", Range(0,2)) = 0.45
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _Occlusion ("Occlusion", Range(0,1)) = 1
        // How much of the baked per-building vertex colour to apply. 1 = fully tinted.
        // Kept as a knob so a future art pass can dial the variation down without a rebuild.
        _VertexTint ("Vertex Colour Tint", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 350
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _BumpStrength;
        half _Metallic;
        half _Glossiness;
        half _Occlusion;
        half _VertexTint;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;   // <- the whole reason this shader exists
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            // lerp rather than a bare multiply, so a mesh authored with NO colours (Unity hands
            // back opaque black for an unpainted mesh) does not render as a black wall.
            c.rgb *= lerp(fixed3(1,1,1), IN.color.rgb, _VertexTint);
            o.Albedo = c.rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Normal.xy *= _BumpStrength;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Occlusion = _Occlusion;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
