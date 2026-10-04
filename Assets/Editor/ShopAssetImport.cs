using UnityEditor;

/// <summary>
/// Maple Row shop data textures. KuroKitMask.png is a lookup (v2: G = shading, R/B/A = helmet /
/// jersey / bibs trim flags), not a picture: it must stay linear, uncompressed and point-sampled or the region bands
/// in KitComposite.shader bleed into each other.
/// </summary>
public sealed class ShopAssetImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.EndsWith("Resources/Shop/KuroKitMask.png")) return;
        var ti = (TextureImporter)assetImporter;
        ti.sRGBTexture = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.filterMode = UnityEngine.FilterMode.Point;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;   // v2: A = bibs trim flag
        ti.alphaIsTransparency = false;
        ti.isReadable = true;
    }
}
