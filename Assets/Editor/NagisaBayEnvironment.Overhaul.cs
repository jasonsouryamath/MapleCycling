// Nagisa Bay overhaul - NB0 stage hook (COORDINATION.md, "PRIORITY 1: Nagisa Bay overhaul").
//
// Each NB package adds its OWN new partial file, Assets/Editor/NagisaBayEnvironment.<Pkg>.cs, with
//
//     [NagisaStage(30, "Highway")]
//     private static void BuildHighwayStage(Transform group) { ... }
//
// ApplyOverhaul() runs every tagged stage in Order (or only those named in the env var
// MR_NB_STAGES, e.g. "Highway,Surf"). Each stage gets a fresh child "NB <Name>" under
// "Nagisa Bay Environment/NB Overhaul". The group is destroyed and rebuilt by exact name, so re-runs
// converge and one package never touches another's objects. The partial class gives stages the
// shared helpers (_route, _ground, CanPlace, Finish, AddMesh, Cel, LoadOrCreate, Tex, ...).
//
// Run order until the B5/NB2 owner adds RunOverhaulStages(root, null) at the end of Apply():
//     NagisaBayEnvironment.Apply  then  NagisaBayEnvironment.ApplyOverhaul
// Apply rebuilds its root from scratch, so it wipes "NB Overhaul" until that one-liner lands.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[AttributeUsage(AttributeTargets.Method)]
public sealed class NagisaStageAttribute : Attribute
{
    public readonly int Order;
    public readonly string Name;
    public NagisaStageAttribute(int order, string name) { Order = order; Name = name; }
}

public static partial class NagisaBayEnvironment
{
    public const string OverhaulGroupName = "NB Overhaul";

    [MenuItem("MapleRide/Environment/Nagisa Bay Overhaul Stages")]
    public static void ApplyOverhaul()
    {
        bool headless = Application.isBatchMode;
        if (headless && EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var roots = FindRootsByExactName(RootName);
        if (roots.Count == 0)
        {
            Debug.LogError($"[nagisa-nb] no '{RootName}' root in the scene: run NagisaBayEnvironment.Apply first.");
            return;
        }

        if (_route == null || _ground == null)
        {
            MaterialCache.Clear();
            _meshNames.Clear();
            var route = NagisaRoute.Load();
            var ground = NagisaGround.Load();
            HealGround(ground, route);
            BenchUnderRoad(ground, route);
            _route = route; _ground = ground;
        }

        RunOverhaulStages(roots[0].transform, Environment.GetEnvironmentVariable("MR_NB_STAGES"));

        AssetDatabase.SaveAssets();
        if (headless)
        {
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[nagisa-nb] saved '{active.path}'.");
        }
    }

    public static void RunOverhaulStages(Transform root, string only)
    {
        HashSet<string> want = null;
        if (!string.IsNullOrWhiteSpace(only))
            want = new HashSet<string>(only.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
                                       StringComparer.OrdinalIgnoreCase);

        Transform group = null;
        for (int i = 0; i < root.childCount; i++)
            if (root.GetChild(i).name == OverhaulGroupName) { group = root.GetChild(i); break; }
        if (group == null)
        {
            group = new GameObject(OverhaulGroupName).transform;
            group.SetParent(root, false);
        }

        var stages = typeof(NagisaBayEnvironment)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => new { m, a = m.GetCustomAttribute<NagisaStageAttribute>() })
            .Where(x => x.a != null)
            .OrderBy(x => x.a.Order).ThenBy(x => x.a.Name, StringComparer.Ordinal)
            .ToList();

        int ran = 0;
        foreach (var s in stages)
        {
            var ps = s.m.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(Transform))
            {
                Debug.LogError($"[nagisa-nb] stage '{s.a.Name}' ({s.m.Name}) must be static void X(Transform group).");
                continue;
            }
            if (want != null && !want.Contains(s.a.Name)) continue;

            string gname = "NB " + s.a.Name;
            for (int i = group.childCount - 1; i >= 0; i--)
                if (group.GetChild(i).name == gname) UnityEngine.Object.DestroyImmediate(group.GetChild(i).gameObject);
            var sg = new GameObject(gname).transform;
            sg.SetParent(group, false);

            try { s.m.Invoke(null, new object[] { sg }); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); continue; }

            long tris = 0;
            var filters = sg.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in filters)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) tris += mesh.GetIndexCount(sub) / 3;
            }
            Debug.Log($"[nagisa-nb] stage {s.a.Order} '{s.a.Name}': {filters.Length} meshes, {tris:N0} tris.");
            ran++;
        }
        Debug.Log($"[nagisa-nb] ran {ran}/{stages.Count} overhaul stages" + (want != null ? $" (MR_NB_STAGES={only})." : "."));
    }
}
