using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnostic for the promoted A-pose Kuro: the bike stages correctly but the rider does not
/// render, and the mesh-level foot probe put the shoe ~19 m below the pedal. That combination
/// says the SKINNED MESH is being deformed somewhere far from its bones, which is a bindpose /
/// root-scale mismatch rather than a pose problem.
///
/// Prints the facts needed to tell those apart: renderer state, bounds, rootBone, the bone
/// hierarchy's actual world transforms, and the first bindpose.
/// </summary>
public static class KuroPromoDiag
{
    public static void Run()
    {        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SakuraPass.unity")
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);

        var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[diag] no player root"); return; }

        Debug.Log(string.Format("[diag] player root pos={0} rot={1} scale={2}",
            player.transform.position, player.transform.eulerAngles, player.transform.localScale));

        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Debug.Log(string.Format(
                "[diag] SMR '{0}' enabled={1} activeInHierarchy={2} mesh='{3}' verts={4} bones={5}",
                smr.name, smr.enabled, smr.gameObject.activeInHierarchy,
                smr.sharedMesh != null ? smr.sharedMesh.name : "NULL",
                smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0,
                smr.bones != null ? smr.bones.Length : 0));
            Debug.Log(string.Format("[diag]   transform pos={0} scale={1} lossyScale={2}",
                smr.transform.position, smr.transform.localScale, smr.transform.lossyScale));
            Debug.Log(string.Format("[diag]   bounds center={0} size={1}  worldBounds center={2} size={3}",
                smr.localBounds.center, smr.localBounds.size,
                smr.bounds.center, smr.bounds.size));
            Debug.Log(string.Format("[diag]   rootBone={0} updateWhenOffscreen={1}",
                smr.rootBone != null ? smr.rootBone.name : "NULL", smr.updateWhenOffscreen));
            if (smr.sharedMesh != null && smr.sharedMesh.bindposes.Length > 0)
            {
                var bp = smr.sharedMesh.bindposes[0];
                Debug.Log("[diag]   bindpose[0]=\n" + bp);
                Debug.Log("[diag]   bindpose[0].inverse translation=" + bp.inverse.GetColumn(3));
            }
        }

        foreach (var n in new[] { "Armature", "Hips", "Spine", "neck", "Head", "LeftFoot", "RightFoot" })
        {
            var t = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
            if (t == null) { Debug.Log("[diag] bone " + n + " MISSING"); continue; }
            Debug.Log(string.Format("[diag] bone {0,-10} world={1} local={2} lossyScale={3} parent={4}",
                n, t.position, t.localPosition, t.lossyScale,
                t.parent != null ? t.parent.name : "(root)"));
        }

        foreach (var n in new[] { "Bike", "Pedal_L", "Pedal_R", "SaddleTop", "Hood_L" })
        {
            var t = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
            if (t == null) { Debug.Log("[diag] socket " + n + " MISSING"); continue; }
            Debug.Log(string.Format("[diag] socket {0,-10} world={1} lossyScale={2}", n, t.position, t.lossyScale));
        }
    }

    /// <summary>
    /// Neighbour regression sweep for the promotion. Checks the things this project has
    /// historically broken when restaging the player: a leaked second sun, duplicate bike
    /// anchors under the rider, and a vanished NPC roster.
    /// </summary>
    public static void Regress()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SakuraPass.unity")
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);

        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        var dir = lights.Where(l => l.type == LightType.Directional).ToList();
        Debug.Log(string.Format("[regress] lights total={0} directional={1} -> {2}",
            lights.Length, dir.Count,
            dir.Count == 1 ? "OK (single sun)" : "CHECK - expected exactly one sun"));
        foreach (var l in dir)
            Debug.Log("[regress]   sun '" + l.name + "' intensity=" + l.intensity);

        var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Kuro on Sakura Pass");
        if (player != null)
        {
            int anchors = player.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Bike");
            Debug.Log(string.Format("[regress] player bike anchors={0} -> {1}",
                anchors, anchors == 1 ? "OK" : "CHECK - duplicate bike"));

            var smr = player.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
            if (smr != null)
                Debug.Log(string.Format("[regress] player mesh worldBounds size={0} (expect ~1.3 m tall)",
                    smr.bounds.size));
        }
        else Debug.LogError("[regress] player root missing");

        int npcs = Object.FindObjectsByType<CoralBikeRig>(FindObjectsSortMode.None).Length;
        int kuroRigs = Object.FindObjectsByType<KuroBikeRig>(FindObjectsSortMode.None).Length;
        Debug.Log(string.Format("[regress] KuroBikeRig count={0} (expect 1: the player) CoralBikeRig count={1}",
            kuroRigs, npcs));

        Debug.Log("[regress] scene dirty=" + scene.isDirty);
    }
}
