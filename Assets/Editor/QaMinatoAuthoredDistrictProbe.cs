using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// THROWAWAY QA probe: measures the world-space renderer bounds of the two hand-authored port
/// GLBs ("Authored Port District" and "Authored Bridge Approach", built by
/// MinatoCoastEnvironment.BuildAuthoredPortDistrict) against the rideable corridor, and shoots
/// a handful of rider's-eye and overhead frames centred exactly on their anchor points
/// (route metres 900 and 2070) so an on-road placement is visible, not just measured.
/// </summary>
public static class QaMinatoAuthoredDistrictProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RootName = "Minato Coast Environment";
    private const float CorridorHalfM = 4.55f;

    [MenuItem("MapleRide/QA/Probe Minato Authored Port District")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogError("[qa-minato3] root not found - build it first.");
            return;
        }

        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        var log = new StringBuilder();
        log.AppendLine("=== MINATO AUTHORED PORT DISTRICT / BRIDGE APPROACH CORRIDOR CHECK ===");

        foreach (var name in new[] { "Authored Port District", "Authored Bridge Approach" })
        {
            Transform found = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) { found = t; break; }
            if (found == null)
            {
                log.AppendLine($"[qa-minato3] '{name}' NOT FOUND in scene.");
                continue;
            }

            var renderers = found.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                log.AppendLine($"[qa-minato3] '{name}' has NO renderers.");
                continue;
            }
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var colliders = found.GetComponentsInChildren<Collider>(true);

            // Nearest route station to the object's own transform.position (its authored anchor).
            var pos = found.position;
            float bestLat = float.MaxValue; int bestI = -1;
            for (int i = 0; i < route.Count; i++)
            {
                var p = route.Position[i];
                float dx = pos.x - p.x, dz = pos.z - p.z;
                float lat2 = dx * dx + dz * dz;
                if (lat2 < bestLat) { bestLat = lat2; bestI = i; }
            }
            float anchorD = bestI >= 0 ? route.Distance[bestI] : -1f;

            // Now find how close the BOUNDS come to the corridor across the whole relevant
            // route span (anchor +/- 250 m), since a large authored GLB can extend well past
            // its own pivot.
            float worstNearEdge = float.MaxValue; float worstD = -1f; float worstLateral = 0f;
            for (float d = Mathf.Max(0f, anchorD - 250f); d < anchorD + 250f; d += 2f)
            {
                int i = route.IndexAt(d);
                var p = route.Position[i];
                var side = route.SideFlat(i);
                // Sample the bounds' 4 lower corners plus centre in the route-local lateral axis.
                var corners = new[]
                {
                    bounds.center,
                    new Vector3(bounds.min.x, bounds.min.y, bounds.min.z),
                    new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                    new Vector3(bounds.max.x, bounds.min.y, bounds.min.z),
                    new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
                };
                foreach (var c in corners)
                {
                    var toC = new Vector3(c.x - p.x, 0f, c.z - p.z);
                    float lateral = Vector3.Dot(toC, side);
                    float nearEdge = Mathf.Abs(lateral);
                    bool vertOk = c.y < p.y + 3.0f && c.y > p.y - 3.0f;
                    if (vertOk && nearEdge < worstNearEdge)
                    {
                        worstNearEdge = nearEdge; worstD = d; worstLateral = lateral;
                    }
                }
            }

            log.AppendLine($"'{name}': anchor world pos={pos} nearest route d={anchorD:N1} m; " +
                            $"renderer bounds center={bounds.center} size={bounds.size}; " +
                            $"{colliders.Length} colliders in hierarchy; " +
                            $"CLOSEST bounds approach to centreline = {worstNearEdge:N2} m " +
                            $"at d={worstD:N1} m (lateral {worstLateral:N2} m) against a " +
                            $"{CorridorHalfM:N2} m half-width corridor -> " +
                            (worstNearEdge < CorridorHalfM ? "**INTRUDES ON RIDE CORRIDOR**"
                                                            : "clear of corridor"));
        }

        Debug.Log(log.ToString());
        Directory.CreateDirectory("reference/good_graphics");
        File.WriteAllText("reference/good_graphics/qa_minato_authored_district.txt", log.ToString());

        // Visual proof shots centred on the district anchor (900 m) looking straight down the
        // carriageway from rider height, plus an overhead frame.
        int di = route.IndexAt(900f);
        var dp = route.Position[di];
        int dj = route.IndexAt(950f);
        Shot(dp + Vector3.up * 1.5f, route.Position[dj] + Vector3.up * 1.2f, 60f,
             "qa_minato_district_rideeye_900.png");
        Shot(dp + Vector3.up * 140f - route.Tangent[di].normalized * 30f, dp, 60f,
             "qa_minato_district_topdown_900.png");
        Shot(dp + route.SideFlat(di) * 60f + Vector3.up * 8f, dp, 60f,
             "qa_minato_district_side_900.png");
    }

    private static void Shot(Vector3 eye, Vector3 target, float fov, string file)
    {
        var camGo = new GameObject("~QaCam2", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.transform.position = eye;
        var dir = target - eye;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        cam.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 5000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        File.WriteAllBytes(Path.Combine("reference/good_graphics", file), tex.EncodeToPNG());
        Debug.Log($"[qa-minato3] shot {file}  eye {eye}");

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(tex);
    }
}
