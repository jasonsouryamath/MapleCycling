using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes Resources/NpcPortraits/Kuro.png with the same head camera every NPC portrait uses, so
/// the race intro and results cards show Kuro's face instead of the silhouette. The portrait
/// baker needs an NpcGreeting to find a rider, so one is added to the player rider for the bake
/// and removed again; the scene is never saved.
/// Batch: run_steps "KuroPortraitBake.Run|log|1"
/// </summary>
public static class KuroPortraitBake
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Race/Bake Kuro Portrait", priority = 41)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null)
        {
            var boot = Object.FindFirstObjectByType<RideBootstrap>();
            if (boot != null && boot.rider != null) player = boot.rider.gameObject;
        }
        if (player == null) { Debug.LogError("[kuro-portrait] no player rider in the scene"); return; }
        var g = player.AddComponent<NpcGreeting>();
        g.riderName = "Kuro";
        g.useFaceCard = false;
        bool ok = NpcPortraitBake.BakeSingle("Kuro");
        Object.DestroyImmediate(g);
        Debug.Log($"[kuro-portrait] RESULT {(ok ? "baked Resources/NpcPortraits/Kuro.png" : "FAILED")}");
    }
}
