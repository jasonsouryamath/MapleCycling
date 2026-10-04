// THROWAWAY DIAGNOSTIC - delete once the section 24 shadow blocker is closed.
//
// Every conclusion about "does the road receive shadows" so far has been inferred from how a
// graded, ambient-lifted, cel-ramped image LOOKS. That inference has already been wrong twice.
// This shader removes the inference: it writes the raw per-pixel `atten` (the shadow/attenuation
// term the forward pipeline hands the lighting function) straight to the screen.
//
//   WHITE  -> atten == 1 : the surface is fully lit, no shadow is reaching it at all.
//   BLACK  -> atten == 0 : the surface is fully shadowed.
//   A dark patch under a caster -> shadows ARE working, and the problem is purely that
//                                 SakuraCel's ramp/ambient balance is burying the contrast.
Shader "MapleRide/AttenDebug"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        // Same pragma shape as SakuraCel so the variant set matches as closely as possible.
        // `noambient` matters: without it the surface shader adds ambient/probe light on top and
        // the readout would no longer be the raw attenuation term.
        #pragma surface surf AttenDebug fullforwardshadows exclude_path:deferred noambient
        #pragma target 3.0

        struct Input { float2 uv_MainTex; };

        half4 LightingAttenDebug(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            // No albedo, no ambient, no ramp - just the attenuation term itself.
            return half4(atten, atten, atten, 1);
        }

        void surf(Input IN, inout SurfaceOutput o) { o.Albedo = 1; o.Alpha = 1; }
        ENDCG
    }
    FallBack "Diffuse"
}
