using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dump the saddle assembly hierarchy + local/world transforms, and the rider pelvis/hip
/// and shorts-mesh lowest point, so we can compute how the saddle should tuck under the rider.
/// Read-only.</summary>
public static class KuroSaddleInfo
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        var sb = new StringBuilder();
        sb.AppendLine($"[sinfo] player pos={player.transform.position:F3} fwd={player.transform.forward:F3} up={player.transform.up:F3}");
        foreach (var name in new[]{"SaddleRear","SaddleNose","SeatPost","SeatTube"})
        {
            Transform t = null;
            foreach (var x in player.GetComponentsInChildren<Transform>(true)) if (x.name==name){t=x;break;}
            if (t==null){ sb.AppendLine($"[sinfo] {name} NOT FOUND"); continue; }
            sb.AppendLine($"[sinfo] {name} parent='{t.parent.name}' worldPos={t.position:F3} localPos={t.localPosition:F3} localScale={t.localScale:F3}");
            var r = t.GetComponent<Renderer>();
            if (r) sb.AppendLine($"          bounds center={r.bounds.center:F3} size={r.bounds.size:F3} min={r.bounds.min:F3} max={r.bounds.max:F3}");
        }
        // rider pelvis/hips + shorts lowest point (Mesh_0 lowest vert on the back)
        foreach (var name in new[]{"Hips","Spine"})
        {
            foreach (var x in player.GetComponentsInChildren<Transform>(true)) if (x.name==name)
            { sb.AppendLine($"[sinfo] bone {name} world={x.position:F3}"); break; }
        }
        SkinnedMeshRenderer body=null;
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.sharedMesh&&s.sharedMesh.name=="Mesh_0"){body=s;break;}
        if (body) sb.AppendLine($"[sinfo] rider Mesh_0 bounds center={body.bounds.center:F3} size={body.bounds.size:F3} min={body.bounds.min:F3} max={body.bounds.max:F3}");
        Debug.Log(sb.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
