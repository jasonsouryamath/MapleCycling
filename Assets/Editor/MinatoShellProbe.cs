using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Diagnostic: road vs ground vs authored mountain shell around 13.2-13.9 km.</summary>
public static class MinatoShellProbe
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        Transform shell = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.StartsWith("Minato_Mountain_HeroValley")) { shell = t; break; }
        var cols = new System.Collections.Generic.List<MeshCollider>();
        if (shell != null)
            foreach (var mf in shell.GetComponentsInChildren<MeshFilter>(true))
            { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; cols.Add(mc); }
        Physics.SyncTransforms();
        Debug.Log($"[shell-probe] shell meshes={cols.Count}");
        for (float d = 13400f; d <= 19000f; d += 400f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            string line = $"[shell-probe] d={d:0} roadY={p.y:0.0}";
            foreach (float lat in new[] { -25f, 25f })
            {
                var q = p + side * lat;
                float ground = float.NaN, shellY = float.NaN; string gname = "";
                foreach (var h in Physics.RaycastAll(new Vector3(q.x, 3000f, q.z), Vector3.down, 6000f))
                {
                    bool isShell = h.collider is MeshCollider mc && cols.Contains(mc);
                    if (isShell) { if (float.IsNaN(shellY) || h.point.y > shellY) shellY = h.point.y; }
                    else if (float.IsNaN(ground) || h.point.y > ground) { ground = h.point.y; gname = h.collider.name; }
                }
                line += $" | lat{lat:+0;-0}: ground={ground:0.0}({gname}) shell={shellY:0.0}";
            }
            Debug.Log(line);
        }
    }
}
