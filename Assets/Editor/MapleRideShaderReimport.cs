using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces a reimport of the hand-written MapleRide HDRP shaders.
///
/// Unity does not reliably recompile a .shader when only an .hlsl it includes has changed,
/// so an edit to MapleRideHDRPCommon.hlsl can land silently: two consecutive diagnostic
/// captures came back pixel-identical after a real fix to the shared colour-space bridge,
/// which cost a full capture cycle to notice. Run this between editing the include and
/// capturing, or the render being judged is the previous build of the shader.
/// </summary>
public static class MapleRideShaderReimport
{
    private const string Dir = "Assets/Environment/Shared/Shaders/HDRP";

    [MenuItem("MapleRide/Environment/Reimport HDRP Shaders")]
    public static void Run()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { Dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            n++;

            var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (sh != null && ShaderUtil.ShaderHasError(sh))
                Debug.LogError($"[sc-shader] COMPILE ERROR in {path}");
        }

        AssetDatabase.Refresh();
        Debug.Log($"[sc-shader] reimported {n} shader(s) from {Dir}");
    }
}
