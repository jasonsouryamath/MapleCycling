// Agent HQ ticket (2026-09-30, maplerider-environment-artist): "Nagisa Bay road/grass/ocean look
// like flat placeholder blockout vs Sakura's detail level -> Bring Nagisa Bay road wear ... to
// parity with other routes."
//
// Scope note: grass (Materials/Nagisa_Ground.mat "tuning only") and ocean/shoreline
// (Shaders/NagisaOcean*, NagisaBayEnvironment.Surf.cs) remain NB9/NB1-owned in
// COORDINATION.md's Nagisa Bay overhaul table.  NB1's surf implementation is now shipped;
// this package still does NOT touch Nagisa_Ground.mat or anything under Shaders/NagisaOcean*
// / Surf.cs, preserving the ownership boundary.
//
// The ride-road asphalt (Nagisa_Asphalt, built by RoadMaterial() in NagisaBayEnvironment.cs, the
// NB2/B5-owner's request-only file) is not reserved to any NB package. NagisaBayEnvironment.cs
// already carries `MapleRide/HDRP/Road` (Assets/Environment/Shared/Shaders/HDRP/MapleRideRoad.shader),
// a strictly more capable shader than SakuraPass's dedicated SakuraRoad.shader: it already has
// procedural macro variation, resurfacing patches, wheel-track polish/darkening, paving joints,
// crack/ravel noise and edge ravelling built in. RoadMaterial() only ever sets the base
// albedo/verge/shoulder/shade properties, so every wear knob sits at the shader's mild default -
// that is the literal cause of the "flat placeholder" read next to Sakura's tuned SakuraPass_Asphalt.
//
// This stage runs AFTER BuildRoad (order 40, after Road=? see Overhaul stage order table) and just
// turns those existing procedural wear knobs up on the material by exact name - no edits to
// BuildRoad/RoadMaterial, no new geometry, no new textures.
using UnityEditor;
using UnityEngine;

public static partial class NagisaBayEnvironment
{
    private const string RoadWearMatName = "Nagisa_Asphalt";

    [NagisaStage(41, "RoadWear")]
    private static void BuildRoadWearStage(Transform group)
    {
        // This stage doesn't add scene objects (group stays empty); it only tunes the road
        // material asset in place, by exact name, so a re-run is naturally idempotent.
        var path = $"{MaterialDir}/{RoadWearMatName}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Debug.LogWarning($"[nagisa-roadwear] {path} not found - run NagisaBayEnvironment.Apply first (BuildRoad creates it).");
            return;
        }
        if (mat.shader == null || mat.shader.name != "MapleRide/HDRP/Road")
        {
            Debug.LogWarning($"[nagisa-roadwear] {path} uses '{mat.shader?.name}', expected MapleRide/HDRP/Road - skipping.");
            return;
        }

        // Sakura-parity wear levels (SakuraRoad.shader ships _MacroAmount 0.30/_PatchAmount 0.22/
        // _RoughVariation 0.45; Nagisa's asphalt reads flatter at the same defaults because the
        // surrounding scene is lower-contrast, so these are dialled a step further - PROVISIONAL,
        // tune by render).
        mat.SetFloat("_MacroAmount", 0.42f);       // 0.30 default: broader dark/light resurfacing blotches
        mat.SetFloat("_PatchAmount", 0.34f);       // 0.22 default: more visible resurfacing patches
        mat.SetFloat("_RoughVariation", 0.6f);     // 0.45 default: breaks up the uniform gloss
        mat.SetFloat("_TrackDarken", 0.42f);       // 0.30 default: wheel tracks read as worn/polished
        mat.SetFloat("_TrackPolish", 0.7f);        // 0.55 default
        mat.SetFloat("_JointDarken", 0.45f);       // 0.35 default: paving joints more visible
        mat.SetFloat("_EdgeRavelAmount", 0.68f);   // 0.55 default: ravelled/crumbling shoulder edge
        mat.SetFloat("_CrackScale", 0.7f);         // 0.55 default: finer crack noise frequency
        mat.SetFloat("_VergeBreakup", 0.55f);      // 0.42 default: less of a hard verge/road seam
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log($"[nagisa-roadwear] tuned {path} wear knobs (macro {mat.GetFloat("_MacroAmount")}, " +
                  $"patch {mat.GetFloat("_PatchAmount")}, track {mat.GetFloat("_TrackDarken")}).");
    }
}
