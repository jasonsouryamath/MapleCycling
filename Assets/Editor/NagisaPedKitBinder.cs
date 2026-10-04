using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Binds the beach PedKit atlases (Assets/Characters/Pedestrians/Beach) into the pedestrian model
/// library's Kuro-rider set so Nagisa Bay pedestrians can wear them at run time (2026-10-02, Claude).
/// The library lives in Resources, so the serialized texture references ship with the build.
/// Run: NagisaPedKitBinder.Run
/// </summary>
public static class NagisaPedKitBinder
{
    const string Beach = "Assets/Characters/Pedestrians/Beach";

    public static void Run()
    {
        var lib = Resources.Load<PedestrianModelLibrary>(PedestrianModelLibrary.ResourcePath);
        if (lib == null) { Debug.LogError("[nagisa-kits] PedestrianModelLibrary missing"); return; }
        var kits = new List<Texture2D>();
        foreach (var f in Directory.GetFiles(Beach, "PedKit_*.png"))
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace("\\", "/"));
            if (t != null) kits.Add(t);
        }
        kits.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        lib.kuroRider.beachKits = kits.ToArray();
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log($"[nagisa-kits] bound {kits.Count} beach kits to the pedestrian library.");
    }
}
