using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reports Unity's own SRP Batcher compatibility verdict for each hand-written MapleRide HDRP
/// shader, per subshader.
///
/// Disabling the SRP Batcher demonstrably fixed a cross-material texture leak (every CelLit
/// surface was sampling the terrain's grass albedo), which proves the batcher is mis-binding
/// these shaders - but a static audit of Properties vs. the UnityPerMaterial CBUFFER found no
/// mismatch in any of the six. Unity computes the real verdict internally and shows it in the
/// shader Inspector; this reaches that same value through reflection so the incompatibility can
/// be fixed properly instead of shipping with batching permanently off.
/// </summary>
public static class MapleRideBatcherCompat
{
    private const string HdrpShaderDir = "Assets/Environment/Shared/Shaders/HDRP";

    [MenuItem("MapleRide/Environment/SRP Batcher Compatibility Report")]
    public static void Run()
    {
        var shaderUtil = typeof(ShaderUtil);
        var method = shaderUtil.GetMethod("GetSRPBatcherCompatibilityCode",
                                          BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var reasonMethod = shaderUtil.GetMethod("GetSRPBatcherCompatibilityIssueReason",
                                                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        if (method == null)
        {
            Debug.Log("[sc-batchcompat] GetSRPBatcherCompatibilityCode not found; available internals: " +
                      string.Join(", ", shaderUtil.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                                                  .Select(m => m.Name).Distinct().OrderBy(n => n)));
            return;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { HdrpShaderDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (sh == null) continue;

            for (int s = 0; s < sh.subshaderCount; s++)
            {
                object codeObj;
                try { codeObj = method.Invoke(null, new object[] { sh, s }); }
                catch (Exception e) { Debug.Log($"[sc-batchcompat] {sh.name}[{s}]: invoke failed {e.Message}"); continue; }

                int code = Convert.ToInt32(codeObj);
                string reason = "";
                if (reasonMethod != null && code != 0)
                {
                    try { reason = (string)reasonMethod.Invoke(null, new object[] { code }); }
                    catch { reason = "<reason lookup failed>"; }
                }

                Debug.Log($"[sc-batchcompat] '{sh.name}' subshader={s} code={code} " +
                          $"{(code == 0 ? "COMPATIBLE" : "NOT COMPATIBLE: " + reason)}");
            }
        }

        Debug.Log("[sc-batchcompat] DONE");
    }
}
