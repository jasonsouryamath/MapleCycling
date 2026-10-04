// Foliage variant of the cel shader: alpha cutout, two-sided, subsurface translucency and wind sway.
// Used for sakura blossom cards, leaves, ferns and grass so they read as lit volumes, not flat stickers.
Shader "MapleRide/SakuraFoliage"
{
    Properties
    {
        _Color         ("Base Color", Color) = (1,1,1,1)
        _MainTex       ("Albedo (A = cutout)", 2D) = "white" {}
        _Cutoff        ("Alpha Cutoff", Range(0,1)) = 0.4
        _ShadeColor    ("Shade Tint", Color) = (0.48,0.42,0.62,1)
        _ShadeStrength ("Shade Strength", Range(0,1)) = 0.70
        _RampSteps     ("Ramp Steps", Range(2,6)) = 3
        _RampSmooth    ("Ramp Softness", Range(0.002,0.35)) = 0.09
        _Translucency  ("Backlight Translucency", Range(0,2)) = 0.9
        _TransColor    ("Translucency Color", Color) = (1,0.62,0.76,1)
        _RimColor      ("Rim Color", Color) = (1,0.8,0.86,1)
        _RimPower      ("Rim Power", Range(0.5,10)) = 2.5
        _RimStrength   ("Rim Strength", Range(0,3)) = 0.6
        _WindStrength  ("Wind Strength", Range(0,1)) = 0.12
        _WindSpeed     ("Wind Speed", Range(0,5)) = 1.1
        _WindScale     ("Wind Spatial Scale", Range(0.01,1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 300
        Cull Off

        CGPROGRAM
        #pragma surface surf SakuraFoliage vertex:vert alphatest:_Cutoff addshadow fullforwardshadows exclude_path:deferred
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _ShadeColor;
        half _ShadeStrength;
        half _RampSteps;
        half _RampSmooth;
        half _Translucency;
        fixed4 _TransColor;
        fixed4 _RimColor;
        half _RimPower;
        half _RimStrength;
        half _WindStrength;
        half _WindSpeed;
        half _WindScale;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
        };

        half CelRamp(half value)
        {
            half steps = max(2.0, _RampSteps);
            half scaled = saturate(value) * steps;
            half band = floor(scaled);
            half f = scaled - band;
            half soft = smoothstep(0.5 - _RampSmooth, 0.5 + _RampSmooth, f);
            return saturate((band + soft) / steps);
        }

        // Sways verts more the higher they sit above the object pivot, so trunks stay planted.
        void vert(inout appdata_full v)
        {
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            float phase = (wp.x + wp.z) * _WindScale + _Time.y * _WindSpeed;
            float sway = (sin(phase) + sin(phase * 2.31 + 1.7) * 0.5) * 0.5;
            float mask = saturate(v.vertex.y * 0.35);
            v.vertex.x += sway * _WindStrength * mask;
            v.vertex.z += sway * _WindStrength * mask * 0.6;
        }

        half4 LightingSakuraFoliage(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half3 n = normalize(s.Normal);
            half3 v = normalize(viewDir);
            // Foliage cards are double sided; flip the normal toward the viewer so backfaces still light.
            n *= sign(dot(n, v) + 0.0001);

            half ndl = dot(n, lightDir) * 0.5 + 0.5;
            half shadowed = CelRamp(ndl) * CelRamp(atten);
            half3 lit = lerp(_ShadeColor.rgb * _ShadeStrength, half3(1,1,1), shadowed);

            // Light bleeding through the canopy from behind - the signature sakura backlight.
            half back = pow(saturate(dot(v, -lightDir)), 2.0) * _Translucency;

            half rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength;

            half3 col = s.Albedo * lit * _LightColor0.rgb;
            // Additive lights contribute attenuated diffuse only - see SakuraCel.shader for why
            // re-adding the shade floor and rim per light paints a screen-space slab.
            #ifdef UNITY_PASS_FORWARDADD
                return half4(s.Albedo * CelRamp(ndl) * atten * _LightColor0.rgb, s.Alpha);
            #endif
            col += s.Albedo * _TransColor.rgb * back * _LightColor0.rgb * atten;
            col += _RimColor.rgb * rim * _LightColor0.rgb;

            return half4(col, s.Alpha);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }

    FallBack "Transparent/Cutout/Diffuse"
}
