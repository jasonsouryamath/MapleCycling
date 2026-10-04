using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batchmode validation for the rigged Coral NPC. Confirms the GLB imports with a live
/// SkinnedMeshRenderer, a full bone array and sane bounds.
/// </summary>
public static class CoralRigValidate
{
    const string Asset = "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb";
    static readonly string[] Compare =
    {
        "Assets/Kuro/NPC/KuroNPC_Aoi.glb",
        "Assets/Kuro/NPC/KuroNPC_Mika.glb",
    };

    public static void Validate()
    {
        AssetDatabase.Refresh();
        var sb = new StringBuilder();
        bool ok = Report(Asset, sb, true);
        foreach (var p in Compare) Report(p, sb, false);
        Debug.Log("CORALVALIDATE\n" + sb);
        Debug.Log(ok ? "CORALVALIDATE RESULT: PASS" : "CORALVALIDATE RESULT: FAIL");
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Report(string path, StringBuilder sb, bool strict)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null)
        {
            sb.AppendLine("MISSING " + path);
            return false;
        }

        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        sb.AppendLine("ASSET " + path);
        sb.AppendLine("  skinnedMeshRenderers=" + smrs.Length);
        bool ok = smrs.Length > 0;
        foreach (var s in smrs)
        {
            var m = s.sharedMesh;
            int bones = s.bones?.Length ?? 0;
            int nullBones = s.bones?.Count(b => b == null) ?? 0;
            sb.AppendFormat(
                "  mesh='{0}' verts={1} bones={2} nullBones={3} bindposes={4} root='{5}' bounds={6}\n",
                m != null ? m.name : "<null>",
                m != null ? m.vertexCount : 0,
                bones, nullBones,
                m != null ? m.bindposes.Length : 0,
                s.rootBone != null ? s.rootBone.name : "<null>",
                s.bounds.size);

            if (m == null || bones == 0 || nullBones > 0) ok = false;
            if (m != null && m.bindposes.Length != bones) ok = false;
            if (strict)
            {
                float h = s.bounds.size.y;
                if (h < 0.5f || h > 3.0f)
                {
                    sb.AppendLine("  FAIL: height " + h + " is not humanoid");
                    ok = false;
                }
                if (m != null && m.boneWeights.Length != m.vertexCount)
                {
                    sb.AppendLine("  FAIL: boneWeights " + m.boneWeights.Length +
                                  " != verts " + m.vertexCount);
                    ok = false;
                }
                if (m != null)
                {
                    var used = m.boneWeights
                        .Select(w => w.weight0 >= w.weight1 ? w.boneIndex0 : w.boneIndex1)
                        .Distinct().Count();
                    sb.AppendLine("  distinct dominant bones = " + used);
                    if (used < 15)
                    {
                        sb.AppendLine("  FAIL: weights collapsed onto too few bones");
                        ok = false;
                    }
                }
            }
        }
        return ok;
    }
}
