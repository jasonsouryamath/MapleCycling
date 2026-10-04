using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Measures Coral against the player so "she looks too small" can be settled with numbers
/// instead of eyeballed from a perspective screenshot, where whoever is nearer the camera
/// always wins.
/// </summary>
public static class CoralScaleDiagnostics
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Coral/Measure Coral Against Kuro")]
    public static void Measure()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        var coral = GameObject.Find(CoralNpcSetup.NpcName);
        if (player == null || coral == null)
        {
            Debug.LogError("[scale] player or Coral missing from the scene.");
            return;
        }

        ReportSubject("KURO ", player.transform);
        ReportSubject("CORAL", coral.transform);

        float kuroRider = RiderHeight(player.transform);
        float coralRider = RiderHeight(coral.transform);
        float kuroBike = BikeHeight(player.transform);
        float coralBike = BikeHeight(coral.transform);

        Debug.Log($"[scale] rider height ratio coral/kuro = {coralRider / Mathf.Max(0.0001f, kuroRider):0.000}");
        Debug.Log($"[scale] bike  height ratio coral/kuro = {coralBike / Mathf.Max(0.0001f, kuroBike):0.000}");
        Debug.Log($"[scale] to match Kuro, scale Coral's rig by {kuroRider / Mathf.Max(0.0001f, coralRider):0.000}");

        MapleRideSceneBootstrap.DiscardChanges();
    }

    static void ReportSubject(string label, Transform root)
    {
        Bounds all = WorldBounds(root, includeBike: true, bikeOnly: false);
        Bounds rider = WorldBounds(root, includeBike: false, bikeOnly: false);
        Bounds bike = WorldBounds(root, includeBike: true, bikeOnly: true);
        Debug.Log($"[scale] {label} total h={all.size.y:0.000} (y {all.min.y:0.00}->{all.max.y:0.00})  " +
                  $"rider h={rider.size.y:0.000}  bike h={bike.size.y:0.000} len={bike.size.z:0.000}  " +
                  $"lossyScale={root.lossyScale.x:0.000}");
    }

    public static float RiderHeight(Transform root) => WorldBounds(root, false, false).size.y;
    public static float BikeHeight(Transform root) => WorldBounds(root, true, true).size.y;

    /// <summary>
    /// Combined world-space bounds under <paramref name="root"/>. The bike subtree is
    /// identified by an ancestor named "Bike", which is the anchor both rigs use.
    ///
    /// Skinned meshes are baked rather than read from Renderer.bounds. Renderer.bounds for a
    /// SkinnedMeshRenderer is the import-time estimate transformed into world space, not the
    /// posed mesh - it reported Kuro as 2.09 m tall, which is nonsense for a chibi, and would
    /// have sent this whole investigation after the wrong number.
    /// </summary>
    static Bounds WorldBounds(Transform root, bool includeBike, bool bikeOnly)
    {
        bool any = false;
        Bounds result = new Bounds();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponent<ParticleSystem>() != null) continue;
            if (r.transform.name.EndsWith("_Outline")) continue;
            bool isBike = UnderBike(r.transform, root);
            if (bikeOnly && !isBike) continue;
            if (!bikeOnly && !includeBike && isBike) continue;

            Bounds b;
            var skinned = r as SkinnedMeshRenderer;
            if (skinned != null && skinned.sharedMesh != null)
            {
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);
                var verts = baked.vertices;
                if (verts.Length == 0) { Object.DestroyImmediate(baked); continue; }
                // BakeMesh output is in the renderer's local space.
                b = new Bounds(skinned.transform.TransformPoint(verts[0]), Vector3.zero);
                for (int i = 1; i < verts.Length; i++)
                    b.Encapsulate(skinned.transform.TransformPoint(verts[i]));
                Object.DestroyImmediate(baked);
            }
            else b = r.bounds;

            if (!any) { result = b; any = true; }
            else result.Encapsulate(b);
        }
        return any ? result : new Bounds(root.position, Vector3.zero);
    }

    static bool UnderBike(Transform t, Transform root)
    {
        for (var c = t; c != null && c != root.parent; c = c.parent)
            if (c.name == "Bike") return true;
        return false;
    }

    /// <summary>
    /// Parks Coral alongside the player and shoots them both from the same distance, because a
    /// gameplay screenshot always has one of them nearer the camera and perspective alone is
    /// enough to make a correctly sized NPC look wrong (or a wrong one look fine).
    /// </summary>
    [MenuItem("MapleRide/Coral/Capture Scale Comparison")]
    public static void CaptureComparison()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        var coral = GameObject.Find(CoralNpcSetup.NpcName);
        if (player == null || coral == null) { Debug.LogError("[scale] player or Coral missing."); return; }

        // Stand them side by side, facing the same way, on flat ground well clear of scenery.
        Vector3 basePos = player.transform.position;
        Quaternion facing = Quaternion.Euler(0f, 0f, 0f);
        player.transform.position = basePos;
        player.transform.rotation = facing;
        coral.transform.position = basePos + new Vector3(1.9f, 0f, 0f);
        coral.transform.rotation = facing;

        ReportSubject("KURO ", player.transform);
        ReportSubject("CORAL", coral.transform);
        Debug.Log($"[scale] rider ratio now {RiderHeight(coral.transform) / Mathf.Max(0.0001f, RiderHeight(player.transform)):0.000}, " +
                  $"bike ratio now {BikeHeight(coral.transform) / Mathf.Max(0.0001f, BikeHeight(player.transform)):0.000}");

        DumpBikeMaterials("KURO ", player.transform);
        DumpBikeMaterials("CORAL", coral.transform);

        Vector3 mid = basePos + new Vector3(0.95f, 0.75f, 0f);
        Shot(mid + new Vector3(0f, 0f, -6.5f), mid, "coral_scale_front");

        // For the profile pair, separate them along the axis they FACE rather than across it -
        // offset sideways they simply line up behind one another from a side camera, which is
        // how the first attempt produced a shot of Kuro with Coral completely hidden.
        coral.transform.position = basePos + new Vector3(0f, 0f, 2.9f);
        Vector3 sideMid = basePos + new Vector3(0f, 0.75f, 1.45f);
        Shot(sideMid + new Vector3(-7.5f, 0.15f, 0f), sideMid, "coral_scale_side");

        MapleRideSceneBootstrap.DiscardChanges();
    }

    /// <summary>
    /// Logs what the bike is actually painted with. Coral and the player instantiate the SAME
    /// bike GLB, so if one renders red and the other black the difference has to be either the
    /// material instance or something drawn over the top of it.
    /// </summary>
    static void DumpBikeMaterials(string label, Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!UnderBike(r.transform, root)) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) { Debug.Log($"[scale] {label} bike '{r.name}' mat{i}=NULL"); continue; }
                string col = mats[i].HasProperty("_Color") ? mats[i].color.ToString("F3") : "n/a";
                string bc = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor").ToString("F3") : "n/a";
                Debug.Log($"[scale] {label} bike '{r.name}' mat{i}='{mats[i].name}' shader='{mats[i].shader.name}' color={col} baseColor={bc}");
            }
        }
        int outlines = root.GetComponentsInChildren<Renderer>(true)
            .Count(r => r.transform.name.EndsWith("_Outline"));
        Debug.Log($"[scale] {label} outline hull renderers: {outlines}");
    }

    static void Shot(Vector3 pos, Vector3 look, string name)
    {
        var go = new GameObject("~ScaleCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 35f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 3000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        // Match the gameplay grade, or the capture blows out and hides exactly what it is for.
        cam.allowHDR = true;
        go.AddComponent<SakuraPostFX>();

        const int w = 1400, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();

        string dir = MapleRidePaths.RenderDir("coral_npc");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());

        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log("[scale] wrote " + name + ".png");
    }
}
