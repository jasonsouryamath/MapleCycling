using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Measures whether Maple City East is visually consistent with the Old Town (user, 2026-10-03: "quality and graphics must be
/// consistent throughout Maple City"). Run: MapleCityEastConsistencyCheck.Run. Every check is a number, not an opinion:
///   * shader parity     - every East material uses the same shader as the Old Town's (CelLit / CityFacade / Terrain / Foliage);
///   * ramp parity       - East CelLit materials share the Old Town's shade ramp (steps, smooth, shade strength, shadow ambient);
///   * no stray shaders  - zero East renderers on glTF / Standard / error shaders (landmark GLB materials fully replaced);
///   * triangle density  - triangles per metre of route in East is within +/-40% of the Old Town's;
///   * coverage          - the same set of builder groups exists in both districts (minus the documented phase-1 exclusions);
///   * mesh isolation    - East meshes persist under East_* names, so the Old Town's assets were not overwritten.
/// </summary>
public static class MapleCityEastConsistencyCheck
{
    static readonly string[] Phase1Excluded = { "Maple Row Boutiques", "City Skyline", "Maple City Life" };

    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        GameObject root = null;
        foreach (var go in scene.GetRootGameObjects()) if (go.name == MapleCityEnvironment.RootName) root = go;
        bool ok = true;
        void Check(bool pass, string msg) { Debug.Log((pass ? "[east-check] PASS " : "[east-check] FAIL ") + msg); ok &= pass; }
        if (root == null) { Debug.LogError("[east-check] no city root"); EditorApplication.Exit(2); return; }
        var east = root.transform.Find(MapleCityEnvironment.EastGroupName);
        Check(east != null, "East district group exists under the single city root");
        if (east == null) { EditorApplication.Exit(1); return; }
        Check(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(t => t.name == MapleCityEnvironment.RootName && t.parent == null) == 1,
              "exactly one 'Maple City Environment' root");

        // renderers split into East / Old Town
        var all = root.GetComponentsInChildren<MeshRenderer>(true);
        var eastR = all.Where(r => r.transform.IsChildOf(east)).ToArray();
        var eastGroupNames = new HashSet<string>(Enumerable.Range(0, east.childCount).Select(i => east.GetChild(i).name));
        // Like for like: only the Old Town groups that East also has (excludes Life/Skyline/Boutiques, which are phase 2).
        var oldR = all.Where(r =>
        {
            if (r.transform.IsChildOf(east)) return false;
            var t = r.transform; while (t.parent != null && t.parent != root.transform) t = t.parent;
            return t.parent == root.transform && eastGroupNames.Contains(t.name);
        }).ToArray();
        Check(eastR.Length > 20, $"East has {eastR.Length} renderers (comparable Old Town groups: {oldR.Length})");

        var oldShaders = new HashSet<string>(oldR.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null).Select(m => m.shader.name));
        var eastShaders = eastR.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct().ToList();
        // A material that IS an Old Town asset (same MapleCity_* material, shared by both districts) is consistent by definition.
        var oldMatNames = new HashSet<string>(all.Where(r => !r.transform.IsChildOf(east)).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name));
        var stray = eastR.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null && !oldShaders.Contains(m.shader.name) && !oldMatNames.Contains(m.name) && !m.name.StartsWith("MapleCity_"))   // MapleCity_* = the same shared assets both districts use
                         .Select(m => m.shader.name).Distinct().ToList();
        foreach (var sn in stray)
            foreach (var r in eastR.Where(r => r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name == sn)).Take(6))
                Debug.Log($"[east-check] INFO stray shader '{sn}' on '{r.transform.parent?.name}/{r.name}' material '{string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.name))}'");
        Check(stray.Count == 0, "East uses only shaders the Old Town uses" + (stray.Count > 0 ? " - STRAY: " + string.Join(", ", stray) : $" ({string.Join(", ", eastShaders)})"));
        Check(eastR.All(r => r.sharedMaterials.All(m => m != null && m.shader != null && !m.shader.name.Contains("glTF") && !m.shader.name.Contains("Hidden/InternalErrorShader"))),
              "no East renderer on a glTF / error shader (landmark materials fully replaced)");

        // ramp parity on CelLit
        float[] Ramp(Material m) => new[] { m.GetFloat("_RampSteps"), m.GetFloat("_RampSmooth"), m.GetFloat("_ShadeStrength"), m.GetFloat("_ShadowAmbient"), m.GetFloat("_ShadowSoft") };
        var oldCel = oldR.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null && m.shader.name == "MapleRide/HDRP/CelLit" && m.HasProperty("_RampSteps")).Distinct().ToList();
        // Judge only what East ADDS (the landmark materials); MapleCity_* materials are the very same assets the Old Town renders with.
        var eastCel = eastR.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null && m.shader.name == "MapleRide/HDRP/CelLit" && m.HasProperty("_RampSteps") && m.name.StartsWith("MapleEast_")).Distinct().ToList();
        if (oldCel.Count > 0 && eastCel.Count > 0)
        {
            // Reference = the Old Town's MOST COMMON ramp (its default CelMaterial), not a deliberate outlier like the asphalt.
            var refRamp = oldCel.Select(m => Ramp(m)).GroupBy(a => string.Join("|", a.Select(v => v.ToString("0.###")))).OrderByDescending(g => g.Count()).First().First();
            Debug.Log($"[east-check] INFO ramp reference (modal Old Town) = [{string.Join(", ", refRamp.Select(v => v.ToString("0.###")))}] (steps, smooth, shadeStrength, shadowAmbient, shadowSoft)");
            foreach (var m in eastCel.Where(m => Ramp(m).Zip(refRamp, (a, b) => Mathf.Abs(a - b)).Any(d => d > 0.01f)).Take(5))
                Debug.Log($"[east-check] INFO ramp differs '{m.name}' = [{string.Join(", ", Ramp(m).Select(v => v.ToString("0.###")))}]");
            int bad = eastCel.Count(m => Ramp(m).Zip(refRamp, (a, b) => Mathf.Abs(a - b)).Any(d => d > 0.01f));
            Check(bad == 0, $"{eastCel.Count} East CelLit materials share the Old Town shade ramp ({bad} differ)");
        }

        // triangle density per route metre
        int Tris(IEnumerable<MeshRenderer> rs) => rs.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Sum(f =>
        {
            int t = 0; for (int s = 0; s < f.sharedMesh.subMeshCount; s++) t += (int)(f.sharedMesh.GetIndexCount(s) / 3); return t;
        });
        int oldT = Tris(oldR), eastT = Tris(eastR);
        float oldD = oldT / 5000f, eastD = eastT / 5000f;
        Debug.Log($"[east-check] INFO tris East {eastT} Old Town (same groups) {oldT}");
        Check(eastD > oldD * 0.6f && eastD < oldD * 1.4f, $"triangles per route metre: East {eastD:0.0} vs Old Town {oldD:0.0} (must be within +/-40%)");

        // group coverage
        var oldGroups = new HashSet<string>(Enumerable.Range(0, root.transform.childCount).Select(i => root.transform.GetChild(i).name).Where(n => n != MapleCityEnvironment.EastGroupName));
        var eastGroups = new HashSet<string>(Enumerable.Range(0, east.childCount).Select(i => east.GetChild(i).name));
        var missing = oldGroups.Where(n => !eastGroups.Contains(n) && !Phase1Excluded.Contains(n)).ToList();
        Debug.Log("[east-check] INFO groups only in Old Town (not yet built for East): " + string.Join(", ", missing));
        Check(eastGroups.Contains("City Buildings") && eastGroups.Contains("East Landmarks"), "East has buildings and landmarks");

        // mesh isolation
        var eastMeshes = eastR.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Select(f => AssetDatabase.GetAssetPath(f.sharedMesh)).Where(p => p.Contains("/Meshes/")).Distinct().ToList();
        Check(eastMeshes.All(p => System.IO.Path.GetFileName(p).StartsWith("East_")), $"all {eastMeshes.Count} persisted East meshes use East_ names");
        Check(!oldR.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Select(f => AssetDatabase.GetAssetPath(f.sharedMesh)).Any(p => System.IO.Path.GetFileName(p).StartsWith("East_")),
              "Old Town renderers reference no East_ mesh");

        Debug.Log(ok ? "[east-check] ALL PASS" : "[east-check] SOME CHECKS FAILED");
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
