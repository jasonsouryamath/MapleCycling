using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Read-only health report for the hand-written HDRP shader set.
///
/// Written because a capture came back with the terrain and sky simply absent (flat clear
/// colour, no horizon) while the road, trees and houses drew normally. A shader that fails to
/// compile does not necessarily show as magenta under HDRP's custom ForwardOnly passes - it can
/// silently draw nothing at all, which is indistinguishable from "the object is missing" in a
/// screenshot. This prints the one fact that separates those cases.
/// </summary>
public static class MapleRideShaderHealth
{
    private const string HdrpShaderDir = "Assets/Environment/Shared/Shaders/HDRP";

    [MenuItem("MapleRide/Environment/Shader Health Report")]
    public static void Run()
    {
        var guids = AssetDatabase.FindAssets("t:Shader", new[] { HdrpShaderDir });
        Debug.Log($"[sc-health] inspecting {guids.Length} shaders under {HdrpShaderDir}");

        int broken = 0;
        foreach (var guid in guids.OrderBy(g => g))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (sh == null)
            {
                Debug.Log($"[sc-health] FAIL {path}: asset did not load as a Shader");
                broken++;
                continue;
            }

            var msgs = ShaderUtil.GetShaderMessages(sh);
            int errors = msgs.Count(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);

            string state = sh.isSupported && errors == 0 ? "OK  " : "FAIL";
            if (state == "FAIL") broken++;

            Debug.Log($"[sc-health] {state} '{sh.name}' supported={sh.isSupported} " +
                      $"errors={errors} msgs={msgs.Length} passes={sh.passCount} " +
                      $"subshaders={sh.subshaderCount} queue={sh.renderQueue} path={path}");

            foreach (var m in msgs.Take(12))
            {
                Debug.Log($"[sc-health]     [{m.severity}] {m.message} | {m.messageDetails} " +
                          $"({m.file}:{m.line}) platform={m.platform}");
            }
        }

        Debug.Log($"[sc-health] DONE broken={broken}");
    }
}
