using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Authors, stages and verifies Kuro's player-only wraparound cycling goggles.
/// Geometry is generated from named dimensions, saved as a reusable mesh asset and attached
/// directly to the imported Head bone. The transparent lens is deliberately excluded from
/// KuroOutline and uses a dedicated HDRP transparency material.
/// </summary>
public static class KuroGogglesSetup
{
    public const string RootName = "Kuro Cycling Goggles";
    public const string LensName = "Kuro Goggle Lens";
    public const string FrameName = "Kuro Goggle Frame";
    public const string GlintName = "Kuro Goggle Glint";

    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PlayerName = "Kuro on Sakura Pass";
    private const string CharacterPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
    private const string AssetFolder = "Assets/Kuro/Player/Goggles";
    private const string MaterialFolder = "Assets/Kuro/Player/Materials";
    private const string LensMeshPath = AssetFolder + "/KuroCyclingGoggleLens.asset";
    private const string LensMaterialPath = MaterialFolder + "/KuroCyclingGoggleLens.mat";
    private const string FrameMaterialPath = MaterialFolder + "/KuroCyclingGoggleFrame.mat";
    private const string GlintMaterialPath = MaterialFolder + "/KuroCyclingGoggleGlint.mat";

    // PROVISIONAL face-fit dimensions in the imported character's bind frame.
    public const float LensCenterAboveHeadMetres = 0.110f;
    public const float LensCenterForwardMetres = 0.150f;
    public const float LensHalfWidthMetres = 0.190f;
    public const float LensTopMetres = 0.047f;
    public const float LensBottomEdgeMetres = -0.041f;
    public const float LensNoseNotchRiseMetres = 0.016f;
    public const float LensWrapDepthMetres = 0.031f;
    public const int LensColumns = 20;
    public const int LensRows = 4;

    // PROVISIONAL lightweight frame dimensions.
    public const float BrowTubeRadiusMetres = 0.0048f;
    public const float TempleTubeRadiusMetres = 0.0042f;
    public const float BridgeTubeRadiusMetres = 0.0038f;
    public const float TempleRearwardMetres = 0.162f;
    public const float TempleEndHalfWidthMetres = 0.154f;

    // PROVISIONAL player-only material tuning. Alpha is intentionally low enough that Kuro's
    // irises and expression remain legible through the cool smoke lens.
    public static readonly Color LensSmokeSrgb = new Color(0.18f, 0.38f, 0.50f, 0.32f);
    public static readonly Color FrameSrgb = new Color(0.025f, 0.090f, 0.145f, 1f);
    public static readonly Color GlintSrgb = new Color(0.68f, 0.90f, 1f, 0.42f);
    public const int LensRenderQueue = 3000;
    public const int GlintRenderQueue = 3001;

    private static string OutputDirectory =>
        Path.GetFullPath(Path.Combine(
            Application.dataPath, "../reference/good_graphics/kuro_goggles"));

    [MenuItem("MapleRide/Kuro/Stage Cycling Goggles", priority = 67)]
    public static void Stage()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = FindRoot(scene, PlayerName);
        if (player == null) throw new InvalidOperationException("Kuro player root is missing.");
        ConfigurePlayer(player);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[kuro-goggles] staged one head-bone-owned eyewear root and saved clean.");
    }

    public static Transform ConfigurePlayer(GameObject player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        var head = ExactSingle(player, "Head");
        PruneExactChildren(head, RootName);

        EnsureFolder(AssetFolder);
        EnsureFolder(MaterialFolder);
        var lensMesh = UpdateLensMeshAsset();
        var lensMaterial = GetOrCreateLensMaterial();
        var frameMaterial = GetOrCreateFrameMaterial();
        var glintMaterial = GetOrCreateGlintMaterial();

        var root = NewChild(head, RootName);
        SetBindFramePlacement(player, head, root);

        var lens = NewChild(root, LensName);
        var lensFilter = lens.gameObject.AddComponent<MeshFilter>();
        lensFilter.sharedMesh = lensMesh;
        var lensRenderer = lens.gameObject.AddComponent<MeshRenderer>();
        lensRenderer.sharedMaterial = lensMaterial;
        lensRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lensRenderer.receiveShadows = false;
        lensRenderer.sortingOrder = 10;

        var frame = NewChild(root, FrameName);
        BuildFrame(frame, frameMaterial);

        var glint = NewChild(root, GlintName);
        BuildGlint(glint, glintMaterial);

        EnsureOutlineExclusion(player);
        EditorUtility.SetDirty(root);
        return root;
    }

    private static void SetBindFramePlacement(
        GameObject player, Transform head, Transform goggles)
    {
        var sourceHead = PrefabUtility.GetCorrespondingObjectFromSource(head);
        Transform sourceFrame = sourceHead != null ? sourceHead.root : null;
        if (sourceHead != null && sourceFrame != null)
        {
            Vector3 worldCenter = sourceHead.position
                + sourceFrame.up * LensCenterAboveHeadMetres
                + sourceFrame.forward * LensCenterForwardMetres;
            goggles.localPosition = sourceHead.InverseTransformPoint(worldCenter);
            goggles.localRotation = Quaternion.Inverse(sourceHead.rotation) * sourceFrame.rotation;
        }
        else
        {
            Vector3 worldCenter = head.position
                + player.transform.up * LensCenterAboveHeadMetres
                + player.transform.forward * LensCenterForwardMetres;
            goggles.position = worldCenter;
            goggles.rotation = player.transform.rotation;
        }
        goggles.localScale = Vector3.one;
    }

    private static Mesh UpdateLensMeshAsset()
    {
        var generated = BuildLensMesh();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(LensMeshPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, LensMeshPath);
            existing = generated;
        }
        else
        {
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
        }
        return existing;
    }

    private static Mesh BuildLensMesh()
    {
        int width = LensColumns + 1;
        int height = LensRows + 1;
        var vertices = new Vector3[width * height];
        var uv = new Vector2[vertices.Length];
        for (int row = 0; row <= LensRows; row++)
        {
            float v = row / (float)LensRows;
            for (int column = 0; column <= LensColumns; column++)
            {
                float u = column / (float)LensColumns;
                float side = u * 2f - 1f;
                float absSide = Mathf.Abs(side);
                float top = LensTopMetres - 0.006f * side * side;
                float bottom = LensBottomEdgeMetres
                    + LensNoseNotchRiseMetres * (1f - Mathf.SmoothStep(0f, 1f, absSide));
                float x = side * LensHalfWidthMetres;
                float y = Mathf.Lerp(bottom, top, v);
                float z = -LensWrapDepthMetres * side * side;
                int index = row * width + column;
                vertices[index] = new Vector3(x, y, z);
                uv[index] = new Vector2(u, v);
            }
        }

        var triangles = new List<int>(LensColumns * LensRows * 12);
        for (int row = 0; row < LensRows; row++)
        for (int column = 0; column < LensColumns; column++)
        {
            int a = row * width + column;
            int b = a + 1;
            int c = a + width;
            int d = c + 1;
            triangles.Add(a); triangles.Add(b); triangles.Add(d);
            triangles.Add(a); triangles.Add(d); triangles.Add(c);
            triangles.Add(a); triangles.Add(d); triangles.Add(b);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }

        var mesh = new Mesh { name = "KuroCyclingGoggleLens" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void BuildFrame(Transform root, Material material)
    {
        const int segments = 8;
        for (int i = 0; i < segments; i++)
        {
            float a = -1f + 2f * i / segments;
            float b = -1f + 2f * (i + 1) / segments;
            TubeBetween(root, "Brow_" + i.ToString("00"),
                LensTopPoint(a), LensTopPoint(b), BrowTubeRadiusMetres, material);
        }

        BuildOuterLowerRim(root, "LowerRim_L", -1f, -0.20f, material);
        BuildOuterLowerRim(root, "LowerRim_R", 0.20f, 1f, material);

        Vector3 leftEdge = LensTopPoint(-1f);
        Vector3 rightEdge = LensTopPoint(1f);
        Vector3 leftMid = new Vector3(
            -LensHalfWidthMetres - 0.003f, 0.014f, -0.086f);
        Vector3 rightMid = new Vector3(
             LensHalfWidthMetres + 0.003f, 0.014f, -0.086f);
        Vector3 leftEnd = new Vector3(
            -TempleEndHalfWidthMetres, 0.003f, -TempleRearwardMetres);
        Vector3 rightEnd = new Vector3(
             TempleEndHalfWidthMetres, 0.003f, -TempleRearwardMetres);
        TubeBetween(root, "Temple_L_Front", leftEdge, leftMid, TempleTubeRadiusMetres, material);
        TubeBetween(root, "Temple_L_Rear", leftMid, leftEnd, TempleTubeRadiusMetres, material);
        TubeBetween(root, "Temple_R_Front", rightEdge, rightMid, TempleTubeRadiusMetres, material);
        TubeBetween(root, "Temple_R_Rear", rightMid, rightEnd, TempleTubeRadiusMetres, material);
        Ellipsoid(root, "Hinge_L",
            new Vector3(-LensHalfWidthMetres, 0.017f, -LensWrapDepthMetres),
            new Vector3(0.013f, 0.012f, 0.010f), material);
        Ellipsoid(root, "Hinge_R",
            new Vector3( LensHalfWidthMetres, 0.017f, -LensWrapDepthMetres),
            new Vector3(0.013f, 0.012f, 0.010f), material);

        Vector3 bridgeL = new Vector3(-0.034f, -0.010f, 0.004f);
        Vector3 bridgeR = new Vector3( 0.034f, -0.010f, 0.004f);
        TubeBetween(root, "NoseBridge", bridgeL, bridgeR, BridgeTubeRadiusMetres, material);
        TubeBetween(root, "NoseSupport_L", bridgeL,
            new Vector3(-0.017f, -0.032f, 0.008f), 0.0032f, material);
        TubeBetween(root, "NoseSupport_R", bridgeR,
            new Vector3( 0.017f, -0.032f, 0.008f), 0.0032f, material);
        Ellipsoid(root, "NosePad_L",
            new Vector3(-0.021f, -0.035f, 0.010f),
            new Vector3(0.014f, 0.009f, 0.006f), material);
        Ellipsoid(root, "NosePad_R",
            new Vector3( 0.021f, -0.035f, 0.010f),
            new Vector3(0.014f, 0.009f, 0.006f), material);
    }

    private static void BuildGlint(Transform root, Material material)
    {
        TubeBetween(root, "LensHighlight",
            LensPoint(-0.70f, 0.72f) + Vector3.forward * 0.0035f,
            LensPoint(-0.38f, 0.34f) + Vector3.forward * 0.0035f,
            0.0028f, material, 11);
    }

    private static Vector3 LensTopPoint(float side)
    {
        float x = side * LensHalfWidthMetres;
        float y = LensTopMetres - 0.006f * side * side;
        float z = -LensWrapDepthMetres * side * side;
        return new Vector3(x, y, z);
    }

    private static Vector3 LensBottomPoint(float side)
    {
        float absSide = Mathf.Abs(side);
        float y = LensBottomEdgeMetres
            + LensNoseNotchRiseMetres * (1f - Mathf.SmoothStep(0f, 1f, absSide));
        return new Vector3(
            side * LensHalfWidthMetres,
            y,
            -LensWrapDepthMetres * side * side);
    }

    private static Vector3 LensPoint(float side, float vertical)
    {
        return Vector3.Lerp(LensBottomPoint(side), LensTopPoint(side), vertical);
    }

    private static void BuildOuterLowerRim(
        Transform root, string prefix, float fromSide, float toSide, Material material)
    {
        const int segments = 4;
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Lerp(fromSide, toSide, i / (float)segments);
            float b = Mathf.Lerp(fromSide, toSide, (i + 1f) / segments);
            TubeBetween(root, prefix + "_" + i.ToString("00"),
                LensBottomPoint(a), LensBottomPoint(b), 0.0028f, material);
        }
    }

    private static void TubeBetween(
        Transform parent, string name, Vector3 a, Vector3 b,
        float radius, Material material, int sortingOrder = 0)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 delta = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        go.transform.localScale = new Vector3(radius, delta.magnitude * 0.5f, radius);
        var collider = go.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = sortingOrder;
    }

    private static void Ellipsoid(
        Transform parent, string name, Vector3 localPosition,
        Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        var collider = go.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static Material GetOrCreateLensMaterial()
    {
        var material = LoadOrCreateMaterial(
            LensMaterialPath, "Kuro Cycling Goggle Lens", true);
        ConfigureTransparentMaterial(material, LensSmokeSrgb, LensRenderQueue);
        return material;
    }

    private static Material GetOrCreateGlintMaterial()
    {
        var material = LoadOrCreateMaterial(
            GlintMaterialPath, "Kuro Cycling Goggle Glint", true);
        ConfigureTransparentMaterial(material, GlintSrgb, GlintRenderQueue);
        return material;
    }

    private static Material GetOrCreateFrameMaterial()
    {
        var material = LoadOrCreateMaterial(
            FrameMaterialPath, "Kuro Cycling Goggle Frame", false);
        SetMaterialColor(material, FrameSrgb);
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
        if (material.HasProperty("_TransparentZWrite"))
            material.SetFloat("_TransparentZWrite", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.34f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.08f);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LoadOrCreateMaterial(
        string path, string name, bool preferUnlit)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = preferUnlit
            ? Shader.Find("HDRP/Unlit")
            : Shader.Find("MapleRide/HDRP/CelLit");
        shader = shader ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard");
        if (shader == null)
            throw new InvalidOperationException("No compatible shader for Kuro goggles.");
        var material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void ConfigureTransparentMaterial(
        Material material, Color color, int renderQueue)
    {
        SetMaterialColor(material, color);
        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 1f);
        if (material.HasProperty("_BlendMode")) material.SetFloat("_BlendMode", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_TransparentZWrite"))
            material.SetFloat("_TransparentZWrite", 0f);
        if (material.HasProperty("_AlphaCutoffEnable"))
            material.SetFloat("_AlphaCutoffEnable", 0f);
        if (material.HasProperty("_DoubleSidedEnable"))
            material.SetFloat("_DoubleSidedEnable", 1f);
        if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_BLENDMODE_ALPHA");
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = renderQueue;
        material.doubleSidedGI = true;
        EditorUtility.SetDirty(material);
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", color.linear);
    }

    private static void EnsureOutlineExclusion(GameObject player)
    {
        foreach (var outline in player.GetComponentsInChildren<KuroOutline>(true))
        {
            var names = outline.skipUnderNamed != null
                ? outline.skipUnderNamed.Where(n => !string.IsNullOrEmpty(n)).ToList()
                : new List<string>();
            if (!names.Contains(RootName)) names.Add(RootName);
            outline.skipUnderNamed = names.Distinct().ToArray();
            EditorUtility.SetDirty(outline);
        }
    }

    public static void AppendSelfTest(
        GameObject player, ref int failures, StringBuilder log)
    {
        ConfigurePlayer(player);
        ConfigurePlayer(player);
        var rootMatches = player.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == RootName).ToArray();
        Check(rootMatches.Length == 1,
            "goggle staging is idempotent (one exact-name root)",
            ref failures, log);
        if (rootMatches.Length != 1) return;

        var root = rootMatches[0];
        var head = ExactSingle(player, "Head");
        Check(root.parent == head,
            "goggles are parented directly to Kuro's Head bone",
            ref failures, log);
        Check(CountNamed(root.gameObject, LensName) == 1 &&
              CountNamed(root.gameObject, FrameName) == 1 &&
              CountNamed(root.gameObject, GlintName) == 1,
            "goggles contain one lens, frame and controlled highlight",
            ref failures, log);

        var lensRenderer = ExactSingle(root.gameObject, LensName)
            .GetComponent<MeshRenderer>();
        var lensMaterial = lensRenderer != null ? lensRenderer.sharedMaterial : null;
        Color lensColor = lensMaterial != null ? ReadMaterialColor(lensMaterial) : Color.clear;
        Check(lensRenderer != null && lensMaterial != null &&
              AssetDatabase.GetAssetPath(lensMaterial) == LensMaterialPath &&
              lensMaterial.renderQueue >= LensRenderQueue &&
              lensColor.a >= 0.18f && lensColor.a <= 0.40f &&
              ReadZWrite(lensMaterial) < 0.5f,
            "lens uses the player-only alpha-blended smoke material with depth writes off",
            ref failures, log);

        var lensMesh = ExactSingle(root.gameObject, LensName)
            .GetComponent<MeshFilter>().sharedMesh;
        Check(lensMesh != null &&
              Mathf.Abs(lensMesh.bounds.size.x - LensHalfWidthMetres * 2f) < 0.005f &&
              lensMesh.bounds.size.y > 0.075f &&
              lensMesh.bounds.size.z >= LensWrapDepthMetres * 0.95f,
            "lens mesh preserves the named wraparound face-fit dimensions",
            ref failures, log);

        var ownedRenderers = root.GetComponentsInChildren<Renderer>(true);
        Check(ownedRenderers.Length >= 10 &&
              ownedRenderers.SelectMany(r => r.sharedMaterials)
                  .Where(m => m != null)
                  .All(m => AssetDatabase.GetAssetPath(m)
                      .StartsWith("Assets/Kuro/Player/", StringComparison.Ordinal)),
            "all eyewear renderers use Kuro/player-only materials",
            ref failures, log);

        bool outlineExcluded = player.GetComponentsInChildren<KuroOutline>(true)
            .All(o => o.skipUnderNamed != null && o.skipUnderNamed.Contains(RootName));
        Check(outlineExcluded,
            "KuroOutline explicitly excludes the transparent goggles",
            ref failures, log);

        Vector3 localPosition = root.localPosition;
        Quaternion localRotation = root.localRotation;
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null)
        {
            for (int i = 0; i < 12; i++)
            {
                rig.AdvanceCrank(30f);
                rig.ForceSolveOnce();
            }
        }
        Check(Vector3.Distance(root.localPosition, localPosition) < 0.00001f &&
              Quaternion.Angle(root.localRotation, localRotation) < 0.001f,
            "eyewear remains rigidly attached to Head through a full pedal revolution",
            ref failures, log);
    }

    [MenuItem("MapleRide/Kuro/Capture Cycling Goggles", priority = 68)]
    public static void CaptureValidation()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutputDirectory);
        GameObject neutral = null;
        GameObject player = null;
        try
        {
            player = FindRoot(scene, PlayerName);
            if (player == null) throw new InvalidOperationException("Kuro player root is missing.");
            var pose = player.GetComponent<KuroRidePose>();
            var rig = player.GetComponent<KuroBikeRig>();
            if (pose == null || rig == null)
                throw new InvalidOperationException("Kuro posture components are missing.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            if (prefab == null) throw new FileNotFoundException("Kuro character is missing.", CharacterPath);
            neutral = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (neutral == null) neutral = UnityEngine.Object.Instantiate(prefab);
            neutral.name = "~Kuro Goggles Neutral";
            neutral.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            neutral.transform.localScale = player.transform.localScale;
            ConfigurePlayer(neutral);
            player.SetActive(false);
            CaptureHeadViews(neutral, "00_neutral");
            UnityEngine.Object.DestroyImmediate(neutral);
            neutral = null;
            player.SetActive(true);

            bool priorClimb = pose.enableClimbOverlay;
            bool priorSprint = pose.enableSprintOverlay;
            var priorPosture = pose.selectedPosture;
            pose.enableClimbOverlay = false;
            pose.enableSprintOverlay = false;
            pose.SprintOverride = 0f;

            pose.SelectPosture(
                KuroRidePose.CyclingPosture.RoadRacerAggressive, false);
            pose.Tick(1f);
            rig.ForceSolveOnce();
            CaptureHeadViews(player, "01_aggressive");

            pose.SelectPosture(
                KuroRidePose.CyclingPosture.TriathlonTimeTrial, false);
            pose.Tick(1f);
            rig.ForceSolveOnce();
            CaptureHeadViews(player, "03_time_trial");

            pose.enableClimbOverlay = priorClimb;
            pose.enableSprintOverlay = priorSprint;
            pose.SprintOverride = float.NaN;
            pose.SelectPosture(priorPosture, false);
            rig.ForceSolveOnce();
            Debug.Log("[kuro-goggles] captures written to " + OutputDirectory);
        }
        finally
        {
            if (player != null) player.SetActive(true);
            if (neutral != null) UnityEngine.Object.DestroyImmediate(neutral);
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    private static void CaptureHeadViews(GameObject subject, string prefix)
    {
        var head = ExactSingle(subject, "Head");
        var goggles = ExactSingle(subject, RootName);
        Vector3 aim = Vector3.Lerp(head.position, goggles.position, 0.58f);
        Vector3 forward = subject.transform.forward;
        Vector3 right = subject.transform.right;
        Vector3 up = subject.transform.up;
        const float distance = 1.16f;
        const float lift = 0.040f;

        Shot(prefix + "_front", aim + forward * distance + up * lift, aim, 30f);
        Shot(prefix + "_front_left34",
            aim + forward * (distance * 0.72f) - right * (distance * 0.72f) + up * lift,
            aim, 30f);
        Shot(prefix + "_front_right34",
            aim + forward * (distance * 0.72f) + right * (distance * 0.72f) + up * lift,
            aim, 30f);
        Shot(prefix + "_side", aim + right * distance + up * lift, aim, 30f);
        Shot(prefix + "_rear_head",
            aim - forward * distance + up * (lift + 0.060f), aim, 30f);
    }

    private static void Shot(string name, Vector3 position, Vector3 lookAt, float fov)
    {
        var ambience = RegionDirector.SakuraAmbience;
        var go = new GameObject("~KuroGoggleCamera");
        var camera = go.AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.LookAt(lookAt, Vector3.up);
        camera.fieldOfView = fov;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 14000f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = ambience.bloomThreshold;
        fx.bloomIntensity = ambience.bloomIntensity;
        fx.exposure = ambience.exposure;
        fx.saturation = ambience.saturation;
        fx.contrast = ambience.contrast;
        fx.lift = ambience.lift;
        fx.gain = ambience.gain;
        fx.vignetteStrength = ambience.vignette;
        fx.vignetteSoftness = ambience.vignetteSoftness;
        fx.dofStrength = 0f;

        const int width = 1200;
        const int height = 1000;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4
        };
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("[kuro-goggles] wrote " + name + ".png");
    }

    private static Color ReadMaterialColor(Material material)
    {
        if (material.HasProperty("_UnlitColor")) return material.GetColor("_UnlitColor");
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        if (material.HasProperty("baseColorFactor"))
            return material.GetColor("baseColorFactor").gamma;
        return Color.clear;
    }

    private static float ReadZWrite(Material material)
    {
        if (material.HasProperty("_TransparentZWrite"))
            return material.GetFloat("_TransparentZWrite");
        if (material.HasProperty("_ZWrite")) return material.GetFloat("_ZWrite");
        return 0f;
    }

    private static void Check(
        bool condition, string label, ref int failures, StringBuilder log)
    {
        if (condition) log.AppendLine("  [ok] " + label);
        else
        {
            failures++;
            log.AppendLine("  [FAIL] " + label);
        }
    }

    private static GameObject FindRoot(Scene scene, string exactName)
    {
        return scene.GetRootGameObjects().SingleOrDefault(root => root.name == exactName);
    }

    private static Transform ExactSingle(GameObject root, string exactName)
    {
        var matches = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == exactName).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Expected exactly one '" + exactName + "' under " + root.name +
                ", found " + matches.Length + ".");
        return matches[0];
    }

    private static int CountNamed(GameObject root, string exactName)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .Count(t => t.name == exactName);
    }

    private static void PruneExactChildren(Transform parent, string exactName)
    {
        var matches = new List<Transform>();
        foreach (Transform child in parent)
            if (child.name == exactName) matches.Add(child);
        foreach (var child in matches)
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static Transform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
