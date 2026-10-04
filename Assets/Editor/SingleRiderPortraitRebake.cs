using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batchmode entry points for a SURGICAL single-rider portrait re-bake
/// (<see cref="NpcPortraitBake.BakeSingle"/>): rewrite exactly one rider's face card and leave
/// every other portrait on disk untouched. Used to repair an individual defective portrait
/// (e.g. a stale blown-out bake) without re-running the destructive BakeAll pass, which would
/// delete the parked portraits of whichever region is not currently awake.
///
/// The rider name comes from a "-rider &lt;Name&gt;" command-line argument so no code edit is
/// needed per rider:
///   Unity ... -executeMethod SingleRiderPortraitRebake.InSakura      -rider Yuki
///   Unity ... -executeMethod SingleRiderPortraitRebake.InPersistent  -rider Nami
/// </summary>
public static class SingleRiderPortraitRebake
{
    const string SakuraScene = "Assets/Scenes/SakuraPass.unity";
    const string PersistentScene = "Assets/Scenes/SC_Persistent.unity";

    /// <summary>Re-bake one rider that lives in the SakuraPass scene (Sakura roster).</summary>
    public static void InSakura() => Run(SakuraScene);

    /// <summary>Re-bake one rider that lives in the SC_Persistent scene (Shiosai pool).</summary>
    public static void InPersistent() => Run(PersistentScene);

    static void Run(string scenePath)
    {
        string rider = ArgValue("-rider");
        if (string.IsNullOrEmpty(rider))
        {
            Debug.LogError("[single-rebake] missing '-rider <Name>' argument.");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        bool ok = NpcPortraitBake.BakeSingle(rider);
        Debug.Log($"[single-rebake] rider='{rider}' scene='{scenePath}' result={(ok ? "OK" : "FAILED")}.");
    }

    static string ArgValue(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}
