// Dedicated ATMOSPHERIC PERSPECTIVE shader for the distant background ranges.
//
// THE DEFECT THIS CLOSES. The Shiosai backdrop / inland range bands were painted with
// MapleRide/HDRP/CelLit and were then hand-authored very dark so they would not out-brighten
// the sky. That produced exactly the artifact the opening QA called "cardboard cones": flat
// dark-teal cut-outs with hard straight edges, sitting at 2-7 km with NO haze between them and
// the camera, because the scene's warm cream RenderSettings fog (MR_ApplyFog) is tuned for the
// 0-400 m gameplay band and contributes almost nothing at those authored densities - and the
// aerial/mist post stage reads camera depth, which these silhouettes were not usefully feeding.
//
// THE FIX. A distant ridge does not need lighting; it needs DISTANCE. This shader computes its
// own aerial perspective in the fragment and does not touch the scene fog at all:
//
//   * distance haze  - the whole ridge fades toward _HazeColor between _HazeStart and _HazeFull,
//                      from _HazeMin to _HazeMax. Each band is given a different pair, so four
//                      ranges stacked back to the horizon read as four SEPARATE layers.
//   * valley fill    - the lower a pixel sits above _BaseY, the more haze it gets, so the feet
//                      of a range dissolve into the air while its crest stays legible. This is
//                      the single strongest "layered ridges" cue in the reference plate, and it
//                      is what stops a ridge reading as a solid cut-out shape.
//   * form shading   - a *little* wrapped diffuse so the two flanks of a crest separate. Kept
//                      deliberately weak: a fully modelled distant mountain stops reading as
//                      distance.
//   * macro noise    - a low-frequency tonal break so a large flat facet does not read as one
//                      poster-paint triangle.
//
// It writes depth (DepthForwardOnly, ZWrite On) so anything downstream that samples the depth
// pyramid sees the ranges rather than looking straight through them at the sky.
//
// NO ShadowCaster PASS, DELIBERATELY. These meshes are kilometres across; letting them cast
// would drag HDRP's directional cascades out to enclose them and destroy the near shadow
// resolution the opening depends on. Omitting the pass makes that impossible to switch on by
// accident from the renderer's shadowCastingMode.
Shader "MapleRide/HDRP/RidgeHaze"
{
    Properties
    {
        _Color        ("Ridge Base Color", Color) = (0.17,0.26,0.24,1)
        _CrestColor   ("Ridge Crest Color", Color) = (0.24,0.33,0.33,1)
        _HazeColor    ("Haze / Horizon Color", Color) = (0.72,0.84,0.93,1)

        [Header(Distance haze)]
        _HazeStart    ("Haze Start (m)", Float) = 600
        _HazeFull     ("Haze Full (m)", Float) = 6000
        _HazeMin      ("Haze At Start", Range(0,1)) = 0.25
        _HazeMax      ("Haze At Full", Range(0,1)) = 0.86

        [Header(Valley fill)]
        _BaseY        ("Ridge Base World Y", Float) = 0
        _ValleyHeight ("Valley Fill Height (m)", Float) = 620
        _ValleyFill   ("Valley Fill Amount", Range(0,1)) = 0.55

        [Header(Form)]
        _FormShading  ("Form Shading", Range(0,1)) = 0.22
        _CrestHeight  ("Crest Gradient Height (m)", Float) = 900
        _MacroNoise   ("Macro Tonal Noise", Range(0,1)) = 0.18
        _MacroScale   ("Macro Noise Size (m)", Float) = 900
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Color, _CrestColor, _HazeColor;
        float  _HazeStart, _HazeFull, _HazeMin, _HazeMax;
        float  _BaseY, _ValleyHeight, _ValleyFill;
        float  _FormShading, _CrestHeight, _MacroNoise, _MacroScale;
    CBUFFER_END

    float3 MRR_Shade(MRVaryings i)
    {
        float3 v = normalize(GetAbsolutePositionWS(GetPrimaryCameraPosition()) - i.positionWS);

        // GEOMETRIC NORMAL, NOT THE MESH NORMAL. ShiosaiCoastEnvironment.Ridge emits every quad
        // with BOTH windings (a range has to stay visible from either side of its crest on a
        // route that doubles back). Mesh.RecalculateNormals averages the incident face normals,
        // and two coincident faces of opposite winding sum to EXACTLY ZERO - so a large fraction
        // of these vertices carry a (0,0,0) normal, normalize() of which is NaN. That NaN
        // propagated through the shading term and out through saturate(), whose behaviour on NaN
        // is undefined per-lane: the first render of this shader showed the ranges as hard
        // black-and-white zebra stripes. (The old CelLit material hid the same broken normals
        // because it ran its ndl through saturate() BEFORE using it, which quietly ate the NaN.)
        //
        // Reconstructing the normal from screen-space derivatives of the world position gives
        // the true per-triangle facing regardless of what the vertex normals say, and cannot be
        // degenerate for a triangle with area.
        float3 gn = cross(ddy(i.positionWS), ddx(i.positionWS));
        float gl = length(gn);
        float3 n = (gl > 1e-9) ? gn / gl : float3(0.0, 1.0, 0.0);
        n *= sign(dot(n, v) + 0.0001);

        float above = max(0.0, i.positionWS.y - _BaseY);

        // Crest gradient: sunlit rock and thinner vegetation up top, deeper green in the folds.
        float crest = saturate(above / max(_CrestHeight, 1.0));
        float3 base = lerp(MR_AuthoredCol(_Color.rgb), MR_AuthoredCol(_CrestColor.rgb), crest);

        // Weak wrapped diffuse. Enough for the two flanks of a crest to separate, not enough to
        // model the range into a near hill. Saturated before use so no downstream term can ever
        // be fed an out-of-range or non-finite value.
        float ndl = saturate(dot(n, MR_SunDirection()) * 0.5 + 0.5);
        base *= lerp(1.0, 0.68 + 0.64 * ndl, saturate(_FormShading));

        // Low-frequency tonal break so a single large facet is not one flat fill.
        float macro = saturate(MR_ValueNoise(i.positionWS.xz, 1.0 / max(_MacroScale, 1.0)));
        base *= 1.0 + (macro - 0.5) * 2.0 * saturate(_MacroNoise);

        // --- aerial perspective ------------------------------------------------------------
        float dist = length(i.positionRWS);
        float t = saturate((dist - _HazeStart) / max(_HazeFull - _HazeStart, 1.0));
        float haze = lerp(saturate(_HazeMin), saturate(_HazeMax), t * t * (3.0 - 2.0 * t));

        // Valley fill: haze pools low, so the base of a range dissolves and only the crest
        // survives. Applied on TOP of the distance term rather than added to it, so it can never
        // push past fully hazed.
        float valley = (1.0 - saturate(above / max(_ValleyHeight, 1.0))) * saturate(_ValleyFill);
        haze = saturate(haze + (1.0 - haze) * valley);

        float3 col = lerp(max(base, 0.0), MR_AuthoredCol(_HazeColor.rgb), haze);

        // NOTE: MR_ApplyFog is deliberately NOT called. The scene's warm cream fog is authored
        // for the 0-400 m gameplay band; layering it on top of this would re-warm every ridge
        // back toward cream and undo the layering the haze ramp just created.
        return col;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            // Both windings are emitted by the generator; Cull Off keeps a range visible from
            // either side of its crest, which is what a 42 km route that doubles back needs.
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex MR_Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            float4 Frag(MRVaryings i) : SV_Target
            {
                return float4(MR_ToRender(MRR_Shade(i)) * GetCurrentExposureMultiplier(), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex MR_Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            void FragDepth(MRVaryings i) { }
            ENDHLSL
        }
    }

    Fallback Off
}
