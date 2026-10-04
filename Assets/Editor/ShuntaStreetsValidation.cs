using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ShuntaStreetsValidation
{
    static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("[shunta-streets] " + label);
        Debug.Log("[shunta-streets] PASS " + label);
    }

    public static void Run()
    {
        // A legacy persistent flag must not run any seek harness on the next Play press.
        TestStaleFlag(typeof(ShuntaRideHostTest), "Key", "OnState");
        TestStaleFlag(typeof(ShuntaMetroHostSmoke), "Key", "OnState");
        TestStaleFlag(typeof(ShuntaMetroPlayCapture), "PrefKey", "OnMode");
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        var route = UnityEngine.Object.FindFirstObjectByType<ShuntaRouteBuilder>();
        Check(route != null, "playable Shunta route present");
        route.Rebuild();
        var graph = ShuntaRouteProvider.Augment(Resources.Load<RouteGraph>(RouteGraph.ResourceName));
        var course = graph.BuildCourse(ShuntaRouteProvider.CourseId);
        Check(course.Count > 2000 && course.Checkpoints.Length == 12, "full route samples and twelve checkpoints");
        foreach (var checkpoint in course.Checkpoints)
        {
            float d = checkpoint.Distance;
            float maxStep = 0f;
            for (float offset = -20; offset < 20; offset += .25f)
                maxStep = Mathf.Max(maxStep, Vector3.Distance(course.PositionAt(d+offset), course.PositionAt(d+offset+.25f)));
            Check(maxStep < .26f, "continuous travel through " + checkpoint.Name + " max quarter-metre step " + maxStep.ToString("F4"));
        }
        GameObject root = new GameObject("Street infrastructure validation");
        try
        {
            var city = root.AddComponent<ShuntaNeoTokyo>(); city.Prepare(route);
            foreach (var zone in route.Course.zones)
            {
                float km = (zone.startKm+zone.endKm)*.5f;
                city.PreviewAt(km);
                Check(city.StreetlightCount > 50, "streetlights cover zone " + zone.index);
                Check(city.ChunkCount <= 15, "bounded street chunks zone " + zone.index);
                var lights = root.GetComponentsInChildren<Light>(true);
                int near = 0; bool validLamps = true;
                foreach (var light in lights)
                {
                    if (!light.name.StartsWith("Shunta streetlight ")) continue;
                    if ((light.transform.position-route.PositionAtKm(km)).sqrMagnitude < 90f*90f) near++;
                    validLamps &= light.type == LightType.Spot && light.shadows == LightShadows.None
                        && Vector3.Dot(light.transform.forward,Vector3.down) > .999f;
                }
                Check(validLamps, "downward bounded lights zone " + zone.index);
                Check(near >= 4, "no lamp gap at zone " + zone.index);
            }
            Check(root.GetComponentsInChildren<Collider>(true).Length == 0, "street infrastructure leaves rider collision corridor clear");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("[shunta-streets] ALL PASS");
    }

    static void TestStaleFlag(Type type, string member, string callback)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        var property = type.GetProperty(member,flags);
        string key = property != null ? (string)property.GetValue(null) : (string)type.GetField(member,flags).GetValue(null);
        SessionState.SetBool(key,false); EditorPrefs.SetBool(key,true);
        var stage = type.GetField("_stage",flags); stage.SetValue(null,-999);
        try
        {
            type.GetMethod(callback,flags).Invoke(null,new object[]{PlayModeStateChange.EnteredPlayMode});
            Check((int)stage.GetValue(null) == -999,"stale persistent flag cannot arm " + type.Name);
        }
        finally { EditorPrefs.DeleteKey(key); }
    }

    const string CaptureKey = "ChatGPT.ShuntaStreets.Capture";
    static RideBootstrap boot;
    static int captureStage, captureFrames, captureIndex;
    static float previousDistance, captureStartDistance;
    static Vector3 previousPosition;
    static double deadline;
    static readonly float[] captureKm = { 2.6f,14.7f,25.8f };
    static ShuntaStreetsValidation() { EditorApplication.playModeStateChanged += Mode; }
    public static void Capture()
    {
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        SessionState.SetBool(CaptureKey,true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(CaptureKey,false)) return;
        if (mode == PlayModeStateChange.EnteredPlayMode)
        {
            captureStage = captureFrames = captureIndex = 0;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += CaptureTick;
        }
        else if (mode == PlayModeStateChange.EnteredEditMode)
        { SessionState.SetBool(CaptureKey,false); EditorApplication.update -= CaptureTick; }
    }
    static void End(bool ok)
    {
        SessionState.SetBool(CaptureKey,false); EditorApplication.update -= CaptureTick;
        Debug.Log("[shunta-streets] LIVE " + (ok ? "PASS" : "FAIL"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }
    static void CaptureTick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("capture deadline");
            if (captureStage == 0)
            {
                boot = UnityEngine.Object.FindFirstObjectByType<RideBootstrap>();
                if (boot == null || ++captureFrames < 45) return;
                boot.Resolve(); Check(boot.regions.FastTravel(RegionCatalog.ShuntaMetro),"real Shunta loads");
                boot.devices.acceptKeyboardEffort = false; boot.devices.HoldZeroPower = false;
                boot.devices.EffortInput = .7f;
                captureStage = 1;
            }
            if (captureStage == 1)
            {
                var city = ShuntaNeoTokyo.Instance;
                if (city == null || !city.Ready) return;
                boot.session.SeekTo(captureKm[captureIndex] / city.route.Course.distanceKm * boot.session.Course.Length);
                boot.follower.Apply(); previousDistance = boot.session.DistanceM; previousPosition = boot.rider.position;
                captureStartDistance = previousDistance; captureFrames = 0; captureStage = 2; return;
            }
            var rider = boot.rider.position;
            float travel = boot.session.DistanceM - previousDistance;
            if (travel < -.01f || travel > Mathf.Max(.2f,boot.session.SpeedMps*Time.deltaTime+.1f) || Vector3.Distance(rider,previousPosition) > Mathf.Max(.3f,travel+.15f))
                throw new Exception("live unexpected jump: distance " + travel);
            previousPosition = rider; previousDistance = boot.session.DistanceM;
            if (++captureFrames < 120 || RideInputGate.Locked || boot.session.DistanceM-captureStartDistance < 1f) return;
            Debug.Log("[shunta-streets] PASS uninterrupted live riding " + (boot.session.DistanceM-captureStartDistance).ToString("F2") + " m");
            var activeCity = ShuntaNeoTokyo.Instance;
            Check(activeCity.StreetlightCount > 50,"live streamed streetlights present");
            int enabledLamps = 0; float nearest = float.MaxValue;
            foreach (var light in activeCity.GetComponentsInChildren<Light>())
                if (light.name.StartsWith("Shunta streetlight") && light.enabled)
                { enabledLamps++; nearest = Mathf.Min(nearest,Vector3.Distance(light.transform.position,rider)); }
            Check(enabledLamps >= 4 && nearest < 35f,"streetlights illuminate the live rider: " + enabledLamps + " enabled, nearest " + nearest.ToString("F1") + " m");
            var camera = boot.rideCamera != null ? boot.rideCamera : Camera.main;
            var rt = new RenderTexture(960,540,24,RenderTextureFormat.ARGBHalf); rt.Create();
            var image = new Texture2D(960,540,TextureFormat.RGB24,false);
            var prevTarget = camera.targetTexture; var prevActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,960,540),0,0); image.Apply();
                Directory.CreateDirectory("docs/shunta_captures");
                string file = "docs/shunta_captures/zone_"+activeCity.route.Course.ZoneAtKm(captureKm[captureIndex]).index+"_streets.png";
                File.WriteAllBytes(file,image.EncodeToPNG()); Debug.Log("[shunta-streets] CAPTURE "+file);
            }
            finally
            {
                camera.targetTexture = prevTarget; RenderTexture.active = prevActive;
                UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
            if (++captureIndex >= captureKm.Length) End(true); else captureStage = 1;
        }
        catch (Exception e) { Debug.LogException(e); End(false); }
    }
}
