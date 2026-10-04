using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks the actual runtime traffic pool and captures one refined rider on the Shunta road.</summary>
[InitializeOnLoad]
public static class RefinedNpcGameplayCheck
{
    static string Key => "ChatGPT.RefinedNpcGameplay." + Application.dataPath.GetHashCode();
    static RideBootstrap boot;
    static RefinedNpcApplied[] actors;
    static Vector3 foot;
    static int frames, lastFrame;
    static double deadline;
    static bool failed;
    static RefinedNpcGameplayCheck() { EditorApplication.playModeStateChanged += Mode; }
    public static void Run()
    {
        RefinedNpcBuild.Validate();
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (mode == PlayModeStateChange.EnteredPlayMode)
        {
            boot = null; actors = null; frames = 0; lastFrame = -1; failed = false;
            deadline = EditorApplication.timeSinceStartup + 240; EditorApplication.update += Tick;
        }
        if (mode == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            Debug.Log("[refined-gameplay] " + (failed ? "FAIL" : "PASS"));
            if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        }
    }
    static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); Debug.Log("[refined-gameplay] PASS " + message); }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("refined NPC gameplay startup");
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            if (boot == null)
            {
                boot = UnityEngine.Object.FindFirstObjectByType<RideBootstrap>(); if (boot == null) return;
                boot.Resolve(); Check(boot.regions.FastTravel(RegionCatalog.ShuntaMetro), "Shunta ride loads");
                boot.session.ExternalControl = true; boot.devices.HoldZeroPower = true; boot.devices.acceptKeyboardEffort = false;
                // Test in the middle of the course: at distance zero the approaching
                // pool deliberately holds several identities beyond the start boundary.
                boot.session.SeekTo(2600f / 28400f * boot.session.Course.Length);
                if (boot.follower != null) boot.follower.Apply();
            }
            var traffic = ShuntaCyclistTraffic.Instance;
            if (traffic == null || traffic.VisibleCount == 0) return;
            if (actors == null)
            {
                actors = traffic.GetComponentsInChildren<RefinedNpcApplied>(true);
                var names = actors.Select(a => a.identity).Distinct().ToArray();
                if (names.Length < 6) { actors = null; return; }
                Check(new[] { "Akihiro", "Akane", "Shiori", "Shinobu", "Coral", "Hanakage" }.All(names.Contains), "all six refined identities in live traffic");
                Check(actors.All(a => a.GetComponentInChildren<KuroBikeRig>(true) != null), "all six retain cycling rigs");
                Check(actors.All(a => a.GetComponentsInChildren<Collider>(true).All(c => !c.enabled)), "no NPC collision blockers");
                foot = actors[0].GetComponentsInChildren<Transform>(true).First(t => t.name == "LeftFoot").position;
            }
            if (++frames < 90) return;
            var rider = actors.FirstOrDefault(a => a.gameObject.activeInHierarchy && a.GetComponentsInChildren<Renderer>().Any(r => r.enabled));
            Check(rider != null, "refined rider visible on road");
            var movedFoot = actors[0].GetComponentsInChildren<Transform>(true).First(t => t.name == "LeftFoot").position;
            Check((movedFoot - foot).sqrMagnitude > .00001f, "live animated rider advances");
            var go = new GameObject("Refined NPC road capture"); var camera = go.AddComponent<Camera>();
            camera.CopyFrom(boot.rideCamera); camera.fieldOfView = 38;
            var head = rider.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
            camera.transform.position = head.position + rider.transform.forward * 2.5f + rider.transform.right * 1.4f + Vector3.up * .35f;
            camera.transform.LookAt(head.position - Vector3.up * .27f);
            var target = new RenderTexture(1000,800,24); target.Create(); camera.targetTexture = target;
            var image = new Texture2D(1000,800,TextureFormat.RGB24,false);
            try
            {
                camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0,0,1000,800),0,0); image.Apply();
                Directory.CreateDirectory("docs"); File.WriteAllBytes("docs/refined_npc_shunta_gameplay.png", image.EncodeToPNG());
                Debug.Log("[refined-gameplay] CAPTURE " + rider.identity);
            }
            finally
            {
                RenderTexture.active = null; camera.targetTexture = null; camera.enabled = false;
                UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(image);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
            Finish();
        }
        catch (Exception e) { failed = true; Debug.LogError(e); Finish(); }
    }
    static void Finish()
    {
        EditorApplication.update -= Tick;
        if (Application.isBatchMode)
        {
            // The runner owns this dedicated batch. End it after the proof is captured,
            // avoiding an unnecessary play-to-editor rebuild of the entire streamed world.
            SessionState.SetBool(Key, false);
            Debug.Log("[refined-gameplay] " + (failed ? "FAIL" : "PASS"));
            EditorApplication.Exit(failed ? 1 : 0);
        }
        else EditorApplication.ExitPlaymode();
    }
}
