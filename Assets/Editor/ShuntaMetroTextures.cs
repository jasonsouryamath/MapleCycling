using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for Assets/Textures/ShuntaMetro/* (Sky/ is owned by someone else and skipped) and the
/// Resources/ShuntaMetro/ShuntaTextureSet.asset table. Naming drives the settings:
///   *_Albedo / *_Emission: sRGB.  *_Normal: NormalMap.  *_Mask / *_Detail: linear (HDRP mask / detail packing).
/// Run: ShuntaMetroTextures.Import (forces a reimport with these settings and rebuilds the set).
/// Generators: tools/textures/ShuntaMetro/make_shunta_*.py
/// </summary>
public class ShuntaMetroTextures : AssetPostprocessor
{
    const string Root = "Assets/Textures/ShuntaMetro/";
    const string SetPath = "Assets/Resources/ShuntaMetro/ShuntaTextureSet.asset";

    static bool Mine(string path) => path.Replace('\\', '/').StartsWith(Root) && !path.Replace('\\', '/').StartsWith(Root + "Sky/");

    void OnPreprocessTexture()
    {
        if (!Mine(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string n = Path.GetFileNameWithoutExtension(assetPath);
        bool normal = n.EndsWith("_Normal");
        bool linear = n.EndsWith("_Mask") || n.EndsWith("_Detail");
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (!normal) ti.sRGBTexture = !linear;
        ti.mipmapEnabled = true;
        ti.streamingMipmaps = true;
        ti.anisoLevel = 8;
        ti.filterMode = FilterMode.Trilinear;
        ti.wrapMode = n.StartsWith("Sign_Atlas") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.isReadable = false;
        ti.maxTextureSize = 4096;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.compressionQuality = 50;
        ti.crunchedCompression = false;
        ti.alphaSource = (n == "Road_Crosswalk_Albedo" || n.EndsWith("_Mask")) ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = n == "Road_Crosswalk_Albedo";
        if (n == "Road_Crosswalk_Albedo" || n.StartsWith("Sign_Atlas")) ti.mipMapsPreserveCoverage = n == "Road_Crosswalk_Albedo";
    }

    public static void Import()
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures/ShuntaMetro" });
        var paths = new List<string>();
        foreach (var g in guids) { var p = AssetDatabase.GUIDToAssetPath(g); if (Mine(p)) paths.Add(p); }
        paths.Sort();
        AssetDatabase.StartAssetEditing();
        try { foreach (var p in paths) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
        finally { AssetDatabase.StopAssetEditing(); }

        Directory.CreateDirectory("Assets/Resources/ShuntaMetro");
        var set = AssetDatabase.LoadAssetAtPath<ShuntaTextureSet>(SetPath);
        if (set == null) { set = ScriptableObject.CreateInstance<ShuntaTextureSet>(); AssetDatabase.CreateAsset(set, SetPath); }
        set.entries.Clear();
        int missing = 0;
        foreach (var p in paths)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t == null) { missing++; continue; }
            set.entries.Add(new ShuntaTextureSet.Entry { name = Path.GetFileNameWithoutExtension(p), tex = t });
        }
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[shunta-tex] imported {paths.Count} textures ({missing} failed), set entries {set.entries.Count}");
    }
}
