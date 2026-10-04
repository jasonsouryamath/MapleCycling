using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Repairs NaN / zero normals and tangents in the Nagisa Bay mesh assets (2026-10-02, Claude).
///
/// Symptom it fixes: whole objects (the Japanese black pines on the gateway dunes) render exactly
/// (0,0,0) in play. Their meshes are built in code with spheres whose poles collapse to one UV, so
/// RecalculateTangents yields NaN tangents; the CelLit shader feeds tangentWS straight into the
/// normal-map basis whenever _NormalStrength &gt; 0, and a NaN pixel is clamped to black by HDRP.
/// Streamed cells reference these mesh assets by GUID, so fixing the asset fixes every cell.
/// </summary>
public static class NagisaMeshRepair
{
    private const string Folder = "Assets/Environment/NagisaBay/Meshes";

    public static void Run()
    {
        int scanned = 0, fixedMeshes = 0, badNormals = 0, badTangents = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null || !mesh.isReadable) continue;
            scanned++;
            int n = Repair(mesh, out int nb, out int tb);
            if (n == 0) continue;
            badNormals += nb; badTangents += tb; fixedMeshes++;
            EditorUtility.SetDirty(mesh);
            Debug.Log($"[nagisa-mesh-repair] {mesh.name}: fixed {nb} normals, {tb} tangents ({path})");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[nagisa-mesh-repair] scanned {scanned} meshes; repaired {fixedMeshes} ({badNormals} normals, {badTangents} tangents).");
    }

    private static bool Bad(Vector3 v) =>
        float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
        float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z) || v.sqrMagnitude < 1e-8f;

    private static int Repair(Mesh mesh, out int nb, out int tb)
    {
        nb = tb = 0;
        int count = mesh.vertexCount;
        var normals = new List<Vector3>(); mesh.GetNormals(normals);
        var tangents = new List<Vector4>(); mesh.GetTangents(tangents);
        bool hasN = normals.Count == count, hasT = tangents.Count == count;
        if (hasN)
        {
            bool any = false;
            for (int i = 0; i < count; i++) if (Bad(normals[i])) { any = true; break; }
            if (any)
            {
                mesh.RecalculateNormals(); mesh.GetNormals(normals);
                for (int i = 0; i < count; i++)
                    if (Bad(normals[i])) { normals[i] = Vector3.up; nb++; }
                mesh.SetNormals(normals);
            }
        }
        if (hasT && hasN)
        {
            bool dirty = false;
            for (int i = 0; i < count; i++)
            {
                var t = tangents[i];
                bool bad = float.IsNaN(t.x) || float.IsNaN(t.y) || float.IsNaN(t.z) || float.IsNaN(t.w) ||
                           float.IsInfinity(t.x) || float.IsInfinity(t.y) || float.IsInfinity(t.z) ||
                           new Vector3(t.x, t.y, t.z).sqrMagnitude < 1e-8f;
                if (!bad) continue;
                // Any tangent perpendicular to the normal is valid when the UV basis is degenerate.
                var n = normals[i];
                var axis = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up;
                var tan = Vector3.Cross(axis, n).normalized;
                tangents[i] = new Vector4(tan.x, tan.y, tan.z, 1f);
                tb++; dirty = true;
            }
            if (dirty) mesh.SetTangents(tangents);
        }
        return nb + tb;
    }
}
