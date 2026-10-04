using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// QA diagnostic: dumps the shader/keyword/alpha state of a Minato-staged rider's body material
/// vs. the Player_Material_0 template, to find why the CelLit-converted NPC helmet shows real
/// background-through "tears" that never show on the raw-glTF legacy Coral diagnostic instance.
/// Read-only. Never saves.
/// </summary>
public static class FiMinatoMaterialDump
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        DumpTemplate("Assets/Kuro/Materials/Player_Material_0.mat");

        var traffic = FindIncludingInactive("Minato Traffic");
        if (traffic == null)
        {
            Debug.LogError("[matdump] 'Minato Traffic' root not found in scene.");
            MapleRideSceneBootstrap.DiscardChanges();
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        int dumped = 0;
        foreach (Transform rider in traffic.transform)
        {
            if (dumped >= 2) break;
            foreach (var r in rider.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name == "SmileDecal") continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (!m.name.Contains("_Body")) continue;
                    DumpMaterial($"RIDER[{rider.name}] {r.name}/{m.name}", m);
                }
            }
            dumped++;
        }

        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static GameObject FindIncludingInactive(string name)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name == name && t.gameObject.scene.IsValid())
                return t.gameObject;
        }
        return null;
    }

    static void DumpTemplate(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { Debug.LogError($"[matdump] template not found at {path}"); return; }
        DumpMaterial($"TEMPLATE {path}", m);
    }

    static void DumpMaterial(string label, Material m)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[matdump] === {label} ===");
        sb.AppendLine($"[matdump]   shader = {m.shader?.name}");
        sb.AppendLine($"[matdump]   keywords = {string.Join(", ", m.shaderKeywords)}");
        if (m.HasProperty("_Cull")) sb.AppendLine($"[matdump]   _Cull = {m.GetFloat("_Cull")}");
        if (m.HasProperty("_AlphaCutoffEnable")) sb.AppendLine($"[matdump]   _AlphaCutoffEnable = {m.GetFloat("_AlphaCutoffEnable")}");
        if (m.HasProperty("_AlphaCutoff")) sb.AppendLine($"[matdump]   _AlphaCutoff = {m.GetFloat("_AlphaCutoff")}");
        if (m.HasProperty("_Cutoff")) sb.AppendLine($"[matdump]   _Cutoff = {m.GetFloat("_Cutoff")}");
        if (m.HasProperty("_DoubleSidedEnable")) sb.AppendLine($"[matdump]   _DoubleSidedEnable = {m.GetFloat("_DoubleSidedEnable")}");
        if (m.HasProperty("_SurfaceType")) sb.AppendLine($"[matdump]   _SurfaceType = {m.GetFloat("_SurfaceType")}");
        if (m.HasProperty("_BUILTIN_AlphaClip")) sb.AppendLine($"[matdump]   _BUILTIN_AlphaClip = {m.GetFloat("_BUILTIN_AlphaClip")}");
        var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        sb.AppendLine($"[matdump]   _MainTex = {(tex != null ? tex.name : "null")}");
        Debug.Log(sb.ToString());
    }
}
