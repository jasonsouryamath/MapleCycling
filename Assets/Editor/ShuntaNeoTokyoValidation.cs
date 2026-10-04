using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Checks generated city chunks and captures the actual Shunta ride camera after route load.</summary>
[InitializeOnLoad]
public static class ShuntaNeoTokyoValidation
{
    const string Key = "ChatGPT.ShuntaNeoTokyo.Capture";
    const string Output = @"C:\Users\jason\OneDrive\Desktop\MapleRide\docs\shunta_captures";
    static readonly float[] Locations = { .85f, 2.6f, 9.4f, 14.7f, 16.5f, 25.8f };
    static RideBootstrap boot;
    static int index, frames, lastFrame;
    static double deadline;
    static bool failed;
    static bool capturingAfter;
    static RenderTexture captureTarget, previousTarget;
    sealed class CanvasState { public Canvas canvas; public Camera camera; public float distance; }
    static readonly List<CanvasState> overlays = new List<CanvasState>();

    static ShuntaNeoTokyoValidation() { EditorApplication.playModeStateChanged += Mode; }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[neo-tokyo] " + message);
        Debug.Log("[neo-tokyo] PASS " + message);
    }

    public static void Run()
    {
        var importer = AssetImporter.GetAtPath("Assets/Resources/ShuntaMetro/City/NeoTokyoSigns.png") as TextureImporter;
        Require(importer != null, "sign atlas imports");
        importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = true; importer.sRGBTexture = true; importer.anisoLevel = 8;
        importer.filterMode = FilterMode.Trilinear; importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        var route = UnityEngine.Object.FindFirstObjectByType<ShuntaRouteBuilder>();
        Require(route != null, "playable route exists");
        GameObject go = new GameObject("Neo Tokyo validation");
        try
        {
            var city = go.AddComponent<ShuntaNeoTokyo>(); city.Prepare(route);
            Require(Mathf.Abs(ShuntaNeoTokyo.DistanceSquaredToRoadSegment(new Vector3(10,0,1),Vector3.zero,new Vector3(20,0,0))-1f)<.0001f, "road clearance detects between-sample intrusion");
            Require(city.Ready, "city materials + route ready");
            city.PreviewAt(.85f);
            Require(city.ChunkCount <= 11 && city.BuildingCount > 40 && city.SignCount > 30, "urban chunk content " + city.BuildingCount + " buildings / " + city.SignCount + " signs");
            int firstBuildings = city.BuildingCount, firstTriangles = city.TriangleCount;
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
                Require(renderer.sharedMaterial != null && renderer.sharedMaterial.shader.isSupported, "supported " + renderer.sharedMaterial.name);
            Require(go.GetComponentsInChildren<Collider>(true).Length == 0, "no gameplay colliders");
            city.PreviewAt(14.7f);
            Require(city.ChunkCount <= 15, "bounded resident chunks after long seek");
            city.Prepare(route); city.PreviewAt(.85f);
            Require(city.BuildingCount == firstBuildings && city.TriangleCount == firstTriangles, "deterministic rebuild and retired counts");
            Debug.Log("[neo-tokyo] street preview " + city.BuildingCount + " buildings / " + city.SignCount + " signs / " + city.DetailCount + " detail parts / " + city.TriangleCount + " triangles; last block " + city.LastBuildMs.ToString("F2") + " ms CPU build");
            Debug.Log("[neo-tokyo] ALL PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    public static void Capture()
    {
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (mode == PlayModeStateChange.EnteredPlayMode)
        {
            index = frames = 0; lastFrame = -1; boot = null; failed = false; capturingAfter = false;
            deadline = EditorApplication.timeSinceStartup + 600;
            captureTarget = null; overlays.Clear();
            EditorApplication.update += Tick;
        }
        else if (mode == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            Debug.Log(failed ? "[neo-tokyo] CAPTURE FAIL" : "[neo-tokyo] GAMEPLAY CAPTURE PASS");
            if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        }
    }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("city gameplay capture timeout");
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (boot == null)
            {
                boot = UnityEngine.Object.FindFirstObjectByType<RideBootstrap>();
                if (boot == null) return;
                boot.Resolve();
                Require(boot.session != null && boot.regions != null && boot.rideCamera != null, "gameplay rig resolves");
                Require(boot.regions.FastTravel(RegionCatalog.ShuntaMetro), "Shunta fast travel loads");
                boot.devices.HoldZeroPower = true; boot.devices.acceptKeyboardEffort = false;
                boot.session.ExternalControl = true;
                previousTarget = boot.rideCamera.targetTexture;
                captureTarget = new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
                captureTarget.Create(); boot.rideCamera.targetTexture = captureTarget;
            }
            var city = ShuntaNeoTokyo.Instance;
            if (city == null || !city.Ready) return;
            if (frames == 0 && !capturingAfter) {city.gameObject.SetActive(false);ShuntaActorLighting.CorrectionsEnabled=false;}
            float km = Locations[index];
            boot.session.SeekTo(km / city.route.Course.distanceKm * boot.session.Course.Length);
            boot.follower.Apply();
            var camera = boot.rideCamera;
            var follow = camera.GetComponent<KuroFollowCamera>();
            if (follow != null && follow.target != null)
            {
                camera.transform.position = follow.target.position + follow.target.TransformDirection(follow.offset);
                camera.transform.LookAt(follow.target.position + Vector3.up * 1.1f);
            }
            // Draw overlays through the live target camera without changing the HUD's content.
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    overlays.Add(new CanvasState {canvas=canvas,camera=canvas.worldCamera,distance=canvas.planeDistance});
                    canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1f;
                }
            if (++frames < 150) return;
            Directory.CreateDirectory(Output);
            if (!capturingAfter)
            {
                Save(Path.Combine(Output,"neo_before_"+index+".png"));
                city.gameObject.SetActive(true); ShuntaActorLighting.CorrectionsEnabled=true;capturingAfter=true; frames=0;
                return;
            }
            Require(city.ChunkCount > 0, "chunks live at " + km + " km");
            Require(ShuntaActorLighting.Instance!=null && ShuntaActorLighting.Instance.CorrectedRenderers>0,"character correction bound in live camera");
            var race=ShuntaRaceDirector.Instance;
            Require(race!=null && race.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length>=ShuntaRaceDirector.AiCount,"AI field reuses skinned rider models");
            Save(Path.Combine(Output, "neo_gameplay_" + index + ".png"));
            Debug.Log("[neo-tokyo] km=" + km + " resident=" + city.ChunkCount + " buildings=" + city.BuildingCount + " signs=" + city.SignCount + " triangles=" + city.TriangleCount + " lastBlockBuildMs=" + city.LastBuildMs.ToString("F2") + " frameMs=" + (Time.unscaledDeltaTime*1000f).ToString("F2"));
            frames = 0; capturingAfter=false; index++;
            if (index == Locations.Length) Finish();
        }
        catch (Exception e) { failed = true; Debug.LogError("[neo-tokyo] " + e); Finish(); }
    }
    static void Finish()
    {
        EditorApplication.update -= Tick;
        foreach(var state in overlays) if(state.canvas!=null)
        {state.canvas.renderMode=RenderMode.ScreenSpaceOverlay;state.canvas.worldCamera=state.camera;state.canvas.planeDistance=state.distance;}
        overlays.Clear();
        ShuntaActorLighting.CorrectionsEnabled=true;
        if(ShuntaNeoTokyo.Instance!=null) ShuntaNeoTokyo.Instance.gameObject.SetActive(true);
        if(boot!=null && boot.rideCamera!=null) boot.rideCamera.targetTexture=previousTarget;
        if(captureTarget!=null) {captureTarget.Release();UnityEngine.Object.Destroy(captureTarget);captureTarget=null;}
        EditorApplication.ExitPlaymode();
    }

    static void Save(string path)
    {
        var active = RenderTexture.active;
        var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active = captureTarget;
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = active; UnityEngine.Object.Destroy(image);
        }
    }
}
