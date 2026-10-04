using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// QA-ONLY dedicated capture that frames LIVE Shiosai traffic-NPC heads/faces up close, to
/// verify (by looking, not by a scene probe) the three reported rider defects the fix agent
/// could not reproduce in its own gameplay frames because no rider happened to be in shot:
///   2  featureless eyeless "blob" heads
///   3  oversized nearest-rider head
///   4  melted / blistered helmets
///
/// It wakes a spread of the parked pool riders (deliberately choosing the salmon/orange, purple,
/// magenta and teal liveries seen in badcliff.png / messedup.png), lays them across the road at
/// KM 0.54 and KM 2.09, and photographs them:
///   * from the FRONT at head height (faces: eyes + SmileDecal + helmet legible), and
///   * from BEHIND, low, near-frame (reproducing the badcliff/messedup chase framing).
///
/// It restores the region and re-parks the riders afterwards and NEVER saves the scene.
///
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -quit -executeMethod QaShiosaiFaces.Capture
/// </summary>
public static class QaShiosaiFaces
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string EnvironmentRootName = "Shiosai Coast Environment";
    const string TrafficRootName = "Shiosai Traffic";

    // Pool indices chosen so their liveries cover the exact colours the screenshots flagged:
    //  02 Kaito (orange/navy), 05 Rei (coral-red), 06 Hayato (amber), 10 Mio (purple),
    //  00 Nami (magenta), 01 Isuzu (teal), 04 Suzu (aqua), 11 Toma (charcoal).
    static readonly int[] PickIndices = { 2, 5, 10, 0, 1, 4, 6, 11 };

    [MenuItem("MapleRide/NPCs/QA Capture Shiosai Faces", priority = 40)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[qa-faces] no RegionDirector."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var env = FindRoot(EnvironmentRootName);
        if (env == null) { Debug.LogWarning("[qa-faces] no Shiosai env root."); return; }
        if (!env.gameObject.activeSelf) env.gameObject.SetActive(true);
        var traffic = env.Find(TrafficRootName);
        if (traffic == null) { Debug.LogWarning("[qa-faces] no Shiosai Traffic root - stage traffic first."); return; }
        if (!traffic.gameObject.activeSelf) traffic.gameObject.SetActive(true);

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        Debug.Log($"[qa-faces] route {route.Count} samples, length {route.Length:0} m.");

        // Collect the chosen riders (parked, inactive) in pool order.
        var picks = new List<Transform>();
        var kids = new List<Transform>();
        foreach (Transform c in traffic) kids.Add(c);
        foreach (int idx in PickIndices)
            if (idx < kids.Count) picks.Add(kids[idx]);
        Debug.Log($"[qa-faces] woke {picks.Count} riders: " +
                  string.Join(", ", picks.ConvertAll(p => p.name)));

        foreach (var (tag, chainage) in new[] { ("km054", 540f), ("km209", 2090f) })
        {
            int i = route.IndexAt(chainage);
            var p = route.Position[i];
            var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            var s = route.SideFlat(i);
            float headY = p.y + 1.30f;

            // Lay the riders across the carriageway, staggered in depth so heads don't fully
            // occlude, all oriented DOWN the road (+t).  Offsets: (along t, across s).
            var layout = new (float along, float across)[]
            {
                (0.0f, -2.2f), (1.1f, -0.6f), (2.2f, 1.0f), (3.3f, 2.6f),
                (0.6f, 3.4f), (1.7f, -3.2f), (2.8f, -1.6f), (3.9f, 0.4f),
            };
            for (int r = 0; r < picks.Count && r < layout.Length; r++)
            {
                var tr = picks[r];
                tr.gameObject.SetActive(true);
                var pos = p + t * layout[r].along + s * layout[r].across;
                pos.y = p.y;
                tr.position = pos;
                tr.rotation = Quaternion.LookRotation(t, Vector3.up);
                // Guarantee the frozen-bounds skinned bodies are not culled once moved.
                foreach (var smr in tr.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.updateWhenOffscreen = true;
            }

            // 1) FACE ROW: camera AHEAD looking back (-t) at head height -> faces, eyes, decals,
            //    helmets, and relative head size all legible.
            {
                var eye = p + t * 7.0f + Vector3.up * 1.35f - s * 0.4f;
                var look = p + t * 1.6f + Vector3.up * 1.30f;
                Shot(dir, $"qa_shiosai_faces_{tag}", eye, look, 34f);
            }
            // 2) TIGHT single-head on the nearest rider (defect 2/4 at maximum legibility).
            {
                var lead = picks.Count > 0 ? picks[0] : null;
                if (lead != null)
                {
                    var hp = lead.position + Vector3.up * headY0(lead);
                    var eye = hp + t * 1.9f + s * 0.35f + Vector3.up * 0.05f;
                    Shot(dir, $"qa_shiosai_face_closeup_{tag}", eye, hp, 30f);
                }
            }
            // 3) BADCLIFF/MESSEDUP reproduction: chase camera low behind, near-frame heads.
            {
                var eye = p - t * 3.2f + Vector3.up * 1.55f;
                var look = p + t * 10f + Vector3.up * 0.95f;
                Shot(dir, $"qa_shiosai_chase_{tag}", eye, look, 52f);
            }

            // Re-park before moving to the next station so the two stations don't collide.
            foreach (var tr in picks) tr.gameObject.SetActive(false);
        }

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[qa-faces] done; restored region '{regions.currentRegionId}'. Scene NOT saved.");
    }

    static float headY0(Transform rider)
    {
        // Approximate head height above the rider root from its skinned bounds.
        float top = 0f;
        foreach (var smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            top = Mathf.Max(top, smr.bounds.max.y - rider.position.y);
        return top > 0.5f ? top - 0.12f : 1.28f;
    }

    static Transform FindRoot(string name)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go.transform;
        // May be parented; fall back to a deep search including inactive.
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr.name == name) return tr;
        return null;
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~QaFaceCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 6000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.ShiosaiAmbience;
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        // No DoF: faces must be sharp for defect inspection.
        fx.dofStrength = 0f;
        if (a.aerialRange > 0f)
        {
            fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange;
            fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
            fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
        }

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());

        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[qa-faces] wrote {name}.png");
    }
}
