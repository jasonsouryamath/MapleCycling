// Bright-pass -> separable blur -> upsample -> composite with filmic grade and vignette.
// Driven by SakuraPostFX.cs on the Sakura Camera.
Shader "Hidden/MapleRide/SakuraPostFX"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    sampler2D _DofTex;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    float4 _BloomParams;    // x = threshold, y = knee*2, z = 0.25/knee, w = intensity
    float4 _BlurDir;
    float4 _GradeParams;    // x = exposure, y = saturation, z = contrast
    float4 _Lift;
    float4 _Gain;
    float4 _VignetteParams; // x = strength, y = softness
    // Depth of field. x = focus distance (m, everything nearer stays sharp), y = falloff range
    // (m), z = falloff curve exponent, w = maximum blend toward the blurred image.
    // Far-field only: a mountain pass wants the road under the wheels crisp and the volcano
    // soft, and a symmetric near+far CoC on a 1.5 m-high chase camera just smears the asphalt.
    float4 _DofParams;

    // --- Section 25: atmosphere and depth -------------------------------------------------
    // Built-in exponential fog is a single distance->colour lerp: it makes far geometry recede
    // but does nothing about the two things section 25 actually asks for - distance-based
    // DESATURATION and REDUCED CONTRAST in distant terrain. Without them the far wall of sakura
    // stays as saturated as the foreground and competes with it ("distant objects should not
    // compete with foreground detail"). These run in the composite because that is the only
    // place with both scene colour and depth.
    float4 _AerialParams;   // x = start (m), y = range (m), z = desaturation, w = contrast flatten
    float4 _AerialTint;     // rgb = far tint, a = tint amount
    // Valley mist. Height fog needs a world position, which an image effect has to reconstruct
    // from depth - hence _CamToWorld and _CamFrustum, pushed from SakuraPostFX.cs.
    float4 _MistParams;     // x = base Y (full mist), y = top Y (no mist), z = strength, w = start (m)
    float4 _MistColor;
    float4x4 _CamToWorld;
    float4 _CamFrustum;     // x = tan(hFov/2), y = tan(vFov/2)

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    // Soft-knee threshold (Unity/Karis style) keeps highlights from popping on/off.
    half3 Prefilter(half3 c)
    {
        half br = max(c.r, max(c.g, c.b));
        half rq = clamp(br - _BloomParams.x + _BloomParams.y, 0.0, _BloomParams.y);
        rq = rq * rq * _BloomParams.z;
        half contribution = max(rq, br - _BloomParams.x) / max(br, 1e-5);
        return c * contribution;
    }

    half4 fragPrefilter(v2f i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb;
        return half4(Prefilter(c), 1.0);
    }

    // 9-tap gaussian along _BlurDir.
    half4 fragBlur(v2f i) : SV_Target
    {
        float2 d = _BlurDir.xy;
        half3 sum = tex2D(_MainTex, i.uv).rgb * 0.2270270270;
        sum += tex2D(_MainTex, i.uv + d * 1.3846153846).rgb * 0.3162162162;
        sum += tex2D(_MainTex, i.uv - d * 1.3846153846).rgb * 0.3162162162;
        sum += tex2D(_MainTex, i.uv + d * 3.2307692308).rgb * 0.0702702703;
        sum += tex2D(_MainTex, i.uv - d * 3.2307692308).rgb * 0.0702702703;
        return half4(sum, 1.0);
    }

    half4 fragUpsample(v2f i) : SV_Target
    {
        half3 a = tex2D(_MainTex, i.uv).rgb;
        half3 b = tex2D(_BloomTex, i.uv).rgb;
        return half4(a + b, 1.0);
    }

    // Filmic tone curve - keeps the sunset from clipping to white.
    half3 ACESFilm(half3 x)
    {
        const half a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    half4 fragComposite(v2f i) : SV_Target
    {
        half3 col = tex2D(_MainTex, i.uv).rgb;

        // Depth is needed by DoF, aerial perspective and mist alike, so resolve it once.
        float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
        float eye = LinearEyeDepth(raw);

        // --- depth of field: blend toward the blurred copy with distance ------------------
        // Skybox pixels come back at the far plane, so they take the full blend, which is what
        // gives the horizon its haze-soft edge. The sharp-to-soft transition is placed out past
        // the far end of the visible carriageway, never on it.
        if (_DofParams.w > 0.0001)
        {
            float coc = saturate((eye - _DofParams.x) / max(_DofParams.y, 0.001));
            coc = pow(coc, max(_DofParams.z, 0.001)) * _DofParams.w;
            col = lerp(col, tex2D(_DofTex, i.uv).rgb, coc);
        }

        // --- section 25: aerial perspective -----------------------------------------------
        // One normalised distance term drives all three cues so they stay in step: saturation
        // falls away, local contrast flattens toward mid-grey, and the residue drifts to the
        // horizon tint. Applied to scene colour BEFORE bloom and the grade, because it is a
        // property of the air between camera and surface, not of the film.
        //
        // Skybox pixels resolve at (or within a hair of) the far clip plane no matter where
        // they land on screen, so eye ends up >> aerialRange everywhere the sky shows through
        // and `aerial` saturates to 1.0 across the *entire* dome - flattening SakuraSky's
        // painted zenith->horizon gradient and clouds into one flat tint. _ProjectionParams.z
        // is the camera's actual far clip distance (set to 9000 m for gameplay/diagnostic
        // cameras, far beyond anything the aerial range/fog are tuned for, ~1200 m at most), so
        // it reliably separates true skybox depth from any real terrain/mountain geometry and
        // lets the sky keep its gradient while terrain aerial haze is untouched.
        bool isSky = eye >= _ProjectionParams.z - 1.0;
        float aerial = isSky ? 0.0 : saturate((eye - _AerialParams.x) / max(_AerialParams.y, 0.001));
        if (!isSky && (_AerialParams.z > 0.0001 || _AerialParams.w > 0.0001))
        {
            half alum = dot(col, half3(0.2126, 0.7152, 0.0722));
            col = lerp(col, half3(alum, alum, alum), aerial * _AerialParams.z);
            col = lerp(col, half3(0.5, 0.5, 0.5), aerial * _AerialParams.w * 0.5);
            col = lerp(col, _AerialTint.rgb, aerial * _AerialTint.a);
        }

        // --- section 25: valley mist ------------------------------------------------------
        // World-space height fog, reconstructed from depth. It pools in the valley floor and
        // clears by the ridge line, which is what separates "mid" from "far" on the shelf and
        // summit marks where plain distance fog alone reads as a flat wash.
        if (_MistParams.z > 0.0001)
        {
            float2 ndc = i.uv * 2.0 - 1.0;
            float3 viewRay = float3(ndc.x * _CamFrustum.x, ndc.y * _CamFrustum.y, -1.0);
            float3 wpos = _WorldSpaceCameraPos + mul((float3x3)_CamToWorld, viewRay) * eye;
            float h = 1.0 - saturate((wpos.y - _MistParams.x) / max(0.001, _MistParams.y - _MistParams.x));
            float d = saturate((eye - _MistParams.w) / max(1.0, _MistParams.w));
            col = lerp(col, _MistColor.rgb, saturate(h * d * _MistParams.z));
        }

        half3 bloom = tex2D(_BloomTex, i.uv).rgb;
        col += bloom * _BloomParams.w;

        col *= _GradeParams.x;
        col = ACESFilm(col);

        half lum = dot(col, half3(0.2126, 0.7152, 0.0722));
        col = lerp(half3(lum, lum, lum), col, _GradeParams.y);
        col = saturate((col - 0.5) * _GradeParams.z + 0.5);
        col = col * _Gain.rgb + _Lift.rgb;

        float2 uv = i.uv * 2.0 - 1.0;
        float vig = 1.0 - saturate(dot(uv, uv) * _VignetteParams.x / max(0.01, _VignetteParams.y));
        col *= lerp(1.0, vig, saturate(_VignetteParams.x));

        return half4(saturate(col), 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0 - bright pass
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPrefilter
            ENDCG
        }
        // 1 - separable blur
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlur
            ENDCG
        }
        // 2 - upsample / accumulate
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUpsample
            ENDCG
        }
        // 3 - composite + grade
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            ENDCG
        }
    }

    FallBack Off
}
