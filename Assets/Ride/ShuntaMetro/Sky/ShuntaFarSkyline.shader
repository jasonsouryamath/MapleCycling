// Shunta Metro far skyline layers (claude, 2026-10-07).
//
// Rings of distant tower silhouettes. Like the night-sky dome, each vertex keeps its true on-screen position
// but is pushed to a fixed depth just inside the far plane (pin * far clip): towers 10 km away never get clipped,
// are always hidden by nearer buildings (they test against the opaque depth) and are not touched by HDRP's fog,
// which would otherwise erase everything past a few mean free paths. Atmospheric perspective is applied here
// instead, as a per-layer blend toward the sky's horizon colour. Layers are drawn far -> near by render queue, and
// towers inside one layer are emitted far -> near by the builder (painter's order), since depth is shared.
// Windows are procedural; when a window is smaller than a pixel it fades to its average glow (no shimmer).
// Output units: "rel" (1 = display pixel value 1 at the look's fixed EV), like the neon emissives and the sky.
Shader "MapleRide/Shunta/FarSkyline"
{
    Properties
    {
        _TowerA ("Tower colour A (dark)", Color) = (0.05, 0.07, 0.11, 1)
        _TowerB ("Tower colour B (light)", Color) = (0.11, 0.14, 0.20, 1)
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _TowerA;
        float4 _TowerB;
        float4 _Haze;       // rgb horizon colour the layer fades toward
        float4 _Layer;      // x mean distance (m), y haze 0..1, z base-haze height (m), w ambient brightness
        float4 _Win;        // x cell width (m), y cell height (m), z off-threshold 0..1 (higher = fewer lit), w window gain (rel)
        float4 _Pin;        // x time, y nits per rel, z pin fraction of far clip, w beacon gain (rel)
        float4 _WarmWin;
        float4 _CoolWin;
    CBUFFER_END

    struct Attr
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;       // r face shade, g tint, b seed, a tower height / 1000
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct Vary
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 col : TEXCOORD1;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    Vary Vert(Attr input)
    {
        Vary o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        float3 rws = TransformObjectToWorld(input.positionOS.xyz);          // camera-relative
        float3 dir = normalize(rws);
        float dist = _ProjectionParams.z * _Pin.z;
        o.positionCS = TransformWorldToHClip(dir * dist);                  // same screen position, pinned depth
        o.uv = input.uv;
        o.col = input.color;
        return o;
    }

    float H12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float4 Frag(Vary i) : SV_Target
    {
        float shade = i.col.r, tint = i.col.g, seed = i.col.b, heightM = i.col.a * 1000.0;
        float side = step(0.6, shade);                                       // top faces are shaded .55
        float3 baseCol = lerp(_TowerA.rgb, _TowerB.rgb, tint) * shade * _Layer.w;

        // windows
        float2 g = i.uv / max(_Win.xy, 0.01);
        float2 id = floor(g), f = frac(g);
        float px = max(fwidth(g.x), fwidth(g.y));                            // window cells per pixel
        float rect = step(0.22, f.x) * step(f.x, 0.78) * step(0.25, f.y) * step(f.y, 0.75);
        float h = H12(id + seed * 91.7);
        float lit = step(_Win.z, h);
        float3 winCol = lerp(_WarmWin.rgb, _CoolWin.rgb, H12(id * 1.7 + seed * 13.1)) * (0.55 + 0.9 * h);
        float detail = saturate(1.0 - px * 2.0);                              // below ~half a pixel: use the average glow
        float3 avgGlow = lerp(_WarmWin.rgb, _CoolWin.rgb, 0.45) * (1.0 - _Win.z) * 0.38;
        float3 emis = lerp(avgGlow, rect * lit * winCol, detail) * _Win.w * side;
        // street-level glow: lower floors glow more (shops), fading out with height
        emis *= lerp(1.0, 0.55, saturate(i.uv.y / 120.0));

        // red aircraft beacon on the crown, blinking
        float crown = step(heightM - 5.0, i.uv.y) * side;
        float blink = step(0.55, frac(_Pin.x * 0.8 + seed * 7.0));
        float3 beacon = float3(1.0, 0.12, 0.08) * crown * blink * _Pin.w;

        // atmospheric perspective: more haze with distance, and more near the base (ground haze)
        float baseHaze = (1.0 - saturate(i.uv.y / max(_Layer.z, 1.0))) * 0.45;
        float hz = saturate(_Layer.y + baseHaze);
        float3 col = lerp(baseCol, _Haze.rgb, hz) + emis * (1.0 - hz * 0.65) + beacon;
        col = clamp(col, 0.0, 40.0);
        return float4(col * _Pin.y * GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+1" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    Fallback Off
}
