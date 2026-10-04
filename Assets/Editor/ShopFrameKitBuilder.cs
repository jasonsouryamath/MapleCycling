using System.IO;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine;

/// <summary>
/// Builds the Maple Row frameset prefabs the shop loads at runtime (2026-09-26):
///   Assets/Shop/Frames/FrameKit_&lt;id&gt;.glb   (design_assets/3d/kuro/build_shop_frames.py)
///   Assets/Shop/Frames/Textures/Frame_&lt;id&gt;_{BaseColor,Mask}.png   (paint_shop_frames.py)
///   -> Assets/Resources/Shop/Frames/FrameKit_&lt;id&gt;.prefab + Frame_&lt;id&gt;_Paint.mat
/// The paint is HDRP/Lit with a CLEAR COAT (coat mask 1) over the baked albedo and mask map
/// (R metallic, G occlusion, A smoothness): gloss paint, metal flake, chrome lettering and raw
/// carbon all come from the maps. Also sets the import settings of the textured-garment design
/// atlases (Resources/Shop/Kits). Idempotent; materials are overwritten in place (GUIDs kept).
///
///   run_steps.ps1 "ShopFrameKitBuilder.Build|copilot_shop_frames.log|1"
/// </summary>
public static class ShopFrameKitBuilder
{
    const string SrcDir = "Assets/Shop/Frames";
    const string TexDir = SrcDir + "/Textures";
    const string OutDir = "Assets/Resources/Shop/Frames";
    const string KitDir = "Assets/Resources/Shop/Kits";

    [MenuItem("MapleRide/Shop/Build Frame Kits")]
    public static void Build()
    {
        Directory.CreateDirectory(OutDir);
        AssetDatabase.Refresh();
        int kits = 0;
        if (Directory.Exists(KitDir))
            foreach (var png in Directory.GetFiles(KitDir, "Kit_*.png"))
                if (Texture(png.Replace('\\', '/'), srgb: true)) kits++;
        Debug.Log($"[shop-frames] {kits} garment design atlases imported (sRGB, 2048)");

        int built = 0;
        foreach (var glb in Directory.GetFiles(SrcDir, "FrameKit_*.glb"))
        {
            string path = glb.Replace('\\', '/');
            string id = Path.GetFileNameWithoutExtension(path).Substring("FrameKit_".Length);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) { Debug.LogError($"[shop-frames] {path} did not import"); continue; }
            string basePath = $"{TexDir}/Frame_{id}_BaseColor.png", maskPath = $"{TexDir}/Frame_{id}_Mask.png";
            Texture(basePath, srgb: true);
            Texture(maskPath, srgb: false);
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
            var maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            if (baseTex == null || maskTex == null) { Debug.LogError($"[shop-frames] {id}: textures missing"); continue; }

            var mat = PaintMaterial($"{OutDir}/Frame_{id}_Paint.mat", baseTex, maskTex);
            var inst = (GameObject)Object.Instantiate(src);
            inst.name = $"FrameKit_{id}";
            int renderers = 0, tris = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderers++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }
            bool ok = GearVisuals.FindDeep(inst.transform, "Frame_Main") != null &&
                      GearVisuals.FindDeep(inst.transform, "SteerRef") != null &&
                      GearVisuals.FindDeep(inst.transform, "Frame_Fork") != null;
            PrefabUtility.SaveAsPrefabAsset(inst, $"{OutDir}/FrameKit_{id}.prefab");
            Object.DestroyImmediate(inst);
            Debug.Log($"[shop-frames] {id}: prefab {(ok ? "OK" : "MISSING Frame_Main/SteerRef/Frame_Fork")}, {renderers} renderers, {tris} tris");
            if (ok) built++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[shop-frames] built {built} frame kits into {OutDir}");
    }

    static bool Texture(string path, bool srgb)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return false;
        bool dirty = ti.sRGBTexture != srgb || ti.maxTextureSize != 2048 || !ti.mipmapEnabled || ti.anisoLevel != 8 ||
                     ti.textureCompression != TextureImporterCompression.CompressedHQ;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = srgb;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.maxTextureSize = 2048;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 8;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        if (dirty) ti.SaveAndReimport();
        return true;
    }

    static Material PaintMaterial(string path, Texture2D baseTex, Texture2D mask)
    {
        var shader = Shader.Find("HDRP/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;
        mat.SetFloat("_MaterialID", 1f);                 // standard
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BaseColorMap", baseTex);
        mat.SetTexture("_MaskMap", mask);
        mat.SetFloat("_Metallic", 1f);
        mat.SetFloat("_MetallicRemapMin", 0f);
        mat.SetFloat("_MetallicRemapMax", 1f);
        mat.SetFloat("_SmoothnessRemapMin", 0f);
        mat.SetFloat("_SmoothnessRemapMax", 1f);
        mat.SetFloat("_AORemapMin", 0f);
        mat.SetFloat("_AORemapMax", 1f);
        mat.SetFloat("_CoatMask", 1f);                   // clear coat over paint, flake and carbon
        mat.SetFloat("_SpecularOcclusionMode", 1f);
        mat.enableInstancing = true;
        HDMaterial.ValidateMaterial(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
