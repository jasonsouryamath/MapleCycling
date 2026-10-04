using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;

/// <summary>Renderer census and repeatable 900 m Minato render-loop timing evidence.</summary>
public static class MinatoCrowdDiagnostics
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string RootName = "Minato Coast Environment";

    [MenuItem("MapleRide/Diagnostics/Profile Minato Crowd")]
    public static void Profile()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Minato Coast is not staged.");
            var actors = root.GetComponentsInChildren<MinatoCrowdActor>(true);
            var crowdRenderers = actors.SelectMany(
                a => a.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
            int crowdInstances = actors.Length;
            long triangles = 0;
            foreach (var mr in crowdRenderers)
            {
                Mesh mesh = mr is SkinnedMeshRenderer smr ? smr.sharedMesh :
                            mr.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int s = 0; s < mesh.subMeshCount; s++)
                    triangles += (long)mesh.GetIndexCount(s) / 3;
            }
            int skins = crowdRenderers.OfType<SkinnedMeshRenderer>().Count();
            int meshRenderers = crowdRenderers.OfType<MeshRenderer>().Count();
            int animators = root.GetComponentsInChildren<Animator>(true).Length;
            int walkers = actors.Count(a => a.motion == MinatoCrowdActor.MotionKind.Walk);
            string groups = string.Join(", ", actors.GroupBy(PlacementGroup)
                .OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
            Debug.Log($"[minato-perf] crowd instances={crowdInstances:N0}, " +
                      $"renderers={crowdRenderers.Length:N0}, triangles={triangles:N0}, " +
                      $"renderers/instance={(crowdInstances > 0 ? (float)crowdRenderers.Length / crowdInstances : 0f):N2}, " +
                      $"skinned={skins:N0}, mesh={meshRenderers:N0}, proceduralActors={actors.Length:N0}, " +
                      $"walkers={walkers:N0}, animators={animators:N0}; groups: {groups}");

            var route = MinatoCoastEnvironment.MinatoRoute.Load();
            int i = route.IndexAt(900f);
            var p = route.Position[i];
            var fwd = Vector3.ProjectOnPlane(route.Tangent[i], Vector3.up).normalized;
            var camGo = new GameObject("~MinatoPerfCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.transform.position = p - fwd * 5.5f + Vector3.up * 2.4f;
            cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.allowHDR = true;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2
            };
            cam.targetTexture = rt;
            var drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 256);
            var setPasses = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 256);
            var renderedTriangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 256);
            var mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 256);
            var renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 256);

            // Warm shaders/culling, then time the populated route-zone render itself. This is
            // repeatable editor-side CPU submission evidence; FrameTimingManager supplies GPU
            // timing when the active graphics backend exposes it.
            for (int n = 0; n < 20; n++) cam.Render();
            const int Frames = 120;
            var samples = new List<double>(Frames);
            var sw = new System.Diagnostics.Stopwatch();
            FrameTimingManager.CaptureFrameTimings();
            for (int n = 0; n < Frames; n++)
            {
                sw.Restart();
                cam.Render();
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
                FrameTimingManager.CaptureFrameTimings();
            }
            samples.Sort();
            var timings = new FrameTiming[32];
            uint got = FrameTimingManager.GetLatestTimings((uint)timings.Length, timings);
            double gpu = 0.0, cpu = 0.0;
            if (got > 0)
            {
                for (int n = 0; n < got; n++)
                {
                    gpu += timings[n].gpuFrameTime;
                    cpu += timings[n].cpuFrameTime;
                }
                gpu /= got;
                cpu /= got;
            }
            double avg = samples.Average();
            double p95 = samples[Mathf.Clamp(Mathf.CeilToInt(Frames * 0.95f) - 1, 0, Frames - 1)];
            Debug.Log($"[minato-perf] 900m camera {Frames} renders 1280x720: " +
                      $"Camera.Render CPU avg={avg:N2}ms p95={p95:N2}ms; " +
                      $"FrameTiming samples={got} cpu={cpu:N2}ms gpu={gpu:N2}ms");
            Debug.Log($"[minato-perf] profiler recorders: drawCalls={RecorderValue(drawCalls)}, " +
                      $"setPass={RecorderValue(setPasses)}, triangles={RecorderValue(renderedTriangles)}, " +
                      $"mainThread={RecorderMilliseconds(mainThread):N2}ms, " +
                      $"renderThread={RecorderMilliseconds(renderThread):N2}ms");

            drawCalls.Dispose();
            setPasses.Dispose();
            renderedTriangles.Dispose();
            mainThread.Dispose();
            renderThread.Dispose();
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    static long RecorderValue(ProfilerRecorder recorder)
    {
        return recorder.Valid && recorder.Count > 0 ? recorder.LastValue : -1L;
    }

    static double RecorderMilliseconds(ProfilerRecorder recorder)
    {
        return recorder.Valid && recorder.Count > 0 ? recorder.LastValue * 1e-6 : -1.0;
    }

    static string PlacementGroup(MinatoCrowdActor actor)
    {
        foreach (string name in new[]
                 { "Boulevard Cyclists", "Waterfront Pedestrians", "City Beach", "Market Shoppers" })
            for (Transform t = actor.transform; t != null; t = t.parent)
                if (t.name == name) return name;
        return "Other";
    }
}
