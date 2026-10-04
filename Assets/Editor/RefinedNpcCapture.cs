using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>Real HDRP rendering of the six production cycling prefabs, including a pedal phase.</summary>
public static class RefinedNpcCapture
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string folder = "Assets/Kuro/NPC/ImageGenRefined/UnityCaptures"; Directory.CreateDirectory(folder);
        var volumeObject = new GameObject("Reference exposure");
        var volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 100;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
        var exposure = profile.Add<Exposure>(true); exposure.mode.value = ExposureMode.Fixed; exposure.fixedExposure.value = 0;
        var cameraObject = new GameObject("Reference camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = .02f; camera.farClipPlane = 20; camera.fieldOfView = 35;
        var hd = cameraObject.AddComponent<HDAdditionalCameraData>(); hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = new Color(.35f,.37f,.40f,1);
        var rt = new RenderTexture(800,1000,24,RenderTextureFormat.ARGBHalf); rt.Create();
        var readback = new Texture2D(800,1000,TextureFormat.RGB24,false);
        foreach (string name in new[] { "Akihiro", "Akane", "Shiori", "Shinobu", "Coral", "Hanakage" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/RefinedNpcCast/Riders/"+name+".prefab");
            if (prefab == null) throw new System.InvalidOperationException("Missing refined cycling prefab: "+name);
            var rider = Object.Instantiate(prefab); rider.transform.position = Vector3.zero;
            var rig = rider.GetComponent<CoralBikeRig>(); rig.ForceSolveOnce();
            camera.transform.position = new Vector3(1.7f,1.15f,2.2f); camera.transform.LookAt(new Vector3(0,.65f,0));
            for (int phase=0; phase<2; phase++)
            {
                if (phase==1) rig.AdvanceCrank(90);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0,0,800,1000),0,0); readback.Apply();
                RenderTexture.active = null; camera.targetTexture = null;
                File.WriteAllBytes(folder+"/"+name+"_phase"+phase+".png",readback.EncodeToPNG());
                Debug.Log("[refined-npcs] CAPTURE "+name+" phase "+phase);
            }
            Object.DestroyImmediate(rider);
        }
        Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(volumeObject); Object.DestroyImmediate(profile);
        Object.DestroyImmediate(readback); rt.Release(); Object.DestroyImmediate(rt);
        Debug.Log("[refined-npcs] SIX CYCLING PREFABS CAPTURED");
    }
}
