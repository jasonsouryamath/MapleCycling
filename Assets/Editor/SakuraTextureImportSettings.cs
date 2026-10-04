using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the procedurally generated Sakura Pass texture set.
///
/// These matter more than they look: a normal or roughness map imported as sRGB is gamma
/// decoded on sampling, so the normals bend the wrong way and the roughness comes out far too
/// dark. The blossom/leaf atlases need alpha-is-transparency or the cutout edges fringe black
/// where the transparent pixels bleed in under mip filtering.
/// </summary>
public static class SakuraTextureImportSettings
{
    private const string TextureDir = "Assets/Environment/SakuraPass/Textures";
    private const string CoastTextureDir = "Assets/Environment/ShiosaiCoast/Textures";

    /// <summary>
    /// Every procedural texture folder in the project. Shiosai Coast authors its own coastal
    /// set (tools/blender/build_shiosai_textures.py) and needs exactly the same normal/rough
    /// linear-vs-sRGB handling; leaving it out imported Shiosai_*_Normal.png as sRGB colour.
    ///
    /// THE LIST IS DISCOVERED, NOT HARD-CODED, AND THAT IS A BUG FIX.
    ///
    /// This used to name Sakura and Shiosai only - but SIX region builders call ApplyAll() at
    /// the top of their Apply(), each one reasonably assuming it covers its own maps. It did
    /// not. AzoraHighlands, TakaMountains, FujiRidge, MapleCity and MinatoCoast were all
    /// importing their procedural ground textures on Unity's DEFAULTS, which means
    /// anisoLevel 1. Anisotropic filtering is precisely the thing that keeps a tiling ground
    /// texture from turning into a moire lattice when the camera looks along it at a grazing
    /// angle - and a grazing look down an open fell is the single most common shot in this
    /// game. Azora's QA finding of "visible ground-texture tiling" was in large part this:
    /// the regular dot grid across the fell was aliasing, not the tile boundary.
    ///
    /// Every region folder that exists is now included, so a new region cannot silently miss
    /// out by forgetting to add itself here.
    /// </summary>
    private static string[] Dirs()
    {
        var dirs = new List<string>();
        const string root = "Assets/Environment";
        if (!AssetDatabase.IsValidFolder(root)) return new[] { TextureDir };
        foreach (var region in AssetDatabase.GetSubFolders(root))
        {
            string tex = region + "/Textures";
            if (AssetDatabase.IsValidFolder(tex)) dirs.Add(tex);
        }
        if (dirs.Count == 0) dirs.Add(TextureDir);
        return dirs.ToArray();
    }

    [MenuItem("MapleRide/Environment/Configure Texture Importers", priority = 22)]
    public static void ApplyMenu()
    {
        int changed = ApplyAll();
        Debug.Log($"Sakura Pass: re-imported {changed} textures with corrected settings.");
    }

    /// <summary>Returns the number of textures whose importer settings actually changed.</summary>
    public static int ApplyAll()
    {
        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", Dirs()))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Configure(path)) changed++;
        }
        if (changed > 0) AssetDatabase.Refresh();
        return changed;
    }

    private static bool Configure(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return false;

        string name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

        bool isNormal = name.EndsWith("_normal");
        // HDRP Lit mask map: R=metallic, G=AO, B=detail, A=smoothness. It is DATA, so it must
        // import linear - imported as sRGB the smoothness channel is gamma-decoded and every
        // PBR surface comes out far glossier than authored.
        bool isMask = name.EndsWith("_mask");
        bool isData = isNormal || isMask
                      || name.EndsWith("_rough") || name.EndsWith("_roughness");
        bool isAtlas = name.Contains("atlas") || name.Contains("petal") || name.Contains("sprite");
        bool isGradient = name.Contains("gradient");

        var type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        bool srgb = !isData;
        // The ridge gradient is a 16x256 ramp: tiling or filtering across its edges would
        // bleed snow into rock, so it clamps.
        var wrap = (isAtlas || isGradient) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;

        bool dirty = importer.textureType != type
                     || importer.sRGBTexture != srgb
                     || importer.wrapMode != wrap
                     || importer.alphaIsTransparency != isAtlas
                     || (isMask && importer.alphaSource != TextureImporterAlphaSource.FromInput)
                     || importer.mipmapEnabled != !isGradient
                     || importer.anisoLevel != (isData || isAtlas ? 4 : 8)
                     || importer.filterMode != FilterMode.Bilinear;

        if (!dirty) return false;

        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.wrapMode = wrap;
        importer.alphaIsTransparency = isAtlas;
        importer.mipmapEnabled = !isGradient;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = (isData || isAtlas) ? 4 : 8;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        if (isNormal) importer.convertToNormalmap = false;
        // Smoothness lives in A; dropping the alpha channel would silently mirror-flip every
        // masked surface to fully rough.
        if (isMask) importer.alphaSource = TextureImporterAlphaSource.FromInput;

        importer.SaveAndReimport();
        return true;
    }
}
