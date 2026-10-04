using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PEDESTRIAN MODEL-SWAP PIPELINE (claude-peds, 2026-09-26). The list of drop-in humanoid
/// character models that may replace the donor "rider" bodies of ambient pedestrians.
///
/// Asset: Assets/Resources/Pedestrians/PedestrianModelLibrary.asset, (re)built by the editor step
/// <c>PedestrianModelLibraryBuilder.Build</c>, which scans Assets/Characters/Pedestrians/Import/
/// for every FBX/GLB, measures it and makes its cel (CelLit) materials. Hand edits to the
/// per-model fields (enabled, tags, heightScale, regions, variants) survive a rebuild.
///
/// OPT-IN, two switches (both must be on, so with either off the pedestrians are unchanged):
///   1. PlayerPrefs "MapleRide.Pedestrians.ModelSet" = a set name (e.g. "default"); empty/missing = off.
///   2. The library's <see cref="enabledGlobally"/> flag, plus each model's own <see cref="Entry.enabled"/>
///      and <see cref="Entry.regions"/> (region ids the model may appear in, empty = everywhere) and
///      <see cref="Entry.sets"/> (set names it belongs to, empty = every set).
/// Docs: docs/PEDESTRIAN_MODEL_IMPORT.md.
/// </summary>
[CreateAssetMenu(menuName = "MapleRide/Pedestrian Model Library", fileName = "PedestrianModelLibrary")]
public sealed class PedestrianModelLibrary : ScriptableObject
{
    public const string ModelSetPref = "MapleRide.Pedestrians.ModelSet";
    public const string ResourcePath = "Pedestrians/PedestrianModelLibrary";

    public enum Rig { Unknown, Mixamo, VRoid, Plain, Other }

    [Serializable]
    public sealed class Variant
    {
        public string id = "";
        [Tooltip("Replaces the model's materials slot by slot (null slots keep the default).")]
        public Material[] materials = Array.Empty<Material>();
    }

    [Serializable]
    public sealed class Entry
    {
        public string id = "";
        public GameObject prefab;
        [Tooltip("Off = this model is never used, even when the library is on.")]
        public bool enabled = true;
        [Tooltip("Free-form body tags, e.g. male, female, senior, child, runner.")]
        public string[] tags = Array.Empty<string>();
        public Rig rig;
        [Tooltip("True when the importer built a valid Humanoid Avatar (bones come from Animator.GetBoneTransform).")]
        public bool humanoid;
        [Tooltip("Measured standing height of the imported prefab at scale 1, metres (feet to crown).")]
        public float nativeHeight = 1.8f;
        [Tooltip("1 = exactly the height of the donor figure it replaces. 0.9 = 10% shorter, etc.")]
        public float heightScale = 1f;
        [Tooltip("Relative pick weight among the eligible models.")]
        public float weight = 1f;
        [Tooltip("Region ids (RegionCatalog) this model may appear in. Empty = all.")]
        public string[] regions = Array.Empty<string>();
        [Tooltip("Model-set names this model belongs to. Empty = every set.")]
        public string[] sets = Array.Empty<string>();
        [Tooltip("Default cel materials, one per material slot of the model's renderers (in renderer order).")]
        public Material[] materials = Array.Empty<Material>();
        [Tooltip("Optional outfit variants; one is picked per pedestrian.")]
        public Variant[] variants = Array.Empty<Variant>();
        [Tooltip("Upper-arm lowering from the bind pose, degrees. <0 = automatic (T-pose rigs ~78).")]
        public float armDropDegrees = -1f;
        [Tooltip("Humanoid idle / walk clips found beside the model (kept for an Animator-based walk; unused by the procedural walk).")]
        public AnimationClip idleClip, walkClip;
        public string sourcePath = "";
        public string notes = "";
    }

    /// <summary>
    /// PRIMARY path (user direction 2026-09-26): pedestrians are the Kuro rider chibi bodies (painted
    /// anime face, RiderBlink eyelids) off the bike. The Maple crowd donors already ARE Kuro-rig
    /// bodies standing in MinatoCrowdActor's crowd pose (helmet sunk by the wardrobe's helmetless
    /// mesh); this set replaces the hair cap with authored hair meshes, the civilian atlas with
    /// street-clothes kits (PedKit_*.png, same UV layout as KuroKit_*.png) and adds blinking.
    /// Used for every set name except "fbx" (which forces the FBX model swap above as a test).
    /// </summary>
    [Serializable]
    public sealed class KuroRiderSet
    {
        public bool enabled = true;
        [Tooltip("Street-clothes atlases in the Kuro rider UV layout (PedKit_<name>.png).")]
        public Texture2D[] streetKits = Array.Empty<Texture2D>();
        [Tooltip("Beach/resort atlases (PedKit_Aloha/Bikini/Sundress/Tank/Trunks*.png), same UV layout. Used for Nagisa Bay pedestrians.")]
        public Texture2D[] beachKits = Array.Empty<Texture2D>();
        [Tooltip("Hair meshes (PedHair_*.fbx): origin at the Head bone, +Y up, +Z = face direction, metres at figure scale 1.")]
        public GameObject[] hairStyles = Array.Empty<GameObject>();
        public Vector3 hairOffset = Vector3.zero;
        public float hairScale = 1f;
        [Tooltip("Greyscale strand texture for the hair (PedHair_Strands.png); tinted per figure on a clone of the body's CelLit material.")]
        public Texture2D hairStrands;
        [Tooltip("Hair tints (PROVISIONAL palette: black, dark brown, brown, chestnut, auburn, ash, grey, blonde).")]
        public Color[] hairColours = {
            new Color(0.10f, 0.09f, 0.10f), new Color(0.22f, 0.15f, 0.11f), new Color(0.38f, 0.25f, 0.16f),
            new Color(0.50f, 0.30f, 0.17f), new Color(0.52f, 0.22f, 0.14f), new Color(0.45f, 0.42f, 0.40f),
            new Color(0.70f, 0.70f, 0.72f), new Color(0.80f, 0.66f, 0.42f) };
        [Tooltip("Attach RiderBlink eyelids (only bodies with baked lid data blink).")]
        public bool blink = true;
        [Tooltip("Manifest JSON written by the model generator, if any (logged by the builder).")]
        public string manifestPath = "";
    }
    public KuroRiderSet kuroRider = new KuroRiderSet();
    public const string FbxTestSet = "fbx";

    [Tooltip("Master switch for the whole library. Off = the old donor look everywhere.")]
    public bool enabledGlobally = true;
    [Tooltip("Fraction of eligible Maple City pedestrians that get a model (1 = all).")]
    [Range(0f, 1f)] public float coverage = 1f;
    [Tooltip("Region ids where the swap may run at all. Empty = all regions with ambient pedestrians.")]
    public string[] regions = { "maple_city" };
    public List<Entry> models = new List<Entry>();

    static PedestrianModelLibrary _cached;
    static bool _loaded;

    public static PedestrianModelLibrary Load()
    {
        if (!_loaded) { _cached = Resources.Load<PedestrianModelLibrary>(ResourcePath); _loaded = true; }
        return _cached;
    }

    /// <summary>The active model-set name, or null when the pipeline is off (the default).</summary>
    public static string ActiveSet()
    {
        string s = PlayerPrefs.GetString(ModelSetPref, "");
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static bool Allows(string[] list, string value) =>
        list == null || list.Length == 0 || Array.IndexOf(list, value) >= 0;

    /// <summary>Models eligible for a region under the active set (empty when switched off).</summary>
    public List<Entry> Eligible(string regionId)
    {
        var result = new List<Entry>();
        string set = ActiveSet();
        if (set == null || !enabledGlobally || !Allows(regions, regionId)) return result;
        foreach (var e in models)
            if (e != null && e.enabled && e.prefab != null && e.weight > 0f &&
                Allows(e.regions, regionId) && Allows(e.sets, set))
                result.Add(e);
        return result;
    }

    /// <summary>Deterministic weighted pick for a stable per-figure hash.</summary>
    public static Entry Pick(List<Entry> eligible, uint hash)
    {
        if (eligible == null || eligible.Count == 0) return null;
        float total = 0f;
        foreach (var e in eligible) total += e.weight;
        float x = (hash % 100000u) / 100000f * total;
        foreach (var e in eligible) { x -= e.weight; if (x <= 0f) return e; }
        return eligible[eligible.Count - 1];
    }
}
