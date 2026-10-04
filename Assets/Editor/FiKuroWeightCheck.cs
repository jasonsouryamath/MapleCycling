using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FiKuroWeightCheck
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    public static void Run()
    {
        AssetDatabase.ImportAsset("Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb",
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[wc] no player"); EditorApplication.Exit(1); return; }

        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var bones = smr.bones;
            int liA = System.Array.FindIndex(bones, b => b && b.name == "LeftArm");
            int liS = System.Array.FindIndex(bones, b => b && b.name == "LeftShoulder");
            int riA = System.Array.FindIndex(bones, b => b && b.name == "RightArm");
            int riS = System.Array.FindIndex(bones, b => b && b.name == "RightShoulder");
            var bw = mesh.boneWeights;
            double la = 0, ls = 0, ra = 0, rs = 0;
            foreach (var w in bw)
            {
                la += WeightOf(w, liA); ls += WeightOf(w, liS);
                ra += WeightOf(w, riA); rs += WeightOf(w, riS);
            }
            Debug.Log($"[wc] SMR='{smr.name}' mesh='{mesh.name}' verts={bw.Length} bonesLen={bones.Length} " +
                      $"idx(LA={liA},LS={liS},RA={riA},RS={riS}) totalW: LeftArm={la:F1} LeftShoulder={ls:F1} RightArm={ra:F1} RightShoulder={rs:F1}");
            Debug.Log($"[wc] mesh assetPath={AssetDatabase.GetAssetPath(mesh)}");
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static float WeightOf(BoneWeight w, int idx)
    {
        if (idx < 0) return 0;
        float s = 0;
        if (w.boneIndex0 == idx) s += w.weight0;
        if (w.boneIndex1 == idx) s += w.weight1;
        if (w.boneIndex2 == idx) s += w.weight2;
        if (w.boneIndex3 == idx) s += w.weight3;
        return s;
    }
}
