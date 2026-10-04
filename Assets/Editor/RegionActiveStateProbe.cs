using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RegionActiveStateProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/Region Active State Probe")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string[] names = { "Taka NPCs", "Maple City NPCs", "Azora NPCs", "NPCs", "Shiosai Coast Environment" };
        foreach (var n in names)
        {
            var go = GameObject.Find(n);
            if (go == null) { Debug.Log($"[region-probe] '{n}': NOT FOUND"); continue; }
            int activeChildren = 0, totalChildren = 0;
            foreach (var g in go.GetComponentsInChildren<NpcGreeting>(true))
            {
                totalChildren++;
                if (g.gameObject.activeInHierarchy) activeChildren++;
            }
            Debug.Log($"[region-probe] '{n}': activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy} " +
                      $"NpcGreeting children total={totalChildren} activeInHierarchy={activeChildren}");
        }
        MapleRideSceneBootstrap.DiscardChanges();
    }

    [MenuItem("MapleRide/Diagnostics/Region Active State Probe (SC_Persistent)")]
    public static void RunOnShiosaiScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SC_Persistent.unity", OpenSceneMode.Single);
        var all = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[region-probe] SC_Persistent.unity: {all.Length} NpcGreeting total");
        var traffic = GameObject.Find("Shiosai Traffic");
        Debug.Log($"[region-probe] 'Shiosai Traffic' root: {(traffic == null ? "NOT FOUND" : $"activeSelf={traffic.activeSelf}")}");
        var env = GameObject.Find("Shiosai Coast Environment");
        Debug.Log($"[region-probe] 'Shiosai Coast Environment' root: {(env == null ? "NOT FOUND" : $"activeSelf={env.activeSelf}")}");
        int active = 0;
        foreach (var g in all) if (g.gameObject.activeInHierarchy) active++;
        Debug.Log($"[region-probe] active-in-hierarchy count: {active}");
        MapleRideSceneBootstrap.DiscardChanges();
    }

    [MenuItem("MapleRide/Diagnostics/Dump Kaito Materials (SC_Persistent)")]
    public static void DumpKaito()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SC_Persistent.unity", OpenSceneMode.Single);
        var all = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var g in all)
        {
            if (g.riderName != "Kaito") continue;
            Debug.Log($"[kaito-dump] rider object '{g.gameObject.name}' active={g.gameObject.activeInHierarchy}");
            var seen = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;
                    string tex = "n/a";
                    foreach (var p in new[] { "_MainTex", "_BaseColorMap", "baseColorTexture" })
                        if (m.HasProperty(p)) { var t = m.GetTexture(p); tex = t != null ? t.name : "NULL"; break; }
                    string col = m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "n/a";
                    Debug.Log($"[kaito-dump]   '{r.name}' mat='{m.name}' shader='{m.shader.name}' tex={tex} color={col}");
                }
            }
        }
        MapleRideSceneBootstrap.DiscardChanges();
    }

    [MenuItem("MapleRide/Diagnostics/Dump Rig Scale (SC_Persistent)")]
    public static void DumpRigScale()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SC_Persistent.unity", OpenSceneMode.Single);
        var all = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var g in all)
        {
            var head = FindChild(g.transform, "Head");
            var bike = g.transform.Find("Bike");
            Debug.Log($"[rig-dump] '{g.gameObject.name}' riderName={g.riderName} " +
                      $"rootLossyScale={g.transform.lossyScale} headFound={(head != null)} " +
                      $"headLossyScale={(head != null ? head.lossyScale.ToString() : "n/a")} " +
                      $"headLocalPos={(head != null ? head.position.ToString() : "n/a")} " +
                      $"bikeFound={(bike != null)}");
        }
        MapleRideSceneBootstrap.DiscardChanges();
    }

    [MenuItem("MapleRide/Diagnostics/Dump Named Rider Materials (SakuraPass)")]
    public static void DumpYuki()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var all = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var g in all)
        {
            if (g.riderName != "Yuki") continue;
            Debug.Log($"[yuki-dump] rider object '{g.gameObject.name}' active={g.gameObject.activeInHierarchy}");
            var seen = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;
                    string tex = "n/a";
                    foreach (var p in new[] { "_MainTex", "_BaseColorMap", "baseColorTexture" })
                        if (m.HasProperty(p)) { var t = m.GetTexture(p); tex = t != null ? t.name : "NULL"; break; }
                    string col = m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "n/a";
                    string metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("F3") : "n/a";
                    Debug.Log($"[yuki-dump]   '{r.name}' mat='{m.name}' shader='{m.shader.name}' tex={tex} color={col} metallic={metallic}");
                }
            }
        }
        MapleRideSceneBootstrap.DiscardChanges();
    }

    [MenuItem("MapleRide/Diagnostics/Dump Rider Orientation (both scenes)")]
    public static void DumpOrientation()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SC_Persistent.unity", OpenSceneMode.Single);
        foreach (var g in Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.riderName != "Kaito") continue;
            var t = g.transform;
            var rider = t.Find(g.riderName + "Rider");
            Debug.Log($"[orient] SHIOSAI '{g.gameObject.name}' root.eulerAngles={t.eulerAngles} root.forward={t.forward} " +
                      $"root.localPos={t.localPosition} root.position={t.position} " +
                      $"riderChildFound={(rider != null)} riderLocalRot={(rider != null ? rider.localRotation.eulerAngles.ToString() : "n/a")}");
        }

        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var g in Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.riderName != "Fubuki" && g.riderName != "Coral") continue;
            var t = g.transform;
            Debug.Log($"[orient] SAKURA-SIDE '{g.gameObject.name}' riderName={g.riderName} root.eulerAngles={t.eulerAngles} " +
                      $"root.forward={t.forward} root.position={t.position}");
        }
        MapleRideSceneBootstrap.DiscardChanges();
    }

    [MenuItem("MapleRide/Diagnostics/Diagnose Kaito Pose (SC_Persistent)")]
    public static void DiagnoseKaitoPose()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SC_Persistent.unity", OpenSceneMode.Single);
        NpcGreeting target = null;
        foreach (var g in Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.riderName == "Kaito") { target = g; break; }
        }
        if (target == null) { Debug.LogError("[kaito-pose] no Kaito found"); return; }

        Debug.Log($"[kaito-pose] object '{target.gameObject.name}' activeSelf(before)={target.gameObject.activeSelf}");
        if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);

        var head = FindChild(target.transform, "Head");
        var hips = FindChild(target.transform, "Hips");
        Debug.Log($"[kaito-pose] AFTER-WAKE head.pos={(head ? head.position.ToString() : "null")} " +
                  $"head.localRot={(head ? head.localRotation.eulerAngles.ToString() : "null")} " +
                  $"hips.pos={(hips ? hips.position.ToString() : "null")}");

        var rig = target.GetComponentInChildren<KuroBikeRig>(true);
        Debug.Log($"[kaito-pose] rig found={(rig != null)}");
        if (rig != null)
        {
            rig.ForceSolveOnce();
            Debug.Log($"[kaito-pose] AFTER-SOLVE head.pos={(head ? head.position.ToString() : "null")} " +
                      $"head.localRot={(head ? head.localRotation.eulerAngles.ToString() : "null")} " +
                      $"hips.pos={(hips ? hips.position.ToString() : "null")}");
        }

        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../good_graphics/bluecast"));
        System.IO.Directory.CreateDirectory(dir);

        // Replicate NpcPortraitBake's EXACT camera math (same fwd/face/position/rotation),
        // but perspective and pulled well back, so we can see what is actually in front of the
        // portrait camera without the tight orthographic crop or near-clip hiding the answer.
        Vector3 fwd = target.transform.forward; fwd.y = 0f; fwd.Normalize();
        float k = Mathf.Max(0.01f, head.lossyScale.y);
        Vector3 face = head.position + Vector3.up * (0.15f * k) + fwd * (0.015f * k);
        Vector3 portraitCamPos = face + fwd * (0.62f * k);
        Debug.Log($"[kaito-pose] fwd={fwd} k={k} face={face} portraitCamPos={portraitCamPos}");

        ShotFrom(portraitCamPos + fwd * 1.2f, face, dir, "kaito_repro_wide_front"); // pulled back further along +fwd, same look target
        ShotFrom(face - fwd * 1.2f, face, dir, "kaito_repro_wide_opposite"); // from the opposite side, for comparison
        ShotFrom(face + Vector3.up * 1.0f + fwd * 0.3f, face, dir, "kaito_repro_wide_top"); // from above, looking down
        ShotFrom(portraitCamPos, face, dir, "kaito_repro_exact_perspective"); // EXACT bake position, perspective
        ShotOrtho(portraitCamPos, fwd, 0.17f * k, dir, "kaito_repro_exact_ortho"); // EXACT bake position AND projection

        // NpcPortraitBake also force-enables the SmileDecal renderer for the shot - test that
        // specifically, since it's the one thing my reproduction above did NOT do yet.
        var smile = FindRenderer(target.transform, "SmileDecal");
        Debug.Log($"[kaito-pose] smile renderer found={(smile != null)} enabledBefore={(smile != null ? smile.enabled.ToString() : "n/a")}");
        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            Debug.Log($"[kaito-pose] renderer '{r.gameObject.name}' layer={r.gameObject.layer} ({LayerMask.LayerToName(r.gameObject.layer)})");
        if (smile != null)
        {
            smile.enabled = true;
            ShotOrtho(portraitCamPos, fwd, 0.17f * k, dir, "kaito_repro_exact_ortho_smileon");
        }

        // NpcPortraitBake also parents a warm directional "key light" to the camera - the last
        // remaining difference from my reproduction above.
        ShotOrthoWithKeyLight(portraitCamPos, fwd, 0.17f * k, dir, "kaito_repro_exact_ortho_keylight");

        MapleRideSceneBootstrap.DiscardChanges();
    }

    static void ShotFrom(Vector3 pos, Vector3 look, string dir, string name)
    {
        var camGo = new GameObject("~DiagCam");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = pos;
        camGo.transform.LookAt(look);
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 100f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.6f, 0.9f, 1f);
        cam.allowHDR = false;
        const int w = 700, h = 700;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Debug.Log($"[kaito-pose] wrote {name}.png");
    }

    static void ShotOrtho(Vector3 pos, Vector3 fwd, float orthoSize, string dir, string name)
    {
        var camGo = new GameObject("~DiagCamOrtho");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = pos;
        camGo.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        cam.orthographic = true;
        cam.orthographicSize = orthoSize;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = (0.62f + 0.34f) * 1.2f; // same ballpark as the real bake's far clip
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.6f, 0.9f, 1f);
        cam.allowHDR = false;
        const int w = 512, h = 512;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Debug.Log($"[kaito-pose] wrote {name}.png");
    }

    static void ShotOrthoWithKeyLight(Vector3 pos, Vector3 fwd, float orthoSize, string dir, string name)
    {
        var camGo = new GameObject("~DiagCamOrthoKL");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = pos;
        camGo.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        cam.orthographic = true;
        cam.orthographicSize = orthoSize;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = (0.62f + 0.34f) * 1.2f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.075f, 0.106f, 0.129f, 1f);
        cam.allowHDR = false;

        var lightGo = new GameObject("~PortraitKeyDiag");
        lightGo.transform.SetParent(camGo.transform, false);
        var key = lightGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.15f;
        key.color = new Color(1f, 0.97f, 0.93f, 1f);
        lightGo.transform.rotation = Quaternion.LookRotation(
            Quaternion.AngleAxis(-18f, Vector3.up) * (-fwd) + Vector3.down * 0.35f);

        const int w = 512, h = 512;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Debug.Log($"[kaito-pose] wrote {name}.png");
    }

    static Renderer FindRenderer(Transform root, string name)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == name) return r;
        return null;
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindChild(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
