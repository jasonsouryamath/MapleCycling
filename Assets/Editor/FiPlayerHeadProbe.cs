using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot diagnostic for the player Kuro "exploded hair" defect. Renders the player's head
/// close up (front + back), once as-is and once with every inverted-hull "*_Outline" renderer
/// under the player disabled, and dumps the player's skinned mesh wiring. Batchmode-safe.
/// </summary>
public static class FiPlayerHeadProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[fi-probe] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        // Dump renderers under player
        var sb = new StringBuilder();
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            var smr = r as SkinnedMeshRenderer;
            string mesh = smr != null && smr.sharedMesh != null ? smr.sharedMesh.name : (r.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "-");
            int vc = smr != null && smr.sharedMesh != null ? smr.sharedMesh.vertexCount : (r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0);
            int bs = smr != null && smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
            sb.AppendLine($"[fi-probe] R '{GetPath(r.transform, player.transform)}' type={r.GetType().Name} mesh='{mesh}' verts={vc} blend={bs} mats={string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "null"))}");
        }
        Debug.Log(sb.ToString());

        // Find head center: top of the rider (non-bike, non-outline) baked bounds
        Bounds head = HeadBounds(player.transform);
        Debug.Log($"[fi-probe] head bounds center={head.center} size={head.size}");

        // Baseline shots
        ShotHead(head, "fi2_unity_head_front", front: true);
        ShotHead(head, "fi2_unity_head_back", front: false);

        // Disable outline hulls
        var outlines = player.GetComponentsInChildren<Renderer>(true).Where(r => r.name.EndsWith("_Outline")).ToList();
        Debug.Log($"[fi-probe] disabling {outlines.Count} outline renderers");
        foreach (var o in outlines) o.enabled = false;

        ShotHead(head, "fi2_unity_head_front_noOutline", front: true);
        ShotHead(head, "fi2_unity_head_back_noOutline", front: false);

        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static Bounds HeadBounds(Transform root)
    {
        bool any = false; Bounds full = new Bounds();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.EndsWith("_Outline")) continue;
            if (UnderBike(r.transform, root)) continue;
            var smr = r as SkinnedMeshRenderer;
            Bounds b;
            if (smr != null && smr.sharedMesh != null)
            {
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                var v = baked.vertices; if (v.Length == 0) { Object.DestroyImmediate(baked); continue; }
                b = new Bounds(smr.transform.TransformPoint(v[0]), Vector3.zero);
                for (int i = 1; i < v.Length; i++) b.Encapsulate(smr.transform.TransformPoint(v[i]));
                Object.DestroyImmediate(baked);
            }
            else b = r.bounds;
            if (!any) { full = b; any = true; } else full.Encapsulate(b);
        }
        // head ~ top 35% of rider
        Vector3 c = full.center; c.y = full.max.y - full.size.y * 0.17f;
        return new Bounds(c, new Vector3(full.size.x, full.size.y * 0.4f, full.size.z));
    }

    static bool UnderBike(Transform t, Transform root)
    {
        for (var c = t; c != null && c != root.parent; c = c.parent) if (c.name == "Bike") return true;
        return false;
    }

    static void ShotHead(Bounds head, string name, bool front)
    {
        var go = new GameObject("~HeadCam");
        var cam = go.AddComponent<Camera>();
        float dist = Mathf.Max(0.6f, head.size.magnitude * 1.4f);
        Vector3 dir = front ? new Vector3(0, 0.1f, -1f) : new Vector3(0.15f, 0.1f, 1f);
        cam.transform.position = head.center + dir.normalized * dist;
        cam.transform.LookAt(head.center);
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.02f; cam.farClipPlane = 3000f;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        go.AddComponent<SakuraPostFX>();
        const int w = 800, h = 800;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        string dir2 = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        File.WriteAllBytes(Path.Combine(dir2, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log("[fi-probe] wrote " + name + ".png");
    }

    static string GetPath(Transform t, Transform root)
    {
        var s = t.name;
        for (var c = t.parent; c != null && c != root.parent; c = c.parent) s = c.name + "/" + s;
        return s;
    }
}
