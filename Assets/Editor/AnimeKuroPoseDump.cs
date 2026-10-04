using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot diagnostic: dump the SOLVED local rotations of the anime Kuro rider's bones in
/// the staged ShiosaiCoast ride pose.
///
/// WHY: the "pale shroud" over Kuro's shoulder/flank only appears in the GAME pose. A
/// Blender reproduction that rotated the upper arms forward 70 deg deformed perfectly
/// cleanly, which proves the arm reach is NOT the cause -- so the real pose (spine bend,
/// shoulder roll, neck/head counter-rotation, and whatever the KuroBikeRig arm solve does)
/// has to be measured rather than guessed. This prints it in a form the Blender pose
/// harness (mrb3_pose_test.py) can replay, giving a ~1 min iteration loop instead of a
/// ~10 min Unity reimport+stage+capture cycle.
///
/// Read-only: it opens the staged scene, reads transforms and logs. It changes nothing.
/// </summary>
public static class AnimeKuroPoseDump
{
    private static readonly string[] Bones =
    {
        "Hips", "Spine02", "Spine01", "Spine", "neck", "Head",
        "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
        "RightShoulder", "RightArm", "RightForeArm", "RightHand"
    };

    public static void Run()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            ShiosaiCoastEnvironment.ScenePath);
        Debug.Log($"[posedump] opened {scene.name}");

        GameObject poc = null;
        // FindObjectsByType<GameObject> skips INACTIVE objects, and the POC rider is parented
        // under the coast root, which region visibility deactivates when the scene opens on a
        // different region - that is why the first run reported "rider not staged" even though
        // Stage() had just succeeded. Include inactive.
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None))
        {
            if (t.name == "Shiosai POC Realistic Rider") { poc = t.gameObject; break; }
        }
        if (poc == null) { Debug.Log("[posedump] ERROR rider not staged"); return; }

        var sb = new StringBuilder();
        foreach (var bn in Bones)
        {
            var t = Find(poc.transform, bn);
            if (t == null) { sb.AppendLine($"[posedump] MISSING {bn}"); continue; }
            var e = t.localRotation.eulerAngles;
            sb.AppendLine($"[posedump] {bn} localEuler=({Norm(e.x):F2},{Norm(e.y):F2},{Norm(e.z):F2}) " +
                          $"localPos=({t.localPosition.x:F4},{t.localPosition.y:F4},{t.localPosition.z:F4})");
        }
        Debug.Log(sb.ToString());
    }

    private static float Norm(float a) => a > 180f ? a - 360f : a;

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = Find(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
