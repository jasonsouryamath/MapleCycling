// Maple City neon signage (vaporwave neo-Tokyo layer). Unlit, HDR: texture x HDR gain so the signs clear the region's bloom
// threshold and glow. Same pass pattern as the built-in "Unlit/Color" the city's window/lantern glow already uses, so it renders
// in the project's HDRP setup exactly like those. Per-sign electrical flicker is derived from world position (no per-instance data).
Shader "MapleRide/City/NeonUnlit"
{
    Properties
    {
        _MainTex ("Sign atlas", 2D) = "white" {}
        [HDR] _Color ("HDR gain", Color) = (2.2, 2.2, 2.2, 1)
        _Flicker ("Flicker amount", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST; float4 _Color; float _Flicker;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash11(float x) { return frac(sin(x * 12.9898) * 43758.5453); }

            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                float id = floor(i.wp.x * 0.21) * 7.0 + floor(i.wp.z * 0.27) * 13.0 + floor(i.wp.y * 0.5);
                float tick = floor(_Time.y * 8.0 + hash11(id) * 40.0);
                float flick = 1.0 - _Flicker * step(0.965, hash11(tick + id * 3.1));
                return float4(t.rgb * _Color.rgb * flick, 1.0);
            }
            ENDCG
        }
    }
}
