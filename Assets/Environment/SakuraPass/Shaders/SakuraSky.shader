// Painted anime sunset skybox: zenith -> horizon -> ground gradient with a sun disc, horizon bloom
// and soft procedural cloud banding. Replaces the null skybox that produced the hard flat horizon.
Shader "MapleRide/SakuraSky"
{
    Properties
    {
        _ZenithColor   ("Zenith Color", Color) = (0.14,0.17,0.38,1)
        _MidColor      ("Mid Sky Color", Color) = (0.42,0.38,0.58,1)
        _HorizonColor  ("Horizon Color", Color) = (0.98,0.66,0.52,1)
        _GroundColor   ("Ground Color", Color) = (0.16,0.18,0.26,1)
        _HorizonSharp  ("Horizon Falloff", Range(0.5,12)) = 3.2
        _MidPoint      ("Mid Sky Blend Height", Range(0.02,1)) = 0.34

        _SunColor      ("Sun Color", Color) = (1,0.82,0.60,1)
        _SunDirection  ("Sun Direction (xyz)", Vector) = (0.38,0.30,0.87,0)
        _SunSize       ("Sun Size", Range(0.001,0.3)) = 0.045
        _SunSoftness   ("Sun Edge Softness", Range(0.0001,0.2)) = 0.012
        _SunGlow       ("Sun Glow Strength", Range(0,4)) = 1.4
        _SunGlowPower  ("Sun Glow Falloff", Range(1,256)) = 18

        _CloudColor    ("Cloud Color", Color) = (1,0.74,0.68,1)
        _CloudStrength ("Cloud Strength", Range(0,1)) = 0.35
        _CloudScale    ("Cloud Scale", Range(0.5,12)) = 3.5
        _CloudHeight   ("Cloud Band Height", Range(0,1)) = 0.18
        _CloudSpread   ("Cloud Band Spread", Range(0.01,1)) = 0.30

        _Exposure      ("Exposure", Range(0,4)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _ZenithColor, _MidColor, _HorizonColor, _GroundColor;
            half _HorizonSharp, _MidPoint;
            half4 _SunColor;
            float4 _SunDirection;
            half _SunSize, _SunSoftness, _SunGlow, _SunGlowPower;
            half4 _CloudColor;
            half _CloudStrength, _CloudScale, _CloudHeight, _CloudSpread;
            half _Exposure;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // Clouds are sampled on the 3D view direction rather than an (atan2, height) chart.
            // The chart had a wrap seam at +-pi and, because the band was several times tighter
            // vertically than horizontally, its noise degenerated into constant-altitude stripes
            // that read as concentric rings across the whole sky.
            float Hash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                               lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                               lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0, amp = 0.5;
                for (int i = 0; i < 4; i++) { sum += ValueNoise(p) * amp; p *= 2.07; amp *= 0.5; }
                return sum;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;

                // --- vertical gradient -------------------------------------------------
                float up = saturate(pow(saturate(h), 1.0 / _HorizonSharp));
                half3 sky = lerp(_HorizonColor.rgb, _MidColor.rgb, saturate(up / _MidPoint));
                sky = lerp(sky, _ZenithColor.rgb, saturate((up - _MidPoint) / max(0.001, 1.0 - _MidPoint)));

                // Below the horizon fades to a haze-matched ground tone so distant terrain seats cleanly.
                half3 below = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-h * _HorizonSharp));
                half3 col = lerp(below, sky, saturate(h * 40.0 + 0.5));

                // --- sun disc + atmospheric glow --------------------------------------
                float3 sunDir = normalize(_SunDirection.xyz);
                float sd = dot(d, sunDir);
                float glow = pow(saturate(sd), _SunGlowPower) * _SunGlow;
                col += _SunColor.rgb * glow;

                float ang = acos(clamp(sd, -1.0, 1.0));
                float disc = 1.0 - smoothstep(_SunSize, _SunSize + _SunSoftness, ang);
                col = lerp(col, _SunColor.rgb * 1.6, disc);

                // --- drifting cloud band ----------------------------------------------
                float band = exp(-pow((h - _CloudHeight) / _CloudSpread, 2.0));
                float3 cp = d * _CloudScale;
                cp.y *= 2.6;                  // flatten the puffs without stratifying them
                cp.x += _Time.y * 0.012;
                float clouds = saturate(Fbm(cp) * 1.7 - 0.52);
                col = lerp(col, _CloudColor.rgb, clouds * band * _CloudStrength);

                return half4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
