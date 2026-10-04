using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Narrow, road-only build + verification pass for Sakura Pass.
///
/// Why this exists rather than re-running <see cref="SakuraPassEnvironment.BuildEnvironmentPass"/>:
/// the full pass destroys and re-stages the ENTIRE environment root and re-runs the Built-in
/// cel material upgrade, which under HDRP would put every material in the scene back on a
/// Built-in shader that HDRP has no pass for (i.e. the world stops rendering). The carriageway,
/// its markings and its drainage are staged as GLB prefab INSTANCES, so a Blender rebuild is
/// already live in the scene - all that is left is materials, the one new object, and the
/// carriageway's collision. That is what this pass does, and nothing else.
///
/// Both entry points drive <see cref="RegionDirector"/> into the Sakura region first. The scene
/// ships saved with whatever region was last worked on (it was 'maple_city'), and every other
/// region's environment is hidden - which is why the existing diag_road_*.png captures were
/// 1600x900 of empty sky.
/// </summary>
public static class SakuraRoadPass
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RoadGroupPath = "Sakura Pass Environment/Roadway";
    private const string MaterialDir = "Assets/Environment/SakuraPass/Materials";
    private const string TextureDir = "Assets/Environment/SakuraPass/Textures";
    private const string AssetDir = "Assets/Environment/SakuraPass/BlenderAssets";

    private const string RoadShaderName = "MapleRide/HDRP/Road";
    private const string PaintShaderName = "MapleRide/HDRP/RoadPaint";
    private const string CelShaderName = "MapleRide/HDRP/CelLit";

    /// <summary>Exact scene name of the staged drainage group. Exact-name matching (never
    /// Contains) plus an active prune is what keeps this pass idempotent - see invariant 4.</summary>
    private const string DrainageObjectName = "Road Drainage";

    // ------------------------------------------------------------ road look tunables
    // ALL PROVISIONAL. The GEOMETRY CONTRACT values (_UvPerMetre, _UvCentreM, _RoadWidthM,
    // track/shoulder/verge widths) are NOT duplicated here - they are pushed straight from
    // SakuraPassEnvironment, which is where build_road.py's cross-section is mirrored.

    /// <summary>Gain on the asphalt albedo. Was 1.55 against the old pale texture; the rebuilt
    /// dark-binder asphalt needs almost none, and the old gain is what lifted a warm-grey
    /// texture into the purple-grey the feedback screenshot shows.</summary>
    private static readonly Color AsphaltTint = new Color(1.10f, 1.08f, 1.05f, 1f);
    /// <summary>Shade tint. Was (0.72, 0.66, 0.74) - a literally purple shadow colour on the
    /// single largest surface in the frame. Neutral cool grey instead.</summary>
    private static readonly Color AsphaltShade = new Color(0.58f, 0.60f, 0.66f, 1f);
    /// <summary>How much of the kit's banded cel ramp the road keeps. Low on purpose: hard N.L
    /// bands across a 7 m surface are the strongest "toy road" cue there is.</summary>
    private const float RoadCelAmount = 0.18f;
    private const float RoadGloss = 0.30f;
    private const float RoadSpecStrength = 0.18f;
    /// <summary>Grazing forward-scatter sheen - the cue that makes the road recede.</summary>
    private const float RoadSheenStrength = 0.55f;
    private const float RoadTrackPolish = 0.55f;
    /// <summary>Wheel-track darkening. Lower than the Built-in 0.30: the new asphalt is already
    /// dark, and 0.30 on top of it printed two black stripes per lane.</summary>
    private const float RoadTrackDarken = 0.20f;
    /// <summary>Longitudinal cold joint, metres from the centreline. 2.55 m puts it just
    /// outboard of the outer wheel track, where a widening joint sits on a real touge. 0
    /// disables it (the centreline joint is hidden under the yellow line anyway).</summary>
    private const float RoadJointOffsetM = 2.55f;
    private const float RoadJointWidthM = 0.045f;
    private const float RoadJointDarken = 0.30f;
    private const float RoadEdgeRavelM = 0.45f;
    private const float RoadEdgeRavelAmount = 0.55f;
    private const float RoadCrackScale = 0.55f;

    // --- paint. SakuraPassEnvironment.MarkingPeak caps the white so it never glows.
    private static readonly Color PaintWhiteWorn = new Color(0.46f, 0.45f, 0.44f, 1f);
    /// <summary>中央線 yellow. JIS safety yellow, knocked back for a weathered rural road.</summary>
    private static readonly Color PaintYellow = new Color(0.80f, 0.62f, 0.15f, 1f);
    private static readonly Color PaintYellowWorn = new Color(0.47f, 0.41f, 0.29f, 1f);
    private static readonly Color PaintGrime = new Color(0.52f, 0.50f, 0.47f, 1f);
    private const float PaintWearAmount = 0.38f;
    private const float PaintEdgeGrime = 0.45f;
    /// <summary>build_road.py::_stripe writes v = arc metres * 0.5, so one v unit is 2 m.</summary>
    private const float PaintMetresPerV = 2.0f;

    // --- drainage
    private static readonly Color GutterConcrete = new Color(0.60f, 0.59f, 0.56f, 1f);
    private static readonly Color GutterJoint = new Color(0.30f, 0.29f, 0.28f, 1f);
    private static readonly Color DrainGrate = new Color(0.17f, 0.17f, 0.18f, 1f);
    private static readonly Color DrainVoid = new Color(0.045f, 0.045f, 0.05f, 1f);

    // ---------------------------------------------------------------- census

    [MenuItem("MapleRide/Environment/Road/Census (log materials)", priority = 40)]
    public static void Census()
    {
        OpenScene();
        var region = EnterSakuraRegion();
        LogRoadCensus();
        LeaveRegion(region);
    }

    private static void OpenScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void LogRoadCensus()
    {
        var road = GameObject.Find(RoadGroupPath);
        if (road == null) { Debug.LogWarning("[road] no Roadway group in scene."); return; }

        foreach (Transform group in road.transform)
        {
            var rends = group.GetComponentsInChildren<Renderer>(true);
            var shaders = new Dictionary<string, int>();
            foreach (var r in rends)
                foreach (var m in r.sharedMaterials)
                {
                    string key = m == null ? "<null>" : $"{m.name} [{(m.shader ? m.shader.name : "<no shader>")}]";
                    shaders[key] = shaders.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            var b = rends.Length > 0 ? rends[0].bounds : new Bounds();
            foreach (var r in rends) b.Encapsulate(r.bounds);
            Debug.Log($"[road] {group.name}: {rends.Length} renderers, active={group.gameObject.activeInHierarchy}, " +
                      $"centre {b.center} size {b.size}");
            foreach (var kv in shaders.OrderByDescending(k => k.Value))
                Debug.Log($"[road]     {kv.Value,5} x {kv.Key}");
        }
    }

    // ---------------------------------------------------------------- region

    private struct RegionState { public RegionDirector dir; public string previous; }

    private static RegionState EnterSakuraRegion()
    {
        var dir = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (dir == null)
        {
            Debug.LogWarning("[road] no RegionDirector - scene visibility left as found.");
            return new RegionState();
        }
        dir.Resolve();
        var state = new RegionState { dir = dir, previous = dir.currentRegionId };
        dir.currentRegionId = RegionCatalog.SakuraPass;
        dir.ApplyEnvironmentVisibility();
        dir.ApplyAmbience();
        Debug.Log($"[road] region '{state.previous}' -> '{dir.currentRegionId}' for the pass.");
        return state;
    }

    /// <summary>Puts the scene back exactly as it was found; captures must not mutate it.</summary>
    private static void LeaveRegion(RegionState state)
    {
        if (state.dir == null) return;
        state.dir.currentRegionId = state.previous;
        state.dir.ApplyEnvironmentVisibility();
        state.dir.ApplyAmbience();
        Debug.Log($"[road] region restored to '{state.previous}'.");
    }

    // ---------------------------------------------------------------- apply

    [MenuItem("MapleRide/Environment/Road/Apply Road Surface", priority = 41)]
    public static void ApplyRoadSurface()
    {
        OpenScene();
        var region = EnterSakuraRegion();

        SakuraTextureImportSettings.ApplyAll();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var road = GameObject.Find(RoadGroupPath);
        if (road == null)
        {
            Debug.LogError("[road] no Roadway group - run SakuraPassEnvironment.BuildEnvironmentPass first.");
            LeaveRegion(region);
            return;
        }

        ApplyToRoadGroup(road.transform);
        AssetDatabase.SaveAssets();
        LogRoadCensus();
        LeaveRegion(region);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[road] saved '{scene.path}'.");
    }

    /// <summary>
    /// The whole road-surface milestone applied to one staged Roadway group: carriageway
    /// material + collision, painted markings, and the mountain-side drainage.
    ///
    /// SakuraPassEnvironment.StageRoad calls this too, so the full environment pass and this
    /// narrow pass cannot drift apart. Under a Built-in pipeline it is a no-op: the HDRP road
    /// shaders have no Built-in passes, so taking the material over there would make the
    /// carriageway invisible rather than better.
    /// </summary>
    public static void ApplyToRoadGroup(Transform roadway)
    {
        if (roadway == null) return;
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
        {
            Debug.Log("[road] Built-in pipeline active - HDRP road surface pass skipped.");
            return;
        }

        var asphalt = BuildAsphaltMaterial();
        var white = BuildPaintMaterial("SakuraPass_LineWhite",
                                       new Color(SakuraPassEnvironment.MarkingPeak,
                                                 SakuraPassEnvironment.MarkingPeak,
                                                 SakuraPassEnvironment.MarkingPeak * 0.97f, 1f),
                                       PaintWhiteWorn);
        var yellow = BuildPaintMaterial("SakuraPass_LineYellow", PaintYellow, PaintYellowWorn);

        AssignCarriageway(roadway.gameObject, asphalt);
        AssignMarkings(roadway.gameObject, white, yellow);
        StageDrainage(roadway);
    }

    // ------------------------------------------------------------ materials

    private static Texture2D LoadTexture(string file)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{file}");
        if (tex == null) Debug.LogWarning($"[road] missing texture {file}");
        return tex;
    }

    private static Material LoadOrCreate(string name, string shaderName)
    {
        string path = $"{MaterialDir}/{name}.mat";
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[road] shader '{shaderName}' not found - is the .shader importing?");
            return null;
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Directory.CreateDirectory(MaterialDir);
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        return mat;
    }

    /// <summary>
    /// The carriageway material. Every value is pushed explicitly: editing a shader's property
    /// DEFAULT does nothing to a .mat that already has the property serialised, and this
    /// material has been round-tripped through two pipelines.
    /// </summary>
    private static Material BuildAsphaltMaterial()
    {
        var m = LoadOrCreate("SakuraPass_Asphalt", RoadShaderName);
        if (m == null) return null;

        m.SetTexture("_MainTex", LoadTexture("Sakura_Asphalt_Albedo.png"));
        m.SetTexture("_DetailNormal", LoadTexture("Sakura_Asphalt_Normal.png"));
        m.SetColor("_Color", AsphaltTint);
        m.SetColor("_ShadeColor", AsphaltShade);
        m.SetFloat("_ShadeStrength", 0.62f);
        m.SetFloat("_CelAmount", RoadCelAmount);
        m.SetFloat("_RampSteps", 4f);
        m.SetFloat("_RampSmooth", 0.18f);
        m.SetFloat("_RampOffset", 0f);

        // --- geometry contract: these MUST track build_road.py. -------------------------
        m.SetFloat("_UvPerMetre", SakuraPassEnvironment.RoadUvPerMetre);
        m.SetFloat("_UvCentreM", SakuraPassEnvironment.RoadUvCentreM);
        m.SetFloat("_RoadWidthM", SakuraPassEnvironment.RoadWidthM);

        m.SetFloat("_DetailTile", SakuraPassEnvironment.RoadDetailTile);
        m.SetFloat("_DetailStrength", 0.55f);
        m.SetFloat("_MacroScale", SakuraPassEnvironment.RoadMacroScale);
        m.SetFloat("_MacroAmount", 0.26f);
        m.SetFloat("_PatchScale", SakuraPassEnvironment.RoadPatchScale);
        m.SetFloat("_PatchAmount", SakuraPassEnvironment.RoadPatchAmount);
        m.SetColor("_PatchColor", new Color(0.88f, 0.87f, 0.88f, 1f));
        m.SetFloat("_RoughVariation", SakuraPassEnvironment.RoadRoughVariation);

        m.SetFloat("_TrackOffsetM", SakuraPassEnvironment.RoadTrackOffsetM);
        m.SetFloat("_TrackWidthM", SakuraPassEnvironment.RoadTrackWidthM);
        m.SetFloat("_TrackDarken", RoadTrackDarken);
        m.SetFloat("_TrackPolish", RoadTrackPolish);

        m.SetFloat("_JointOffsetM", RoadJointOffsetM);
        m.SetFloat("_JointWidthM", RoadJointWidthM);
        m.SetFloat("_JointDarken", RoadJointDarken);
        m.SetFloat("_CrackScale", RoadCrackScale);
        m.SetFloat("_EdgeRavelM", RoadEdgeRavelM);
        m.SetFloat("_EdgeRavelAmount", RoadEdgeRavelAmount);

        m.SetFloat("_ShoulderWidthM", SakuraPassEnvironment.RoadShoulderWidthM);
        m.SetFloat("_ShoulderAmount", 0.45f);
        m.SetColor("_ShoulderColor", SakuraPassEnvironment.RoadShoulderColor);
        m.SetFloat("_VergeWidthM", SakuraPassEnvironment.RoadVergeWidthM);
        m.SetFloat("_VergeAmount", SakuraPassEnvironment.RoadVergeAmount);
        m.SetFloat("_VergeBreakup", SakuraPassEnvironment.RoadVergeBreakup);
        m.SetColor("_VergeGravelColor", SakuraPassEnvironment.RoadVergeGravelColor);
        m.SetColor("_VergeSoilColor", SakuraPassEnvironment.RoadVergeSoilColor);

        m.SetFloat("_Gloss", RoadGloss);
        m.SetFloat("_SpecStrength", RoadSpecStrength);
        m.SetFloat("_SheenStrength", RoadSheenStrength);
        m.SetColor("_SpecTint", new Color(1f, 0.96f, 0.90f, 1f));
        m.SetFloat("_RimStrength", 0.10f);
        m.SetFloat("_AmbientStrength", 0.85f);
        m.SetFloat("_ShadowAmbient", 0.45f);
        m.SetFloat("_Cull", 2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material BuildPaintMaterial(string name, Color paint, Color worn)
    {
        var m = LoadOrCreate(name, PaintShaderName);
        if (m == null) return null;
        m.SetColor("_Color", paint);
        m.SetColor("_WornColor", worn);
        m.SetColor("_GrimeColor", PaintGrime);
        m.SetFloat("_WearAmount", PaintWearAmount);
        m.SetFloat("_WearScale", 0.42f);
        m.SetFloat("_EdgeGrime", PaintEdgeGrime);
        m.SetFloat("_MetresPerV", PaintMetresPerV);
        m.SetFloat("_BeadSparkle", 0.30f);
        m.SetFloat("_Gloss", 0.12f);
        m.SetFloat("_SpecStrength", 0.05f);
        m.SetColor("_ShadeColor", new Color(0.62f, 0.64f, 0.70f, 1f));
        m.SetFloat("_ShadeStrength", 0.60f);
        m.SetFloat("_AmbientStrength", 0.85f);
        m.SetFloat("_ShadowAmbient", 0.50f);
        m.SetFloat("_Cull", 2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material CelLike(string name, Color color, float gloss, float spec,
                                    Texture2D tex, Color shade)
    {
        var m = LoadOrCreate(name, CelShaderName);
        if (m == null) return null;
        m.SetColor("_Color", color);
        m.SetColor("_ShadeColor", shade);
        m.SetFloat("_ShadeStrength", 0.55f);
        m.SetFloat("_Gloss", gloss);
        m.SetFloat("_SpecStrength", spec);
        m.SetFloat("_RimStrength", 0.12f);
        m.SetFloat("_AmbientStrength", 0.9f);
        m.SetFloat("_ShadowAmbient", 0.45f);
        if (tex != null) m.SetTexture("_MainTex", tex);
        m.SetFloat("_Cull", 2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------ assignment

    private static void AssignCarriageway(GameObject road, Material asphalt)
    {
        if (asphalt == null) return;
        var group = road.transform.Find("Carriageway");
        if (group == null) { Debug.LogWarning("[road] no Carriageway child."); return; }

        int n = 0;
        foreach (var r in group.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = asphalt;
            r.sharedMaterials = mats;
            n++;
        }

        // The carriageway is the riding surface: re-assert its collision after any restage.
        int colliders = 0;
        foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.enabled = true;
            colliders++;
        }
        Debug.Log($"[road] carriageway: {n} renderers -> {asphalt.shader.name}, {colliders} mesh colliders.");
    }

    private static void AssignMarkings(GameObject road, Material white, Material yellow)
    {
        var group = road.transform.Find("Road Markings");
        if (group == null) { Debug.LogWarning("[road] no Road Markings child."); return; }

        int w = 0, y = 0;
        foreach (var r in group.GetComponentsInChildren<Renderer>(true))
        {
            // Match on the OBJECT name (Marking_Centre / Marking_Edge_*), which build_road.py
            // owns, and fall back to the glTF material name. Matching only on the imported
            // material name breaks the moment the GLB is re-exported.
            bool isCentre = r.gameObject.name.StartsWith("Marking_Centre");
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                bool yellowSlot = isCentre ||
                    (mats[i] != null && mats[i].name.Contains("LineYellow"));
                mats[i] = yellowSlot ? yellow : white;
                if (yellowSlot) y++; else w++;
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Debug.Log($"[road] markings: {y} yellow + {w} white slots -> {PaintShaderName}.");
    }

    // ------------------------------------------------------------ drainage staging

    /// <summary>
    /// Stages SakuraPass_RoadDrain.glb under the Roadway group. Idempotent: every existing
    /// child with the exact display name is destroyed first, so repeated runs cannot leak a
    /// second gutter down the whole pass.
    /// </summary>
    public static void StageDrainage(Transform roadway)
    {
        for (int i = roadway.childCount - 1; i >= 0; i--)
        {
            var child = roadway.GetChild(i);
            if (child.name == DrainageObjectName) Object.DestroyImmediate(child.gameObject);
        }

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetDir}/SakuraPass_RoadDrain.glb");
        if (asset == null)
        {
            Debug.LogWarning("[road] SakuraPass_RoadDrain.glb missing - run tools/blender/build_road.py");
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        go.name = DrainageObjectName;
        go.transform.SetParent(roadway, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // Concrete, NOT the brown scree texture: the first render came back with a tan-brown
        // gutter that read as packed earth. CelLit's procedural weathering supplies the surface
        // break-up instead, and a mountain gutter genuinely grows moss along its wet edge.
        var concrete = CelLike("SakuraPass_GutterConcrete", GutterConcrete, 0.10f, 0.05f, null,
                               new Color(0.60f, 0.62f, 0.70f, 1f));
        if (concrete != null)
        {
            concrete.SetFloat("_WeatherAmount", 0.55f);
            concrete.SetFloat("_DetailScale", 14f);
            concrete.SetFloat("_DetailAmount", 0.30f);
            concrete.SetFloat("_TintVariation", 0.10f);
            concrete.SetFloat("_MossAmount", 0.28f);
            concrete.SetFloat("_MossHeight", 0.35f);
            concrete.SetFloat("_GrimeAmount", 0.20f);
        }
        var joint = CelLike("SakuraPass_GutterJoint", GutterJoint, 0.05f, 0.02f, null,
                            new Color(0.50f, 0.52f, 0.60f, 1f));
        var grate = CelLike("SakuraPass_DrainGrate", DrainGrate, 0.35f, 0.18f, null,
                            new Color(0.40f, 0.44f, 0.54f, 1f));
        var voidMat = CelLike("SakuraPass_DrainVoid", DrainVoid, 0.02f, 0.0f, null,
                              new Color(0.10f, 0.11f, 0.14f, 1f));
        if (voidMat != null)
        {
            // The inlet must read as a hole. Ambient is unshadowed in this kit, so albedo and
            // the ambient floor are the only levers that make an opening look dark.
            voidMat.SetFloat("_AmbientStrength", 0.25f);
            voidMat.SetFloat("_ShadowAmbient", 0.2f);
            voidMat.SetFloat("_RimStrength", 0f);
        }

        int parts = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name;
            Material m = n.StartsWith("Drain_GrateBars") ? grate
                       : n.StartsWith("Drain_Grates") ? voidMat
                       : n.StartsWith("Drain_Joints") ? joint
                       : concrete;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = m;
            r.sharedMaterials = mats;
            // A 2 cm concrete band lying on the road contributes nothing but shadow acne.
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            parts++;
        }
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        Debug.Log($"[road] staged '{DrainageObjectName}' ({parts} parts).");
    }

    // ---------------------------------------------------------------- capture

    [MenuItem("MapleRide/Environment/Road/Capture Road Views", priority = 42)]
    public static void CaptureRoadViews()
    {
        OpenScene();
        var region = EnterSakuraRegion();
        LogRoadCensus();

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        float climb = ClimbLength(route);

        // Rider's-eye: the two frames the user's feedback was taken from.
        RiderShot(dir, "diag_roadsurf_start", route, climb * 0.06f, 2.4f, 55f, true);
        RiderShot(dir, "diag_roadsurf_mid", route, climb * 0.45f, 2.4f, 55f, true);
        RiderShot(dir, "diag_roadsurf_mid_raw", route, climb * 0.45f, 2.4f, 55f, false);

        // Surface close-up: 1.3 m off the deck, pitched down, so aggregate, wheel tracks and
        // the paint edge are actually resolvable instead of being three pixels tall.
        SurfaceShot(dir, "diag_roadsurf_close", route, climb * 0.45f, 1.3f, 6.5f, 40f);

        // Plan view: the marking hierarchy and the shoulder/gutter widths, measured.
        PlanShot(dir, "diag_roadsurf_plan", route, climb * 0.45f, 14f, 38f);

        // Shoulder / drainage detail on the mountain (rider's left) side.
        ShoulderShot(dir, "diag_roadsurf_shoulder", route, climb * 0.45f);

        LeaveRegion(region);
    }

    private static float ClimbLength(SakuraPassEnvironment.SakuraRoute route)
    {
        // The climb is everything up to the highest sample; the descent follows it.
        int top = 0;
        for (int i = 1; i < route.Count; i++)
            if (route.Position[i].y > route.Position[top].y) top = i;
        return route.Distance[top];
    }

    private static void RiderShot(string dir, string name, SakuraPassEnvironment.SakuraRoute route,
                                  float d, float up, float fov, bool postFx)
    {
        int i = Mathf.Clamp(route.IndexAt(d), 0, route.Count - 1);
        int ahead = Mathf.Min(route.Count - 1, i + Mathf.Max(1, route.Count / 40));
        var eye = route.Position[i] + Vector3.up * up - route.Tangent[i] * 6f - route.Side[i] * 1.2f;
        var look = route.Position[ahead] + Vector3.up * 1.4f;
        Debug.Log($"[road] {name}: d={route.Distance[i]:0} m eye {eye}");
        Shot(dir, name, eye, look, fov, postFx);
    }

    private static void SurfaceShot(string dir, string name, SakuraPassEnvironment.SakuraRoute route,
                                    float d, float up, float ahead, float fov)
    {
        int i = Mathf.Clamp(route.IndexAt(d), 0, route.Count - 1);
        int j = Mathf.Clamp(route.IndexAt(d + ahead), 0, route.Count - 1);
        var eye = route.Position[i] + Vector3.up * up - route.Side[i] * 1.0f;
        var look = route.Position[j] + Vector3.up * 0.02f;
        Debug.Log($"[road] {name}: d={route.Distance[i]:0} m eye {eye}");
        Shot(dir, name, eye, look, fov, true);
    }

    private static void PlanShot(string dir, string name, SakuraPassEnvironment.SakuraRoute route,
                                 float d, float up, float fov)
    {
        int i = Mathf.Clamp(route.IndexAt(d), 0, route.Count - 1);
        var eye = route.Position[i] + Vector3.up * up;
        Debug.Log($"[road] {name}: d={route.Distance[i]:0} m eye {eye}");
        // worldUp along the tangent so the road runs up the screen rather than at a random roll.
        Shot(dir, name, eye, route.Position[i], fov, false, route.Tangent[i]);
    }

    private static void ShoulderShot(string dir, string name, SakuraPassEnvironment.SakuraRoute route, float d)
    {
        int i = Mathf.Clamp(route.IndexAt(d), 0, route.Count - 1);
        int j = Mathf.Clamp(route.IndexAt(d + 9f), 0, route.Count - 1);
        // Stand over the mountain-side (rider's LEFT = -side) shoulder and look along it.
        var eye = route.Position[i] - route.Side[i] * 2.2f + Vector3.up * 1.6f;
        var look = route.Position[j] - route.Side[j] * 4.0f;
        Debug.Log($"[road] {name}: d={route.Distance[i]:0} m eye {eye}");
        Shot(dir, name, eye, look, 42f, true);
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, bool postFx)
        => Shot(dir, name, pos, look, fov, postFx, Vector3.up);

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                             bool postFx, Vector3 worldUp)
    {
        var go = new GameObject("~RoadDiagCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, worldUp);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        if (postFx)
        {
            // A fresh component carries field-initialiser defaults; push the region grade.
            var fx = go.AddComponent<SakuraPostFX>();
            var a = RegionDirector.SakuraAmbience;
            fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
            fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
            fx.lift = a.lift; fx.gain = a.gain;
            fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
            fx.dofFocusDistance = a.dofFocusDistance; fx.dofFocusRange = a.dofFocusRange;
            fx.dofStrength = a.dofStrength;
        }

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());

        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[road] wrote {name}.png");
    }
}
