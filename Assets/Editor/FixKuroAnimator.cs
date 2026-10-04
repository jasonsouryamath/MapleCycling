using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class FixKuroAnimator
{
    [MenuItem("MapleRide/Fix Kuro Animator")]
    public static void Fix()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Kuro/KuroCycle.controller");
        if (controller == null) throw new System.IO.FileNotFoundException("KuroCycle.controller not found");
        var layers = controller.layers;
        for (var i = 0; i < layers.Length; i++) layers[i].defaultWeight = 1f;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/KuroPreview.unity");
        var animator = Object.FindFirstObjectByType<Animator>();
        if (animator != null) { animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.enabled = true; }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log("Kuro Animator fixed: Base Layer weight=1, AlwaysAnimate enabled.");
    }
}
