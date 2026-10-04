using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ShuntaMemoryCleanupValidation
{
    const string Key = "mapleride.shunta.cleanup.validation";
    static GameObject root;
    static int cycle, waitFrames;
    static bool passed;
    static double deadline;
    static readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
    static readonly List<UnityEngine.Object> sharedResources = new List<UnityEngine.Object>();

    static ShuntaMemoryCleanupValidation()
    {
        EditorApplication.playModeStateChanged += OnMode;
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnMode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            cycle = waitFrames = 0;
            resources.Clear();
            sharedResources.Clear();
            passed = false;
            deadline = EditorApplication.timeSinceStartup + 60;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Debug.Log("[shunta-cleanup] " + (passed ? "PASS" : "FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 2);
        }
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Cleanup timed out");
            if (root == null)
            {
                root = new GameObject("Shunta cleanup regression");
                root.SetActive(false);
                Verify<ShuntaLookDriver>(new[] { "Shunta Look (generated)", "Shunta Look Volume", "Shunta Rider Fill", "Shunta Sun/Moon" });
                Verify<ShuntaNightSky>(new[] { "Shunta Night Sky Dome" });
                Verify<ShuntaLandmarkSet>(new[] { LandmarkRootName() });
                waitFrames = 0;
                return;
            }
            if (++waitFrames < 3) return;
            foreach (Transform host in root.transform)
            {
                if (host.childCount != 1 || host.GetChild(0).name != "Keep me")
                    throw new Exception("Generated children survived cleanup or unrelated child was removed");
            }
            foreach (var resource in resources)
                if (resource != null) throw new Exception("Generated native resource survived cleanup");
            resources.Clear();
            foreach (var resource in sharedResources)
            {
                if (resource == null) throw new Exception("Shared resource was incorrectly destroyed");
                UnityEngine.Object.Destroy(resource);
            }
            sharedResources.Clear();
            UnityEngine.Object.Destroy(root);
            root = null;
            if (++cycle < 20) return;
            passed = true;
            Debug.Log("[shunta-cleanup] 20 cycles, 3 components, duplicate generated roots removed; unrelated children preserved");
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogError("[shunta-cleanup] " + e);
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
        }
    }

    static string LandmarkRootName()
    {
        return (string)typeof(ShuntaLandmarkSet).GetField("GenName", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
    }

    static void Verify<T>(string[] names) where T : MonoBehaviour
    {
        var host = new GameObject(typeof(T).Name);
        host.transform.SetParent(root.transform, false);
        var component = host.AddComponent<T>();
        new GameObject("Keep me").transform.SetParent(host.transform, false);
        foreach (string name in names)
            for (int i = 0; i < 2; i++)
                new GameObject(name).transform.SetParent(host.transform, false);
        if (typeof(T) == typeof(ShuntaLookDriver))
        {
            var generated = host.transform.Find(names[0]).gameObject;
            var mesh = new Mesh { hideFlags = HideFlags.DontSave };
            var texture = new Texture2D(4, 4) { hideFlags = HideFlags.DontSave };
            var material = new Material(Shader.Find("HDRP/Lit")) { hideFlags = HideFlags.DontSave };
            material.SetTexture("_BaseColorMap", texture);
            var sharedTexture = new Texture2D(4, 4);
            material.SetTexture("_MaskMap", sharedTexture);
            sharedResources.Add(sharedTexture);
            generated.AddComponent<MeshFilter>().sharedMesh = mesh;
            generated.AddComponent<MeshRenderer>().sharedMaterial = material;
            typeof(T).GetField("gen", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, generated);
            resources.Add(mesh); resources.Add(texture); resources.Add(material);
            var road = new Mesh { hideFlags = HideFlags.DontSave };
            typeof(T).GetField("roadMesh", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, road);
            resources.Add(road);
        }
        var cleanup = typeof(T).GetMethod("Teardown", BindingFlags.Instance | BindingFlags.NonPublic);
        // Run twice in the same frame to exercise pending destruction and idempotency.
        cleanup.Invoke(component, null);
        cleanup.Invoke(component, null);
    }
}
