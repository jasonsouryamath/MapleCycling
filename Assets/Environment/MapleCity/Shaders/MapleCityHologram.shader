// Maple City hologram billboards: additive, two-sided, animated. Scanlines scroll up the panel in world space, edges fade,
// and a rare horizontal glitch row slips the UVs. Additive on purpose (a hologram adds light, it never darkens what is behind).
Shader "MapleRide/City/HologramAdd"
{
    Properties
    {
        _MainTex ("Hologram atlas", 2D) = "white" {}
        [HDR] _Color ("HDR tint", Color) = (0.5, 1.6, 2.0, 1)
        _Scan ("Scanline strength", Range(0, 1)) = 0.35
        _Glitch ("Glitch amount", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST; float4 _Color; float _Scan; float _Glitch;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; float2 uv0 : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv0 = v.uv2;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash11(float x) { return frac(sin(x * 12.9898) * 43758.5453); }

            float4 frag(v2f i) : SV_Target
            {
                float row = floor(i.wp.y * 6.0);
                float tick = floor(_Time.y * 5.0);
                float glitchRow = step(0.985, hash11(row * 1.7 + tick * 5.3)) * _Glitch;
                float2 uv = i.uv + float2(glitchRow * (hash11(row + tick) - 0.5) * 0.08, 0);
                float4 t = tex2D(_MainTex, uv);
                float scan = 1.0 - _Scan * (0.5 + 0.5 * sin(i.wp.y * 22.0 - _Time.y * 4.0));
                float2 e = saturate(min(i.uv0, 1.0 - i.uv0) * 9.0);          // edge fade (uv2 is the quad's own 0..1; uv carries the atlas sub-rect)
                float edge = e.x * e.y;
                float flick = 0.88 + 0.12 * hash11(tick * 3.7);
                return float4(t.rgb * _Color.rgb * scan * edge * flick, 1.0);
            }
            ENDCG
        }
    }
}
