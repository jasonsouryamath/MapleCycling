using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// FIX-IMPLEMENTER probe: dumps the RUNTIME material state of the SakuraPass player
/// ("Kuro on Sakura Pass") so the "crushed to black" complaint can be diagnosed off ground
/// truth instead of the .mat files (which may not be what is actually assigned). Then renders
/// the exact complaint framing via AnimeKuroChaseShot.RunPlayer.
public static class FiKuroBlackKitDump
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[kdump] player not found"); return; }

        // Scene lighting context.
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Debug.Log($"[kdump] LIGHT '{l.name}' type={l.type} intensity={l.intensity} colour={l.color} " +
                      $"fwd={l.transform.forward} enabled={l.enabled} active={l.gameObject.activeInHierarchy}");

        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var m in smr.sharedMaterials)
            {
                if (m == null) { Debug.Log($"[kdump] SMR '{smr.name}' has NULL material"); continue; }
                string tex = "none";
                if (m.HasProperty("_MainTex"))
                {
                    var t = m.GetTexture("_MainTex");
                    tex = t ? t.name : "NULL";
                }
                Debug.Log(string.Format(
                    "[kdump] SMR '{0}' mat='{1}' shader='{2}'\n" +
                    "        _Color={3} _MainTex={4}\n" +
                    "        ambient={5:F2} shadeStr={6:F2} shadowAmb={7:F2} rimStr={8:F2} specStr={9:F2} gloss={10:F3}",
                    smr.name, m.name, m.shader ? m.shader.name : "null",
                    m.HasProperty("_Color") ? m.GetColor("_Color").ToString("F3") : "n/a",
                    tex,
                    m.HasProperty("_AmbientStrength") ? m.GetFloat("_AmbientStrength") : -1f,
                    m.HasProperty("_ShadeStrength") ? m.GetFloat("_ShadeStrength") : -1f,
                    m.HasProperty("_ShadowAmbient") ? m.GetFloat("_ShadowAmbient") : -1f,
                    m.HasProperty("_RimStrength") ? m.GetFloat("_RimStrength") : -1f,
                    m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength") : -1f,
                    m.HasProperty("_Gloss") ? m.GetFloat("_Gloss") : -1f));
            }
        }

        Debug.Log("[kdump] --- rendering complaint framing via AnimeKuroChaseShot.RunPlayer ---");
        AnimeKuroChaseShot.RunPlayer();
    }
}
