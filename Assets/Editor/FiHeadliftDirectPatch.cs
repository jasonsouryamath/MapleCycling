using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Minimal, surgical fix for the "bobblehead" defect: directly corrects the stale
/// <see cref="CoralBikeRig.poseExtraHeadLiftDegrees"/> field (-18.5 -> -12, the value
/// NpcCanonicalConformance.Configure already writes for every NEWLY staged rider) on every
/// EXISTING rig instance still carrying the old value, without pruning, reimporting, or
/// recreating anything - the smallest possible change that fixes the reported symptom, in one
/// fast pass instead of four full region restages.
///
/// Run headless:
///   Unity.exe -projectPath <abs> -batchmode -quit -executeMethod FiHeadliftDirectPatch.Run
/// </summary>
public static class FiHeadliftDirectPatch
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const float BadValue = -18.5f;
    const float GoodValue = -12f;

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var rigs = Object.FindObjectsByType<CoralBikeRig>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int patched = 0, alreadyGood = 0, otherValue = 0;
        foreach (var rig in rigs)
        {
            if (Mathf.Approximately(rig.poseExtraHeadLiftDegrees, BadValue))
            {
                rig.poseExtraHeadLiftDegrees = GoodValue;
                EditorUtility.SetDirty(rig);
                patched++;
            }
            else if (Mathf.Approximately(rig.poseExtraHeadLiftDegrees, GoodValue))
            {
                alreadyGood++;
            }
            else
            {
                otherValue++;
                Debug.LogWarning($"[headlift-patch] {rig.name}: unexpected value " +
                                  rig.poseExtraHeadLiftDegrees + " - left untouched.");
            }
        }
        Debug.Log($"[headlift-patch] total rigs {rigs.Length}, patched {patched}, " +
                  $"already-good {alreadyGood}, other/untouched {otherValue}");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[headlift-patch] scene saved.");
    }
}
