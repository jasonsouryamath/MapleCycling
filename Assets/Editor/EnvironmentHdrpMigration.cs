using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot (but idempotent) repair pass that finishes the HDRP migration for the four
/// environment regions that were never covered by it.
///
/// BACKGROUND. The project moved to HDRP by porting every custom built-in shader to an
/// HDRP equivalent under Assets/Environment/Shared/Shaders/HDRP/. The port was verified
/// scene-by-scene, and the audit that signed it off (zz_hdrp_audit_BEFORE_report.txt) only
/// ever inspected renderers in the *currently loaded* scene. Sakura Pass, Shiosai Coast and
/// Minato Coast were loaded and got migrated. Fuji Ridge, Taka Mountains, Maple City and
/// Azora Highlands were not, so their material ASSETS still point at the dead built-in
/// shaders (MapleRide/SakuraCel etc.). Under HDRP those resolve to nothing and the regions
/// render as solid error-shader magenta.
///
/// The HDRP shaders are strict supersets of the built-in ones they replace (verified property
/// by property), so assigning the new shader preserves every authored value; the additional
/// HDRP-only properties fall back to their shader defaults. That makes this a lossless swap
/// rather than a re-authoring job.
/// </summary>
public static class EnvironmentHdrpMigration
{
    /// <summary>
    /// Regions whose material assets were missed by the original HDRP migration.
    /// Sakura Pass / Shiosai Coast / Minato Coast are deliberately absent: they are already
    /// migrated and re-running over them would be a no-op at best.
    /// </summary>
    private static readonly string[] Regions =
    {
        "Assets/Environment/FujiRidge",
        "Assets/Environment/TakaMountains",
        "Assets/Environment/MapleCity",
        "Assets/Environment/AzoraHighlands",
    };

    /// <summary>
    /// Built-in shader name -> HDRP port. Every pair was checked for property parity, so the
    /// swap keeps the authored look instead of resetting it.
    /// </summary>
    private static readonly Dictionary<string, string> ShaderPorts = new Dictionary<string, string>
    {
        { "MapleRide/SakuraCel",        "MapleRide/HDRP/CelLit"    },
        { "MapleRide/SakuraFoliage",    "MapleRide/HDRP/Foliage"   },
        { "MapleRide/SakuraTerrain",    "MapleRide/HDRP/Terrain"   },
        { "MapleRide/SakuraSky",        "MapleRide/HDRP/Sky"       },
        { "MapleRide/SakuraRoad",       "MapleRide/HDRP/Road"      },
        { "MapleRide/SakuraWater",      "MapleRide/HDRP/Ocean"     },
        { "MapleRide/MapleCityFacade",  "MapleRide/HDRP/CityFacade"},
    };

    /// <summary>
    /// Legacy unlit/particle shaders used by the regions' distant backdrop rings and VFX
    /// (chimney smoke, seed drift, lantern and window glows).
    ///
    /// DELIBERATELY NOT MIGRATED HERE. Unlike the lit shaders above these still *render*
    /// correctly under HDRP - the Fuji baseline capture shows the three backdrop ranges in
    /// their authored blue-grey tints while everything around them is error magenta. They are
    /// also NOT a lossless swap: HDRP/Unlit stores its tint in _UnlitColor/_UnlitColorMap
    /// whereas these store it in _Color/_MainTex, so a naive shader assignment would silently
    /// drop every authored colour and return three white domes.
    ///
    /// Moving these to MapleRide/HDRP/RidgeHaze (what Shiosai Coast uses for its headlands)
    /// is a genuine fidelity upgrade and is queued as part of each region's art milestone,
    /// where the tint transfer can be authored and checked against a render.
    /// </summary>
    private static readonly HashSet<string> RendersButNotHdrpNative = new HashSet<string>
    {
        "Particles/Standard Unlit",
        "Legacy Shaders/Particles/Alpha Blended Premultiply",
        "Sprites/Default",
        "Unlit/Color",
        "Unlit/Texture",
        "Unlit/Transparent",
    };

    [MenuItem("MapleRide/Environment/Migrate Region Materials To HDRP", priority = 20)]
    public static void Migrate() => Run(dryRun: false);

    [MenuItem("MapleRide/Environment/Audit Region Materials (HDRP)", priority = 21)]
    public static void Audit() => Run(dryRun: true);

    private static void Run(bool dryRun)
    {
        var log = new StringBuilder();
        var tag = dryRun ? "[hdrp-audit]" : "[hdrp-migrate]";

        // Resolve the HDRP shaders once and fail loudly if any is missing. Silently skipping a
        // missing target is how you end up with a "0 errors" pass over a still-magenta region.
        var resolved = new Dictionary<string, Shader>();
        foreach (var target in ShaderPorts.Values.Distinct())
        {
            var s = Shader.Find(target);
            if (s == null)
            {
                Debug.LogError($"{tag} ABORT - HDRP shader '{target}' not found. Nothing was changed.");
                return;
            }
            resolved[target] = s;
        }

        int scanned = 0, swapped = 0, alreadyOk = 0, unknown = 0, deferred = 0;
        var unknownNames = new SortedSet<string>();

        foreach (var region in Regions)
        {
            int regionSwapped = 0;
            var guids = AssetDatabase.FindAssets("t:Material", new[] { region });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                scanned++;

                var current = mat.shader.name;

                // Already on an HDRP shader (including a previous run of this pass) -> leave it.
                // This is what makes the pass idempotent.
                if (current.StartsWith("MapleRide/HDRP/") || current.StartsWith("HDRP/"))
                {
                    alreadyOk++;
                    continue;
                }

                string target = null;
                if (ShaderPorts.TryGetValue(current, out var port)) target = port;

                if (target == null)
                {
                    // Renders acceptably today; intentionally deferred to the region art pass.
                    if (RendersButNotHdrpNative.Contains(current)) { deferred++; continue; }
                    unknown++;
                    unknownNames.Add($"{current}  (e.g. {System.IO.Path.GetFileName(path)})");
                    continue;
                }

                log.AppendLine($"{tag}   {System.IO.Path.GetFileName(path),-34} {current}  ->  {target}");
                if (!dryRun)
                {
                    Undo.RecordObject(mat, "HDRP shader migration");
                    // Assigning .shader preserves every property the new shader also declares.
                    mat.shader = resolved[target];
                    EditorUtility.SetDirty(mat);
                }
                swapped++;
                regionSwapped++;
            }

            Debug.Log($"{tag} {region}: {regionSwapped} material(s) {(dryRun ? "would be" : "")} migrated.");
        }

        if (!dryRun)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (log.Length > 0) Debug.Log(log.ToString());
        foreach (var u in unknownNames)
            Debug.LogWarning($"{tag} UNMAPPED shader still in use: {u}");

        Debug.Log($"{tag} DONE scanned={scanned} swapped={swapped} alreadyHdrp={alreadyOk} deferredUnlit={deferred} unmapped={unknown}");
    }
}
