using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// SHIOSAI COAST - HDRP migration gate.
///
/// The rebuild spec (SC/SHIOSAI_COAST_HDRP_REBUILD_SPEC.md, section 23) asks for automated
/// validation that the pipeline is configured, that no Built-in-only / pink shader survives into
/// production Shiosai content, that the route contract still holds, and that the eight reference
/// PNGs are never modified. This tool is that gate. It is written to run BEFORE HDRP exists so
/// the pre-migration state is recorded as evidence, and to keep answering the same questions
/// after HDRP is installed - the answers change, the questions do not.
///
/// It never modifies scenes or materials; it reads, reports and (once) publishes the immutable
/// reference-image manifest. Run headless with:
///   Unity.exe -batchmode -quit -projectPath &lt;abs&gt; -executeMethod ShiosaiMigrationAudit.Run
/// </summary>
public static class ShiosaiMigrationAudit
{
    private static string ScenePath =>
        System.IO.File.Exists(ShiosaiSceneBuilder.ShiosaiScenePath)
            ? ShiosaiSceneBuilder.ShiosaiScenePath          // spec 6.1 dedicated Shiosai scene
            : "Assets/Scenes/SakuraPass.unity";
    private const string RegionDir   = "Assets/Environment/ShiosaiCoast";
    private const string SakuraDir   = "Assets/Environment/SakuraPass";
    private const string ReferenceDir = RegionDir + "/SC";
    private const string DataDir     = RegionDir + "/Data";
    private const string ManifestPath = DataDir + "/sc_reference_manifest.json";
    private const string ReportName  = "sc_migration_audit.md";

    // ------------------------------------------------------------------ provisional contract
    // PROVISIONAL (spec section 5): the production target. The live mock is still 2.892 km, so
    // the route gate reports PENDING rather than FAIL until the 42 km centreline is authored.
    private const float TargetRouteMetres = 20000f;
    private const float RouteTolerance    = 0.02f;   // +/- 2%, spec section 23

    // Validated alignment contracts that the rebuild must not lose (spec section 3).
    private const float ContractRoadHalfWidth = 3.5f;    // 7 m carriageway
    private const float ContractShoulder      = 0.55f;
    private const float ContractCrown         = 0.06f;
    private const float ContractLaneOffset    = 1.72f;

    /// <summary>Required chapter anchors, spec section 5.5.</summary>
    private static readonly string[] RequiredAnchors =
    {
        "SC_KM_000_Start", "SC_KM_060_GatewaySummit", "SC_KM_110_SwitchbackExit",
        "SC_KM_140_ShrineOverlook", "SC_KM_155_TunnelEntry", "SC_KM_160_TunnelExit",
        "SC_KM_180_VillageCenter", "SC_KM_200_BridgeApproach", "SC_KM_215_BridgeMidpoint",
        "SC_KM_230_BridgeExit", "SC_KM_255_LighthouseClimbStart", "SC_KM_275_LighthouseSummit",
        "SC_KM_280_FarSideReconnect", "SC_KM_310_SeaArch", "SC_KM_340_HighwayStart",
        "SC_KM_400_IslandReveal", "SC_KM_420_OverlookFinish",
    };

    /// <summary>Built-in shaders Shiosai/shared content depends on, and their HDRP successors
    /// (spec section 4.4). Used to report migration progress, never to delete anything.</summary>
    private static readonly (string builtin, string hdrp)[] ShaderMatrix =
    {
        ("MapleRide/ShiosaiArchitecture", "MapleRide/HDRP/ShiosaiArchitecture"),
        ("MapleRide/ShiosaiShallows",     "MapleRide/HDRP/ShiosaiShallows"),
        ("MapleRide/SakuraCel",           "MapleRide/HDRP/CelLit"),
        ("MapleRide/SakuraWater",         "MapleRide/HDRP/Ocean"),
        ("MapleRide/SakuraTerrain",       "MapleRide/HDRP/Terrain"),
        ("Standard",                      "HDRP/Lit"),
        ("Particles/Standard Unlit",      "HDRP/Unlit (particle)"),
        ("Unlit/Texture",                 "HDRP/Unlit"),
    };

    private static readonly List<string> Lines = new List<string>();
    private static int _fail, _warn, _pending;

    [MenuItem("MapleRide/Environment/Audit Shiosai HDRP Migration (open coast scene)", priority = 33)]
    public static void RunWithScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // read-only: never saved
        Run();
    }

    [MenuItem("MapleRide/Environment/Audit Shiosai HDRP Migration", priority = 32)]
    public static void Run()
    {
        Lines.Clear(); _fail = _warn = _pending = 0;

        Head($"Shiosai Coast HDRP migration audit - {DateTime.Now:yyyy-MM-dd HH:mm}");
        AuditPipeline();
        AuditShaders();
        AuditRoute();
        AuditReferences();

        string root = MapleRidePaths.RepoRoot;
        string outDir = MapleRidePaths.Renders;
        Directory.CreateDirectory(outDir);
        string path = Path.Combine(outDir, ReportName);
        File.WriteAllText(path, string.Join(Environment.NewLine, Lines) + Environment.NewLine);

        Debug.Log($"[sc-audit] report -> {path}");
        Debug.Log($"[sc-audit] SUMMARY fail={_fail} warn={_warn} pending={_pending}");
    }

    // ----------------------------------------------------------------------------- pipeline

    private static void AuditPipeline()
    {
        Head("## 1. Render pipeline state (spec 4.2)");

        string manifest = File.ReadAllText(Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "Packages", "manifest.json"));
        bool hdrpPkg = manifest.Contains("com.unity.render-pipelines.high-definition");
        bool urpPkg  = manifest.Contains("com.unity.render-pipelines.universal");

        Check("HDRP package present in manifest.json", hdrpPkg, Level.Pending,
              hdrpPkg ? "installed" : "not installed - Built-in renderer active");
        Check("URP package absent", !urpPkg, Level.Fail, urpPkg ? "URP present" : "absent");

        var defaultRp = GraphicsSettings.defaultRenderPipeline;
        Check("Default render pipeline asset assigned", defaultRp != null, Level.Pending,
              defaultRp != null ? defaultRp.name : "none (Built-in)");

        int tiers = 0;
        for (int i = 0; i < QualitySettings.names.Length; i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i) != null) tiers++;
        Check("Quality tiers with a pipeline override", tiers >= 4, Level.Pending,
              $"{tiers}/{QualitySettings.names.Length} levels ({string.Join(", ", QualitySettings.names)})");

        bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
        Check("Linear color space (HDRP requirement, spec 4.2.7)", linear, Level.Pending,
              PlayerSettings.colorSpace.ToString());

        var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
        bool dx12 = apis.Length > 0 && apis[0] == GraphicsDeviceType.Direct3D12;
        Check("Windows graphics API order starts at DirectX 12 (spec 4.2.6)", dx12, Level.Pending,
              (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64)
                  ? "auto: " : "explicit: ") + string.Join(", ", apis.Select(a => a.ToString())));
    }

    // ------------------------------------------------------------------------------ shaders

    private enum Level { Fail, Warn, Pending }

    private static void AuditShaders()
    {
        Head("## 2. Shader / material audit (spec 4.4, 23)");

        var guids = AssetDatabase.FindAssets("t:Material", new[] { RegionDir, SakuraDir });
        var byShader = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var broken = new List<string>();

        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            string name = m.shader == null ? "<null shader>" : m.shader.name;
            if (m.shader == null || name.Contains("InternalErrorShader")) broken.Add(p + " -> " + name);
            byShader[name] = byShader.TryGetValue(name, out int c) ? c + 1 : 1;
        }

        Line($"Scanned {guids.Length} materials under {RegionDir} and {SakuraDir}.");
        Line("");
        Line("| Shader | Materials |");
        Line("|---|---:|");
        foreach (var kv in byShader) Line($"| `{kv.Key}` | {kv.Value} |");
        Line("");

        Check("No pink / missing-shader material in Shiosai + Sakura asset folders", broken.Count == 0,
              Level.Fail, broken.Count == 0 ? "none" : string.Join("; ", broken.Take(10)));

        // ---------------------------------------------------- post-HDRP compatibility census
        // A Built-in shader does NOT go null or fail to compile under HDRP: it resolves and
        // compiles while rendering magenta, because HDRP never runs its passes. ShaderHasError
        // therefore reports a clean bill of health on a frame that is entirely pink - the exact
        // green-metric trap this project has been burned by before. The honest test is the
        // subshader "RenderPipeline" tag: anything that does not declare HDRenderPipeline is
        // incompatible with the active pipeline and will render magenta.
        bool hdrpActive = GraphicsSettings.defaultRenderPipeline != null &&
                          GraphicsSettings.defaultRenderPipeline.GetType().Name.Contains("HDRenderPipelineAsset");
        var errored = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (hdrpActive)
        {
            foreach (var kv in byShader)
            {
                if (kv.Key == "<null shader>") continue;
                var sh = Shader.Find(kv.Key);
                if (sh == null) { errored[kv.Key] = kv.Value; continue; }
                bool compatible = false;
                int subshaders = Mathf.Min(8, sh.subshaderCount);
                for (int si = 0; si < subshaders && !compatible; si++)
                {
                    var tag = sh.FindSubshaderTagValue(si, new ShaderTagId("RenderPipeline"));
                    if (!string.IsNullOrEmpty(tag.name) && tag.name.Contains("HDRenderPipeline")) compatible = true;
                }
                if (!compatible || ShaderUtil.ShaderHasError(sh)) errored[kv.Key] = kv.Value;
            }
        }

        Head("### 2.2 Pipeline-compatibility census (pink-material truth table)");
        if (!hdrpActive)
        {
            Line("- HDRP is not the active pipeline; compatibility census not applicable.");
        }
        else if (errored.Count == 0)
        {
            Line("- Every shader used by Shiosai and Sakura materials declares HDRenderPipeline.");
        }
        else
        {
            Line("| Shader | Materials | Status |");
            Line("|---|---:|---|");
            int unexpected = 0;
            foreach (var kv in errored)
            {
                bool expected = ShaderMatrix.Any(m => m.builtin == kv.Key) ||
                                kv.Key.StartsWith("MapleRide/") || kv.Key.StartsWith("Hidden/MapleRide/") ||
                                kv.Key.StartsWith("Legacy Shaders/") || kv.Key.StartsWith("Particles/") ||
                                kv.Key == "Standard" || kv.Key.StartsWith("Unlit/") || kv.Key.StartsWith("Skybox/") ||
                                // Spec-gap finding, catalogued 2026-09-14: the glTF importer's own
                                // Built-in shader is not in the section 4.4 matrix, yet it backs every
                                // imported rider, bike and prop material. It needs an HDRP Lit
                                // remap in the same milestone as the MapleRide shaders.
                                kv.Key.StartsWith("glTF/");
                if (!expected) unexpected++;
                string note = kv.Key.StartsWith("glTF/")
                    ? "expected - pending HDRP conversion (NOT in spec 4.4 matrix; catalogued spec gap)"
                    : "expected - pending HDRP conversion (spec 4.4)";
                Line($"| `{kv.Key}` | {kv.Value} | {(expected ? note : "UNEXPECTED - investigate")} |");
            }
            Line("");
            Check("Every HDRP-incompatible shader is catalogued as expected-pending-conversion",
                  unexpected == 0, Level.Fail,
                  unexpected == 0 ? $"{errored.Count} shader(s), all expected" : $"{unexpected} unexpected");
            Check("All Shiosai/Sakura shaders compatible with the active pipeline", false, Level.Pending,
                  $"{errored.Count} shader(s) pending conversion, {errored.Values.Sum()} materials affected - " +
                  "scene renders magenta until spec 4.4 lands");
        }

        Head("### 2.1 Migration matrix progress");
        Line("| Built-in dependency | In use | HDRP replacement | Replacement exists |");
        Line("|---|---:|---|---|");
        foreach (var (builtin, hdrp) in ShaderMatrix)
        {
            int used = byShader.TryGetValue(builtin, out int c) ? c : 0;
            bool exists = Shader.Find(hdrp) != null;
            Line($"| `{builtin}` | {used} | `{hdrp}` | {(exists ? "yes" : "NOT YET")} |");
            if (!exists && used > 0) _pending++;
        }
        Line("");

        // Scene-level pink scan: only meaningful when the coast scene is actually open.
        var scene = SceneManager.GetActiveScene();
        if (scene.path == ScenePath && scene.isLoaded)
        {
            int missing = 0, checkedMats = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    checkedMats++;
                    if (m.shader == null || m.shader.name.Contains("InternalErrorShader")) missing++;
                }
            }
            Check($"No pink renderer material in open scene ({checkedMats} checked)", missing == 0,
                  Level.Fail, missing == 0 ? "clean" : $"{missing} missing-shader materials");
        }
        else
        {
            Line($"- SKIPPED scene renderer scan: `{ScenePath}` is not the open scene.");
        }
    }

    // -------------------------------------------------------------------------------- route

    private static void AuditRoute()
    {
        Head("## 3. Route contract (spec 5, 20, 23)");

        var graph = AssetDatabase.LoadAssetAtPath<RouteGraph>(RouteGraph.AssetPath);
        if (graph == null) { Check("RouteGraph asset loads", false, Level.Fail, RouteGraph.AssetPath); return; }

        Check("Road half width preserved", Mathf.Approximately(graph.roadHalfWidth, ContractRoadHalfWidth),
              Level.Fail, $"{graph.roadHalfWidth} m (contract {ContractRoadHalfWidth} m)");
        Check("Shoulder width preserved", Mathf.Approximately(graph.shoulderWidth, ContractShoulder),
              Level.Fail, $"{graph.shoulderWidth} m (contract {ContractShoulder} m)");
        Line($"- Crown contract {ContractCrown} m, lane offset contract {ContractLaneOffset} m " +
             "(swept by the environment builder; recalculate from the authored cross-section, spec 5.3).");

        var seg = graph.Segment("shiosai");
        if (seg == null) { Check("`shiosai` segment exists", false, Level.Fail, "missing"); return; }

        Line($"- Segment `shiosai` \"{seg.displayName}\": {seg.Count} samples, {seg.Length:0.000} m.");

        bool monotonic = true;
        for (int i = 1; i < seg.Count; i++) if (seg.distance[i] <= seg.distance[i - 1]) { monotonic = false; break; }
        Check("Route distances strictly monotonic", monotonic, Level.Fail, monotonic ? "ok" : "regression found");

        // Smoothed grade, sampled the way the trainer samples it (spec 5.2 / 20).
        float maxGrade = 0f, maxStep = 0f; int window = 10;
        for (int i = window; i < seg.Count; i++)
        {
            float dd = seg.distance[i] - seg.distance[i - window];
            if (dd < 0.01f) continue;
            float g = (seg.position[i].y - seg.position[i - window].y) / dd;
            maxGrade = Mathf.Max(maxGrade, Mathf.Abs(g));
            if (i > window)
            {
                float dd0 = seg.distance[i - 1] - seg.distance[i - 1 - window];
                if (dd0 > 0.01f)
                {
                    float g0 = (seg.position[i - 1].y - seg.position[i - 1 - window].y) / dd0;
                    maxStep = Mathf.Max(maxStep, Mathf.Abs(g - g0));
                }
            }
        }
        Check("Max smoothed grade within +/-10% (spec 5.2)", maxGrade <= 0.10f, Level.Warn,
              $"{maxGrade * 100f:0.0}%");
        Check("No instantaneous grade discontinuity (<2%/sample step)", maxStep <= 0.02f, Level.Warn,
              $"max step {maxStep * 100f:0.0}%");

        var course = graph.Course("shiosai_breeze");
        if (course != null)
        {
            float courseLen = 0f;
            foreach (var leg in course.legs) courseLen += Mathf.Abs(leg.to - leg.from);
            Line($"- Course `shiosai_breeze` \"{course.displayName}\" region `{course.regionId}` " +
                 $"closed={course.closed} legs={course.legs.Length} length {courseLen:0.0} m, " +
                 $"target {course.targetMinutes:0} min.");
            bool onTarget = Mathf.Abs(courseLen - TargetRouteMetres) <= TargetRouteMetres * RouteTolerance;
            Check($"Course length is {TargetRouteMetres / 1000f:0} km +/-{RouteTolerance * 100f:0}% (spec 5.1)",
                  onTarget, Level.Pending, $"{courseLen / 1000f:0.000} km, closed={course.closed}" +
                  (onTarget ? "" : " - not yet authored to the 42 km Grand Coast alignment"));
        }

        int present = graph.checkpoints.Count(c => RequiredAnchors.Contains(c.displayName));
        Check($"Required chapter anchors present ({RequiredAnchors.Length} in spec 5.5)",
              present == RequiredAnchors.Length, Level.Pending,
              $"{present}/{RequiredAnchors.Length}" +
              (present == RequiredAnchors.Length ? "" : " - missing anchors must be authored into shiosai_route.py"));
    }

    // --------------------------------------------------------------------------- references

    private static void AuditReferences()
    {
        Head("## 4. Reference image integrity (do-not-drift rule 21)");

        string refAbs = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                     ReferenceDir.Replace('/', Path.DirectorySeparatorChar));
        var files = Directory.GetFiles(refAbs, "*.png")
                             .OrderBy(f => f, StringComparer.Ordinal).ToArray();

        var entries = new List<string>();
        var current = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            byte[] bytes = File.ReadAllBytes(f);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            PngSize(bytes, out int w, out int h);
            string key = Path.GetFileName(f);
            string val = $"{w}x{h}:{hash}";
            current[key] = val;
            entries.Add($"  \"{key}\": \"{val}\"");
        }

        Check("Eight chapter reference PNGs present", files.Length == 8, Level.Fail, $"{files.Length} found");

        string manifestAbs = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                          ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestAbs))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(manifestAbs));
            File.WriteAllText(manifestAbs,
                "{" + Environment.NewLine + string.Join("," + Environment.NewLine, entries) +
                Environment.NewLine + "}" + Environment.NewLine);
            Line($"- PUBLISHED baseline manifest `{ManifestPath}` ({files.Length} images).");
            AssetDatabase.ImportAsset(ManifestPath);
        }
        else
        {
            string text = File.ReadAllText(manifestAbs);
            var drifted = current.Where(kv => !text.Contains($"\"{kv.Key}\": \"{kv.Value}\"")).Select(kv => kv.Key).ToList();
            Check("Reference PNGs unchanged since baseline", drifted.Count == 0, Level.Fail,
                  drifted.Count == 0 ? "all hashes match" : "DRIFTED: " + string.Join(", ", drifted));
        }

        Line("");
        Line("| Reference | Size | SHA-256 (first 16) |");
        Line("|---|---|---|");
        foreach (var kv in current)
        {
            var parts = kv.Value.Split(':');
            Line($"| {kv.Key} | {parts[0]} | `{parts[1].Substring(0, 16)}` |");
        }
    }

    private static void PngSize(byte[] b, out int w, out int h)
    {
        w = h = 0;
        if (b.Length < 24) return;
        w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
    }

    // -------------------------------------------------------------------------------- output

    private static void Head(string s) { Line(""); Line(s); Line(""); }
    private static void Line(string s) { Lines.Add(s); }

    private static void Check(string what, bool ok, Level level, string detail)
    {
        string tag;
        if (ok) tag = "PASS";
        else
        {
            switch (level)
            {
                case Level.Fail: tag = "FAIL"; _fail++; break;
                case Level.Warn: tag = "WARN"; _warn++; break;
                default: tag = "PENDING"; _pending++; break;
            }
        }
        Line($"- **{tag}** {what} - {detail}");
        Debug.Log($"[sc-audit] {tag} {what} - {detail}");
    }
}
