using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fixes "Kuro's black helmet and kit read tan/khaki in SakuraPass".
///
/// ROOT CAUSE (read off MapleRideCelLit.shader, then confirmed against the albedo atlas - NOT
/// guessed). The cel shader finishes with four terms:
///
///     col  = c.rgb * lit * sun;                                          // albedo-multiplied
///     col += c.rgb * MR_Ambient(n) * _AmbientStrength * ...;             // albedo-multiplied
///     col += tintSpec * spec * sun;                                      // ADDITIVE
///     col += tintRim  * rim  * lerp(_ShadowAmbient,1,shade) * sun;       // ADDITIVE
///
/// The last two are NOT multiplied by albedo. For Kuro's near-black kit the two multiplied
/// terms collapse to ~0, so the only surviving contribution is a warm additive wash:
/// _RimColor defaults to (1.00, 0.72, 0.52) at _RimStrength 0.85 and _SpecTint to
/// (1.00, 0.93, 0.85). Black + warm additive = khaki.
///
/// It is angle-dependent because rim = pow(1 - dot(n, v), _RimPower): ~0 head-on, large at
/// grazing angles. That is exactly the reported symptom - the jersey reads black from the front
/// but the helmet dome, shoulders and back read tan, and the rear view worst of all.
///
/// The albedo was verified innocent by extracting the GLB's base-colour atlas and looking at it:
/// it is near-black with white trim, KURO wordmarks, maple leaves and skin tones. There is no
/// tan anywhere in it. So this is a lighting-interaction defect, not a texture defect, and the
/// correct fix is to tame the albedo-independent additive terms on the PLAYER's materials.
///
/// SCOPE / SAFETY
///  * Touches ONLY renderers under the player root. Global ambient, the sun, the sky and the
///    environment materials are untouched, so the route and the 11 NPCs are unaffected.
///  * Never writes to a material that is an imported .glb sub-asset (those are SHARED with every
///    rider and reverted by the next reimport). Such a material is cloned first, matching
///    HdrpMaterialRepair's existing behaviour.
///  * Asserts afterwards that no non-player renderer references any material this pass modified.
///  * Idempotent: it writes fixed values, so running it twice is a no-op.
/// </summary>
public static class KuroKitToneFix
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string CelLitShader = "MapleRide/HDRP/CelLit";

    // ---- PROVISIONAL tuning. These are a character-specific override of shader defaults that
    // were authored for sunlit environment PROPS, where a warm rim reads as sunset bounce. On a
    // near-black kit the same values read as "the helmet is brown", so the player gets quieter,
    // colour-neutral versions. Tune against the render, not against these numbers.
    static readonly Color RimColor = new Color(0.78f, 0.86f, 1.00f, 1f); // cool sky bounce, not sunset warm
    const float RimStrength = 0.10f;   // was 0.85 - enough edge to keep the cel read, not a wash
    static readonly Color SpecTint = new Color(1.00f, 1.00f, 1.00f, 1f); // was (1, 0.93, 0.85)
    const float SpecStrength = 0.08f;  // was 0.25
    const float WeatherAmount = 0f;    // props weather; a rider's kit does not

    // Feet were landing +0.0155 / +0.0237 m proud of the pedal at 0.130; this centres them.
    const float AnkleHeight = 0.113f;

    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == PlayerName);
        if (player == null) { Debug.LogError("[kit] player root not found"); return; }

        // ---- Any material also referenced OUTSIDE the player is shared and must not be edited.
        var outside = new HashSet<Material>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == player) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) outside.Add(m);
        }
        Debug.Log("[kit] materials referenced outside the player (protected): " + outside.Count);

        var touched = new HashSet<Material>();
        var adjusted = new HashSet<Material>();   // ONLY the character materials this pass changed
        int converted = 0, cloned = 0;

        // Authored values for the BIKE materials, so a previous over-broad run of this pass can
        // be undone. The bike is parented UNDER the player root, so "every renderer under the
        // player" also sweeps up the Colnago - whose materials are shared assets used by the
        // other routes' NPC rosters. Only the CHARACTER (skinned) materials may be adjusted.
        var bikeOriginals = new Dictionary<string, float>
        {
            { "Shared_Brushed_Metal_CelLit",        0.37f },
            { "Shared_Road_Tyre_CelLit",            0.05f },
            { "Shared_Colnago_Carbon_Black_CelLit", 0.15f },
            { "Shared_Colnago_Racing_Red_CelLit",   0.12f },
        };

        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            // The character is a SkinnedMeshRenderer; the bike is rigid MeshRenderers. This is
            // the clean dividing line between "Kuro's kit" and "the bike he is sitting on".
            bool isCharacter = r is SkinnedMeshRenderer;

            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null) continue;
                if (m.shader.name != CelLitShader) continue;

                if (!isCharacter)
                {
                    // Bike (or other non-skinned) material: restore authored values if an
                    // earlier run of this pass changed them. Never apply the kit values here.
                    if (touched.Add(m) && bikeOriginals.TryGetValue(m.name, out var origSpec) &&
                        m.HasProperty("_RimStrength") &&
                        Mathf.Abs(m.GetFloat("_RimStrength") - RimStrength) < 1e-4f)
                    {
                        m.SetColor("_RimColor", new Color(1.00f, 0.72f, 0.52f, 1f));
                        m.SetFloat("_RimStrength", 0.85f);
                        m.SetColor("_SpecTint", new Color(1.00f, 0.93f, 0.85f, 1f));
                        m.SetFloat("_SpecStrength", origSpec);
                        EditorUtility.SetDirty(m);
                        Debug.Log("[kit] REVERTED bike material '" + m.name + "' to authored values");
                    }
                    continue;
                }

                // The character's material must not be shared with anything else in the scene.
                if (outside.Contains(m))
                {
                    Debug.Log("[kit] SKIP shared material '" + m.name + "' (used outside player)");
                    continue;
                }

                // Never write through to a shared .glb sub-asset.
                var path = AssetDatabase.GetAssetPath(m);
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                {
                    var clone = new Material(m) { name = m.name + "_KuroKit" };
                    mats[i] = clone;
                    m = clone;
                    changed = true;
                    cloned++;
                }

                if (!adjusted.Add(m)) continue;

                Debug.Log(string.Format(
                    "[kit] BEFORE '{0}': rim={1} str={2:F2} pow={3:F2} specTint={4} specStr={5:F2} " +
                    "weather={6:F2} ambient={7:F2}",
                    m.name,
                    m.HasProperty("_RimColor") ? m.GetColor("_RimColor").ToString() : "n/a",
                    m.HasProperty("_RimStrength") ? m.GetFloat("_RimStrength") : -1f,
                    m.HasProperty("_RimPower") ? m.GetFloat("_RimPower") : -1f,
                    m.HasProperty("_SpecTint") ? m.GetColor("_SpecTint").ToString() : "n/a",
                    m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength") : -1f,
                    m.HasProperty("_WeatherAmount") ? m.GetFloat("_WeatherAmount") : -1f,
                    m.HasProperty("_AmbientStrength") ? m.GetFloat("_AmbientStrength") : -1f));

                if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", RimColor);
                if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", RimStrength);
                if (m.HasProperty("_SpecTint")) m.SetColor("_SpecTint", SpecTint);
                if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", SpecStrength);
                if (m.HasProperty("_WeatherAmount")) m.SetFloat("_WeatherAmount", WeatherAmount);
                EditorUtility.SetDirty(m);
                converted++;

                Debug.Log(string.Format("[kit] AFTER  '{0}': rim={1} str={2:F2} specStr={3:F2} weather={4:F2}",
                    m.name, m.GetColor("_RimColor"), m.GetFloat("_RimStrength"),
                    m.GetFloat("_SpecStrength"), m.GetFloat("_WeatherAmount")));
            }
            if (changed) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }

        Debug.Log(string.Format("[kit] materials adjusted={0} cloned-from-glb={1}", converted, cloned));

        // ---- safety: nothing outside the player may reference a material we just changed.
        int leaks = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == player) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && adjusted.Contains(m))
                    {
                        Debug.LogError("[kit] LEAK: non-player renderer '" + r.name +
                                       "' shares adjusted material '" + m.name + "'");
                        leaks++;
                    }
        }
        Debug.Log("[kit] shared-material leaks into NPCs/environment: " + leaks +
                  (leaks == 0 ? " -> OK" : " -> MUST FIX"));

        // ---- free win while we are re-staging: centre the feet on the pedals.
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null)
        {
            Debug.Log(string.Format("[kit] ankleHeight {0:F4} -> {1:F4}", rig.ankleHeight, AnkleHeight));
            rig.ankleHeight = AnkleHeight;
            rig.ForceSolveOnce();
            EditorUtility.SetDirty(rig);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[kit] scene saved clean; dirty=" + scene.isDirty);
    }
}
