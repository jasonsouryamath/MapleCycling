using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Replaces only the player mesh hierarchy in the existing SakuraPass scene.  This deliberately
/// keeps the rider GameObject, bike, controls and route data intact; rebuilding the scene just
/// to change Kuro's visual would discard unrelated world work.
/// </summary>
public static class KuroPlayerModelSwap
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    // Canonical approved anime player. KuroNPC_KuroAnime_Rigged.glb now carries the bytes of the
    // literal user-approved RB (Rebound) anime chibi (single Mesh_0, one Image_0 atlas). This
    // replaced the misleadingly named KuroNPC_KuroReal_Rigged.glb, which held a raw photoreal sculpt.
    const string ReplacementPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";

    [MenuItem("MapleRide/Kuro/Apply approved Kuro visual rig")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player != null ? player.GetComponent<KuroBikeRig>() : null;
        if (rig == null) throw new System.InvalidOperationException("Kuro on Sakura Pass has no KuroBikeRig.");

        var replacement = AssetDatabase.LoadAssetAtPath<GameObject>(ReplacementPath);
        if (replacement == null) throw new System.IO.FileNotFoundException("Replacement Kuro rig was not imported.", ReplacementPath);

        // The bike is intentionally kept: it owns the named pedal, saddle and hood sockets
        // that KuroBikeRig drives.  Every other visual child belongs to the rejected P14 mesh.
        var bike = rig.transform.Cast<Transform>().FirstOrDefault(t => t.name == "Bike");
        var keeper = new GameObject("__KuroBikeKeeper");
        if (bike != null) bike.SetParent(keeper.transform, true);

        var oldChildren = rig.transform.Cast<Transform>().ToArray();
        foreach (var child in oldChildren) Object.DestroyImmediate(child.gameObject);

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(replacement);
        if (visual == null) visual = Object.Instantiate(replacement);
        visual.name = "Kuro_ApprovedVisual";
        visual.transform.SetParent(rig.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        if (bike != null) bike.SetParent(rig.transform, true);
        Object.DestroyImmediate(keeper);

        // The canonical anime GLB (KuroNPC_KuroAnime_Rigged.glb) still ships raw glTF PBR
        // materials.  HDRP renders those as chrome/near-black at MapleRide's exposure.  Persist the proven per-atlas CelLit clones immediately
        // after EVERY swap; never leave the player dependent on an optional later menu pass.
        KuroPlayerBodyCelLit.ConvertPlayerBody(rig.gameObject, save: false);
        if (!KuroPlayerBodyCelLit.AssertNoGltfBody(rig.gameObject))
            throw new System.InvalidOperationException("Canonical Kuro was left on raw glTF materials.");

        // KuroBikeRig caches all bones.  Clear those saved bind flags so it resolves the new
        // hierarchy and records a fresh unposed baseline the next time it is enabled.
        var serialized = new SerializedObject(rig);
        serialized.FindProperty("bindCaptured").boolValue = false;
        serialized.FindProperty("pedalBindCaptured").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        rig.enabled = false;
        rig.enabled = true;

        // REIMPORT-SAFE GUARD: re-instantiating the GLB above re-binds the body renderers to the
        // GLB's raw embedded glTF materials (Material_0.002/.003, metallicFactor = 1 -> chrome).
        // Re-apply the matte CelLit body conversion so the player never ships on the glTF metallic
        // shader. This is the THIRD recurrence of a reimport/restage reverting the player body;
        // the assertion fails loudly rather than silently shipping a chrome Kuro.
        int reconv = KuroPlayerBodyCelLit.ConvertPlayerBody(rig.gameObject, save: false);
        Debug.Log($"[kuro-swap] re-applied matte CelLit to {reconv} player body material(s).");
        if (!KuroPlayerBodyCelLit.AssertNoGltfBody(rig.gameObject))
            throw new System.InvalidOperationException(
                "Player body left on the glTF metallic shader after the visual swap - see [player-body-cellit] error above.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Kuro player visual replaced with the approved compact cycling rig.");
    }

    [MenuItem("MapleRide/Kuro/Fix chase presentation")]
    public static void FixPresentation()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var follow = Object.FindFirstObjectByType<KuroFollowCamera>();
        if (follow == null) throw new System.InvalidOperationException("SakuraPass has no KuroFollowCamera.");

        follow.enforceGameplayPresentation = true;
        follow.gameplayOffset = new Vector3(0f, 1.45f, -3.60f);
        follow.gameplayFieldOfView = 44f;
        follow.addGameplayFill = false;
        follow.gameplayFillIntensity = 0f;
        var oldFill = follow.transform.Find("~KuroGameplayFill");
        if (oldFill != null) oldFill.gameObject.SetActive(false);

        EditorUtility.SetDirty(follow);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Kuro chase camera restored to a lit, readable distance.");
    }
}


