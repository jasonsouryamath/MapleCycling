using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the Minato Coast background crowd from the approved MinatoNPC concepts.
///
/// WHAT THIS REPLACES
/// ------------------
/// The waterfront used to be dressed with <c>Minato_People_Cyclist/StandA/StandB/Walk/Wave/Sit</c>
/// FBX box people - untextured blocks that read as headless red crates on the contact cameras.
/// The approved concept sheet (<c>Assets/Kuro/NPC/MinatoNPC_00_BoulevardPack_lineup.png</c>) was
/// rendered from the real donor rigs, so the replacement uses those same donors. Full-resolution
/// live-skinned variants are exported under <c>Assets/Kuro/NPC/CrowdHigh/</c>; the welded,
/// decimated variants under <c>Assets/Kuro/NPC/Crowd/</c> are distant 3D LODs only.
///
/// PERFORMANCE MODEL (the whole reason this is a separate pass)
/// ------------------------------------------------------------
/// A crowd figure remains AI-free environment decor, but its visible near/mid presentation is a
/// real character rig:
///  * Every archetype is built ONCE into a template, then <see cref="Object.Instantiate"/>d.
///  * LOD0 retains the donor's full-resolution 24-joint <see cref="SkinnedMeshRenderer"/> rig,
///    imported PBR materials, shadows, probes and procedural skeleton animation.
///  * LOD1 uses <see cref="SkinnedMeshRenderer.BakeMesh"/> only for the distant lower-detail 3D
///    fallback. It is never the visible near/mid presentation and is not a billboard or sprite.
///  * Cyclist bicycle geometry is consolidated by material while the rider remains live-skinned.
///    Materials and distant meshes are shared across instances.
///  * Instances are flagged OccludeeStatic ONLY. BatchingStatic would duplicate a 40k-triangle
///    mesh per instance and ContributeGI would drag hundreds of figures into the lightmap; both
///    would cost far more than they save at this triangle count.
///  * Every instance carries a two-level <see cref="LODGroup"/>: live-skinned high detail,
///    lower-detail static 3D, then hard culling at long range.
///  * No gameplay AI, no colliders, no rigidbodies, no <c>CoralBikeRig</c> on any instance.
///
/// OUTLINES
/// --------
/// Crowd figures deliberately get NO <c>KuroOutline</c>. The outline is an inverted-hull pass
/// that doubles draw calls and triangles for a silhouette that is invisible at crowd distance,
/// and <c>KuroOutline.IsExcluded</c> refuses skinned renderers outright anyway. Any outline that
/// is ever added here must keep "Bike" in its <c>skipUnderNamed</c> list, or the bicycle gets
/// hulled along with the rider.
///
/// BIKE FIT
/// --------
/// Crowd cyclists are fitted by <see cref="NpcCanonicalConformance"/> - the same single
/// authority the gameplay NPCs use - and keep a real child named exactly "Bike". The rider GLB
/// intentionally keeps its donor origin (see the Cyclist exemption in
/// <c>set_origin_to_contact</c>), so the baked rider is dropped onto the solved fixture at the
/// exact local transform the solve produced and contact holds by construction.
/// </summary>
public static class MinatoCrowdPopulation
{
    // ------------------------------------------------------------------ authored inputs

    const string CrowdGlbDir = "Assets/Kuro/NPC/Crowd";
    const string HighCrowdGlbDir = "Assets/Kuro/NPC/CrowdHigh";
    const string BakedMeshDir = "Assets/Environment/MinatoCoast/Meshes/Crowd";
    const string BakedMatDir = "Assets/Environment/MinatoCoast/Materials/Crowd";

    public const string Cyclist = "Cyclist";
    public const string Wave = "Wave";
    public const string StandA = "StandA";
    public const string StandB = "StandB";
    public const string Walk = "Walk";
    public const string Sit = "Sit";

    /// <summary>The crowd archetypes (see the KURO-BASED CROWD note below).</summary>
    static readonly (string Key, string Archetype, string Donor, string Glb)[] Cast =
    {
        // KURO-BASED CROWD (2026-09-24): donors are the Minato riders' Kuro-based bodies
        // (minato_crowd_export.py). The old Coral-family donors were ~20k-island triangle soups
        // that tore when posed. Five of twelve archetypes now WALK (was one of eight).
        ("01_Cyclist_Minori", Cyclist, "Minori", "MinatoCrowd_01_Cyclist_Minori.glb"),
        ("02_Wave_Kohaku", Wave, "Kohaku", "MinatoCrowd_02_Wave_Kohaku.glb"),
        ("03_StandA_Marina", StandA, "Marina", "MinatoCrowd_03_StandA_Marina.glb"),
        ("04_StandB_Kaoru", StandB, "Kaoru", "MinatoCrowd_04_StandB_Kaoru.glb"),
        ("05_Walk_Rina", Walk, "Rina", "MinatoCrowd_05_Walk_Rina.glb"),
        ("06_Sit_Sena", Sit, "Sena", "MinatoCrowd_06_Sit_Sena.glb"),
        ("07_Cyclist_Tatsuya", Cyclist, "Tatsuya", "MinatoCrowd_07_Cyclist_Tatsuya.glb"),
        ("08_Cyclist_Ryoko", Cyclist, "Ryoko", "MinatoCrowd_08_Cyclist_Ryoko.glb"),
        ("09_Walk_Hibiki", Walk, "Hibiki", "MinatoCrowd_09_Walk_Hibiki.glb"),
        ("10_Walk_Asuka", Walk, "Asuka", "MinatoCrowd_10_Walk_Asuka.glb"),
        ("11_Walk_Yutaka", Walk, "Yutaka", "MinatoCrowd_11_Walk_Yutaka.glb"),
        ("12_Walk_Junpei", Walk, "Junpei", "MinatoCrowd_12_Walk_Junpei.glb"),
    };

    /// <summary>Donor rigs the seated solve is run against (must match MinatoCrowdPoseDump).</summary>
    static readonly Dictionary<string, string> DonorRig = new Dictionary<string, string>
    {
        { "Minori",  "Assets/Kuro/NPC/KuroRiders/KuroRider_bob.glb" },
        { "Tatsuya", "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb" },
        { "Ryoko",   "Assets/Kuro/NPC/KuroRiders/KuroRider_long.glb" },
    };

    /// <summary>
    /// Per-donor bicycle frame colours, cloned from the shared aero bike GLB's materials.
    /// The shared GLB materials are NEVER tinted in place - three crowd cyclists share one bike
    /// asset, so recolouring the import would repaint the player's bicycle too. PROVISIONAL:
    /// these read off the approved concept sheet's teal/coral/cream bikes.
    /// </summary>
    static readonly Dictionary<string, Color> BikeFrameColor = new Dictionary<string, Color>
    {
        { "Minori",  new Color(0.12f, 0.62f, 0.66f) },   // teal
        { "Tatsuya", new Color(0.90f, 0.33f, 0.28f) },   // coral
        { "Ryoko",   new Color(0.93f, 0.92f, 0.88f) },   // cream
    };

    // ------------------------------------------------------------------ tunables

    /// <summary>
    /// Screen-relative HEIGHT thresholds for the two crowd LOD tiers. A 1.3 m figure in a
    /// 60 deg vertical view occupies h / (2 d tan 30) of the screen, so LowDetailScreenHeight
    /// 0.0045 retires a figure at roughly 250 m.
    ///
    /// The old 0.034 threshold replaced the live, dimensional rig at roughly 33 m. That distance
    /// is still close enough for the chase camera to resolve the static bake as visibly flatter,
    /// especially when figures on opposite sides of the road happened to straddle the boundary.
    /// Keep the real skinned figure through the readable mid-ground and reserve the already-3D
    /// static tier for genuinely distant decoration. 0.013 switches at roughly 85-95 m for the
    /// approved 1.3 m crowd figures. PROVISIONAL.
    /// </summary>
    public const float HighDetailScreenHeight = 0.013f;
    public const float LowDetailScreenHeight = 0.0045f;

    // ---------------------------------------------------- distant crowd readability
    /// <summary>
    /// Folder holding the distance-legible atlas variants written by
    /// <c>tools/make_minato_crowd_distance_atlas.py</c>.
    /// </summary>
    const string DistanceAtlasDir = "Assets/Environment/MinatoCoast/Textures/Crowd";

    /// <summary>
    /// Per-archetype tint applied to the DISTANT crowd tier only.
    ///
    /// The donor atlases all average to a similar warm grey once their crushed blacks are
    /// lifted, so without this every far figure would read as the same neutral smudge. These
    /// multiply the (already bright) distance atlas, and every one keeps a high minimum
    /// channel so it shifts hue without darkening the figure back into a silhouette. The
    /// result is the mixed, colourful background crowd the concept art shows.
    ///
    /// PROVISIONAL: illustrative tuning echoing the existing Minato_Crowd_Top_* palette in
    /// MinatoCoastEnvironment, not a confirmed art requirement.
    /// </summary>
    static readonly Dictionary<string, Color> DistanceTint = new Dictionary<string, Color>
    {
        { "01_Cyclist_Minori", Color.white },
        { "02_Wave_Kohaku", Color.white },
        { "03_StandA_Marina", Color.white },
        { "04_StandB_Kaoru", Color.white },
        { "05_Walk_Rina", Color.white },
        { "06_Sit_Sena", Color.white },
        { "07_Cyclist_Tatsuya", Color.white },
        { "08_Cyclist_Ryoko", Color.white },
        { "09_Walk_Hibiki", Color.white },
        { "10_Walk_Asuka", Color.white },
        { "11_Walk_Yutaka", Color.white },
        { "12_Walk_Junpei", Color.white },  // kits are already coloured
    };

    /// <summary>
    /// Flat ambient floor and rim strength forced onto the distant tier's cloned body
    /// materials. The shipped defaults (_CharAmbient 0.32, _RimStrength 0.85) let a figure's
    /// shaded side crush toward black, which at a few pixels tall averages straight back into
    /// the flat dark pin this pass exists to remove. PROVISIONAL tuning.
    /// </summary>
    public const float DistanceCharAmbient = 0.55f;
    public const float DistanceRimStrength = 1.20f;

    /// <summary>
    /// Seat height of the lightweight timber waterfront bench authored under every seated
    /// figure. The sitter origin is the ground and the hips land at this height.
    /// </summary>
    public const float BenchSeatHeight = 0.38f;
    public const float BenchWidth = 1.12f;
    public const float BenchDepth = 0.42f;

    // ------------------------------------------------------------------ template cache

    class Template
    {
        public string Key;
        public string Archetype;
        public GameObject Root;
        /// <summary>Signed offset from the template origin down to its lowest rendered point.</summary>
        public float BoundsBottom;
        /// <summary>True when the caller should drop the lowest rendered point onto the ground.</summary>
        public bool AlignToBoundsBottom;
        public int HighTriangles;
        public int LowTriangles;
    }

    static Dictionary<string, Template> _templates;
    static GameObject _holder;
    static Material _benchMaterial;
    static Mesh _benchMesh;

    public static string[] CyclistKeys =>
        Cast.Where(c => c.Archetype == Cyclist).Select(c => c.Key).ToArray();

    public static string[] PedestrianKeys =>
        Cast.Where(c => c.Archetype == Wave || c.Archetype == StandA ||
                        c.Archetype == StandB || c.Archetype == Walk)
            .Select(c => c.Key).ToArray();

    public static string[] BeachKeys =>
        Cast.Where(c => c.Archetype != Cyclist).Select(c => c.Key).ToArray();

    public static bool Ready => _templates != null && _templates.Count > 0;

    // ------------------------------------------------------------------ build

    /// <summary>
    /// Bake every archetype template once. Idempotent: regenerates the baked mesh/material
    /// folders from scratch and prunes any leftover holder by EXACT name, so a repeated
    /// environment build cannot accumulate a second set of templates.
    /// </summary>
    public static void Prepare()
    {
        // Idempotent AND re-entrant. The beach pass calls this after the boulevard pass has
        // already spawned figures that reference the baked mesh assets; regenerating the asset
        // folder at that point would null out every MeshFilter already in the scene. Callers
        // that genuinely want a rebuild (a fresh environment Apply) call Dispose() first.
        if (Ready) return;
        Dispose();
        EnsureFolder(BakedMeshDir);
        EnsureFolder(BakedMatDir);
        // Full regeneration - stale baked meshes from a previous archetype revision would
        // otherwise linger as orphaned assets and be picked up by name.
        AssetDatabase.DeleteAsset(BakedMeshDir);
        AssetDatabase.DeleteAsset(BakedMatDir);
        EnsureFolder(BakedMeshDir);
        EnsureFolder(BakedMatDir);

        // Exact-name prune, never Contains: a "Contains" match here has previously grabbed
        // unrelated scene objects in this project.
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,
                                                               FindObjectsSortMode.None))
            if (go.name == HolderName && go.transform.parent == null)
                Object.DestroyImmediate(go);

        _holder = new GameObject(HolderName);
        _holder.hideFlags = HideFlags.HideAndDontSave;
        _holder.SetActive(false);
        _templates = new Dictionary<string, Template>();

        foreach (var entry in Cast)
        {
            var t = BuildTemplate(entry.Key, entry.Archetype, entry.Donor, entry.Glb);
            if (t != null) _templates[entry.Key] = t;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[minato-crowd] prepared {_templates.Count}/{Cast.Length} crowd templates " +
                  $"({_templates.Values.Sum(t => t.HighTriangles):N0} high-detail and " +
                  $"{_templates.Values.Sum(t => t.LowTriangles):N0} low-detail unique triangles)");
    }

    const string HolderName = "~MinatoCrowdTemplates";

    public static void Dispose()
    {
        if (_holder != null) Object.DestroyImmediate(_holder);
        _holder = null;
        _templates = null;
        _benchMaterial = null;
        _benchMesh = null;
    }

    static Template BuildTemplate(string key, string archetype, string donor, string glb)
    {
        string highPath = $"{HighCrowdGlbDir}/{glb}";
        string lowPath = $"{CrowdGlbDir}/{glb}";
        var highPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(highPath);
        var lowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(lowPath);
        if (highPrefab == null || lowPrefab == null)
        {
            Debug.LogError($"[minato-crowd] missing high/low crowd GLB for {key}. " +
                           "Run minato_crowd_export.py --high and the regular crowd export.");
            return null;
        }

        var root = new GameObject("MinatoCrowd_" + key);
        root.transform.SetParent(_holder.transform, false);

        // LOD0: full donor topology, live 24-joint skin, imported PBR atlas and real shadows.
        var high = new GameObject("LOD0 High Skinned");
        high.transform.SetParent(root.transform, false);
        Transform highSlot = high.transform;
        if (archetype == Cyclist)
        {
            highSlot = BuildSolvedBike(high, donor, key);
            if (highSlot == null) { Object.DestroyImmediate(root); return null; }
            var highBike = high.transform.Find("Bike");
            if (highBike != null)
                ConsolidateStaticRenderers(highBike.gameObject, key + "_HighBike");
        }
        var highCrowd = (GameObject)PrefabUtility.InstantiatePrefab(highPrefab, high.transform);
        highCrowd.name = "Rigged Character";
        highCrowd.transform.SetLocalPositionAndRotation(highSlot.localPosition,
                                                        highSlot.localRotation);
        highCrowd.transform.localScale = highSlot.localScale;
        if (highSlot != high.transform) Object.DestroyImmediate(highSlot.gameObject);
        var highRig = high.GetComponent<CoralBikeRig>();
        if (highRig != null) Object.DestroyImmediate(highRig);
        ConfigureHighRenderers(high);

        // LOD1: the existing decimated asset remains a genuinely three-dimensional far model,
        // but it is never used for the visible near/mid crowd.
        var low = new GameObject("LOD1 Low 3D");
        low.transform.SetParent(root.transform, false);
        Transform lowSlot = low.transform;
        if (archetype == Cyclist)
        {
            lowSlot = BuildSolvedBike(low, donor, key);
            if (lowSlot == null) { Object.DestroyImmediate(root); return null; }
        }
        var lowCrowd = (GameObject)PrefabUtility.InstantiatePrefab(lowPrefab, low.transform);
        lowCrowd.name = "~LowCrowdSource";
        lowCrowd.transform.SetLocalPositionAndRotation(lowSlot.localPosition, lowSlot.localRotation);
        lowCrowd.transform.localScale = lowSlot.localScale;
        if (lowSlot != low.transform) Object.DestroyImmediate(lowSlot.gameObject);
        var lowRig = low.GetComponent<CoralBikeRig>();
        if (lowRig != null) Object.DestroyImmediate(lowRig);
        int lowTris = BakeSkinToStatic(lowCrowd, low.transform, key);
        Object.DestroyImmediate(lowCrowd);
        ConsolidateStaticRenderers(low, key);
        ApplyDistanceReadability(low, key, donor);
        foreach (var mr in low.GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mr.lightProbeUsage = LightProbeUsage.BlendProbes;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Simple;
        }

        int highTris = RendererTriangles(high.GetComponentsInChildren<Renderer>(true));
        // SkinnedMeshRenderer.bounds is an import-time estimate and is wildly wrong for these
        // posed chibi rigs. Bake the live skin before deriving contact or scale.
        var b = BakedWorldBounds(high);
        var t = new Template
        {
            Key = key,
            Archetype = archetype,
            Root = root,
            BoundsBottom = b.min.y,
            // A sitter's origin IS its seat plane and a ledge is authored under it, so dropping
            // its lowest point (the dangling feet) onto the ground would bury the ledge.
            AlignToBoundsBottom = archetype != Sit,
            HighTriangles = highTris,
            LowTriangles = lowTris,
        };
        Debug.Log($"[minato-crowd] template {key} high={highTris:N0} tris/" +
                  $"{high.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} skins, " +
                  $"low={lowTris:N0} tris/{low.GetComponentsInChildren<MeshRenderer>(true).Length} renderer, " +
                  $"height={b.size.y:N2} bottom={b.min.y:N3}");
        return t;
    }

    static void ConfigureHighRenderers(GameObject high)
    {
        foreach (var r in high.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.Simple;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            foreach (var m in r.sharedMaterials)
                if (m != null) m.enableInstancing = true;
            if (r is SkinnedMeshRenderer smr)
            {
                // These full donor rigs use fragmented skinned material renderers. Their
                // imported local bounds are too tight for a procedural gait and can cull the
                // legs while the torso remains visible. Keep LOD0 skins updating and let the
                // LODGroup, rather than stale sub-renderer bounds, control visibility.
                smr.updateWhenOffscreen = true;
                smr.skinnedMotionVectors = true;
            }
        }
    }

    static int RendererTriangles(IEnumerable<Renderer> renderers)
    {
        int triangles = 0;
        foreach (var r in renderers)
        {
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh :
                        r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            for (int s = 0; s < mesh.subMeshCount; s++)
                triangles += (int)(mesh.GetIndexCount(s) / 3);
        }
        return triangles;
    }

    /// <summary>
    /// Run the canonical seated solve and return the transform the rider ended up at, leaving a
    /// correctly fitted child named exactly "Bike" on <paramref name="root"/>.
    /// </summary>
    static Transform BuildSolvedBike(GameObject root, string donor, string key)
    {
        if (!DonorRig.TryGetValue(donor, out string rigPath))
        {
            Debug.LogError($"[minato-crowd] no donor rig registered for {donor}");
            return null;
        }
        var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
        var bikePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NpcCanonicalConformance.BikeAssetPath);
        if (bodyPrefab == null || bikePrefab == null)
        {
            Debug.LogError($"[minato-crowd] missing donor/bike for {donor}");
            return null;
        }

        // Exact name "Bike", nested under the rig. If this is renamed or reparented,
        // CoralBikeRig.Setup falls back to a global GameObject.Find("Bike") and can grab the
        // player's bicycle out of the scene.
        var bike = new GameObject("Bike");
        bike.transform.SetParent(root.transform, false);
        bike.transform.localScale = Vector3.one * NpcCanonicalConformance.BikeScale;
        var bikeModel = (GameObject)PrefabUtility.InstantiatePrefab(bikePrefab, bike.transform);
        bikeModel.name = "BikeMesh";

        var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, root.transform);
        body.name = "~SolveBody";

        var rig = root.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikePrefab;
        NpcCanonicalConformance.Configure(rig);
        NpcCanonicalConformance.FinalizeStagedPose(rig);

        CloneBikeLivery(bike, donor, key);
        return body.transform;
    }

    /// <summary>
    /// Clone this cyclist's bicycle materials before recolouring. The aero bike GLB is SHARED by
    /// all three crowd cyclists and by the player, so tinting the imported materials in place
    /// would repaint every bicycle in the game.
    /// </summary>
    static void CloneBikeLivery(GameObject bike, string donor, string key)
    {
        if (!BikeFrameColor.TryGetValue(donor, out var frameColor)) return;
        var cache = new Dictionary<Material, Material>();
        foreach (var mr in bike.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (!cache.TryGetValue(src, out var clone))
                {
                    string cloneName = $"MinatoCrowd_{key}_{src.name}";
                    string clonePath = $"{BakedMatDir}/{cloneName}.mat";
                    clone = AssetDatabase.LoadAssetAtPath<Material>(clonePath);
                    if (clone == null)
                    {
                        clone = new Material(src) { name = cloneName };
                        clone.enableInstancing = true;
                        AssetDatabase.CreateAsset(clone, clonePath);
                    }
                    // The donor bike materials are raw glTF import sub-assets, and a plain
                    // new Material(src) inherits that shader - which HDRP cannot render (it is
                    // what left 41 crowd materials flagged BAD/pipeline-incompatible). Convert
                    // the clone we privately own BEFORE tinting, so the livery colour lands on
                    // CelLit's _Color rather than a property the final shader will discard.
                    // Idempotent: a no-op once the clone is already on CelLit.
                    NpcCelLitConversion.ConvertInPlace(clone);
                    {
                        bool isFrame = NpcCanonicalConformance.FrameMaterialNames
                            .Any(n => src.name.StartsWith(
                                n, System.StringComparison.OrdinalIgnoreCase));
                        if (isFrame) SetColor(clone, frameColor);
                    }
                    EditorUtility.SetDirty(clone);
                    cache[src] = clone;
                }
                mats[i] = clone;
            }
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
    }

    static void SetColor(Material m, Color c)
    {
        foreach (var id in new[] { "_BaseColor", "_Color", "_UnlitColor", "_BaseColorMap" })
            if (m.HasProperty(id) && id != "_BaseColorMap") m.SetColor(id, c);
    }

    /// <summary>Albedo texture slot names, most-likely first. See ApplyDistanceReadability.</summary>
    static readonly string[] AlbedoTextureProps =
        { "baseColorTexture", "_BaseColorMap", "_MainTex" };

    /// <summary>Albedo tint slot names, most-likely first. See ApplyDistanceReadability.</summary>
    static readonly string[] AlbedoColorProps =
        { "baseColorFactor", "_BaseColor", "_Color", "_UnlitColor" };

    /// <summary>
    /// Tint the distant tier's cloned body material. Kept separate from <see cref="SetColor"/>
    /// so adding the glTFast slot name cannot repaint the already-verified bike livery.
    ///
    /// The tint is LUMINANCE-NORMALISED before it is applied: the raw palette entries above all
    /// have a luminance below one (cyan measures 0.85, violet 0.82), so applying them directly
    /// darkened every far figure by 10-18% and partly undid the atlas brightening this pass
    /// exists to deliver. Dividing by the entry's own luminance keeps the hue shift while
    /// leaving perceived brightness unchanged; channels above one are a legal baseColorFactor
    /// multiplier and simply push the tinted hue brighter.
    /// </summary>
    static bool SetDistanceTint(Material m, Color c)
    {
        float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        if (lum > 0.001f) c = new Color(c.r / lum, c.g / lum, c.b / lum, 1f);

        bool any = false;
        foreach (var id in AlbedoColorProps)
        {
            if (!m.HasProperty(id)) continue;
            m.SetColor(id, c);
            any = true;
        }
        return any;
    }

    /// <summary>
    /// Make the DISTANT crowd tier read as colourful little people instead of flat dark pins.
    ///
    /// ROOT CAUSE (measured, not guessed): every crowd figure is textured from a single 2048px
    /// donor atlas containing large regions of pure black (hair, shorts, shoes, cel line-art).
    /// Past ~80 m a figure covers a handful of pixels, so the GPU samples the deepest mips -
    /// which are simply that atlas's average. Those averages measure lum 68-98 out of 255,
    /// against a plaza pavement measured at lum 186, so every figure collapses into a
    /// near-monochrome silhouette: the reported "NPCs look 2D" defect. LOD0 and LOD1 share the
    /// same material references (see BakeSkinToStatic), so this was never an LOD1 bake fault -
    /// it is purely what distance does to a dark atlas.
    ///
    /// THE FIX: re-point the distant tier at a brightened, saturation-boosted copy of the same
    /// atlas, tint it per archetype for crowd variety, and lift the cel shader's ambient floor
    /// so the shaded side cannot crush back to black.
    ///
    /// Materials are CLONED into persisted assets first, exactly like <see cref="CloneBikeLivery"/>:
    /// the donor atlas materials are imported .glb sub-assets shared by the hero/named NPCs, so
    /// editing them in place would repaint those riders too (and be reverted on reimport).
    /// The near-range LOD0 tier deliberately keeps the untouched originals, so the contact-range
    /// look that already reads correctly cannot regress.
    /// </summary>
    static void ApplyDistanceReadability(GameObject low, string key, string donor)
    {
        string atlasPath = $"{DistanceAtlasDir}/MinatoCrowd_{donor}_Atlas_Distance.png";
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        if (atlas == null)
        {
            Debug.LogError($"[minato-crowd] {key}: missing distance atlas '{atlasPath}'. " +
                           "Run tools/make_minato_crowd_distance_atlas.py.");
            return;
        }
        if (!DistanceTint.TryGetValue(key, out var tint)) tint = Color.white;

        var cache = new Dictionary<Material, Material>();
        int retextured = 0;
        foreach (var mr in low.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                // The bicycle's materials are already crowd-owned clones living in BakedMatDir;
                // only the body materials still come straight off the shared donor .glb.
                string srcPath = AssetDatabase.GetAssetPath(src);
                if (srcPath.StartsWith(BakedMatDir, System.StringComparison.Ordinal)) continue;
                // Kuro-based bodies carry a separate hair piece; it must keep its own tinted
                // hair texture, never the body's distance atlas.
                if (src.name.Contains("NpcHair")) continue;

                if (!cache.TryGetValue(src, out var clone))
                {
                    string cloneName = $"MinatoCrowd_{key}_Dist_{src.name}";
                    string clonePath = $"{BakedMatDir}/{cloneName}.mat";
                    clone = AssetDatabase.LoadAssetAtPath<Material>(clonePath);
                    if (clone == null)
                    {
                        clone = new Material(src) { name = cloneName };
                        AssetDatabase.CreateAsset(clone, clonePath);
                    }
                    clone.enableInstancing = true;

                    // Same root cause as CloneBikeLivery: the crowd body clones inherit the raw
                    // glTF import shader from the donor .glb sub-asset, which HDRP cannot render
                    // and which has none of the cel properties the readability floors below need.
                    // Converting here (idempotent) is what makes those floors actually land, and
                    // it also moves the albedo into _MainTex - which the AlbedoTextureProps list
                    // below already covers as its hand-authored fallback.
                    NpcCelLitConversion.ConvertInPlace(clone);

                    // Swap the albedo for the distance-legible variant of the same atlas.
                    // Historically the crowd bodies arrived as glTFast-imported materials, whose
                    // albedo slot is "baseColorTexture"/"baseColorFactor" - NOT the
                    // _MainTex/_BaseColorMap of a hand-authored Unity material. Both spellings are
                    // kept: the ConvertInPlace above now normally lands the clone on
                    // MapleRideCelLit (_MainTex), but a donor that arrives already converted, or a
                    // future import path, can still present the glTF names.
                    foreach (var prop in AlbedoTextureProps)
                    {
                        if (!clone.HasProperty(prop)) continue;
                        if (clone.GetTexture(prop) == null) continue;
                        clone.SetTexture(prop, atlas);
                        retextured++;
                        break;
                    }
                    SetDistanceTint(clone, tint);
                    // Cel-shader readability floors. Before the ConvertInPlace above these were
                    // silently skipped on EVERY distant material (the glTF shader declares no
                    // such properties), so half of this readability pass never actually ran.
                    if (clone.HasProperty("_CharAmbient"))
                        clone.SetFloat("_CharAmbient", DistanceCharAmbient);
                    if (clone.HasProperty("_RimStrength"))
                        clone.SetFloat("_RimStrength", DistanceRimStrength);
                    EditorUtility.SetDirty(clone);
                    cache[src] = clone;
                }
                mats[i] = clone;
            }
            mr.sharedMaterials = mats;
        }
        if (retextured == 0)
            Debug.LogError($"[minato-crowd] {key}: distance readability found NO albedo slot to " +
                           "re-point. The donor materials use an unexpected shader - this pass " +
                           "would silently do nothing. Check the albedo property name.");
        Debug.Log($"[minato-crowd] {key}: distance readability applied - {cache.Count} body " +
                  $"material(s) cloned, {retextured} re-textured, tint {tint}");
    }

    /// <summary>
    /// Freeze every SkinnedMeshRenderer into a temporary plain static mesh. A second pass folds
    /// these meshes and the bicycle's imported MeshRenderers into one shared multi-submesh asset.
    /// </summary>
    static int BakeSkinToStatic(GameObject source, Transform destParent, string key)
    {
        int tris = 0, n = 0;
        foreach (var smr in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var mesh = new Mesh
            {
                name = $"MinatoCrowd_{key}_{n:00}",
                indexFormat = IndexFormat.UInt32,
            };
            // useScale:true bakes the renderer's lossy scale into the vertices, which is why the
            // baked object below is placed at scale one.
            smr.BakeMesh(mesh, true);
            float unitFix = MinatoCrowdActor.BakeUnitFix(smr, mesh);   // Kuro 0.01-armature bodies
            if (unitFix != 1f)
            {
                var vs = mesh.vertices;
                for (int k = 0; k < vs.Length; k++) vs[k] *= unitFix;
                mesh.vertices = vs;
            }
            mesh.RecalculateBounds();
            var go = new GameObject(smr.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(destParent, false);
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterials = smr.sharedMaterials;
            foreach (var m in mr.sharedMaterials)
                if (m != null && !m.enableInstancing) m.enableInstancing = true;

            tris += mesh.triangles.Length / 3;
            n++;
        }
        return tris;
    }

    /// <summary>
    /// Consolidate every enabled static renderer under one template into a single renderer.
    /// Geometry is first merged per exact Material reference, then assembled as one mesh with
    /// one submesh/material slot per material group. This preserves transparent materials,
    /// normals, tangents, UVs and livery clones while reducing a cyclist from ~121 renderers to 1.
    /// </summary>
    static void ConsolidateStaticRenderers(GameObject root, string key)
    {
        var groups = new Dictionary<Material, List<CombineInstance>>();
        int triangles = 0;
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!mr.enabled || !mr.gameObject.activeSelf) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh;
            var mats = mr.sharedMaterials;
            if (mats.Length == 0) continue;
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var mat = mats[Mathf.Min(sub, mats.Length - 1)];
                if (mat == null) continue;
                mat.enableInstancing = true;
                if (!groups.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    groups.Add(mat, list);
                }
                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = sub,
                    transform = toRoot,
                });
                triangles += (int)(mesh.GetIndexCount(sub) / 3);
            }
        }
        if (groups.Count == 0)
            throw new System.InvalidOperationException($"{key}: no static geometry to consolidate.");

        var materialMeshes = new List<Mesh>();
        var finalParts = new List<CombineInstance>();
        var materials = new List<Material>();
        foreach (var pair in groups.OrderBy(p => p.Key.name, System.StringComparer.Ordinal))
        {
            var merged = new Mesh
            {
                name = $"~{key}_{pair.Key.name}",
                indexFormat = IndexFormat.UInt32,
            };
            merged.CombineMeshes(pair.Value.ToArray(), true, true, false);
            merged.RecalculateBounds();
            materialMeshes.Add(merged);
            finalParts.Add(new CombineInstance { mesh = merged, transform = Matrix4x4.identity });
            materials.Add(pair.Key);
        }

        var final = new Mesh
        {
            name = $"MinatoCrowd_{key}_Consolidated",
            indexFormat = IndexFormat.UInt32,
        };
        final.CombineMeshes(finalParts.ToArray(), false, false, false);
        final.RecalculateBounds();
        if (final.vertexCount == 0 || final.subMeshCount != materials.Count)
            throw new System.InvalidOperationException(
                $"{key}: consolidation failed ({final.vertexCount} verts, " +
                $"{final.subMeshCount}/{materials.Count} submeshes).");
        AssetDatabase.CreateAsset(final, $"{BakedMeshDir}/{final.name}.asset");

        var visual = new GameObject("Crowd Visual", typeof(MeshFilter), typeof(MeshRenderer));
        visual.transform.SetParent(root.transform, false);
        visual.GetComponent<MeshFilter>().sharedMesh = final;
        var finalRenderer = visual.GetComponent<MeshRenderer>();
        finalRenderer.sharedMaterials = materials.ToArray();
        finalRenderer.shadowCastingMode = ShadowCastingMode.On;
        finalRenderer.receiveShadows = true;
        finalRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        foreach (var child in root.transform.Cast<Transform>().ToArray())
            if (child != visual.transform) Object.DestroyImmediate(child.gameObject);
        foreach (var mesh in materialMeshes) Object.DestroyImmediate(mesh);
        Debug.Log($"[minato-crowd] consolidated {key}: {triangles:N0} tris, " +
                  $"{groups.Count} material groups, 1 renderer");
    }

    // ------------------------------------------------------------------ spawn

    /// <summary>
    /// Place one crowd figure. <paramref name="groundY"/> is the surface the figure stands on;
    /// the template's own bounds decide how far the origin has to lift so nothing floats or
    /// sinks, which makes grounding structural rather than a tuned constant.
    /// </summary>
    public static GameObject Spawn(string key, Transform parent, Vector3 xzOnGround,
                                   Quaternion rot, float scale, Material ledgeMaterial = null,
                                   Vector3 motionAxis = default, float pathLength = 0f,
                                   float motionSpeed = 0.78f, bool addBench = true)
    {
        if (_templates == null || !_templates.TryGetValue(key, out var t)) return null;

        var go = (GameObject)Object.Instantiate(t.Root, parent);
        go.name = "Crowd_" + key;
        go.hideFlags = HideFlags.None;
        go.SetActive(true);
        go.transform.rotation = rot;
        go.transform.localScale = Vector3.one * scale;

        float lift = t.AlignToBoundsBottom ? -t.BoundsBottom * scale : 0f;
        go.transform.position = xzOnGround + Vector3.up * lift;

        // The seated archetype normally brings its own timber bench. A cafe sitter already has
        // an authored chair under it, so callers that supply their own seat opt out - otherwise
        // a park bench materialises through the middle of every cafe table.
        if (t.Archetype == Sit && addBench)
            AddBench(go.transform, ledgeMaterial);

        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            child.gameObject.hideFlags = HideFlags.None;

        var high = go.transform.Find("LOD0 High Skinned");
        var liveRig = high != null ? high.Find("Rigged Character") : null;
        if (liveRig != null)
        {
            var actor = go.AddComponent<MinatoCrowdActor>();
            MinatoCrowdActor.MotionKind kind =
                t.Archetype == Walk ? MinatoCrowdActor.MotionKind.Walk :
                t.Archetype == Wave ? MinatoCrowdActor.MotionKind.Wave :
                t.Archetype == Sit ? MinatoCrowdActor.MotionKind.Sit :
                t.Archetype == Cyclist ? MinatoCrowdActor.MotionKind.Cyclist :
                MinatoCrowdActor.MotionKind.Idle;
            Vector3 axis = Vector3.ProjectOnPlane(motionAxis, Vector3.up).normalized;
            if (kind != MinatoCrowdActor.MotionKind.Walk || axis.sqrMagnitude < 0.5f)
                pathLength = 0f;
            Vector3 half = axis * Mathf.Max(0f, pathLength) * 0.5f;
            float phase = Mathf.Repeat((xzOnGround.x * 0.071f + xzOnGround.z * 0.113f), 1f);
            actor.Configure(kind, liveRig, go.transform.position - half,
                            go.transform.position + half, motionSpeed, phase);
        }

        AddCullGroup(go);
        return go;
    }

    /// <summary>Full-resolution live skin near/mid, lower-detail 3D far, then long-range cull.</summary>
    static void AddCullGroup(GameObject go)
    {
        var high = go.transform.Find("LOD0 High Skinned");
        var low = go.transform.Find("LOD1 Low 3D");
        if (high == null || low == null) return;
        var bench = go.transform.Find("Timber Waterfront Bench")?.GetComponent<Renderer>();
        var highRenderers = high.GetComponentsInChildren<Renderer>(true).ToList();
        var lowRenderers = low.GetComponentsInChildren<Renderer>(true).ToList();
        if (bench != null)
        {
            highRenderers.Add(bench);
            lowRenderers.Add(bench);
        }
        var group = go.GetComponent<LODGroup>();
        if (group == null) group = go.AddComponent<LODGroup>();
        // Animated cross-fading renders the moving rig and its frozen distant pose together for
        // several frames. Their silhouettes do not line up, so one half can look dimensional
        // while the other reads like a transparent card. At ~90 m a clean LOD hand-off is less
        // noticeable and keeps the silhouette solid from every view direction.
        group.fadeMode = LODFadeMode.None;
        group.animateCrossFading = false;
        group.SetLODs(new[]
        {
            new LOD(HighDetailScreenHeight, highRenderers.ToArray()),
            new LOD(LowDetailScreenHeight, lowRenderers.ToArray()),
        });
        group.RecalculateBounds();
    }

    /// <summary>
    /// A lightweight intentional timber waterfront bench under the seated figure.
    /// </summary>
    static void AddBench(Transform sitter, Material material)
    {
        var go = new GameObject("Timber Waterfront Bench", typeof(MeshFilter),
                                typeof(MeshRenderer));
        go.transform.SetParent(sitter, false);
        go.GetComponent<MeshFilter>().sharedMesh = BenchMesh();
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material != null ? material : BenchMaterial();
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    static Mesh BenchMesh()
    {
        if (_benchMesh != null) return _benchMesh;
        string path = $"{BakedMeshDir}/MinatoCrowd_TimberBench.asset";
        _benchMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (_benchMesh != null) return _benchMesh;
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var parts = new List<CombineInstance>();
        void Box(Vector3 centre, Vector3 size, Quaternion rotation)
        {
            parts.Add(new CombineInstance
            {
                mesh = cube,
                transform = Matrix4x4.TRS(centre, rotation, size),
            });
        }
        // Slatted seat, two legs and a low back: recognisably intentional street furniture,
        // but still one 72-triangle renderer shared by every sitter.
        Box(new Vector3(0f, BenchSeatHeight - 0.045f, -0.08f),
            new Vector3(BenchWidth, 0.09f, BenchDepth), Quaternion.identity);
        Box(new Vector3(-0.42f, 0.18f, -0.08f),
            new Vector3(0.09f, 0.36f, 0.30f), Quaternion.identity);
        Box(new Vector3(0.42f, 0.18f, -0.08f),
            new Vector3(0.09f, 0.36f, 0.30f), Quaternion.identity);
        Box(new Vector3(0f, 0.60f, 0.16f),
            new Vector3(BenchWidth, 0.12f, 0.08f), Quaternion.Euler(-8f, 0f, 0f));
        Box(new Vector3(0f, 0.78f, 0.19f),
            new Vector3(BenchWidth, 0.12f, 0.08f), Quaternion.Euler(-8f, 0f, 0f));
        _benchMesh = new Mesh
        {
            name = "MinatoCrowd_TimberBench",
            indexFormat = IndexFormat.UInt32,
        };
        _benchMesh.CombineMeshes(parts.ToArray(), true, true, false);
        _benchMesh.RecalculateBounds();
        AssetDatabase.CreateAsset(_benchMesh, path);
        return _benchMesh;
    }

    static Material BenchMaterial()
    {
        if (_benchMaterial != null) return _benchMaterial;
        string path = $"{BakedMatDir}/MinatoCrowd_TimberBench.mat";
        _benchMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (_benchMaterial == null)
        {
            var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            _benchMaterial = new Material(shader) { name = "MinatoCrowd_TimberBench" };
            SetColor(_benchMaterial, new Color(0.48f, 0.25f, 0.11f));
            if (_benchMaterial.HasProperty("_Smoothness"))
                _benchMaterial.SetFloat("_Smoothness", 0.18f);
            _benchMaterial.enableInstancing = true;
            AssetDatabase.CreateAsset(_benchMaterial, path);
        }
        return _benchMaterial;
    }

    // ------------------------------------------------------------------ helpers

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static Bounds BakedWorldBounds(GameObject go)
    {
        bool any = false;
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            // useScale:true + position/rotation only - the SAME space BakeSkinToStatic places its
            // bake in - plus BakeUnitFix, because on the Kuro-based crowd bodies (0.01 glTF
            // armature scale) BakeMesh itself returns the skin 100x too large (measured 130 m).
            smr.BakeMesh(baked, true);
            float unitFix = MinatoCrowdActor.BakeUnitFix(smr, baked);
            var t = smr.transform;
            foreach (var v in baked.vertices)
            {
                Vector3 w = t.position + t.rotation * (v * unitFix);
                if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; }
                else bounds.Encapsulate(w);
            }
            Object.DestroyImmediate(baked);
        }
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                Vector3 w = mf.transform.TransformPoint(v);
                if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; }
                else bounds.Encapsulate(w);
            }
        }
        return any ? bounds : new Bounds(go.transform.position, Vector3.zero);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
        Directory.CreateDirectory(path);
    }
}
