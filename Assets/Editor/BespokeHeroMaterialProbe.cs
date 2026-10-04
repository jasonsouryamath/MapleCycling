using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Checks the three bespoke-rig heroes staged by the Maple City, Azora and Taka rosters (Akihiro,
/// Shinobu, Akane), which used to skip CelLit conversion and render with the raw glTF "chrome"
/// shader. For each one it logs every body material's shader and renders a front three-quarter
/// close-up beside an ordinary Kuro-based rider from the same roster, lit identically, so the two
/// can be compared by eye. The scene is NEVER saved.
///
///   run_steps.ps1 "BespokeHeroMaterialProbe.Run|claude_chrome_probe.log|1"
///
/// Frames go to reference/good_graphics/npc_chrome/.
/// </summary>
public static class BespokeHeroMaterialProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string OutDir = "reference/good_graphics/npc_chrome";

    static readonly (string region, string parent, string hero)[] Heroes =
    {
        (RegionCatalog.MapleCity,      "Maple City NPCs", "Maple City NPC Akihiro"),
        (RegionCatalog.AzoraHighlands, "Azora NPCs",      "Azora NPC Shinobu"),
        (RegionCatalog.TakaMountains,  "Taka NPCs",       "Taka NPC Akane"),
    };

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutDir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) regions.Resolve();

        int gltfTotal = 0;
        foreach (var (region, parentName, heroName) in Heroes)
        {
            if (regions != null)
            {
                regions.currentRegionId = region;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }

            var parent = Find(parentName);
            var hero = parent != null ? parent.Find(heroName) : null;
            if (hero == null) { Debug.LogError($"[chrome-probe] {heroName} not found under {parentName}."); continue; }
            ActivateChain(hero);

            int gltf = 0;
            foreach (var r in hero.GetComponentsInChildren<Renderer>(true))
            {
                if (UnderBike(r.transform)) continue;   // bike livery is not in question
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    bool bad = NpcCelLitConversion.IsUnconvertedGltf(m);
                    if (bad) gltf++;
                    Debug.Log($"[chrome-probe] {heroName} / {r.gameObject.name}: '{m.name}' -> {m.shader.name}" +
                              (bad ? "  <-- RAW GLTF" : ""));
                }
            }
            gltfTotal += gltf;
            Debug.Log($"[chrome-probe] {heroName}: {gltf} raw glTF material slot(s).");

            // Reference: the first other rider in the same roster (a Kuro-based, already-CelLit body).
            var reference = parent.Cast<Transform>().FirstOrDefault(t => t != hero && t.name.StartsWith(parentName.Replace("NPCs", "NPC ")));
            Solve(hero);
            var fwd = Vector3.ProjectOnPlane(hero.forward, Vector3.up).normalized;
            var side = Vector3.Cross(Vector3.up, fwd);
            if (reference != null)
            {
                ActivateChain(reference);
                reference.SetPositionAndRotation(hero.position + side * 1.6f, hero.rotation);
                Solve(reference);
            }

            var centre = hero.position + side * (reference != null ? 0.8f : 0f) + Vector3.up * 0.8f;
            var eye = centre + fwd * 3.6f - side * 1.4f + Vector3.up * 0.35f;
            Shot(Path.Combine(OutDir, $"chrome_{region}_{hero.name.Split(' ').Last()}.png"), eye, centre, 38f);
        }
        Debug.Log($"[chrome-probe] DONE: {gltfTotal} raw glTF slot(s) across {Heroes.Length} heroes.");
    }

    static bool UnderBike(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name == "BikeMesh") return true;
        return false;
    }

    static void ActivateChain(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
    }

    static void Solve(Transform rider)
    {
        var rig = rider.GetComponentInChildren<CoralBikeRig>(true);
        if (rig != null) rig.ForceSolveOnce();
        foreach (var smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;
    }

    static Transform Find(string name)
    {
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr.name == name) return tr;
        return null;
    }

    static void Shot(string path, Vector3 pos, Vector3 look, float fov)
    {
        const int w = 1400, h = 900;
        var go = new GameObject("~ChromeProbeCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 6000f;
        cam.allowHDR = true;

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        cam.Render();   // HDRP history (exposure, TAA) settles on the second frame

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        Debug.Log($"[chrome-probe] wrote {path}");

        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }
}
