using UnityEditor;
using UnityEngine;

/// <summary>
/// Fixes MapleRide materials that ask for a normal map but have none assigned (2026-10-02, Claude).
///
/// Symptom: the Nagisa Bay Japanese black pines (trunk + needle pads), and anything else built
/// the same way, render exactly (0,0,0). CelLit/Foliage guard the normal-map branch with
/// _NormalStrength &gt; 0 only, so a material with strength 0.9 and an EMPTY _NormalMap samples a
/// black texture; UnpackNormalScale then gives xy = (-1,-1) and z = sqrt(1 - 2) = NaN, and HDRP
/// clamps the NaN pixel to black. The tangent-space branch has nothing to apply anyway, so
/// strength 0 (flat shading from the vertex normal) is the correct value.
/// </summary>
public static class NormalMapRepair
{
    public static void Run()
    {
        int scanned = 0, repaired = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null || !m.shader.name.StartsWith("MapleRide/")) continue;
            if (!m.HasProperty("_NormalMap") || !m.HasProperty("_NormalStrength")) continue;
            scanned++;
            if (m.GetFloat("_NormalStrength") <= 0f || m.GetTexture("_NormalMap") != null) continue;
            Debug.Log($"[normalmap-repair] {path}: _NormalStrength {m.GetFloat("_NormalStrength"):F2} with no _NormalMap -> 0 ({m.shader.name})");
            m.SetFloat("_NormalStrength", 0f);
            EditorUtility.SetDirty(m);
            repaired++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[normalmap-repair] scanned {scanned} normal-map-capable materials; repaired {repaired}.");
    }
}
