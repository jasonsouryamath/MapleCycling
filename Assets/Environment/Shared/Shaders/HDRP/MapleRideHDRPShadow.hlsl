#ifndef MAPLERIDE_HDRP_SHADOW_INCLUDED
#define MAPLERIDE_HDRP_SHADOW_INCLUDED

// Real cast-shadow reception for the hand-written MapleRide HDRP passes.
//
// THE GAP THIS CLOSES. The HDRP port of this shader kit (MapleRideCelLit / Terrain / Road /
// Foliage) is hand-written rather than Shader Graph, so it gets none of HDRP's generated
// light-loop plumbing. Every mesh CASTS into HDRP's shadow maps via its ShadowCaster pass,
// but nothing SAMPLED them - so the whole world rendered with a perfectly even key and no
// contact shadows at all, which is most of what read as the "Roblox" look at the ShiosaiCoast
// start line. (Verified by render, not by log: a 6 m canary cube placed over the carriageway
// in ShiosaiStartLab2 cast no shadow whatsoever.)
//
// WHY IT IS SAFE TO REACH INTO THE LIGHT LOOP HERE. HDRP itself does exactly this for its
// Unlit "Shadow Matte" path and for HDRISky's backplate: both are hand-written/templated
// passes that include the light-loop shadow headers directly and call
// GetDirectionalShadowAttenuation. The include list, the defines and the shadow-quality
// multi_compiles below are copied from HDRISky.shader, which is the reference implementation
// shipping inside the installed HDRP package - so this is a supported combination rather than
// an improvised one.
//
// DIRECTIONAL ONLY, BY DESIGN. HDRP's ShadowLoopMin() also walks the punctual and area light
// lists. This scene carries ~91 realtime point lights, and the art direction only ever wanted
// SUN shadows, so sampling just _DirectionalShadowIndex keeps the cost to a single cascade
// lookup per pixel and cannot be perturbed by lamp placement.
//
// This file must be included from INSIDE a pass (not from the shared HLSLINCLUDE block):
// DepthForwardOnly and ShadowCaster have no light-loop constants bound, and pulling the
// headers into their translation unit would either fail to compile or bind garbage.

#define SHADERPASS SHADERPASS_FORWARD_UNLIT
#define HAS_LIGHTLOOP
#define LIGHTLOOP_DISABLE_TILE_AND_CLUSTER

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"

// positionRWS: camera-relative world position (MRVaryings.positionRWS) - HDRP's shadow code
//              expects camera-relative, NOT absolute, world space.
// normalWS:    shading normal, used for the normal-bias offset that kills shadow acne.
// positionSS:  raw SV_POSITION.xy, used for the cascade dither/blend.
// Returns 1 = fully lit, 0 = fully shadowed.
float MR_SunShadow(float3 positionRWS, float3 normalWS, float2 positionSS)
{
    if (_DirectionalShadowIndex < 0)
        return 1.0;

    DirectionalLightData light = _DirectionalLightDatas[_DirectionalShadowIndex];
    if (light.lightDimmer <= 0.0 || light.shadowDimmer <= 0.0 || light.shadowIndex < 0)
        return 1.0;

    HDShadowContext shadowContext = InitShadowContext();
    float atten = GetDirectionalShadowAttenuation(shadowContext, positionSS, positionRWS,
                                                 normalWS, light.shadowIndex, -light.forward);
    // shadowDimmer is the light's own "Dimmer" slider; honouring it keeps the artist-facing
    // control meaningful instead of silently ignored.
    return saturate(lerp(1.0, atten, light.shadowDimmer));
}

#endif // MAPLERIDE_HDRP_SHADOW_INCLUDED
