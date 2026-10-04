using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the SEATED "boulevard cyclist" crowd archetype for Minato Coast, as PNG reference
/// art, using the same conformant rider+bike fixture the global NPC conformance pass uses.
///
/// Why Unity and not Blender: the only pre-seated GLB on disk
/// (<c>Assets/Kuro/NPC/KuroNPC_Coral_OnBike.glb</c>) is a stale baked export whose helmet and
/// head are visibly exploded and out of scale with the bike - confirmed by looking at a render,
/// not by a metric. The live fixture path (<see cref="NpcCanonicalConformance.Configure"/> plus
/// <see cref="CoralBikeRig"/>) is the solved seating, so the render comes from there.
///
/// This is a CAPTURE-ONLY pass: it builds throwaway fixtures, renders, destroys them, and
/// discards all scene changes. It writes candidates into Logs/minato_npc_candidates; promotion
/// to Assets/Kuro/NPC happens only after the complete pack has been visually approved.
/// All framing numbers below are provisional art tuning for a reference sheet.
/// </summary>
public static class MinatoNpcRenderSheet
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>Donor rigs used as the Minato boulevard cyclists, in filename order.</summary>
    static readonly (string Index, string Donor, string BodyPath)[] Cyclists =
    {
        ("01", "Akihiro",  "Assets/Kuro/NPC/KuroNPC_Akihiro_Rigged.glb"),
        ("07", "Shiori",   "Assets/Kuro/NPC/KuroNPC_Shiori_Rigged.glb"),
        ("08", "Hanakage", "Assets/Kuro/NPC/KuroNPC_Hanakage_Rigged.glb"),
    };

    // Orthographic framing. A seated rider on the canonical bike is ~1.35 m tall and ~1.7 m
    // long, so a half-height of 1.05 leaves clear air all round - nothing cropped, nothing
    // obstructed. PROVISIONAL.
    const float ShotSize = 1.05f;
    const float EyeY = 0.72f;
    const float LookY = 0.70f;
    const float Radius = 6f;

    /// <summary>
    /// Fixtures are staged high above the world, not at the origin. At the origin the Sakura
    /// Pass terrain and sky render straight through the solid-colour clear and the sheet
    /// becomes a landscape photo with a rider in it; the archetype sheet has to be readable
    /// and unobstructed. PROVISIONAL.
    /// </summary>
    static readonly Vector3 Stage = new Vector3(0f, 600f, 0f);

    static readonly (string View, float Deg)[] Views =
    {
        ("front", 0f), ("q34", 38f), ("side", 90f),
    };

    /// <summary>
    /// Extra capture key light. Kept at ZERO: the exposure ladder in Logs/minato_sheet_probe
    /// proved a plain Light is a no-op on these characters - 0.0 and 1.8 rendered pixel-alike,
    /// because the cel materials are driven by the globals RegionDirector.ApplyAmbience pushes,
    /// not by Unity's standard light loop. Left in place as the documented negative result so
    /// the next person does not re-run the same experiment. PROVISIONAL.
    /// </summary>
    public static float CaptureKeyIntensity = 0f;

    static GameObject AddCaptureKey()
    {
        if (CaptureKeyIntensity <= 0f) return null;
        var go = new GameObject("~MinatoSheetKey");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = CaptureKeyIntensity;
        l.color = new Color(1.0f, 0.98f, 0.94f);
        l.shadows = LightShadows.None;
        go.transform.rotation = Quaternion.Euler(38f, -30f, 0f);
        return go;
    }

    /// <summary>Sweeps the capture key light so the exposure is chosen by LOOKING, not guessed.</summary>
    [MenuItem("MapleRide/NPCs/Probe Minato Crowd Sheet Exposure", priority = 24)]
    public static void CaptureExposureLadder()
    {
        string probeDir = Path.Combine(MapleRidePaths.RepoRoot, "Logs", "minato_sheet_probe");
        Directory.CreateDirectory(probeDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplySheetAmbience();
            GameObject root = null;
            try
            {
                root = BuildFixture("Akihiro", Cyclists[0].BodyPath, Stage);
                foreach (float intensity in new[] { 0f, 0.5f, 0.9f, 1.3f, 1.8f })
                {
                    float saved = CaptureKeyIntensity;
                    CaptureKeyIntensity = intensity;
                    var key = AddCaptureKey();
                    try
                    {
                        Shot(Path.Combine(probeDir,
                                "ladder_" + intensity.ToString("0.0").Replace('.', 'p') + ".png"),
                             Stage + new Vector3(Radius, EyeY, 0f),
                             Stage + new Vector3(0f, LookY, 0f), ShotSize);
                    }
                    finally
                    {
                        if (key != null) UnityEngine.Object.DestroyImmediate(key);
                        CaptureKeyIntensity = saved;
                    }
                }
            }
            finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("[minato-npc] exposure ladder -> " + probeDir);
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    /// <summary>
    /// Sweeps region ambience so the sheet's grade is chosen by LOOKING, not guessed.
    /// (The earlier light-intensity ladder proved a plain Light is a no-op here: the cel
    /// materials are driven by the region ambience the RegionDirector pushes, so 0.0 and 1.8
    /// rendered identically.)
    /// </summary>
    [MenuItem("MapleRide/NPCs/Probe Minato Crowd Sheet Grade", priority = 25)]
    public static void CaptureGradeLadder()
    {
        string probeDir = Path.Combine(MapleRidePaths.RepoRoot, "Logs", "minato_sheet_probe");
        Directory.CreateDirectory(probeDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
                FindObjectsInactive.Include);
            GameObject root = null;
            try
            {
                root = BuildFixture("Akihiro", Cyclists[0].BodyPath, Stage);
                foreach (string region in new[]
                {
                    RegionCatalog.MinatoCoast, RegionCatalog.SakuraPass,
                    RegionCatalog.AzoraHighlands, RegionCatalog.MapleCity,
                    RegionCatalog.TakaMountains,
                })
                {
                    if (regions != null)
                    {
                        regions.Resolve();
                        regions.currentRegionId = region;
                        regions.ApplyEnvironmentVisibility();
                        regions.ApplyAmbience();
                    }
                    Shot(Path.Combine(probeDir, "grade_" + region + ".png"),
                         Stage + new Vector3(Radius, EyeY, 0f),
                         Stage + new Vector3(0f, LookY, 0f), ShotSize);
                }
            }
            finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("[minato-npc] grade ladder -> " + probeDir);
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static void ApplySheetAmbience()
    {
        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
            FindObjectsInactive.Include);
        if (regions == null)
        {
            Debug.LogWarning("[minato-npc] no RegionDirector - exposure may be off.");
            return;
        }
        regions.Resolve();
        // Maple City's ambience, NOT Minato Coast's. Chosen by looking at the grade ladder in
        // Logs/minato_sheet_probe: Minato's own grade is a blue dusk that is correct in
        // gameplay but renders the archetype sheet dark and colour-cast, and a livery you
        // cannot read defeats the point of the sheet. Maple City is the closest thing this
        // project has to a neutral studio grade. PROVISIONAL.
        regions.currentRegionId = RegionCatalog.MapleCity;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
    }

    [MenuItem("MapleRide/NPCs/Capture Minato Crowd Cyclist Sheet", priority = 23)]
    public static void Capture()
    {
        string outDir = Path.Combine(MapleRidePaths.RepoRoot, "Logs",
                                     "minato_npc_candidates");
        Directory.CreateDirectory(outDir);
        int written = 0;
        GameObject key = null;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Do NOT just add a bright capture light. The first two attempts added a 1.2
            // directional on top of the Sakura sun and both came back as pure white blobs.
            // What the working diagnostics captures do is let RegionDirector set the region's
            // own key/fill/ambient, then shoot with a plain HDR camera. That alone is a dusk
            // grade though, so a modest, swept capture key is layered on top - see
            // CaptureExposureLadder.
            ApplySheetAmbience();
            key = AddCaptureKey();

            var group = new List<GameObject>();
            try
            {
                for (int i = 0; i < Cyclists.Length; i++)
                {
                    var (index, donor, bodyPath) = Cyclists[i];
                    GameObject root = null;
                    try
                    {
                        root = BuildFixture(donor, bodyPath, Stage);
                        foreach (var (view, deg) in Views)
                        {
                            float a = deg * Mathf.Deg2Rad;
                            // +Z is in FRONT of the fixture: the rider faces +Z, so a camera
                            // on -Z photographs the back of their head. The first sheet did
                            // exactly that and labelled it "front".
                            var eye = Stage + new Vector3(Mathf.Sin(a) * Radius, EyeY,
                                                  Mathf.Cos(a) * Radius);
                            Shot(Path.Combine(outDir,
                                    $"MinatoNPC_{index}_Cyclist_{donor}_{view}.png"),
                                 eye, Stage + new Vector3(0f, LookY, 0f), ShotSize);
                            written++;
                        }
                    }
                    finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
                }

                // Optional neutral-background preview. Production individual files above
                // retain transparent alpha.
                for (int i = 0; i < Cyclists.Length; i++)
                    group.Add(BuildFixture(Cyclists[i].Donor, Cyclists[i].BodyPath,
                                           Stage + new Vector3((i - 1) * 1.45f, 0f, 0f)));
                Shot(Path.Combine(outDir, "MinatoNPC_00_Cyclists_preview.png"),
                     Stage + new Vector3(0f, 1.0f, 11f),
                     Stage + new Vector3(0f, 0.75f, 0f), 1.20f,
                     1600, 900, false);
                written++;
            }
            finally
            {
                foreach (var g in group)
                    if (g != null) UnityEngine.Object.DestroyImmediate(g);
            }

            Debug.Log($"[minato-npc] wrote {written} crowd-cyclist PNG to {outDir}");
        }
        finally
        {
            if (key != null) UnityEngine.Object.DestroyImmediate(key);
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    static GameObject BuildFixture(string name, string bodyPath, Vector3 position)
    {
        var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath);
        var bikePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NpcCanonicalConformance.BikeAssetPath);
        if (bodyPrefab == null || bikePrefab == null)
            throw new InvalidOperationException($"Fixture assets missing for {name}.");

        var root = new GameObject("~MinatoSheet " + name);
        root.transform.position = position;

        // The bike child MUST be named exactly "Bike" and nested under the rig, or
        // CoralBikeRig.Setup falls back to a global GameObject.Find("Bike") and can grab the
        // player's bicycle out of the open scene.
        var bike = new GameObject("Bike");
        bike.transform.SetParent(root.transform, false);
        bike.transform.localScale = Vector3.one * NpcCanonicalConformance.BikeScale;
        var bikeModel = (GameObject)PrefabUtility.InstantiatePrefab(bikePrefab, bike.transform);
        bikeModel.name = "BikeMesh";

        var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, root.transform);
        body.name = name + "ArmatureAndMesh";

        var rig = root.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikePrefab;
        NpcCanonicalConformance.Configure(rig);
        NpcCanonicalConformance.FinalizeStagedPose(rig);
        return root;
    }

    static void Shot(string file, Vector3 pos, Vector3 look, float size,
                     int w = 900, int h = 1150, bool transparent = true)
    {
        var go = new GameObject("~MinatoSheetCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 3000f;
        // A bare Camera renders these characters blown out to near-white. Match the gameplay
        // grade or the capture hides exactly the detail it exists to show. First attempt at
        // this sheet skipped it and produced three white blobs - see the skill's
        // reference/verification.md, "Cameras that actually show the problem".
        cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = transparent
            ? new Color(0.30f, 0.31f, 0.33f, 1f)
            : new Color(0.30f, 0.31f, 0.33f, 1f);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(w, h, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        image.Apply();
        if (transparent)
        {
            // The active render pipeline forces camera alpha to 1. Recover transparency from
            // the flat neutral matte. The exact post-grade matte colour is sampled from the
            // corner, so this remains stable if the colour pipeline shifts slightly.
            var pixels = image.GetPixels32();
            Color32 matte = pixels[0];
            for (int i = 0; i < pixels.Length; i++)
            {
                int distance = Math.Max(Math.Abs(pixels[i].r - matte.r),
                               Math.Max(Math.Abs(pixels[i].g - matte.g),
                                        Math.Abs(pixels[i].b - matte.b)));
                int alpha = Mathf.Clamp(distance * 8, 0, 255);
                if (alpha == 0)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }
                pixels[i] = new Color32(
                    pixels[i].r, pixels[i].g, pixels[i].b,
                    (byte)alpha);
            }
            image.SetPixels32(pixels);
            image.Apply();
        }
        File.WriteAllBytes(file, image.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("[minato-npc] shot " + Path.GetFileName(file));
    }
}
