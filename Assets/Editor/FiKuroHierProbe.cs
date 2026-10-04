using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;

/// FIX-IMPLEMENTER probe: dumps the full player subtree of SakuraPass with component flags, and
/// whether each renderer node descends from the KuroBikeRig transform.
public static class FiKuroHierProbe
{
    public static void Run()
    {
        const string scene = "Assets/Scenes/SakuraPass.unity";
        if (EditorSceneManager.GetActiveScene().path != scene)
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = Object.FindObjectsByType<KuroBikeRig>(FindObjectsSortMode.None).FirstOrDefault();
        Debug.Log($"[hier] player='{(player ? player.name : "null")}' rig on='{(rig ? rig.gameObject.name : "null")}' " +
                  $"sameObject={(player && rig && player == rig.gameObject)}");

        if (player == null) return;
        var sb = new StringBuilder();
        Dump(player.transform, 0, sb, rig != null ? rig.transform : null);
        Debug.Log("[hier]\n" + sb);
    }

    static void Dump(Transform t, int depth, StringBuilder sb, Transform rigRoot)
    {
        string flags = "";
        if (t.GetComponent<SkinnedMeshRenderer>()) flags += " SMR";
        if (t.GetComponent<MeshRenderer>()) flags += " MR";
        if (t.GetComponent<KuroBikeRig>()) flags += " RIG";
        bool underRig = rigRoot != null && t.IsChildOf(rigRoot);
        sb.Append(new string(' ', depth * 2)).Append(t.name).Append(flags)
          .Append(underRig ? " [underRig]" : "").Append('\n');
        // Only descend a bit to keep it readable; skip deep bone chains
        if (depth < 3 || t.GetComponent<SkinnedMeshRenderer>() || t.GetComponent<MeshRenderer>())
            foreach (Transform c in t) Dump(c, depth + 1, sb, rigRoot);
    }
}
