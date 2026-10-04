// Shiosai Coast ONLY. A translucent turquoise shelf laid over the inshore strip of the ocean
// plane.
//
// Why this exists as its own shader instead of a tweak to MapleRide/SakuraWater: that shader is
// shared with Sakura Pass's lake and must not be mutated, and it has no notion of DEPTH at all -
// its "shallow" and "deep" colours are blended by view fresnel, so every square metre of the
// Shiosai bay came out the same flat navy no matter how the colours were tuned. Every one of the
// 20 concept renders shows the opposite: a bright turquoise band hugging the shore that fades
// into deep blue within a few tens of metres. That band is a separate, additive piece of art, so
// it gets a separate, additive piece of geometry.
//
// The gradient is carried in VERTEX ALPHA (opaque against the beach, zero at the seaward edge)
// so the shelf dissolves into the ocean with no hard seam and no depth-texture dependency.
Shader "MapleRide/ShiosaiShallows"
{
    Properties
    {
        _ShoreColor  ("Shore Colour", Color)      = (0.36,0.86,0.84,1)
        _ShelfColor  ("Shelf Colour", Color)      = (0.08,0.55,0.66,1)
        _FoamColor   ("Foam Colour", Color)       = (1,1,1,1)
        _FoamWidth   ("Foam Width", Range(0,1))   = 0.18
        _RippleScale ("Ripple Scale", Float)      = 0.35
        _RippleSpeed ("Ripple Speed", Float)      = 0.45
        _Opacity     ("Opacity", Range(0,1))      = 0.85
    }

    SubShader
    {
        // Transparent, but queued just ahead of the default transparent band so props standing in
        // the water (sea stacks, the harbour mole) still sort correctly in front of it.
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off                // the rider can look at the shelf edge-on from the clifftop road
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _ShoreColor, _ShelfColor, _FoamColor;
            float _FoamWidth, _RippleScale, _RippleSpeed, _Opacity;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float3 world : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // v.color.a = 1 at the waterline, 0 at the seaward edge of the shelf.
                float shore = saturate(i.color.a);

                // Two crossing wave trains so the shelf edge wanders instead of running as a
                // perfectly parallel stripe along the whole 2.9 km coast.
                float t = _Time.y * _RippleSpeed;
                float w = sin(i.world.x * _RippleScale + t)
                        + sin(i.world.z * _RippleScale * 0.77 - t * 1.3);
                shore = saturate(shore + w * 0.035);

                half3 col = lerp(_ShelfColor.rgb, _ShoreColor.rgb, shore * shore);

                // A surf line where the shelf meets the sand.
                float foam = smoothstep(1.0 - _FoamWidth, 1.0, shore);
                col = lerp(col, _FoamColor.rgb, foam * 0.75);

                // Fade out seaward; keep the foam edge solid so the surf line stays crisp.
                float a = max(shore * _Opacity, foam);
                return half4(col * unity_AmbientSky.rgb * 1.6 + col * 0.35, a);
            }
            ENDCG
        }
    }

    FallBack Off
}
