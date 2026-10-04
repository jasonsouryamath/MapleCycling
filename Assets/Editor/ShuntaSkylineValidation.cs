using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ShuntaSkylineValidation
{
    static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("[shunta-skyline] " + label);
        Debug.Log("[shunta-skyline] PASS " + label);
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        var route=UnityEngine.Object.FindFirstObjectByType<ShuntaRouteBuilder>();
        Check(route!=null,"playable route"); route.Rebuild();
        var root=new GameObject("Skyline validation");
        try
        {
            var city=root.AddComponent<ShuntaNeoTokyo>(); city.Prepare(route);
            var seen=new bool[6];
            for(float km=.7f;km<route.Course.distanceKm;km+=1.6f)
            {
                city.PreviewAt(km);
                var counts=city.SkylineFamilyCounts;
                for(int i=0;i<6;i++) { Check(counts[i]>=0,"family accounting "+i); seen[i]|=counts[i]>0; }
                Check(city.ChunkCount<=15,"bounded chunks at "+km);
                Check(city.TriangleCount<1500000,"bounded triangles at "+km+": "+city.TriangleCount);
                Debug.Log("[shunta-skyline] DENSITY km="+km+" infill="+city.InfillBuildingCount+" buildings="+city.BuildingCount+" lastBuildMs="+city.LastBuildMs);
                Check(city.StreetlightCount>50,"streetlights preserved at "+km);
            }
            for(int i=0;i<6;i++) Check(seen[i],"distinct tower family "+i+" present");
            city.PreviewAt(2.6f);
            Check(city.InfillBuildingCount>120,"dense occupied showcase parcels: "+city.InfillBuildingCount);
            Check(city.BuildingCount>250,"continuous city fabric: "+city.BuildingCount+" buildings");
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                foreach(var v in mesh.vertices) CheckFinite(v);
                Check(mesh.bounds.size.y<400f,"finite skyline bounds");
            }
            Check(root.GetComponentsInChildren<Collider>(true).Length==0,"no new ride collision");
            Check(city.BuildingCount>12,"populated showcase");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("[shunta-skyline] ALL PASS");
    }
    static void CheckFinite(Vector3 v)
    { if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z)) throw new Exception("non-finite geometry"); }
    const string CaptureKey = "ChatGPT.ShuntaSkyline.Capture";
    const string ExitKey = "ChatGPT.ShuntaSkyline.ExitAfterPlay";
    static RideBootstrap boot;
    static int captureStage, captureFrames, captureIndex;
    static float previousDistance, captureStartDistance;
    static Vector3 previousPosition;
    static double deadline;
    static readonly float[] captureKm = { 2.6f,9.8f,25.8f,2.6f,2.6f };
    static ShuntaSkylineValidation() { EditorApplication.playModeStateChanged += Mode; }
    public static void Capture()
    {
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        SessionState.SetBool(ExitKey,false); SessionState.SetBool(CaptureKey,true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(mode==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ExitKey,false))
        {
            SessionState.SetBool(ExitKey,false); EditorApplication.Exit(SessionState.GetInt(ExitKey+"Code",1)); return;
        }
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
        Debug.Log("[shunta-skyline] LIVE " + (ok ? "PASS" : "FAIL"));
        if (Application.isBatchMode)
        {
            SessionState.SetInt(ExitKey+"Code",ok?0:1); SessionState.SetBool(ExitKey,true); EditorApplication.ExitPlaymode();
        }
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
            Debug.Log("[shunta-skyline] PASS uninterrupted live riding " + (boot.session.DistanceM-captureStartDistance).ToString("F2") + " m");
            var activeCity = ShuntaNeoTokyo.Instance;
            Check(activeCity.StreetlightCount > 50,"live streamed streetlights present");
            int enabledLamps = 0; float nearest = float.MaxValue;
            foreach (var light in activeCity.GetComponentsInChildren<Light>())
                if (light.name.StartsWith("Shunta streetlight") && light.enabled)
                { enabledLamps++; nearest = Mathf.Min(nearest,Vector3.Distance(light.transform.position,rider)); }
            Check(enabledLamps >= 4 && nearest < 35f,"streetlights illuminate the live rider: " + enabledLamps + " enabled, nearest " + nearest.ToString("F1") + " m");
            var camera = boot.rideCamera != null ? boot.rideCamera : Camera.main;
            var oldPosition=camera.transform.position; var oldRotation=camera.transform.rotation; float oldFov=camera.fieldOfView;
            if(captureIndex>=3)
            {
                var route=activeCity.route; var p=route.PositionAtKm(2.67f); var f=route.TangentAtKm(2.67f); f.y=0; f.Normalize();
                var right=new Vector3(f.z,0,-f.x);
                camera.transform.position=p+f*110f+right*260f+Vector3.up*25f;
                camera.transform.rotation=Quaternion.LookRotation(p+f*95f-right*112f+Vector3.up*75f-camera.transform.position);
                camera.fieldOfView=65f;
                var look=UnityEngine.Object.FindFirstObjectByType<ShuntaLookDriver>();
                look.follow=null; look.previewKm=captureIndex==3?.3f:9.8f; look.ApplyKm(look.previewKm);
                var sun=GameObject.Find("Shunta Sun/Moon");
                if(sun!=null && captureIndex==3)
                {
                    var light=sun.GetComponent<Light>(); light.color=new Color(1f,.93f,.84f); light.transform.rotation=Quaternion.LookRotation(p+f*95f-right*112f+Vector3.up*75f-camera.transform.position-Vector3.up*100f); light.shadows=LightShadows.None;
                    sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>().SetIntensity(15000f,UnityEngine.Rendering.LightUnit.Lux);
                }
            }
            var rt = new RenderTexture(1280,720,24,RenderTextureFormat.ARGBHalf); rt.Create();
            var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
            var prevTarget = camera.targetTexture; var prevActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
                Directory.CreateDirectory("docs/shunta_captures");
                string file = captureIndex>=3 ? "docs/shunta_captures/showcase_"+(captureIndex==3?"day":"night")+"_skyline.png" : "docs/shunta_captures/zone_"+activeCity.route.Course.ZoneAtKm(captureKm[captureIndex]).index+"_skyline.png";
                File.WriteAllBytes(file,image.EncodeToPNG()); Debug.Log("[shunta-skyline] CAPTURE "+file);
            }
            finally
            {
                camera.targetTexture = prevTarget; RenderTexture.active = prevActive;
                camera.transform.SetPositionAndRotation(oldPosition,oldRotation); camera.fieldOfView=oldFov;
                UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
            if (++captureIndex >= captureKm.Length) End(true); else captureStage = 1;
        }
        catch (Exception e) { Debug.LogException(e); End(false); }
    }
}






