using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SHIOSAI COAST - HDRP migration, step 1: package + project settings (spec section 4.2).
///
/// Deliberately reflection-free where it can be, and deliberately split from the asset/quality
/// wiring (<c>HdrpPipelineSetup</c>) because installing the package forces a domain reload and a
/// full reimport: the two halves CANNOT run in one batchmode invocation.
///
/// Nothing here touches a MapleRide shader, material, light or route. Per spec 4.1 no Built-in
/// shader is deleted; they are expected to fall back to the error shader under HDRP and are
/// catalogued by <see cref="ShiosaiMigrationAudit"/> as pending conversion.
/// </summary>
public static class HdrpMigrationStep1
{
    private const string HdrpPackage = "com.unity.render-pipelines.high-definition";

    /// <summary>Adds HDRP at the version Package Manager considers compatible with this editor.</summary>
    public static void InstallHdrpPackage()
    {
        var installed = Client.List(offlineMode: false, includeIndirectDependencies: false);
        while (!installed.IsCompleted) System.Threading.Thread.Sleep(200);
        if (installed.Status == StatusCode.Success &&
            installed.Result.Any(p => p.name == HdrpPackage))
        {
            var p = installed.Result.First(x => x.name == HdrpPackage);
            Debug.Log($"[hdrp-setup] HDRP already installed: {p.name}@{p.version}");
            return;
        }

        Debug.Log("[hdrp-setup] requesting " + HdrpPackage + " ...");
        AddRequest req = Client.Add(HdrpPackage);
        var started = DateTime.UtcNow;
        while (!req.IsCompleted)
        {
            System.Threading.Thread.Sleep(500);
            if ((DateTime.UtcNow - started).TotalMinutes > 20)
            {
                Debug.LogError("[hdrp-setup] FAILED timeout waiting for package add.");
                return;
            }
        }

        if (req.Status == StatusCode.Success)
            Debug.Log($"[hdrp-setup] installed {req.Result.name}@{req.Result.version} " +
                      $"({(DateTime.UtcNow - started).TotalSeconds:0}s)");
        else
            Debug.LogError($"[hdrp-setup] FAILED package add: {req.Error?.message}");
    }

    /// <summary>
    /// Linear color space and the deliberate Windows API order (spec 4.2.6 / 4.2.7).
    /// Linear is project-wide and affects every region, which is why the pre-HDRP checkpoint and
    /// the baseline render set exist. Approved 2026-09-14.
    /// </summary>
    public static void ApplyProjectSettings()
    {
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("[hdrp-setup] color space -> Linear (was Gamma)");
        }
        else Debug.Log("[hdrp-setup] color space already Linear");

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
        Debug.Log("[hdrp-setup] Windows graphics APIs -> explicit Direct3D12, Direct3D11");

        AssetDatabase.SaveAssets();
        Debug.Log("[hdrp-setup] project settings written");
    }

    /// <summary>Package + settings in one run, for the first (reimport-heavy) invocation.</summary>
    public static void InstallAndConfigure()
    {
        InstallHdrpPackage();
        ApplyProjectSettings();
    }
}
