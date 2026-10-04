using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only probe: after seating, dump every bone of the player rig with its offset along the
/// player's BACKWARD (chase-cam) axis relative to the chest, plus lateral/vertical, to find any
/// hand/forearm/finger bone that pokes out the BACK of the torso (the reported "black box" +
/// "white glove" artifacts on the jersey back). Never saves the scene.
/// </summary>
public static class KuroSeatedBoneProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[boneprobe] player not found."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }

        // chest reference = Spine02 (fallback Spine01/Spine/Hips)
        Transform chest = null, hips = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Spine02" && chest == null) chest = t;
            if (t.name == "Hips") hips = t;
        }
        if (chest == null) chest = hips;
        Vector3 fwd = player.transform.forward;   // player faces +fwd; chase cam is at -fwd (behind)
        Vector3 right = player.transform.right;
        Vector3 up = player.transform.up;
        Vector3 cref = chest.position;

        var sb = new StringBuilder();
        sb.AppendLine($"[boneprobe] player fwd={fwd:F2} chestRef(Spine02)={cref:F3}");
        sb.AppendLine("[boneprobe] name | backOffset(+=behind spine, chase side) | lateral(+=right) | vert(+=up)");
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            bool arm = n.Contains("Hand") || n.Contains("ForeArm") || n.Contains("LowerArm") ||
                       n.Contains("Finger") || n.Contains("Thumb") || n.Contains("Index") ||
                       n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky") ||
                       n.Contains("Wrist") || n.Contains("Arm") || n.Contains("Elbow");
            if (!arm) continue;
            Vector3 d = t.position - cref;
            float back = -Vector3.Dot(d, fwd);   // positive => behind the chest (toward chase cam)
            float lat = Vector3.Dot(d, right);
            float ver = Vector3.Dot(d, up);
            string flag = back > 0.02f ? "  <== BEHIND CHEST (pokes back)" : "";
            sb.AppendLine($"  {n,-22} back={back,7:F3} lat={lat,7:F3} vert={ver,7:F3}{flag}");
        }
        Debug.Log(sb.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
