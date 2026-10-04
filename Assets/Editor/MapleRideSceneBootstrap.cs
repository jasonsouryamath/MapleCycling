using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps interactive editor startup on boot while preserving the shared authoring-scene contract.
///
/// Why this exists: every batchmode <c>-executeMethod</c> run starts on an empty untitled
/// scene, and on quit Unity writes that state back to Library/LastSceneManagerSetup.txt with a
/// blank path. The next time the editor is opened by hand it therefore restores "nothing" - the
/// title bar reads "MapleRide - Untitled" and the project looks completely broken, when in
/// fact SakuraPass.unity on disk is perfectly intact. Worse, a batchmode pass that leaves the
/// scene dirty makes Unity write a crash-recovery backup of that empty scene, which the editor
/// then helpfully "recovers" over the top.
///
/// So: if a human opens the editor and lands on an empty untitled scene, put them back in the
/// game. Batchmode is deliberately left alone - tooling opens the scenes it wants explicitly.
/// </summary>
[InitializeOnLoad]
public static class MapleRideSceneBootstrap
{
    // Exporters and staging passes use this shared authoring scene, not the runtime boot menu.
    public const string PlayableScene = "Assets/Scenes/SakuraPass.unity";

    static MapleRideSceneBootstrap()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += SetPlayStartScene;
        EditorApplication.delayCall += RestoreIfEmpty;
    }

    private static void SetPlayStartScene()
    {
        MapleRideStreamingScenes.RegisterBuildScenes();
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MapleRideBoot.ScenePath);
    }

    static void RestoreIfEmpty()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(MapleRideBoot.ScenePath)) return;

        Scene active = EditorSceneManager.GetActiveScene();

        // Only step in for a genuinely blank slate. A named scene, or an unsaved scene the user
        // has actually put objects into, is theirs and must not be replaced.
        bool untitled = string.IsNullOrEmpty(active.path);
        if (!untitled) return;
        if (active.GetRootGameObjects().Length > 0) return;

        Debug.Log("[mapleride] editor opened on an empty untitled scene; restoring " + MapleRideBoot.ScenePath);
        EditorSceneManager.OpenScene(MapleRideBoot.ScenePath, OpenSceneMode.Single);
    }

    /// <summary>
    /// Drops any unsaved modifications a batchmode pass made, so Unity does not write a
    /// crash-recovery backup that the next interactive session will restore over the real scene.
    /// Call this at the end of any editor entry point that inspects or renders the scene
    /// without meaning to change it.
    ///
    /// Implemented by reloading from disk rather than clearing the dirty flag, because
    /// EditorSceneManager.ClearSceneDirtiness does not exist in Unity 6. Reopening discards the
    /// in-memory edits and yields a scene that is genuinely clean, which is the stronger
    /// guarantee anyway.
    /// </summary>
    public static void DiscardChanges()
    {
        Scene active = EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(active.path)) return;
        if (!active.isDirty) return;
        EditorSceneManager.OpenScene(active.path, OpenSceneMode.Single);
    }
}
