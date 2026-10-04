// Architectural material for the Shiosai benchmark town.  Deliberately separate from
// SakuraCel: buildings need continuous light response and texture contrast, not stepped bands.
Shader "MapleRide/ShiosaiArchitecture"
{
    Properties
    {
        _Color ("Albedo Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpStrength ("Normal Strength", Range(0,2)) = 0.65
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.35
        _Occlusion ("Occlusion", Range(0,1)) = 1
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

        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
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
