using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FIX: "Kuro renders as a flat black silhouette in-game (helmet/jersey/gloves have no readable
/// form, no white maple-leaf, no skin) instead of the lit matte-black of Reference.png."
///
/// ROOT CAUSE (verified, not guessed):
///  * The in-scene player "Kuro on Sakura Pass" is ONE SkinnedMeshRenderer (Mesh_0) with ONE
///    material PlayerBody_Image_0_CelLit on shader MapleRide/HDRP/CelLit. Its albedo atlas
///    (Image_0, from KuroNPC_KuroAnime_Rigged.glb) is correct - it contains skin, white "KURO"
///    wordmarks, white maple leaves and a genuinely near-black kit (~0.05 sRGB).
///  * Every lighting term in MR_Shade is albedo-multiplied (col = c.rgb*lit*sun + c.rgb*ambient).
///    The additive rim/spec were already tamed to near-neutral by KuroKitToneFix to stop the kit
///    reading khaki. With a ~0.05 albedo and no floor, the shadowed / BACKLIT side (which the
///    rear chase camera always sees) collapses to pure black. The reference is studio front-lit,
///    so it never hits this.
///
/// FIX: give the cel shader a neutral _MatteFloor (added zero-default, so environment is
/// untouched) and set it only on the PLAYER material, so matte black keeps a real dark-grey value
/// that catches diffuse + ambient and reads as form. Also lift _ShadowAmbient on the player kit a
/// little so the backlit side is a soft cel shadow rather than a black hole.
///
/// SAFETY (mirrors KuroKitToneFix): only renderers under the player root are touched; any material
/// also referenced outside the player is cloned first; asserts afterwards that no non-player
/// renderer references an adjusted material. Idempotent.
/// </summary>
public static class FiKuroMatteFloorFix
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string CelLitShader = "MapleRide/HDRP/CelLit";

    // Tunable against the render. Authored/sRGB values (the shader re-bridges Color props).
    static readonly Color MatteFloor = new Color(0.145f, 0.145f, 0.155f, 1f);
    const float ShadowAmbient = 0.52f;   // was 0.38 - keep the backlit cel shadow readable

    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == PlayerName);
        if (player == null) { Debug.LogError("[matte] player root not found"); return; }

        var outside = new HashSet<Material>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == player) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) outside.Add(m);
        }

        var adjusted = new HashSet<Material>();
        int changed = 0, cloned = 0;

        foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool swap = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name != CelLitShader) continue;

                if (outside.Contains(m))
                {
                    var clone = new Material(m) { name = m.name + "_KuroKit" };
                    mats[i] = clone; m = clone; swap = true; cloned++;
                    Debug.Log("[matte] cloned shared material -> " + clone.name);
                }
                else
                {
                    var path = AssetDatabase.GetAssetPath(m);
                    if (!string.IsNullOrEmpty(path) &&
                        path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var clone = new Material(m) { name = m.name + "_KuroKit" };
                        mats[i] = clone; m = clone; swap = true; cloned++;
                        Debug.Log("[matte] cloned glb-embedded material -> " + clone.name);
                    }
                }

                if (!adjusted.Add(m)) continue;

                Debug.Log(string.Format("[matte] BEFORE '{0}': matteFloor={1} shadowAmb={2:F2}",
                    m.name,
                    m.HasProperty("_MatteFloor") ? m.GetColor("_MatteFloor").ToString("F3") : "n/a",
                    m.HasProperty("_ShadowAmbient") ? m.GetFloat("_ShadowAmbient") : -1f));

                if (m.HasProperty("_MatteFloor")) m.SetColor("_MatteFloor", MatteFloor);
                if (m.HasProperty("_ShadowAmbient")) m.SetFloat("_ShadowAmbient", ShadowAmbient);
                EditorUtility.SetDirty(m);
                changed++;

                Debug.Log(string.Format("[matte] AFTER  '{0}': matteFloor={1} shadowAmb={2:F2}",
                    m.name, m.GetColor("_MatteFloor").ToString("F3"), m.GetFloat("_ShadowAmbient")));
            }
            if (swap) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }

        int leaks = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == player) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && adjusted.Contains(m))
                    { Debug.LogError("[matte] LEAK into '" + r.name + "' via '" + m.name + "'"); leaks++; }
        }
        Debug.Log($"[matte] adjusted={changed} cloned={cloned} leaks={leaks} " + (leaks == 0 ? "-> OK" : "-> MUST FIX"));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[matte] saved; dirty=" + scene.isDirty);

        Debug.Log("[matte] --- rendering after via AnimeKuroChaseShot.RunPlayer ---");
        AnimeKuroChaseShot.RunPlayer();
    }
}
