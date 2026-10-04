using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// THROWAWAY QA probe (not part of the production pipeline). Scans every renderer under the
/// "Minato Coast Environment" root for the first 400 route-metres for anything whose world-space
/// bounds intrude into the rideable corridor (+/- ~4.55 m of the centreline, ground to 2.2 m up),
/// and separately reports whether that renderer's GameObject (or any parent/child) carries a
/// Collider. This directly tests the "non-collidable warehouse in the path" report: a mesh that
/// overlaps the corridor but has zero colliders anywhere in its hierarchy would be invisible to
/// MinatoCoastValidation's physics-based envelope check (which only sees colliders) while still
/// being plainly visible to, and rideable-through by, the player.
/// </summary>
public static class QaMinatoOpeningProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RootName = "Minato Coast Environment";
    private const float CorridorHalfM = 4.55f; // RoadHalfWidth(4.0) + ShoulderW(0.55)
    private const float ProbeEndM = 400f;

    [MenuItem("MapleRide/QA/Probe Minato Opening Corridor")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogError("[qa-minato] Minato Coast Environment root not found - build it first.");
            return;
        }

        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        var log = new StringBuilder();
        log.AppendLine("=== MINATO OPENING CORRIDOR PROBE (0-400 m) ===");

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        log.AppendLine($"[qa-minato] scanning {renderers.Length} renderers under '{RootName}'");

        int flagged = 0;
        var seen = new HashSet<Transform>();
        foreach (var rend in renderers)
        {
            var b = rend.bounds;
            // Find nearest route station to this renderer's centre, restricted to our window by
            // a cheap pre-filter: skip if centre is nowhere near the first 400 m worth of stations.
            float bestD = -1f; float bestLat = float.MaxValue; int bestI = -1;
            for (int i = 0; i < route.Count; i++)
            {
                if (route.Distance[i] > ProbeEndM + 60f) break;
                var p = route.Position[i];
                float dx = b.center.x - p.x, dz = b.center.z - p.z;
                float lat2 = dx * dx + dz * dz;
                if (lat2 < bestLat) { bestLat = lat2; bestD = route.Distance[i]; bestI = i; }
            }
            if (bestI < 0 || bestD > ProbeEndM) continue;

            var side = route.SideFlat(bestI);
            var toCentre = new Vector3(b.center.x - route.Position[bestI].x, 0f,
                                        b.center.z - route.Position[bestI].z);
            float lateral = Vector3.Dot(toCentre, side);
            // Half-extent of the AABB projected onto the lateral axis (conservative box radius).
            float halfExtentLateral = Mathf.Abs(side.x) * b.extents.x + Mathf.Abs(side.z) * b.extents.z;
            float nearEdge = Mathf.Abs(lateral) - halfExtentLateral;

            bool verticallyInCorridor = b.min.y < route.Position[bestI].y + 2.4f &&
                                        b.max.y > route.Position[bestI].y - 0.3f;

            if (nearEdge < CorridorHalfM && verticallyInCorridor)
            {
                var t = rend.transform;
                if (!seen.Add(t)) continue;
                bool hasColliderInHierarchy =
                    t.GetComponentInParent<Collider>() != null ||
                    t.GetComponentInChildren<Collider>() != null ||
                    (rend.gameObject.GetComponent<Collider>() != null);
                // Walk up to find a top-level named instance for a readable report.
                var top = t; while (top.parent != null && top.parent != root.transform) top = top.parent;

                flagged++;
                log.AppendLine($"[qa-minato] INTRUSION #{flagged}: renderer '{t.name}' " +
                                $"(top-level instance '{top.name}') at d~{bestD:N1} m, " +
                                $"lateral {lateral:N2} m (nearest edge {nearEdge:N2} m inside a " +
                                $"{CorridorHalfM:N2} m corridor), bounds center={b.center} " +
                                $"size={b.size}, HAS_COLLIDER={hasColliderInHierarchy}");
            }
        }

        if (flagged == 0)
            log.AppendLine("[qa-minato] no renderer intrusions found in the first 400 m corridor.");

        Debug.Log(log.ToString());
        Directory.CreateDirectory("reference/good_graphics");
        File.WriteAllText("reference/good_graphics/qa_minato_opening_probe.txt", log.ToString());

        // Visual proof: rider's-eye shots marching down the opening, plus a top-down overview
        // of the first 300 m so any off-corridor claim can be checked by eye, not just by number.
        foreach (var d in new[] { 0f, 40f, 80f, 120f, 160f, 200f, 260f, 320f })
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            int j = route.IndexAt(Mathf.Min(d + 60f, route.Length - 1f));
            var eye = p + Vector3.up * 1.5f;
            var target = route.Position[j] + Vector3.up * 1.2f;
            Shot(eye, target, 60f, $"qa_minato_open_d{(int)d:000}.png");
        }
        {
            int i = route.IndexAt(150f);
            var p = route.Position[i];
            Shot(p + Vector3.up * 220f + route.Tangent[i].normalized * -10f, p, 60f,
                 "qa_minato_open_topdown.png");
        }
    }

    private static void Shot(Vector3 eye, Vector3 target, float fov, string file)
    {
        var camGo = new GameObject("~QaCam", typeof(Camera));
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
        Debug.Log($"[qa-minato] shot {file}  eye {eye}");

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(tex);
    }
}
