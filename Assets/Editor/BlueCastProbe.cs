using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only diagnostic for the reported blue/cyan overexposure on the player and NPC riders.
/// Dumps every relevant HDRP/glTF property + keyword on the shared bike materials and the
/// player's body material so the true cause (bad metallic/roughness, missing HD keyword,
/// wrong surface type, stray emission) can be identified before touching anything.
/// </summary>
public static class BlueCastProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    static readonly string[] FloatProps =
    {
        "metallicFactor", "roughnessFactor", "_Metallic", "_Smoothness", "_Glossiness",
        "_SurfaceType", "_BlendMode", "_AlphaCutoffEnable", "_ReceivesSSR", "_EnableSpecularOcclusion",
    };
    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color", "emissiveFactor", "_EmissionColor" };
    static readonly string[] TexProps =
    {
        "baseColorTexture", "_BaseColorMap", "_MainTex",
        "normalTexture", "_NormalMap", "_BumpMap",
        "metallicRoughnessTexture", "_MaskMap", "_MetallicGlossMap",
        "occlusionTexture", "_OcclusionMap",
    };

    [MenuItem("MapleRide/Diagnostics/Blue Cast Probe")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        var coral = GameObject.Find("Sakura NPC Coral");
        Dump("PLAYER", player);
        Dump("CORAL ", coral);

        MapleRideSceneBootstrap.DiscardChanges();
    }

    static void Dump(string label, GameObject root)
    {
        if (root == null) { Debug.Log($"[bluecast] {label}: not found"); return; }

        var seen = new System.Collections.Generic.HashSet<Material>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m)) continue;
                DumpMat(label, r.name, m);
            }
        }
    }

    static void DumpMat(string label, string rendererName, Material m)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[bluecast] {label} '{rendererName}' mat='{m.name}' shader='{m.shader.name}' ");
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) sb.Append($"{p}={m.GetColor(p):F3} ");
        foreach (var p in FloatProps)
            if (m.HasProperty(p)) sb.Append($"{p}={m.GetFloat(p):F3} ");
        foreach (var p in TexProps)
            if (m.HasProperty(p))
            {
                var t = m.GetTexture(p);
                sb.Append($"{p}={(t == null ? "NULL" : t.name)} ");
            }
        sb.Append($"keywords=[{string.Join(",", m.shaderKeywords)}] ");
        sb.Append($"renderQueue={m.renderQueue} ");
        Debug.Log(sb.ToString());
    }

    /// <summary>Close, unobstructed portrait shots of player + Coral under the same lighting.</summary>
    [MenuItem("MapleRide/Diagnostics/Blue Cast Closeups")]
    public static void CaptureCloseups()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        var coral = GameObject.Find("Sakura NPC Coral");
        if (player == null || coral == null) { Debug.LogError("[bluecast] player or coral missing"); return; }

        string dir = MapleRidePaths.RenderDir("bluecast");
        Directory.CreateDirectory(dir);

        Vector3 basePos = player.transform.position;
        player.transform.position = basePos;
        player.transform.rotation = Quaternion.identity;
        coral.transform.position = basePos + new Vector3(2.4f, 0f, 0f);
        coral.transform.rotation = Quaternion.identity;

        ShotAt(dir, "player_portrait", basePos + new Vector3(0f, 1.15f, 1.6f), basePos + new Vector3(0f, 1.05f, 0f));
        ShotAt(dir, "coral_portrait", coral.transform.position + new Vector3(0f, 1.15f, 1.6f),
               coral.transform.position + new Vector3(0f, 1.05f, 0f));
        ShotAt(dir, "both_wide", basePos + new Vector3(1.2f, 1.3f, 4.2f), basePos + new Vector3(1.2f, 0.9f, 0f));

        MapleRideSceneBootstrap.DiscardChanges();
    }

    static void ShotAt(string dir, string name, Vector3 pos, Vector3 look)
    {
        var go = new GameObject("~BlueCastCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 35f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 3000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        go.AddComponent<SakuraPostFX>();

        const int w = 1200, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());

        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log("[bluecast] wrote " + name + ".png");
    }
}
