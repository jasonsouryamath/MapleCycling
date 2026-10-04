using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Close head renders of the player vs a good (Mesh_0 + SmileDecal) roster NPC, in-engine.</summary>
public static class FiRiderHeadCompare
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player != null) ShootHead(player.transform, "fi2_cmp_player_head");

        // first active roster NPC using Mesh_0
        var npc = Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(n => n.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(s => s.sharedMesh != null && s.sharedMesh.name == "Mesh_0"));
        if (npc != null) { Debug.Log("[fi-cmp] good NPC = " + npc.name); ShootHead(npc.transform, "fi2_cmp_good_head"); }
        else Debug.LogWarning("[fi-cmp] no Mesh_0 NPC active");

        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void ShootHead(Transform root, string name)
    {
        // head bounds from baked skinned mesh, excluding bike
        bool any = false; Bounds full = new Bounds();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.EndsWith("_Outline")) continue;
            bool bike = false; for (var c = r.transform; c != null && c != root.parent; c = c.parent) if (c.name == "Bike") { bike = true; break; }
            if (bike) continue;
            var smr = r as SkinnedMeshRenderer; Bounds b;
            if (smr != null && smr.sharedMesh != null)
            {
                var m = new Mesh(); smr.BakeMesh(m, true); var v = m.vertices;
                if (v.Length == 0) { Object.DestroyImmediate(m); continue; }
                b = new Bounds(smr.transform.TransformPoint(v[0]), Vector3.zero);
                for (int i = 1; i < v.Length; i++) b.Encapsulate(smr.transform.TransformPoint(v[i]));
                Object.DestroyImmediate(m);
            }
            else b = r.bounds;
            if (!any) { full = b; any = true; } else full.Encapsulate(b);
        }
        Vector3 c2 = full.center; c2.y = full.max.y - full.size.y * 0.16f;
        Bounds head = new Bounds(c2, new Vector3(full.size.x, full.size.y * 0.4f, full.size.z));

        var go = new GameObject("~Cam"); var cam = go.AddComponent<Camera>();
        float dist = Mathf.Max(0.5f, head.size.magnitude * 1.5f);
        // shoot from behind-ish 3/4 (matches badcliff framing of the near rider)
        cam.transform.position = head.center + new Vector3(0.35f, 0.12f, 1f).normalized * dist;
        cam.transform.LookAt(head.center);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 3000f;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        go.AddComponent<SakuraPostFX>();
        const int w = 720, h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        File.WriteAllBytes(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log("[fi-cmp] wrote " + name);
    }
}
