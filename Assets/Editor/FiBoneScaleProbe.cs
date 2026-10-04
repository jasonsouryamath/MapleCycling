using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dumps the posed head-chain bone transforms for the scene player: local + lossy scale,
/// to detect non-uniform scale / shear that would distort even rigidly-skinned head verts.</summary>
public static class FiBoneScaleProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[bone] player missing"); return; }
        var smr = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var sb = new StringBuilder();
        string[] want = { "Hips", "Spine", "Spine01", "Spine02", "neck", "Head", "headfront", "head_end", "LeftShoulder", "LeftArm" };
        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            foreach (var w in want)
                if (b.name == w)
                {
                    var ls = b.localScale; var gs = b.lossyScale; var lr = b.localEulerAngles;
                    sb.AppendLine($"[bone] {b.name,-13} localScale=({ls.x:0.000},{ls.y:0.000},{ls.z:0.000}) lossyScale=({gs.x:0.000},{gs.y:0.000},{gs.z:0.000}) localEuler=({lr.x:0.0},{lr.y:0.0},{lr.z:0.0})");
                }
        }
        // Player root + rig root scale
        sb.AppendLine($"[bone] playerRoot lossyScale={player.transform.lossyScale}");
        Debug.Log(sb.ToString());
        MapleRideSceneBootstrap.DiscardChanges();
    }
}
