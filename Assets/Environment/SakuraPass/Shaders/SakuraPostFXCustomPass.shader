// HDRP re-host of SakuraPostFX (Hidden/MapleRide/SakuraPostFX). See SakuraGradeCustomPass.cs.
//
// WHY THIS FILE EXISTS: SakuraPostFX.cs / SakuraPostFXShader are Built-in Render Pipeline only
// (OnRenderImage never fires under HDRP - see the header comment on SakuraPostFX.cs for the
// measured proof). This shader reproduces the same four passes - bright-pass, separable blur,
// upsample/accumulate and the grade+DoF+aerial+mist composite - written against HDRP's shader
// library so a CustomPass (SakuraGradeCustomPass) can run them at AfterPostProcess. The maths in
// every pass is a line-for-line port of SakuraPostFXShader; only the texture/sampling macros and
// the depth/world-position plumbing changed to HDRP's API.
Shader "Hidden/MapleRide/SakuraPostFXCustomPass"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

        // --- generic fullscreen-triangle vertex stage, uv included (CustomPassCommon.hlsl's
        // own Varyings does not carry one) ------------------------------------------------
        struct Attributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }

        // --- inputs, pushed per-draw from SakuraGradeCustomPass.cs -------------------------
        TEXTURE2D_X(_Source);
        TEXTURE2D_X(_BloomTex);
        TEXTURE2D_X(_DofTex);

        float4 _BloomParams;    // x = threshold, y = knee*2, z = 0.25/knee, w = intensity
        float4 _BlurDir;
        float4 _GradeParams;    // x = exposure, y = saturation, z = contrast
        float4 _Lift;
        float4 _Gain;
        float4 _VignetteParams; // x = strength, y = softness
        float4 _DofParams;      // x = focus distance, y = falloff range, z = falloff curve, w = strength

        float4 _AerialParams;   // x = start (m), y = range (m), z = desaturation, w = contrast flatten
        float4 _AerialTint;     // rgb = far tint, a = tint amount
        float4 _MistParams;     // x = base Y, y = top Y, z = strength, w = start (m)
        float4 _MistColor;
        float4x4 _CamToWorld;
        float4 _CamFrustum;     // x = tan(hFov/2), y = tan(vFov/2)

        // Soft-knee threshold (Unity/Karis style) - identical to SakuraPostFXShader.
        half3 Prefilter(half3 c)
        {
            half br = max(c.r, max(c.g, c.b));
            half rq = clamp(br - _BloomParams.x + _BloomParams.y, 0.0, _BloomParams.y);
            rq = rq * rq * _BloomParams.z;
            half contribution = max(rq, br - _BloomParams.x) / max(br, 1e-5);
            return c * contribution;
        }

        half4 FragPrefilter(Varyings i) : SV_Target
        {
            half3 c = SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv).rgb;
            return half4(Prefilter(c), 1.0);
        }

        // 9-tap gaussian along _BlurDir - identical weights to SakuraPostFXShader.
        half4 FragBlur(Varyings i) : SV_Target
        {
            float2 d = _BlurDir.xy;
            half3 sum = SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv).rgb * 0.2270270270;
            sum += SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv + d * 1.3846153846).rgb * 0.3162162162;
            sum += SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv - d * 1.3846153846).rgb * 0.3162162162;
            sum += SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv + d * 3.2307692308).rgb * 0.0702702703;
            sum += SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv - d * 3.2307692308).rgb * 0.0702702703;
            return half4(sum, 1.0);
        }

        half4 FragUpsample(Varyings i) : SV_Target
        {
            half3 a = SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv).rgb;
            half3 b = SAMPLE_TEXTURE2D_X(_BloomTex, s_linear_clamp_sampler, i.uv).rgb;
            return half4(a + b, 1.0);
        }

        // Filmic tone curve - keeps the sunset from clipping to white. Identical to
        // SakuraPostFXShader.
        half3 ACESFilm(half3 x)
        {
            const half a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
            return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
        }

        half4 FragComposite(Varyings i) : SV_Target
        {
            half3 col = SAMPLE_TEXTURE2D_X(_Source, s_linear_clamp_sampler, i.uv).rgb;

            // Depth is needed by DoF, aerial perspective and mist alike, so resolve it once.
            // SampleCameraDepth is HDRP's depth-atlas-aware equivalent of tex2D(_CameraDepthTexture, uv).
            float raw = SampleCameraDepth(i.uv);
            float eye = LinearEyeDepth(raw, _ZBufferParams);

            // --- depth of field: blend toward the blurred copy with distance ------------------
            if (_DofParams.w > 0.0001)
            {
                float coc = saturate((eye - _DofParams.x) / max(_DofParams.y, 0.001));
                coc = pow(coc, max(_DofParams.z, 0.001)) * _DofParams.w;
                col = lerp(col, SAMPLE_TEXTURE2D_X(_DofTex, s_linear_clamp_sampler, i.uv).rgb, coc);
            }

            // --- section 25: aerial perspective -----------------------------------------------
            // Skybox pixels resolve at (or within a hair of) the far clip plane; _ProjectionParams.z
            // is the camera's actual far clip distance and reliably separates true sky depth from
            // terrain/mountain geometry, exactly as in SakuraPostFXShader.
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
            // World-space height fog reconstructed from depth via the camera basis/frustum pushed
            // from C# (same technique SakuraPostFXShader used under Built-in; HDRP's own camera
            // matrices are per-render-pass state, not something a CustomPass can rely on staying in
            // sync with, so this keeps the exact tested math instead of chasing an HDRP helper).
            if (_MistParams.z > 0.0001)
            {
                float2 ndc = i.uv * 2.0 - 1.0;
                float3 viewRay = float3(ndc.x * _CamFrustum.x, ndc.y * _CamFrustum.y, -1.0);
                float3 wpos = _WorldSpaceCameraPos + mul((float3x3)_CamToWorld, viewRay) * eye;
                float h = 1.0 - saturate((wpos.y - _MistParams.x) / max(0.001, _MistParams.y - _MistParams.x));
                float d = saturate((eye - _MistParams.w) / max(1.0, _MistParams.w));
                col = lerp(col, _MistColor.rgb, saturate(h * d * _MistParams.z));
            }

            half3 bloom = SAMPLE_TEXTURE2D_X(_BloomTex, s_linear_clamp_sampler, i.uv).rgb;
            col += bloom * _BloomParams.w;

            col *= _GradeParams.x;
            col = ACESFilm(col);

            half lum = dot(col, half3(0.2126, 0.7152, 0.0722));
            col = lerp(half3(lum, lum, lum), col, _GradeParams.y);
            col = saturate((col - 0.5) * _GradeParams.z + 0.5);
            col = col * _Gain.rgb + _Lift.rgb;

            float2 uv2 = i.uv * 2.0 - 1.0;
            float vig = 1.0 - saturate(dot(uv2, uv2) * _VignetteParams.x / max(0.01, _VignetteParams.y));
            col *= lerp(1.0, vig, saturate(_VignetteParams.x));

            return half4(saturate(col), 1.0);
        }
        ENDHLSL

        // 0 - bright pass
        Pass
        {
            Name "SakuraGrade Bright"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            ENDHLSL
        }
        // 1 - separable blur (drawn twice per level: horizontal then vertical, via _BlurDir)
        Pass
        {
            Name "SakuraGrade Blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }
        // 2 - upsample / accumulate
        Pass
        {
            Name "SakuraGrade Upsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpsample
            ENDHLSL
        }
        // 3 - composite + grade (writes straight into the camera colour buffer)
        Pass
        {
            Name "SakuraGrade Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }

    Fallback Off
}
