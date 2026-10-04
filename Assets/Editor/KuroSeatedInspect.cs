using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only in-engine inspector + capture for the seated SakuraPass player. Dumps every renderer
/// under "Kuro on Sakura Pass" with its GO name, world bounds (to catch a stray box near the rig
/// origin), mesh, and per-material shader + gloss/spec/rim, then writes a REAR (chase), SIDE and
/// HELMET capture so the on-bike look is judged from a render. Never saves the scene.
/// </summary>
public static class KuroSeatedInspect
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[inspect] player not found."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

        Vector3 origin = player.transform.position;
        var sb = new StringBuilder();
        sb.AppendLine($"[inspect] player origin={origin}");
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds;
            float distToOrigin = Vector3.Distance(b.center, origin);
            string kind = r is SkinnedMeshRenderer ? "Skinned" : (r is MeshRenderer ? "MeshR" : r.GetType().Name);
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else { var mf = r.GetComponent<MeshFilter>(); if (mf) mesh = mf.sharedMesh; }
            sb.AppendLine($"  [{kind}] '{r.gameObject.name}' enabled={r.enabled} active={r.gameObject.activeInHierarchy} " +
                          $"mesh='{(mesh ? mesh.name : "null")}' size={b.size:F3} center={b.center:F2} dOrigin={distToOrigin:F2}");
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { sb.AppendLine("      mat=<null>"); continue; }
                string g = m.HasProperty("_Gloss") ? m.GetFloat("_Gloss").ToString("F2") : "-";
                string sp = m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength").ToString("F2") : "-";
                string rs = m.HasProperty("_RimStrength") ? m.GetFloat("_RimStrength").ToString("F2") : "-";
                string mf = m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor").ToString("F2")
                          : (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("F2") : "-");
                string path = AssetDatabase.GetAssetPath(m);
                sb.AppendLine($"      mat='{m.name}' shader='{m.shader.name}' gloss={g} spec={sp} rim={rs} metallic={mf} path='{path}'");
            }
        }
        Debug.Log(sb.ToString());

        // ---- captures ----
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));
        Directory.CreateDirectory(dir);
        Vector3 f = player.transform.forward, up = Vector3.up, right = player.transform.right;
        Vector3 hips = player.transform.position;
        Transform head = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") { head = t; break; }

        Shot(dir, "seated_rear",  hips - f * 3.4f + up * 1.55f, hips + up * 1.05f, 52f);
        Shot(dir, "seated_side",  hips + right * 3.2f + up * 1.15f, hips + up * 1.02f, 42f);
        // Tight rear crop on the waist/saddle to identify the reported "black box" there.
        Shot(dir, "seated_waist", hips - f * 1.3f + up * 0.72f, hips + up * 0.60f, 30f);
        if (head != null)
            Shot(dir, "seated_helmet", head.position - f * 1.0f + up * 0.16f, head.position, 26f);
        Report(dir, "seated_rear");

        Debug.Log("[inspect] wrote seated_rear/side/helmet to reference/good_graphics/seated_verify");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Report(string dir, string name)
    {
        string file = Path.Combine(dir, name + ".png");
        if (!File.Exists(file)) return;
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        tex.LoadImage(File.ReadAllBytes(file));
        var px = tex.GetPixels();
        int reddish = 0; float sr = 0, sg = 0, sb2 = 0;
        foreach (var p in px) { sr += p.r; sg += p.g; sb2 += p.b; if (p.r > p.g + 0.12f && p.r > p.b + 0.12f && p.r > 0.25f) reddish++; }
        int n = px.Length;
        Debug.Log($"[inspect] {name}: mean=({sr / n:F3},{sg / n:F3},{sb2 / n:F3}) redPixels={reddish} ({100f * reddish / n:F2}%)");
        Object.DestroyImmediate(tex);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~InspectCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;

        var a = RegionDirector.SakuraAmbience;
        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain; fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 1100, h = 1100;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log($"[inspect] wrote {name}.png");
    }
}
