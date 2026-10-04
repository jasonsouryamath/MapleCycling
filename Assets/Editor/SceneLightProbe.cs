using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Read-only diagnostic: enumerates every Light in the staged scene so a rogue/duplicate light
// can be identified by name, type, intensity, range and position without opening the editor.
public static class SceneLightProbe
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        SakuraSceneDefaults.Fix();

        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[lightprobe] lights={lights.Length}");
        foreach (var l in lights)
        {
            var t = l.transform;
            Debug.Log($"[lightprobe] {Path(t)} type={l.type} on={l.enabled && l.gameObject.activeInHierarchy} " +
                      $"intensity={l.intensity:F3} range={l.range:F1} spot={l.spotAngle:F1} " +
                      $"shadows={l.shadows} bounce={l.bounceIntensity:F2} color={l.color} " +
                      $"pos={t.position:F1} fwd={t.forward:F2}");
        }

        Debug.Log($"[lightprobe] ambient mode={RenderSettings.ambientMode} sky={RenderSettings.ambientSkyColor} " +
                  $"eq={RenderSettings.ambientEquatorColor} gnd={RenderSettings.ambientGroundColor} " +
                  $"intensity={RenderSettings.ambientIntensity:F3} fog={RenderSettings.fog}");

        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(0);
    }

    static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
