// Shunta Metro night sky dome (claude, 2026-10-03).
//
// One full-sphere mesh whose vertex shader PLACES ITSELF on the camera-relative sphere at 0.985 x far clip
// (same trick as AzoraSun): it never needs centring, never clips, and tests against the opaque depth so
// buildings and landmarks hide it. Transparent queue = drawn after HDRP's gradient sky, premultiplied blend.
// Output units: "rel" (1 = display pixel value 1 at the look's fixed EV) -> nits -> pre-exposed, like the neon emissives.
//   * stars + Milky Way : baked textures (ShuntaMetroSky editor script), per-star scintillation by 3D noise
//   * moon              : procedural disc (maria, phase, limb), halo + faint 22 deg ring
//   * thin clouds       : 3D fbm lit pink/cyan from below by the city neon, silvered by the moon
//   * horizon glow      : zone-coloured light pollution that also suppresses faint stars near the horizon
//   * shooting stars    : 3 analytic meteor channels
Shader "MapleRide/Shunta/NightSky"
{
    Properties
    {
        _StarTex ("Star map (sRGB-encoded equirect)", 2D) = "black" {}
        _GlowTex ("Milky Way glow (HDR equirect)", 2D) = "black" {}
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    TEXTURE2D(_StarTex); SAMPLER(sampler_StarTex);
    TEXTURE2D(_GlowTex); SAMPLER(sampler_GlowTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _StarTex_ST;
        float4 _GlowTex_ST;
        float4 _Sky0;          // x visibility 0..1, y star gain, z twinkle, w milky way gain
        float4 _Sky1;          // x time, y rotation (rad), z nits per rel, w far fraction
        float4 _MoonDir;       // xyz direction, w tan(radius)
        float4 _MoonParams;    // x phase 0..1 (0.5 full), y disc gain, z halo gain, w fade
        float4 _CityColor;     // rgb zone colour, a strength
        float4 _NeonA;         // pink-ish
        float4 _NeonB;         // cyan-ish
        float4 _CloudParams;   // x amount 0..1, y scale, z neon gain, w wind speed
        float4 _MeteorParams;  // x mean period (s), y gain
    CBUFFER_END

    struct SkyAttr { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct SkyVary { float4 positionCS : SV_POSITION; float3 dirRWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

    SkyVary VertSky(SkyAttr input)
    {
        SkyVary o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        float3 dir = normalize(input.positionOS.xyz);
        float dist = _ProjectionParams.z * _Sky1.w;
        float3 positionRWS = dir * dist;     // camera-relative: the eye is the origin
        o.positionCS = TransformWorldToHClip(positionRWS);
        o.dirRWS = dir;
        return o;
    }

    float H13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
    float H11(float x) { x = frac(x * 0.1031); x *= x + 33.33; x *= x + x; return frac(x); }
    float VN3(float3 p)
    {
        float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
        float a = lerp(lerp(H13(i), H13(i + float3(1,0,0)), f.x), lerp(H13(i + float3(0,1,0)), H13(i + float3(1,1,0)), f.x), f.y);
        float b = lerp(lerp(H13(i + float3(0,0,1)), H13(i + float3(1,0,1)), f.x), lerp(H13(i + float3(0,1,1)), H13(i + float3(1,1,1)), f.x), f.y);
        return lerp(a, b, f.z);
    }
    float FBM3(float3 p)
    {
        float a = 0.5, s = 0.0;
        [unroll] for (int k = 0; k < 4; k++) { s += a * VN3(p); p = p * 2.03 + 17.1; a *= 0.5; }
        return s;
    }

    // thin shooting star: great-circle arc with a fading tail
    float Meteor(float3 d, float t, float k, float period)
    {
        float T = period * (0.8 + 0.45 * k) + k * 3.7;
        float ts = t / T + k * 0.37;
        float e = floor(ts), ph = frac(ts);
        float dur = min(0.85 / T, 0.5);                        // visible ~0.85 s
        float r0 = H11(e * 7.13 + k * 31.7);
        if (ph > dur || r0 > 0.62) return 0.0;
        float u = ph / dur;
        float r1 = H11(e * 3.71 + k * 5.3 + 1.0), r2 = H11(e * 5.17 + k * 9.1 + 2.0), r3 = H11(e * 2.39 + k * 4.7 + 3.0), r4 = H11(e * 9.3 + k * 1.9 + 4.0);
        float az = r1 * 6.2832, el = lerp(0.30, 1.15, r2);
        float3 a = float3(sin(az) * cos(el), sin(el), cos(az) * cos(el));
        float3 p = normalize(cross(a, float3(0, 1, 0))), q = cross(a, p);
        float ta = r3 * 6.2832;
        float3 b = p * cos(ta) + q * sin(ta);
        float3 nrm = cross(a, b);
        float w = dot(d, nrm);
        if (abs(w) > 0.012) return 0.0;
        float L = 0.20 + 0.16 * r4, tail = 0.45 * L;
        float S = u * L * 1.3;
        float x = dot(d, a), y = dot(d, b);
        if (x <= 0.0) return 0.0;
        float sd = atan2(y, x);
        float rel = (S - sd) / tail;
        if (rel < 0.0 || rel > 1.0) return 0.0;
        float core = exp(-(w * w) / (0.0022 * 0.0022 * (0.5 + 0.9 * (1.0 - rel))));
        float env = sin(3.14159 * u); env = sqrt(max(env, 0.0));
        return pow(1.0 - rel, 2.2) * core * env * (0.35 + 0.65 * smoothstep(0.0, 0.2, 1.0 - rel));
    }

    float4 FragSky(SkyVary i) : SV_Target
    {
        float3 d = normalize(i.dirRWS);
        float h = d.y;
        float t = _Sky1.x;

        // ---- star / glow texture coordinates, with a seam-free derivative (the atan2 wrap) ----
        float cr = cos(_Sky1.y), sr = sin(_Sky1.y);
        float3 sdv = float3(d.x * cr - d.z * sr, d.y, d.x * sr + d.z * cr);
        float u = atan2(sdv.x, sdv.z) * 0.15915494 + 0.5;
        float v = asin(clamp(sdv.y, -1.0, 1.0)) * 0.31830989 + 0.5;
        float u2 = frac(u + 0.5);
        float2 dx1 = ddx(float2(u, v)), dy1 = ddy(float2(u, v));
        float2 dx2 = ddx(float2(u2, v)), dy2 = ddy(float2(u2, v));
        bool alt = (abs(dx1.x) + abs(dy1.x)) > (abs(dx2.x) + abs(dy2.x));
        float2 gx = alt ? dx2 : dx1, gy = alt ? dy2 : dy1;

        float horizon = smoothstep(-0.05, 0.0, h);                // nothing below the horizon
        float ext = saturate(1.0 - exp(-(max(h, 0.0) + 0.02) * 8.0)); // atmospheric extinction near the horizon
        float vis = _Sky0.x;

        // ---- light pollution glow (zone coloured) ----
        float az = atan2(d.x, d.z);
        float patch = 0.75 + 0.5 * VN3(float3(cos(az) * 1.7, sin(az) * 1.7, 3.1));
        float hh = max(h, 0.0);
        float poll = (exp(-hh / 0.14) * 0.9 + exp(-hh / 0.45) * 0.28) * patch * _CityColor.a;
        float3 glow = _CityColor.rgb * poll * 0.55;

        // ---- stars (scintillation: continuous noise, stronger near the horizon) ----
        float4 st = SAMPLE_TEXTURE2D_GRAD(_StarTex, sampler_StarTex, float2(u, v), gx, gy);
        float tn = VN3(float3(u * 760.0, v * 380.0, t * 2.4)) * 0.65 + VN3(float3(u * 1900.0, v * 950.0, t * 6.0 + 9.0)) * 0.35;
        float amp = _Sky0.z * lerp(0.45, 1.5, saturate(1.0 - h * 1.6));
        float tw = max(0.2, 1.0 + amp * (tn * 2.0 - 1.0) * 1.6);
        float3 stars = st.rgb * _Sky0.y * tw;
        stars = max(stars - poll * 0.03, 0.0);                    // city haze swallows faint stars first
        float3 mw = SAMPLE_TEXTURE2D_GRAD(_GlowTex, sampler_GlowTex, float2(u, v), gx, gy).rgb * _Sky0.w;
        mw *= saturate(1.0 - poll * 1.1);

        // ---- clouds ----
        float cloudA = 0.0; float3 cloudLight = 0.0;
        float3 neonMix = 0.0;
        {
            float inv = 1.0 / (hh + 0.20);
            float2 cp = d.xz * inv * _CloudParams.y + float2(t * _CloudParams.w, t * _CloudParams.w * 0.37);
            float n = FBM3(float3(cp, t * 0.015));
            float n2 = FBM3(float3(cp * float2(0.45, 2.3) + 11.0, t * 0.02));
            float cov = lerp(0.62, 0.40, _CloudParams.x);
            float dens = smoothstep(cov, cov + 0.22, n * 0.75 + n2 * 0.35);
            cloudA = dens * _CloudParams.x * 0.62 * smoothstep(0.0, 0.10, h);
            float mixN = smoothstep(0.3, 0.7, VN3(float3(cp * 0.55 + 5.0, 1.7)));
            neonMix = lerp(_NeonA.rgb, _NeonB.rgb, mixN);
            float low = pow(saturate(1.0 - h), 2.4);
            float clit = 0.25 + 0.75 * saturate(dens * 1.5);
            cloudLight = neonMix * _CloudParams.z * low * clit * (0.5 + 0.5 * _CityColor.a * 2.0);
            cloudLight += float3(0.012, 0.018, 0.04);
        }

        // ---- moon ----
        float3 md = normalize(_MoonDir.xyz);
        float cosA = dot(d, md);
        float3 mR = normalize(cross(float3(0, 1, 0), md));
        float3 mU = cross(md, mR);
        float2 pq = float2(dot(d, mR), dot(d, mU)) / max(cosA, 1e-3);
        float rad = _MoonDir.w;
        float2 mu = pq / rad;
        float mr = length(mu);
        float aa = max(fwidth(mr), 1e-4);
        float disc = (cosA > 0.0) ? saturate((1.0 - mr) / aa + 0.5) : 0.0;
        float3 moonRGB = 0.0;
        float lit = (1.0 - cos(6.2831853 * _MoonParams.x)) * 0.5;
        {
            float z = sqrt(saturate(1.0 - mr * mr));
            float3 n = float3(mu.x, mu.y, z);
            float ph = 6.2831853 * _MoonParams.x;
            float3 L = float3(sin(ph), 0.0, -cos(ph));
            float ndl = dot(n, L);
            float lam = smoothstep(-0.03, 0.30, ndl);
            float maria = smoothstep(0.44, 0.60, FBM3(n * 2.7 + 3.7));
            float crat = 1.0 - abs(2.0 * VN3(n * 11.0) - 1.0);
            float fine = FBM3(n * 21.0);
            float albedo = lerp(0.98, 0.50, maria) * (0.88 + 0.12 * crat) * (0.85 + 0.30 * fine);
            float limb = lerp(0.72, 1.0, pow(z, 0.4));
            float3 tint = lerp(float3(1.0, 0.70, 0.42), float3(0.96, 0.97, 1.0), smoothstep(0.0, 0.45, md.y));
            moonRGB = tint * albedo * limb * (lam + 0.035 * (1.0 - lam)) * _MoonParams.y;
        }
        float ang = acos(clamp(cosA, -1.0, 1.0));
        float rr = atan(rad);
        float halo = 0.34 * exp(-max(ang - rr, 0.0) / 0.045) + 0.11 * exp(-ang / 0.17) + 0.025 * exp(-ang / 0.75);
        halo += 0.045 * exp(-pow((ang - 0.384) / 0.022, 2.0));   // 22 degree ice ring
        halo *= (0.18 + 0.82 * lit) * smoothstep(0.0, 0.5, cosA + 0.2);
        float3 haloRGB = float3(0.72, 0.82, 1.0) * halo * _MoonParams.z;
        float moonOn = _MoonParams.w * smoothstep(-0.08, 0.02, md.y);
        float moonSuppress = saturate(1.0 - halo * 1.6 * moonOn - disc * moonOn);
        float3 moonSilver = float3(0.78, 0.86, 1.0) * pow(saturate(cosA), 5.0) * 0.55 * _MoonParams.z * moonOn;

        // ---- shooting stars ----
        float met = 0.0;
        if (_MeteorParams.y > 0.0 && h > 0.02)
            met = Meteor(d, t, 0.0, _MeteorParams.x) + Meteor(d, t, 1.0, _MeteorParams.x) + Meteor(d, t, 2.0, _MeteorParams.x);
        float3 metRGB = float3(0.95, 0.97, 1.0) * met * _MeteorParams.y;

        // ---- compose (premultiplied) ----
        float3 body = (stars * vis + mw * vis) * ext;
        body *= (1.0 - cloudA * 0.85) * moonSuppress;
        float3 rgb = body + glow * horizon + metRGB * horizon;
        rgb += cloudLight * cloudA * horizon;
        rgb += cloudA * moonSilver;
        rgb += (haloRGB * moonOn + moonRGB * disc * moonOn) * horizon;
        rgb += (H13(float3(i.positionCS.xy, frac(t) * 7.0)) - 0.5) * (1.0 / 255.0) * step(0.0004, dot(rgb, 0.33));
        rgb = clamp(rgb, 0.0, 60.0);
        float alpha = max(cloudA, disc * moonOn) * horizon;
        float k = _Sky1.z * GetCurrentExposureMultiplier();
        return float4(rgb * k, alpha);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VertSky
            #pragma fragment FragSky
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    Fallback Off
}
