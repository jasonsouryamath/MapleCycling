using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// THROWAWAY QA verification for the "half 3D, half not" chrome NPC fix (QA finding #2):
/// dumps every Minato rider's body/decal shader (confirming none remain on the raw
/// glTF-pbrMetallicRoughness shader) and captures a zoomed face render on four sampled liveries
/// so the fix can be confirmed by LOOKING, not just by the shader-name check.
/// </summary>
public static class QaMinatoNpcShaderDump
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string GltfShader = "Shader Graphs/glTF-pbrMetallicRoughness";

    public static void Run() => RunFor("Minato Rider ", "qa_minato", new[] { "Minori", "Yutaka", "Sena", "Kaoru" });

    [MenuItem("MapleRide/QA/Dump Shiosai NPC Shaders + Faces")]
    public static void RunShiosai() => RunFor("Shiosai Rider ", "qa_shiosai", null);

    [MenuItem("MapleRide/QA/Dump Sakura NPC Shaders + Faces")]
    public static void RunSakura() => RunFor("Sakura NPC ", "qa_sakura", null);

    static void RunFor(string namePrefix, string tag, string[] sampledOverride)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var riders = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                            .Where(t => t.name.StartsWith(namePrefix))
                            .OrderBy(t => t.name).ToArray();

        Debug.Log($"[{tag}-npcmat] {riders.Length} riders found for prefix '{namePrefix}'.");

        var log = new StringBuilder();
        log.AppendLine($"=== {namePrefix.Trim()} NPC BODY/DECAL SHADER DUMP (QA #2 verification) ===");
        int gltfCount = 0, matCount = 0;

        foreach (var rider in riders)
        {
            var bike = FindChild(rider, "Bike");
            foreach (var r in rider.GetComponentsInChildren<Renderer>(true))
            {
                if (bike != null && r.transform.IsChildOf(bike)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    matCount++;
                    if (m == null) { log.AppendLine($"{rider.name} | {r.name} | <NULL MATERIAL>"); continue; }
                    bool isGltf = m.shader != null && m.shader.name == GltfShader;
                    if (isGltf) gltfCount++;
                    string mf = m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor").ToString("F2")
                              : (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("F2") : "-");
                    string spec = m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength").ToString("F3") : "-";
                    log.AppendLine($"{rider.name} | {r.name,-12} | mat={m.name,-40} | shader={(m.shader == null ? "NULL" : m.shader.name),-38} " +
                                   $"metallicFactor={mf} specStrength={spec}{(isGltf ? "  *** STILL RAW glTF CHROME ***" : "")}");
                }
            }
        }
        log.AppendLine($"=== SUMMARY: {matCount} material slots checked across {riders.Length} riders, " +
                       $"{gltfCount} still on '{GltfShader}' ===");
        Debug.Log(log.ToString());
        Directory.CreateDirectory("reference/good_graphics");
        File.WriteAllText($"reference/good_graphics/{tag}_npc_shader_dump.txt", log.ToString());

        // Visual proof: zoom on a couple of sampled liveries' faces.
        var sampled = sampledOverride ?? riders.Take(2).Select(r => r.name.Substring(namePrefix.Length)).ToArray();
        foreach (var name in sampled)
        {
            var rider = riders.FirstOrDefault(r => r.name.EndsWith(" " + name));
            if (rider == null) { Debug.LogWarning($"[{tag}-npcmat] rider '{name}' not found."); continue; }
            CaptureFace(rider, $"{tag}_{name}", riders);
        }


        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(gltfCount == 0 ? 0 : 1);
    }

    static void CaptureFace(Transform rider, string name, Transform[] allRiders)
    {
        // Wake this rider's own inactive ancestors (mirrors NpcPortraitBake.BakeSingle) so the
        // head camera has something to photograph, and record them to restore afterward.
        var woken = new List<GameObject>();
        for (var t = rider; t != null; t = t.parent)
            if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); woken.Add(t.gameObject); }

        var head = FindChild(rider, "Head");
        if (head == null) { Debug.LogWarning($"[qa-minato-npcmat] {name}: no 'Head' bone - skipped."); return; }

        var smile = FindChild(rider, "SmileDecal");
        Renderer smileR = smile != null ? smile.GetComponent<Renderer>() : null;
        bool prevSmile = smileR != null && smileR.enabled;
        if (smileR != null) smileR.enabled = true;

        // ISOLATE: every ambient traffic pool parks its whole pool at the same local origin
        // until the runtime director places them, so a naive shot here photographs an
        // overlapping pile of thirty riders (see NpcPortraitBake's own note on this). Hide every
        // other rider's renderers for the shot, then restore them.
        var hidden = new List<Renderer>();
        foreach (var other in allRiders)
        {
            if (other == null || other == rider) continue;
            foreach (var r in other.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
        }

        float k = Mathf.Max(0.01f, head.lossyScale.y);
        Vector3 fwd = rider.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 face = head.position + Vector3.up * (0.15f * k) + fwd * (0.015f * k);

        var camGo = new GameObject("~QaFaceCam");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = face + fwd * (0.62f * k);
        camGo.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        cam.orthographic = true;
        cam.orthographicSize = 0.17f * k;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = (0.62f + 0.34f) * k; // just past the back of the head - isolates it
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.075f, 0.106f, 0.129f, 1f);
        cam.allowHDR = true;

        var keyGo = new GameObject("~QaFaceKey");
        keyGo.transform.SetParent(camGo.transform, false);
        keyGo.transform.rotation = Quaternion.LookRotation(
            Quaternion.AngleAxis(-18f, Vector3.up) * (-fwd) + Vector3.down * 0.35f);
        var key = keyGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1f, 0.9f, 0.8f, 1f);
        key.intensity = 3.0f;

        const int Size = 512;
        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes($"reference/good_graphics/qa_minato_npc_face_{name}.png", tex.EncodeToPNG());
        Debug.Log($"[qa-minato-npcmat] captured face render for {name}");

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(tex);

        foreach (var r in hidden) if (r != null) r.enabled = true;
        if (smileR != null) smileR.enabled = prevSmile;
        for (int i = woken.Count - 1; i >= 0; i--)
            if (woken[i] != null) woken[i].SetActive(false);
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var found = FindChild(c, name);
            if (found != null) return found;
        }
        return null;
    }
}
