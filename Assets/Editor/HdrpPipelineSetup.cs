using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// SHIOSAI COAST - HDRP migration, step 2: pipeline assets, quality tiers and wiring
/// (spec sections 4.2.3 - 4.2.8).
///
/// SCOPE FENCE: this pass creates and assigns pipeline assets and applies the HDRP Wizard's
/// project-level fixes. It converts NO MapleRide shader, material, light, volume, water or route,
/// and deletes no Built-in shader (spec 4.1). Existing Built-in custom shaders are EXPECTED to
/// fall back to the error shader after this pass; <see cref="ShiosaiMigrationAudit"/> catalogues
/// them as pending conversion rather than pretending they are fine.
///
/// Quality mapping (approved 2026-09-14) collapses Unity's six levels onto the spec's four
/// Windows presets WITHOUT deleting levels, so no existing quality index shifts and the
/// currently selected level keeps its identity:
///   Very Low + Low -> SC_HDRP_Low, Medium -> SC_HDRP_Medium,
///   High -> SC_HDRP_High, Very High + Ultra -> SC_HDRP_Ultra.
/// </summary>
public static class HdrpPipelineSetup
{
    private const string SettingsDir = "Assets/Settings/HDRP";

    /// <summary>PROVISIONAL tier tuning (spec section 18 targets are explicitly un-benchmarked).
    /// Values are coarse and exist to make the four presets genuinely different, not final.</summary>
    private static readonly (string tier, int shadowAtlas, bool ssr, bool ssao, bool volumetrics,
                             bool water, int levels)[] Tiers =
    {
        ("Low",    2048, false, true,  false, false, 0),
        ("Medium", 4096, true,  true,  true,  true,  1),
        ("High",   4096, true,  true,  true,  true,  2),
        ("Ultra",  8192, true,  true,  true,  true,  3),
    };

    /// <summary>Quality level name -> tier index above. Unmapped names fall back to Medium.</summary>
    private static int TierForQualityLevel(string name)
    {
        switch (name)
        {
            case "Very Low":
            case "Low":       return 0;
            case "Medium":    return 1;
            case "High":      return 2;
            case "Very High":
            case "Ultra":     return 3;
            default:          return 1;
        }
    }

    /// <summary>
    /// Idempotently ASSIGNS HDRP. MapleRide ships on HDRP by owner decision (2026-09-14), so
    /// this entry point may only ever validate and assign an SC_HDRP asset - it must never
    /// clear <c>defaultRenderPipeline</c>. A previous revision "fixed" magenta by reverting the
    /// project to Built-in here, which silently undid HDRP on every launch and made the real
    /// defect (unconverted materials) impossible to see. Converting materials is
    /// <see cref="ShiosaiShaderConversion"/>'s job, not this one's.
    ///
    /// Re-running is safe: assets are loaded from disk (never recreated), tier tuning is
    /// re-applied to the same values, and quality levels already pointing at the right asset
    /// are left alone.
    /// </summary>
    [MenuItem("MapleRide/Environment/Setup HDRP Pipeline")]
    public static void Run()
    {
        EnsureGlobalSettings();

        // Load the four tier assets that already live in the project. Missing assets are a hard
        // error rather than a silent recreate: recreating would discard authored tier tuning.
        var assets = new HDRenderPipelineAsset[Tiers.Length];
        for (int i = 0; i < Tiers.Length; i++)
        {
            string path = $"{SettingsDir}/SC_HDRP_{Tiers[i].tier}.asset";
            assets[i] = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
            if (assets[i] == null)
            {
                Debug.LogError($"[hdrp-setup] ABORT: missing pipeline asset {path}. " +
                               "Refusing to assign a partial HDRP configuration.");
                return;
            }
            ApplyTierSettings(assets[i], i);
            Debug.Log($"[hdrp-setup] loaded tier asset {path}");
        }

        ApplyWizardFixes();

        // Per-quality-level assignment. SetQualityLevel(i, false) makes level i current so the
        // renderPipeline setter writes to that level; the original selection is restored after.
        int selected = QualitySettings.GetQualityLevel();
        var names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            var want = assets[TierForQualityLevel(names[i])];
            if (!ReferenceEquals(QualitySettings.renderPipeline, want))
            {
                QualitySettings.renderPipeline = want;
                Debug.Log($"[hdrp-setup] quality '{names[i]}' -> {want.name}");
            }
            else Debug.Log($"[hdrp-setup] quality '{names[i]}' already {want.name}");
        }
        QualitySettings.SetQualityLevel(selected, false);

        // Project-wide default. This is the value that decides whether HDRP is actually the
        // active pipeline; a quality override alone is not enough.
        var fallback = assets[TierForQualityLevel(names.Length > selected ? names[selected] : "High")];
        if (!ReferenceEquals(GraphicsSettings.defaultRenderPipeline, fallback))
        {
            GraphicsSettings.defaultRenderPipeline = fallback;
            Debug.Log($"[hdrp-setup] GraphicsSettings.defaultRenderPipeline -> {fallback.name}");
        }
        else Debug.Log($"[hdrp-setup] GraphicsSettings.defaultRenderPipeline already {fallback.name}");

        // The API setters mark the settings objects dirty but batchmode can exit before Unity
        // flushes ProjectSettings/*.asset. Saving them explicitly is what makes the assignment
        // survive the next launch - the failure this whole pass exists to prevent.
        EditorUtility.SetDirty(GraphicsSettings.GetGraphicsSettings());
        EditorUtility.SetDirty(QualitySettings.GetQualitySettings());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var active = RenderPipelineManager.currentPipeline;
        Debug.Log($"[hdrp-setup] DONE defaultRP='{GraphicsSettings.defaultRenderPipeline?.name ?? "<null>"}' " +
                  $"activePipeline='{active?.GetType().Name ?? "<none yet>"}' colorSpace={PlayerSettings.colorSpace}");
    }

    /// <summary>Read-only assertion used after captures: proves HDRP is still the active
    /// pipeline rather than trusting that nothing reverted it.</summary>
    [MenuItem("MapleRide/Environment/Verify HDRP Active")]
    public static void Verify()
    {
        var def = GraphicsSettings.defaultRenderPipeline;
        Debug.Log($"[hdrp-verify] defaultRenderPipeline='{def?.name ?? "<null>"}' type={def?.GetType().Name ?? "-"}");

        int selected = QualitySettings.GetQualityLevel();
        var names = QualitySettings.names;
        int bad = 0;
        for (int i = 0; i < names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            var rp = QualitySettings.renderPipeline;
            bool ok = rp is HDRenderPipelineAsset;
            if (!ok) bad++;
            Debug.Log($"[hdrp-verify] quality '{names[i]}' -> {(rp != null ? rp.name : "<null>")} {(ok ? "OK" : "NOT-HDRP")}");
        }
        QualitySettings.SetQualityLevel(selected, false);

        bool hdrpDefault = def is HDRenderPipelineAsset;
        Debug.Log($"[hdrp-verify] RESULT hdrpDefault={hdrpDefault} nonHdrpQualityLevels={bad} " +
                  $"colorSpace={PlayerSettings.colorSpace} => {(hdrpDefault && bad == 0 ? "HDRP ACTIVE" : "HDRP NOT FULLY ACTIVE")}");
    }

    /// <summary>
    /// Tier differentiation through SerializedObject: HDRP exposes
    /// <c>currentPlatformRenderPipelineSettings</c> read-only to user code, and the serialized
    /// layout is the supported way to author presets from a script. Every property is probed by
    /// name and skipped (with a log) when the HDRP version does not have it, so a package upgrade
    /// degrades to "fewer differences", never to a silent wrong value.
    /// </summary>
    private static void ApplyTierSettings(HDRenderPipelineAsset asset, int tierIndex)
    {
        var t = Tiers[tierIndex];
        var so = new SerializedObject(asset);
        int applied = 0, missing = 0;

        void SetBool(string path, bool v)
        {
            var p = so.FindProperty(path);
            if (p == null) { missing++; Debug.Log($"[hdrp-setup]   (skip) {asset.name}: no '{path}'"); return; }
            p.boolValue = v; applied++;
        }
        void SetInt(string path, int v)
        {
            var p = so.FindProperty(path);
            if (p == null) { missing++; Debug.Log($"[hdrp-setup]   (skip) {asset.name}: no '{path}'"); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = v;
            else p.intValue = v;
            applied++;
        }

        const string S = "m_RenderPipelineSettings.";
        SetBool(S + "supportSSR", t.ssr);
        SetBool(S + "supportSSAO", t.ssao);
        SetBool(S + "supportVolumetrics", t.volumetrics);
        SetBool(S + "supportWater", t.water);
        SetBool(S + "supportMotionVectors", true);          // spec 4.2.7
        SetBool(S + "supportDecals", true);
        SetInt(S + "hdShadowInitParams.shadowAtlasConfiguration.shadowAtlasResolution", t.shadowAtlas);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        Debug.Log($"[hdrp-setup] {asset.name}: {applied} tier properties applied, {missing} not present in HDRP 17.4");
    }

    /// <summary>
    /// The project-level corrections the HDRP Wizard performs. Applied through public API so the
    /// result is reproducible headlessly; the Wizard window itself cannot run in batchmode.
    /// </summary>
    private static void ApplyWizardFixes()
    {
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("[hdrp-setup] FIX color space Gamma -> Linear (project-wide, approved)");
        }
        else Debug.Log("[hdrp-setup] color space already Linear");

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
        Debug.Log("[hdrp-setup] FIX Windows graphics APIs -> explicit Direct3D12, Direct3D11 (spec 4.2.6)");

        try
        {
            // PlayerSettings.SetLightmapEncodingQuality / NamedBuildTarget / the public
            // LightmapEncodingQuality enum are not reachable from this assembly on
            // 6000.4.11f1, so the call is reached reflectively and skipped cleanly if absent.
            var pst = typeof(PlayerSettings);
            var nbt = System.Type.GetType("UnityEditor.Build.NamedBuildTarget, UnityEditor");
            var leq = System.Type.GetType("UnityEditor.LightmapEncodingQuality, UnityEditor");
            var mi = nbt != null && leq != null
                ? pst.GetMethod("SetLightmapEncodingQuality",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Static,
                                null, new[] { nbt, leq }, null)
                : null;
            if (mi != null)
            {
                var standalone = nbt.GetProperty("Standalone",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                mi.Invoke(null, new[] { standalone, System.Enum.Parse(leq, "High") });
                Debug.Log("[hdrp-setup] FIX lightmap encoding -> High quality");
            }
            else Debug.Log("[hdrp-setup] lightmap encoding API unavailable on this Unity version; skipped");
        }
        catch (Exception e) { Debug.LogWarning("[hdrp-setup] lightmap encoding fix skipped: " + e.Message); }

        int selected = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
        }
        QualitySettings.SetQualityLevel(selected, false);
        Debug.Log("[hdrp-setup] FIX shadowmask mode -> DistanceShadowmask on all quality levels");
    }

    /// <summary>
    /// HDRP global settings must exist and should live under Assets/Settings/HDRP (spec 4.2.3).
    /// <c>HDRenderPipelineGlobalSettings.Ensure</c> is internal, so it is invoked reflectively and
    /// the outcome is logged either way - this is configuration, not silent magic.
    /// </summary>
    private static void EnsureGlobalSettings()
    {
        var type = typeof(HDRenderPipelineAsset).Assembly
                   .GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
        if (type == null) { Debug.LogWarning("[hdrp-setup] HDRenderPipelineGlobalSettings type not found"); return; }

        var ensure = type.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (ensure != null)
        {
            try
            {
                var ps = ensure.GetParameters();
                object[] args = ps.Select(p => p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType
                                    ? Activator.CreateInstance(p.ParameterType) : null)).ToArray();
                ensure.Invoke(null, args);
                Debug.Log("[hdrp-setup] HDRenderPipelineGlobalSettings.Ensure() invoked");
            }
            catch (Exception e) { Debug.LogWarning("[hdrp-setup] Ensure() failed: " + e.Message); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var guids = AssetDatabase.FindAssets("t:HDRenderPipelineGlobalSettings");
        if (guids.Length == 0) { Debug.LogWarning("[hdrp-setup] no global settings asset found"); return; }

        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (path.StartsWith(SettingsDir)) { Debug.Log($"[hdrp-setup] global settings at {path}"); continue; }
            string dest = $"{SettingsDir}/{Path.GetFileName(path)}";
            string err = AssetDatabase.MoveAsset(path, dest);
            Debug.Log(string.IsNullOrEmpty(err)
                ? $"[hdrp-setup] moved global settings {path} -> {dest}"
                : $"[hdrp-setup] could not move global settings ({err}); left at {path}");
        }
    }
}
