using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Toggles the SRP Batcher on every MapleRide HDRP quality asset.
///
/// Diagnostic for a cross-material texture leak: after the terrain started drawing again, every
/// CelLit surface began sampling the terrain's grass albedo - including lane-marking materials
/// that have no _MainTex assigned at all - while the terrain itself picked up a bark texture.
/// Geometry silhouettes proved the road was still being drawn by its own mesh, so this is a
/// binding failure, not an occlusion or texture-assignment failure: the texture slot is
/// inheriting whatever the previous draw left in it. The SRP Batcher is the subsystem that
/// elides redundant per-draw binds, so toggling it isolates the cause in a single capture.
/// </summary>
public static class HdrpBatcherToggle
{
    private const string Dir = "Assets/Settings/HDRP";

    [MenuItem("MapleRide/Environment/HDRP Batcher - Disable")]
    public static void Disable() => Set(false);

    [MenuItem("MapleRide/Environment/HDRP Batcher - Enable")]
    public static void Enable() => Set(true);

    private static void Set(bool on)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:RenderPipelineAsset", new[] { Dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
            if (asset == null) continue;

            // enableSRPBatcher is a direct serialised field on the asset in this HDRP version
            // (it sits at the asset's top level in the YAML, not under m_RenderPipelineSettings).
            var so = new SerializedObject(asset);
            var prop = so.FindProperty("enableSRPBatcher");
            if (prop == null)
            {
                Debug.LogWarning($"[sc-batcher] {path}: enableSRPBatcher property not found");
                continue;
            }

            prop.boolValue = on;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            Debug.Log($"[sc-batcher] {path}: enableSRPBatcher -> {on}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[sc-batcher] DONE set={on}");
    }
}
