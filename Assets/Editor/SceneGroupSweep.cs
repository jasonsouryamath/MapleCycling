using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Group bisection for a screen-space render artefact.
///
/// A washed rectangle on the Sakura Pass climb survived every single-object test: it is not
/// post-FX, MSAA, shadows, UI, the petal drifts, the duplicate roads or the terrain chunks, it
/// stays axis-aligned under camera roll, and it vanishes when the culling mask is cleared. That
/// combination only narrows down by elimination, so this renders one frame per scene group with
/// that group's subtree disabled. Whichever frame loses the rectangle owns the defect.
///
///   MR_SWEEP_SEG / MR_SWEEP_D   where to stand (defaults: the climb at 150 m)
/// </summary>
public static class SceneGroupSweep
{
    const string OutDir = "../good_graphics/spot";

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        SakuraSceneDefaults.Fix();
        Directory.CreateDirectory(OutDir);

        string segId = Env("MR_SWEEP_SEG", "pass");
        float m = float.Parse(Env("MR_SWEEP_D", "150"), CultureInfo.InvariantCulture);

        var graph = RouteGraph.Load();
        var seg = graph != null ? graph.Segment(segId) : null;
        if (seg == null) { Debug.LogError($"[sweep] no segment '{segId}'"); EditorApplication.Exit(1); return; }

        int a = seg.IndexAt(m);
        int b = Mathf.Min(a + 1, seg.Count - 1);
        float span = seg.distance[b] - seg.distance[a];
        float f = span > 1e-4f ? (m - seg.distance[a]) / span : 0f;
        Vector3 p = Vector3.Lerp(seg.position[a], seg.position[b], f);
        Vector3 tan = Vector3.Slerp(seg.tangent[a], seg.tangent[b], f).normalized;

        var camGo = new GameObject("SweepCam");
        var cam = camGo.AddComponent<Camera>();
        cam.allowHDR = true;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 9000f;
        cam.renderingPath = RenderingPath.Forward;
        cam.transform.position = p - tan * 7f + Vector3.up * 3.2f;
        cam.transform.rotation = Quaternion.LookRotation(
            (p + tan * 18f + Vector3.up * 0.8f) - cam.transform.position, Vector3.up);

        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
        var shot = new Texture2D(1600, 900, TextureFormat.RGB24, false);

        // Every top-level group in the scene, plus the second level under the environment root,
        // which is where the interesting divisions actually live (Roadway / Landscape / ...).
        // MR_SWEEP_UNDER narrows the sweep to the children of one named group once bisection
        // has already pinned the owner to that branch.
        var groups = new List<GameObject>();
        string under = Env("MR_SWEEP_UNDER", "");
        if (under != "")
        {
            GameObject host = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == under) { host = t.gameObject; break; }
            if (host == null) { Debug.LogError($"[sweep] no group '{under}'"); EditorApplication.Exit(1); return; }
            foreach (Transform child in host.transform) groups.Add(child.gameObject);
            Debug.Log($"[sweep] sweeping {groups.Count} child group(s) of '{under}'");
        }
        else
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                groups.Add(root);
                foreach (Transform child in root.transform) groups.Add(child.gameObject);
            }
        }

        Shoot(cam, rt, shot, "sweep_base");
        foreach (var g in groups)
        {
            if (g == camGo || !g.activeSelf) continue;
            g.SetActive(false);
            Shoot(cam, rt, shot, "sweep_" + Sanitize(g.name));
            g.SetActive(true);
        }

        Object.DestroyImmediate(camGo);
        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, RenderTexture rt, Texture2D shot, string name)
    {
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes($"{OutDir}/{name}.png", shot.EncodeToPNG());
        Debug.Log($"[sweep] wrote {name}.png");
    }

    static string Sanitize(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
        return new string(chars);
    }

    static string Env(string k, string d)
    {
        string v = System.Environment.GetEnvironmentVariable(k);
        return string.IsNullOrEmpty(v) ? d : v;
    }
}
