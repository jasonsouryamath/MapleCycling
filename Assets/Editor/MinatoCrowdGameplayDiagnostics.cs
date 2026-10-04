using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Unobstructed gameplay-height validation for the animated Minato crowd.</summary>
public static class MinatoCrowdGameplayDiagnostics
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/Capture Minato Animated Crowd")]
    public static void Capture()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            string dir = MapleRidePaths.Renders;
            Directory.CreateDirectory(dir);

            var actors = UnityEngine.Object.FindObjectsByType<MinatoCrowdActor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (actors.Length == 0) throw new InvalidOperationException("No Minato crowd actors staged.");
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                camera.enabled = false;

            var route = MinatoCoastEnvironment.MinatoRoute.Load();
            int i = route.IndexAt(290f);
            int j = route.IndexAt(330f);
            Vector3 p = route.Position[i];
            Vector3 fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;

            SampleAll(actors, 0f);
            var bridgeActors = actors.OrderBy(a => (a.transform.position - p).sqrMagnitude)
                .Take(10).ToArray();
            var nearest = bridgeActors[0];
            Vector3 nearestTarget = nearest.transform.position + Vector3.up * 0.68f;
            Shot(dir, "diag_minato_gameplay_bridge_close",
                 nearest.transform.position - nearest.transform.forward * 4.2f +
                 Vector3.Cross(Vector3.up, nearest.transform.forward) * 2.6f +
                 Vector3.up * 1.25f,
                 nearestTarget, 38f);
            Shot(dir, "diag_minato_gameplay_bridge_medium",
                 nearest.transform.position - nearest.transform.forward * 10f +
                 Vector3.Cross(Vector3.up, nearest.transform.forward) * 7f +
                 Vector3.up * 4.0f,
                 nearestTarget, 48f);
            Shot(dir, "diag_minato_gameplay_bridge_far",
                 p - fwd * 38f + side * 9f + Vector3.up * 8.5f,
                 p + fwd * 70f + Vector3.up * 1.3f, 46f);

            var walker = actors.Where(a => a.motion == MinatoCrowdActor.MotionKind.Walk)
                .OrderBy(a => (a.transform.position - p).sqrMagnitude).FirstOrDefault();
            if (walker == null) throw new InvalidOperationException("No animated walker staged.");
            Vector3 walkAxis = Vector3.ProjectOnPlane(
                walker.pathEnd - walker.pathStart, Vector3.up).normalized;
            Vector3 walkSide = Vector3.Cross(Vector3.up, walkAxis).normalized;
            for (int n = 0; n < 4; n++)
            {
                float t = n * 0.16f;
                SampleAll(actors, t);
                Physics.SyncTransforms();
                Vector3 target = walker.transform.position + Vector3.up * 0.68f;
                // Three-quarter side view keeps the two legs visually separated while still
                // exposing forward stride and sole-to-pavement clearance.
                Vector3 eye = walker.transform.position + walkSide * 3.8f -
                              walkAxis * 2.1f + Vector3.up * 1.12f;
                Shot(dir, $"diag_minato_walk_t{n}", eye, target, 34f);
                Debug.Log($"[minato-live] walk t={t:0.00}s pos={walker.transform.position} " +
                          $"path={Vector3.Distance(walker.pathStart, walker.pathEnd):0.00}m " +
                          $"speed={walker.moveSpeed:0.00}m/s {walker.DiagnosticLimbState()}");
            }

            CountScene(actors);
            Debug.Log("[minato-live] captured bridge close/medium/far and four walk timestamps.");
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    static void SampleAll(MinatoCrowdActor[] actors, float time)
    {
        foreach (var actor in actors) actor.SampleForDiagnostics(time);
    }

    static void CountScene(MinatoCrowdActor[] actors)
    {
        var renderers = actors.SelectMany(a => a.GetComponentsInChildren<Renderer>(true))
            .Distinct().ToArray();
        var skins = renderers.OfType<SkinnedMeshRenderer>().ToArray();
        var meshes = renderers.OfType<MeshRenderer>().ToArray();
        int highTris = actors.SelectMany(a => {
                var high = a.transform.Find("LOD0 High Skinned");
                return high != null ? high.GetComponentsInChildren<Renderer>(true) :
                                      Array.Empty<Renderer>();
            }).Sum(Triangles);
        int lowTris = actors.SelectMany(a => {
                var low = a.transform.Find("LOD1 Low 3D");
                return low != null ? low.GetComponentsInChildren<Renderer>(true) :
                                     Array.Empty<Renderer>();
            }).Sum(Triangles);
        int animators = actors.Sum(a => a.GetComponentsInChildren<Animator>(true).Length);
        int walkers = actors.Count(a => a.motion == MinatoCrowdActor.MotionKind.Walk);
        Debug.Log($"[minato-live] actors={actors.Length}, procedural={actors.Length}, " +
                  $"walkers={walkers}, SkinnedMeshRenderers={skins.Length}, " +
                  $"MeshRenderers={meshes.Length}, Animators={animators}, " +
                  $"highLOD tris(instanced scene total)={highTris:N0}, " +
                  $"lowLOD tris(instanced scene total)={lowTris:N0}");
    }

    static int Triangles(Renderer r)
    {
        Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh :
                    r.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null) return 0;
        int total = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
            total += (int)(mesh.GetIndexCount(s) / 3);
        return total;
    }

    static void Shot(string dir, string name, Vector3 eye, Vector3 look, float fov)
    {
        var go = new GameObject("~MinatoLiveCamera", typeof(Camera));
        var cam = go.GetComponent<Camera>();
        cam.transform.position = eye;
        cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.08f;
        cam.farClipPlane = 700f;
        cam.allowHDR = true;
        go.AddComponent<SakuraPostFX>();

        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 2
        };
        cam.targetTexture = rt;
        for (int n = 0; n < 4; n++) cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        image.Apply();
        string path = Path.Combine(dir, name + ".png");
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log($"[minato-live] wrote {path}");
    }
}
