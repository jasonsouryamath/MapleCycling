using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only scene-wide census of what shader every *drawn* renderer is actually using.
///
/// Written because the terrain and sky vanished from a capture while every shader in the
/// hand-written HDRP set compiled cleanly and reported supported=True. Under HDRP a material
/// left on a Built-in shader does not render magenta - it renders *nothing*, because no pass
/// matches HDRP's light loop. That failure mode is invisible to both a shader-compile check and
/// a screenshot, so the only way to find it is to ask each renderer in the scene what it is
/// actually pointing at and group by render pipeline compatibility.
///
/// Opens the scene read-only and never saves it.
/// </summary>
public static class ShiosaiSceneShaderCensus
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Scene Shader Census")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // shader name -> (renderer count, an example object, total world bounds volume)
        var counts = new Dictionary<string, int>();
        var examples = new Dictionary<string, string>();
        var biggest = new Dictionary<string, float>();

        int nullMat = 0, total = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // Do NOT skip inactive renderers. Every region except the currently-selected one
                // is deactivated in the saved scene and only switched on by RegionDirector at
                // capture time, so an activeInHierarchy filter hides the entire region under
                // investigation and makes the scene look like it contains only Maple City.
                if (r.sharedMaterials == null) continue;
                total++;
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) { nullMat++; continue; }
                    string key = mat.shader.name;
                    counts.TryGetValue(key, out int c);
                    counts[key] = c + 1;

                    var b = r.bounds.size;
                    float vol = b.x * b.z;
                    biggest.TryGetValue(key, out float bv);
                    if (vol >= bv)
                    {
                        biggest[key] = vol;
                        examples[key] = $"{r.gameObject.name} (footprint {b.x:0}x{b.z:0}m, mat '{mat.name}')";
                    }
                }
            }
        }

        Debug.Log($"[sc-census] active renderers={total} nullMaterialSlots={nullMat} distinctShaders={counts.Count}");

        // Per-material detail for the region under investigation. The pipeline histogram alone
        // cannot distinguish "the ground is missing" from "the ground is drawing, but black".
        var shiosai = new SortedDictionary<string, string>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    if (mat.name.IndexOf("Shiosai", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (shiosai.ContainsKey(mat.name)) continue;

                    string tint = mat.HasProperty("_Color") ? mat.GetColor("_Color").ToString("F3") : "-";
                    var b = r.bounds.size;
                    shiosai[mat.name] = $"shader='{mat.shader.name}' queue={mat.renderQueue} tint={tint} " +
                                        $"obj='{r.gameObject.name}' active={r.gameObject.activeInHierarchy} " +
                                        $"size={b.x:0}x{b.y:0}x{b.z:0}m";
                }
            }
        }
        foreach (var kv in shiosai) Debug.Log($"[sc-shiosai] {kv.Key,-34} {kv.Value}");
        Debug.Log($"[sc-shiosai] {shiosai.Count} distinct Shiosai materials");

        foreach (var kv in counts.OrderByDescending(k => k.Value))
        {
            var sh = Shader.Find(kv.Key);
            string pipe = "<unknown>";
            if (sh != null)
            {
                var tags = new List<string>();
                for (int s = 0; s < sh.subshaderCount; s++)
                    tags.Add(sh.FindSubshaderTagValue(s, new UnityEngine.Rendering.ShaderTagId("RenderPipeline")).name);
                pipe = string.Join("/", tags.Select(t => string.IsNullOrEmpty(t) ? "<none>" : t));
            }

            bool hdrpOk = pipe.Contains("HDRenderPipeline");
            Debug.Log($"[sc-census] {(hdrpOk ? "HDRP   " : "BUILTIN")} n={kv.Value,6} '{kv.Key}' pipelineTag={pipe} " +
                      $"largest={examples[kv.Key]}");
        }

        Debug.Log("[sc-census] DONE");
    }
}
