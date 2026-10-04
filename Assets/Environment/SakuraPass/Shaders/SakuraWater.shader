// Stylised lake water for the Fuji/lakeside vista: layered gerstner-ish normals, sky-tinted fresnel,
// a sun glitter path and animated crest foam. Replaces the flat unlit blue cube.
Shader "MapleRide/SakuraWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.20,0.44,0.56,1)
        _DeepColor    ("Deep Color", Color) = (0.05,0.12,0.26,1)
        _SkyTint      ("Sky Reflection Tint", Color) = (0.85,0.72,0.78,1)
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 4.0
        _FresnelBoost ("Fresnel Strength", Range(0,2)) = 1.0

        _SunColor     ("Sun Glitter Color", Color) = (1,0.86,0.66,1)
        _GlitterPower ("Glitter Tightness", Range(4,512)) = 90
        _GlitterBoost ("Glitter Strength", Range(0,4)) = 1.6

        _WaveScale    ("Wave Scale", Range(0.02,4)) = 0.55
        _WaveSpeed    ("Wave Speed", Range(0,3)) = 0.35
        _WaveStrength ("Wave Normal Strength", Range(0,1)) = 0.30
        // Distance over which the per-pixel ripple detail is faded out to a flat mirror. The
        // ripple normal is evaluated analytically from world XZ, so at grazing angles a single
        // pixel spans many wave periods and the specular aliases into a band of moire stripes
        // right across the horizon. There is no mip chain to rescue it - the only fix is to stop
        // asking for detail the pixel cannot resolve.
        _DetailFadeStart ("Ripple Fade Start (m)", Float) = 140
        _DetailFadeEnd   ("Ripple Fade End (m)", Float) = 900

        _FoamColor    ("Foam Color", Color) = (1,1,1,1)
        _FoamAmount   ("Foam Amount", Range(0,1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        LOD 250

        CGPROGRAM
        #pragma surface surf SakuraWater vertex:vert exclude_path:deferred
        #pragma target 3.0

        half4 _ShallowColor, _DeepColor, _SkyTint, _SunColor, _FoamColor;
        half _FresnelPower, _FresnelBoost;
        half _GlitterPower, _GlitterBoost;
        half _WaveScale, _WaveSpeed, _WaveStrength;
        half _FoamAmount;
        float _DetailFadeStart, _DetailFadeEnd;

        struct Input
        {
            float3 worldPos;
            float3 viewDir;
        };

        void vert(inout appdata_full v)
        {
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            float t = _Time.y * _WaveSpeed;
            float wave = sin(wp.x * _WaveScale * 0.35 + t) * 0.5
                       + sin(wp.z * _WaveScale * 0.51 + t * 1.3) * 0.3;
            v.vertex.y += wave * 0.06;
        }

        // Cheap analytic ripple normal built from three rotated, non-harmonic wave trains.
        //
        // Surface shaders take o.Normal in *tangent* space, where +Z is the surface normal.
        // Returning a Y-up vector here left n and v almost perpendicular everywhere, so the
        // fresnel term pinned to 1 and the whole lake rendered as flat sky tint.
        //
        // Two axis-aligned trains of the same frequency interfere into a perfect lattice, which
        // showed up as regular polka-dot foam across the whole lake. Rotating each train off-axis
        // and using irrational frequency ratios keeps the crests from ever lining up.
        float3 RippleNormal(float2 p)
        {
            float t = _Time.y * _WaveSpeed;
            const float freq[3] = { 1.00, 1.63, 2.41 };
            const float ang[3]  = { 0.35, 1.97, 3.74 };
            const float amp[3]  = { 1.00, 0.55, 0.26 };

            float2 grad = float2(0, 0);
            for (int i = 0; i < 3; i++)
            {
                float2 d = float2(cos(ang[i]), sin(ang[i]));
                float k = _WaveScale * freq[i];
                grad += d * cos(dot(p, d) * k + t * (0.8 + 0.35 * i)) * k * amp[i];
            }
            return normalize(float3(-grad * _WaveStrength, 1.0));
        }

        half4 LightingSakuraWater(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half3 n = normalize(s.Normal);
            half3 v = normalize(viewDir);

            half facing = saturate(dot(n, v));
            half fres = pow(1.0 - facing, _FresnelPower) * _FresnelBoost;
            // s.Albedo carries the shallow/foam tint from surf, so ambient and this body term
            // agree instead of the surface reading white under ambient light.
            half3 body = lerp(_DeepColor.rgb, s.Albedo, facing);
            half3 col = lerp(body, _SkyTint.rgb, saturate(fres));

            // Sun path across the water.
            half3 h = normalize(lightDir + v);
            half glit = pow(saturate(dot(n, h)), _GlitterPower) * _GlitterBoost;

            half ndl = saturate(dot(n, lightDir) * 0.5 + 0.5);
            col *= lerp(0.55, 1.0, ndl) * _LightColor0.rgb;
            col += _SunColor.rgb * glit * _LightColor0.rgb * atten;

            return half4(col, 1.0);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Ripple detail decays with view distance - see _DetailFadeStart. Beyond the fade the
            // surface is a flat mirror, which is also what real water looks like at a grazing
            // angle a kilometre out: the fresnel and sky tint carry it, not the crests.
            float dist = distance(IN.worldPos, _WorldSpaceCameraPos);
            float detail = saturate(1.0 - (dist - _DetailFadeStart)
                                    / max(1.0, _DetailFadeEnd - _DetailFadeStart));
            o.Normal = normalize(lerp(float3(0, 0, 1), RippleNormal(IN.worldPos.xz), detail));

            // Foam picks out the ripple crests.
            float crest = saturate(o.Normal.z);
            float foam = smoothstep(1.0 - _FoamAmount, 1.0, 1.0 - crest) * detail;
            o.Albedo = lerp(_ShallowColor.rgb, _FoamColor.rgb, foam);
            o.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
