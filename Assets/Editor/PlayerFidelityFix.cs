using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Raises the player avatar (Kuro) to the approved model sheet: matte-black aero helmet, a
/// black KAGE bike with red accents (not a red frame), and a legible face under the brim.
///
/// This is a LIVERY / staging pass, deliberately separate from BuildSakuraPass so it does not
/// tear the whole scene (and the environment) down. It is idempotent: it clones the player's
/// materials into Assets/Kuro/Materials/ under FIXED asset names, re-applies the colours every
/// run, and pushes the clones onto the serialized renderers in the saved scene. It never writes
/// to the shared imported GLB materials, so NPC bikes/liveries are untouched, and a reimport
/// cannot revert it (the scene references the .mat assets).
public static class PlayerFidelityFix
{
    const string ScenePath  = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string MatDir     = "Assets/Kuro/Materials";

    // The body/face/hair texture embedded in kuro_cycle_bib_colored.glb ("texture_0") is a
    // corrupted source asset: only mip level 0 actually contains real image data, mip levels
    // 1-11 of the glTFast-imported texture are blank/uninitialized white. At any camera distance
    // where the GPU needs a lower mip (any shot that isn't an extreme close-up), sampling falls
    // into that blank chain and blends toward solid white, which is what reads as a shredded,
    // dithered, near-see-through "static" artifact on skin/hair. A plain re-import of the GLB
    // cannot fix this (the corruption is baked into the source PNG itself, confirmed identical
    // byte-for-byte across every kuro_*_fixed/kuro_cycle_* variant in the project), so instead we
    // re-bake mip 0 (the only valid data) into a normal, standalone Texture2D asset
    // (Kuro_Body_BaseColor.png) that Unity's own texture importer mip-maps correctly, and swap
    // that in on the cloned body material. This preserves every authored pixel (face/eyes/visor
    // accents included) while removing the broken mip chain that produces the artifact.
    //
    // That alone is NOT sufficient, though: this same material also has an emissiveTexture bound
    // to the SAME corrupted image at emissiveFactor = (1,1,1,1) (full strength) - a second,
    // independent binding of the broken asset that keeps painting through as an unlit glow no
    // matter what baseColorTexture is swapped to. No sibling material (Helmet, Hanakage_Body,
    // etc.) has any emission - this is an authoring anomaly on this material alone, so
    // FixBrokenBodyTexture() below also clears it.
    const string BodyBaseColorTex = "Assets/Kuro/Materials/Kuro_Body_BaseColor_PR.png";

    // --- Sheet palette (sRGB) -------------------------------------------------------------
    // Kage Collective: blacks/greys with a single red accent.
    static readonly Color HelmetBlack = new Color32(0x1C, 0x1C, 0x20, 0xFF); // matte aero shell
    static readonly Color FrameBlack  = new Color32(0x15, 0x15, 0x18, 0xFF); // carbon frame
    static readonly Color RacingRed   = new Color32(0xC2, 0x22, 0x28, 0xFF); // retired KAGE accent

    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    // --- BLACK-STAYS-BLACK CLAMP (the "brown helmet" fix) ---------------------------------
    // MapleRide/HDRP/CelLit accumulates four terms (MapleRideCelLit.shader ~L174-177). The
    // first two are multiplied by albedo; the last two are NOT:
    //     col += tintSpec * spec * sun;
    //     col += tintRim  * rim  * lerp(_ShadowAmbient,1,shade) * sun;
    // On a near-black surface the albedo-scaled terms contribute almost nothing, so ALL that
    // survives is warm additive spec + rim (both scaled by the warm MR_SunColor()). That is
    // exactly why the black aero helmet and black kit render brown/tan in-game while the
    // isolated Blender studio turnarounds - no warm sun, no CelLit - render them black.
    // The shader's own defaults are warm and strong (_RimColor (1,0.72,0.52), _RimStrength
    // 0.85, _SpecTint (1,0.93,0.85), _SpecStrength 0.25), and rim peaks at grazing angles
    // (n.v -> 0), which is why the brown concentrates on the helmet ribs and the silhouette.
    //
    // Clamping these two additive terms and cooling their tint cannot crush the white
    // branding (maple leaf / KURO / the kanji), because white is BRIGHT ALBEDO and therefore
    // lives entirely in the albedo-scaled terms, which are left untouched. Same values as
    // ShiosaiRealisticRiderPoc's clamp so the in-game player and the POC read identically.
    // PROVISIONAL tuning.
    static readonly Color CelRimColor  = new Color(0.50f, 0.54f, 0.68f, 1f); // cool: the sun it multiplies is warm
    // Strong enough to separate black hair, helmet, kit and bike at chase-camera distance,
    // while remaining far below the shader's khaki-producing 0.85 default.
    const float CelRimStrength  = 0.14f;
    static readonly Color CelSpecTint  = new Color(0.82f, 0.86f, 1.00f, 1f);
    const float CelSpecStrength = 0.04f;

    // The red wheel-rim accent is RETIRED. It dates from the KAGE-era palette; the approved
    // KURO model sheet (reference/improve/TARGET_kuro_anime_YESYESYES.png) specifies an
    // all-black KURO carbon bike, and the user flagged the visible red part on the staged bike
    // as a defect. Emptying this set leaves Rim_F/Rim_R on the shared brushed-metal material,
    // which is what the sheet's bike panels show. The mechanism below is kept (and RacingRed
    // with it) so a future accent colour is a one-line change rather than a rewrite.
    static readonly System.Collections.Generic.HashSet<string> RedRimParts =
        new System.Collections.Generic.HashSet<string>();

    [MenuItem("MapleRide/Fix Player Fidelity")]
    public static void RunMenu() => Run();

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Kuro", "Materials");

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[fidelity] no player object."); EditorApplication.Exit(1); return; }

        var bikeT = FindChild(player.transform, "Bike");

        // ---- BODY: matte-black helmet -----------------------------------------------------
        // The helmet is its own submesh/material (Kuro_Helmet_White, solid ~white baseColor,
        // no texture) so it recolours cleanly without disturbing the textured body/face/kit.
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = smr.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string bn = BaseName(mats[i]);
                Debug.Log($"[fidelity] body '{smr.name}' slot{i}: '{bn}' shader='{mats[i].shader.name}'");

                // HARDENING GUARD (over-match -> "matte head becomes a black blob"):
                // PlayerBody_*_CelLit are the APPROVED matte body/head/hair/shoe/decal atlas
                // clones built by KuroPlayerBodyCelLit. One of them - PlayerBody_KuroHelmetMatte_CelLit -
                // has "Helmet" in its NAME (it is the head/helmet ATLAS, not the aero shell), so the
                // bn.Contains("Helmet") branch below was cloning it to a solid-black Kuro_Helmet_Black
                // and destroying the head/face texture. These clones are already the approved finish;
                // never let any livery branch here re-process them. This is the specific
                // PlayerBody_KuroHelmetMatte revert the seating-hardening pass must prevent.
                if (bn.StartsWith("PlayerBody_")) { Debug.Log($"[fidelity] skip approved body clone '{bn}'"); continue; }

                if (bn.Contains("Helmet"))
                {
                    var clone = CloneMat(mats[i], "Kuro_Helmet_Black");
                    SetColor(clone, HelmetBlack);
                    SetFloatIfHas(clone, "_Metallic", 0.0f);
                    SetFloatIfHas(clone, "_Smoothness", 0.20f);
                    SetFloatIfHas(clone, "_Glossiness", 0.20f);
                    SetFloatIfHas(clone, "metallicFactor", 0.0f);
                    SetFloatIfHas(clone, "roughnessFactor", 0.62f);
                    EditorUtility.SetDirty(clone);
                    mats[i] = clone; touched = true;
                    Debug.Log("[fidelity] helmet -> matte black");
                }
                // The body/face/hair/kit share one textured material at roughness 0.30 - glossy
                // enough that every flat-shaded hair facet catches a specular highlight, which is
                // what reads as crystalline "shards" of noise. Clone it matte so the hair reads as
                // hair, not shattered glass. Keeps the atlas (SetColor left at white by copy), so
                // the face's red eyes/visor and the kit's red accents are untouched.
                else if (bn.Contains("Material_1") || bn.Contains("Kuro_Body_Matte"))
                {
                    var clone = CloneMat(mats[i], "Kuro_Body_Matte");
                    SetFloatIfHas(clone, "_Metallic", 0.0f);
                    SetFloatIfHas(clone, "_Smoothness", 0.06f);
                    SetFloatIfHas(clone, "_Glossiness", 0.06f);
                    SetFloatIfHas(clone, "metallicFactor", 0.0f);
                    SetFloatIfHas(clone, "roughnessFactor", 0.90f);
                    FixBrokenBodyTexture(clone);
                    EditorUtility.SetDirty(clone);
                    mats[i] = clone; touched = true;
                    Debug.Log("[fidelity] body -> matte (calms hair facets) + rebaked base color texture");
                }
                // The replacement Kuro rig exports its helmet/hair/kit atlas as these two glTF
                // slots.  They were missed by the older P14-specific matching above, leaving
                // the source emissive map active in HDRP and making the whole rider blow out
                // white under the sun.  Keep its correct atlas, but make both slots matte and
                // explicitly non-emissive.
                else if (bn.Contains("Material_0.002") || bn.Contains("Material_0.003"))
                {
                    var clone = CloneMat(mats[i], "Kuro_Real_" + (bn.Contains("002") ? "Kit" : "Detail") + "_Matte");
                    SetFloatIfHas(clone, "_Metallic", 0.0f);
                    SetFloatIfHas(clone, "_Smoothness", 0.08f);
                    SetFloatIfHas(clone, "_Glossiness", 0.08f);
                    SetFloatIfHas(clone, "metallicFactor", 0.0f);
                    SetFloatIfHas(clone, "roughnessFactor", 0.88f);
                    if (clone.HasProperty("emissiveTexture")) clone.SetTexture("emissiveTexture", null);
                    if (clone.HasProperty("_EmissionMap")) clone.SetTexture("_EmissionMap", null);
                    if (clone.HasProperty("emissiveFactor")) clone.SetColor("emissiveFactor", Color.black);
                    if (clone.HasProperty("_EmissionColor")) clone.SetColor("_EmissionColor", Color.black);
                    EditorUtility.SetDirty(clone);
                    mats[i] = clone; touched = true;
                    Debug.Log("[fidelity] replacement Kuro material -> matte, non-emissive");
                }
            }
            if (touched) { smr.sharedMaterials = mats; EditorUtility.SetDirty(smr); }
        }

        // ---- BIKE: fully black frame (no red) --------------------------------------------
        // The Colnago is ~78 parts sharing 4 materials. The FRAME tubes ship on
        // Colnago_Racing_Red (that is why it reads as a red bike), so recolour that to carbon
        // black.
        //
        // ORDERING MATTERS: KuroCharacterCelLitFix clones every still-embedded GLB material
        // into a SHARED Assets/Kuro/Materials/Shared_*_CelLit asset and re-points every rider's
        // renderers - including the player's - at those shared clones. That silently reverts
        // this pass, putting the player's frame back on Shared_Colnago_Racing_Red_CelLit
        // (0.722, 0.112, 0.143) and re-introducing the red seat-tube/fork the user reported.
        // This pass must therefore run AFTER any CelLit conversion. The match below is on the
        // material's base name, so it catches both the raw 'Colnago_Racing_Red' and the
        // converted 'Shared_Colnago_Racing_Red_CelLit'; the clone it writes is per-player, so
        // the shared asset (and every NPC that legitimately rides a red frame) is untouched.
        if (bikeT != null)
        {
            foreach (var r in bikeT.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool touched = false;
                bool isRim = RedRimParts.Contains(r.gameObject.name);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string bn = BaseName(mats[i]);

                    // Whole frame -> carbon black.
                    if (bn.Contains("Racing_Red") || bn.Contains("Frame_Black"))
                    {
                        var clone = CloneMat(mats[i], "KuroBike_Frame_Black");
                        SetColor(clone, FrameBlack);
                        EditorUtility.SetDirty(clone);
                        mats[i] = clone; touched = true;
                    }
                    // The two wheel rims -> KAGE red accent (clone from the shared brushed metal,
                    // assigned ONLY to Rim_F / Rim_R so the spokes/hubs/cranks stay metal).
                    else if (isRim && (bn.Contains("Brushed_Metal") || bn.Contains("Rim_Red")))
                    {
                        var clone = CloneMat(mats[i], "KuroBike_Rim_Red");
                        SetColor(clone, RacingRed);
                        SetFloatIfHas(clone, "_Metallic", 0.2f);
                        SetFloatIfHas(clone, "metallicFactor", 0.2f);
                        SetFloatIfHas(clone, "roughnessFactor", 0.45f);
                        SetFloatIfHas(clone, "_Smoothness", 0.45f);
                        SetFloatIfHas(clone, "_Glossiness", 0.45f);
                        EditorUtility.SetDirty(clone);
                        mats[i] = clone; touched = true;
                    }
                }
                if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
            }
            Debug.Log("[fidelity] bike -> all-black frame (red accent retired)");
        }
        else Debug.LogWarning("[fidelity] no 'Bike' child under player.");

        EditorUtility.SetDirty(player);

        ClampPlayerCelLit(player);

        // KuroPhotorealPass is NOT chained here anymore. The player now ships the anime Kuro
        // build (reference/improve/TARGET_kuro_anime_YESYESYES.png via MapleRideKuroSetup's
        // ModelPath), and KuroPhotorealPass.Apply() unconditionally rebinds a reflective/red-
        // visor photoreal PBR atlas onto "Kuro on Sakura Pass" - the exact override this fix is
        // for. That pass (and its "MapleRide/Kuro/Apply Photoreal Materials" menu item) is left
        // intact for anyone still using it against a legacy photoreal player build; it is simply
        // no longer invoked automatically from this pass.

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[fidelity] done + saved scene.");
        EditorApplication.Exit(0);
    }

    // --- helpers --------------------------------------------------------------------------

    const string CelLitName = "MapleRide/HDRP/CelLit";

    /// Applies the BLACK-STAYS-BLACK clamp (see the note on CelRimColor) to every CelLit
    /// material on the player.
    ///
    /// Materials are cloned into player-owned assets FIRST where needed. KuroCharacterCelLitFix
    /// converts still-embedded GLB materials into SHARED Assets/Kuro/Materials/Shared_*_CelLit
    /// assets referenced by the player AND every NPC, so clamping one of those in place would
    /// silently restyle the whole roster. A material already living in MatDir under a
    /// non-"Shared_" name is this pass's own clone and is clamped directly.
    static void ClampPlayerCelLit(GameObject player)
    {
        int clamped = 0, cloned = 0;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name != CelLitName) continue;

                if (!IsPlayerOwned(m))
                {
                    var clone = CloneMat(m, "Player_" + BaseName(m));
                    mats[i] = clone; m = clone; touched = true; cloned++;
                }

                if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", CelSpecStrength);
                if (m.HasProperty("_SpecTint"))     m.SetColor("_SpecTint", CelSpecTint);
                if (m.HasProperty("_RimStrength"))  m.SetFloat("_RimStrength", CelRimStrength);
                if (m.HasProperty("_RimColor"))     m.SetColor("_RimColor", CelRimColor);
                EditorUtility.SetDirty(m);
                clamped++;
            }
            if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }
        Debug.Log($"[fidelity] black-stays-black clamp on {clamped} CelLit material slot(s) ({cloned} newly cloned).");
    }

    static bool IsPlayerOwned(Material m)
    {
        string p = AssetDatabase.GetAssetPath(m);
        return !string.IsNullOrEmpty(p)
               && p.StartsWith(MatDir + "/")
               && !BaseName(m).StartsWith("Shared_");
    }

    static string BaseName(Material m) => m.name.Replace(" (Instance)", "");

    static Material CloneMat(Material src, string assetName)
    {
        string path = $"{MatDir}/{assetName}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            // Re-runs: refresh from a pristine source only if src is not the clone itself.
            if (src != existing)
            {
                existing.shader = src.shader;
                existing.CopyPropertiesFromMaterial(src);
                existing.name = assetName;
            }
            return existing;
        }
        var m = new Material(src);
        m.CopyPropertiesFromMaterial(src);
        m.name = assetName;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Swaps the material's base color texture for the re-baked, correctly-mipped
    // Kuro_Body_BaseColor.png asset (see comment on BodyBaseColorTex above). Sets it via
    // mainTexture so it works regardless of the exact glTF/PBR shader property name, and also
    // through the common shader-specific slots so it sticks if mainTexture isn't wired for this
    // shader variant.
    static void FixBrokenBodyTexture(Material m)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(BodyBaseColorTex);
        if (tex == null)
        {
            Debug.LogWarning($"[fidelity] missing {BodyBaseColorTex}, leaving original (broken-mip) body texture in place.");
            return;
        }

        m.mainTexture = tex;
        foreach (var p in new[] { "_BaseMap", "_MainTex", "baseColorTexture" })
            if (m.HasProperty(p)) m.SetTexture(p, tex);

        // The real reason the shredded artifact kept showing even after the base color texture
        // swap above: this material also has emissiveFactor = (1,1,1,1) (full strength) with an
        // emissiveTexture bound to the SAME corrupted source image (a GLB subasset, independent
        // of baseColorTexture). Emission is added on top of lit shading regardless of view angle
        // or the base color texture in use, so the broken/blank-mip texture kept painting through
        // as a full-brightness glow no matter what baseColorTexture pointed at. Every sibling
        // material (Kuro_Helmet_Black, Hanakage_Body, etc.) has emissiveFactor = 0 and no
        // emissiveTexture at all - this is an authoring anomaly on this one material, not an
        // intended glow. Clear it to match the correct no-glow design used everywhere else.
        if (m.HasProperty("emissiveTexture")) m.SetTexture("emissiveTexture", null);
        if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", null);
        if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", Color.black);
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
    }

    static void SetColor(Material m, Color c)
    {
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, p == "baseColorFactor" ? c.linear : c);
    }

    static void SetFloatIfHas(Material m, string p, float v)
    {
        if (m.HasProperty(p)) m.SetFloat(p, v);
    }

    static Transform FindChild(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindChild(c, name); if (r != null) return r; }
        return null;
    }
}
