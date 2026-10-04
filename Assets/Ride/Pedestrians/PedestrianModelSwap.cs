using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// PEDESTRIAN MODEL-SWAP PIPELINE (claude-peds, 2026-09-26). Replaces a donor pedestrian body with
/// a drop-in humanoid model from <see cref="PedestrianModelLibrary"/>. Called by
/// <see cref="PedestrianDirector.Scan"/> BEFORE the figure is dressed, only when the library is
/// switched on (PlayerPrefs "MapleRide.Pedestrians.ModelSet" + library flags); otherwise it never
/// runs and the donor look is unchanged.
///
/// WALK STRATEGY (decision): the existing PROCEDURAL walk (MinatoCrowdActor.DrivenWalkPose + the
/// PedestrianBrain idles/reactions) drives the new model's bones through
/// <see cref="PedestrianBoneMap"/>. The poses are applied about WORLD axes from live bone positions,
/// so they are rig-agnostic; the brain's speed-synced gait, rendered-sole foot planting, yield /
/// startle / phone / photo / map overlays and cafe gestures all keep working unchanged. An
/// Animator + Mixamo clips was rejected: the stand-in only ships Idle + RUNNING (no walk), clips
/// would fight the additive overlays that are written in Update/LateUpdate, humanoid retargeting
/// needs every model to be Humanoid-valid, and 1000 Animators cost far more than the tiered
/// procedural tick. The model's Animator is read once for bone mapping, then disabled.
///
/// What it does to one figure:
///   * instantiates the prefab as "LOD0 Model (id)" under the figure, facing +Z, scaled so its
///     standing height = the donor's measured height x entry.heightScale;
///   * gives it the entry's cel materials (or an outfit variant);
///   * hides every donor renderer (hair cap, kit, LOD1/2 cards) except contact shadows, and rebuilds
///     the LODGroup as one level (the model) that culls at the donor's old LOD1 cut;
///   * retargets MinatoCrowdActor via Rebind (alias map) and lowers T-pose arms.
/// </summary>
public sealed class PedestrianModelSwap : MonoBehaviour
{
    public string modelId = "";
    public string variantId = "";
    public string boneSource = "";
    public float donorHeight, modelHeight;
    public Transform modelRoot;
    public SkinnedMeshRenderer[] skins = Array.Empty<SkinnedMeshRenderer>();
    public Material bodyMaterial;
    readonly List<Renderer> _hidden = new List<Renderer>();

    public static int Swapped, Failed;
    static readonly Dictionary<string, float> _footOffset = new Dictionary<string, float>();
    static readonly Dictionary<string, int> _usage = new Dictionary<string, int>();
    public static string UsageSummary()
    {
        var parts = new List<string>();
        foreach (var kv in _usage) parts.Add($"{kv.Key}={kv.Value}");
        return string.Join(", ", parts);
    }

    /// <summary>Stable per-figure hash from its hierarchy path (same town every run).</summary>
    public static uint Hash(Transform t)
    {
        unchecked
        {
            uint h = 2166136261u;
            for (var p = t; p != null; p = p.parent)
                foreach (char c in p.name) { h ^= c; h *= 16777619u; }
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return h;
        }
    }

    /// <summary>Try to swap; returns the component on success, else null (the figure is untouched).</summary>
    public static PedestrianModelSwap TrySwap(MinatoCrowdActor actor, PedestrianModelLibrary lib, List<PedestrianModelLibrary.Entry> eligible)
    {
        if (actor == null || lib == null || eligible == null || eligible.Count == 0) return null;
        var go = actor.gameObject;
        if (go.GetComponent<PedestrianModelSwap>() != null) return go.GetComponent<PedestrianModelSwap>();
        uint h = Hash(go.transform);
        if (lib.coverage < 1f && (h >> 8) % 1000u >= (uint)(lib.coverage * 1000f)) return null;
        var entry = PedestrianModelLibrary.Pick(eligible, h);
        if (entry == null || entry.prefab == null) return null;

        float donorH = DonorHeight(actor);
        if (donorH < 0.3f || donorH > 4f) { Failed++; return null; }

        var inst = Instantiate(entry.prefab, go.transform, false);
        inst.name = $"LOD0 Model ({entry.id})";
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        SetLayer(inst.transform, go.layer);

        // bones BEFORE the Animator is switched off (GetBoneTransform needs it initialised)
        var map = PedestrianBoneMap.Resolve(inst.transform, out string source);
        var missing = PedestrianBoneMap.MissingForWalk(map);
        if (missing.Count > 0)
        {
            Debug.LogWarning($"[peds-model] '{entry.id}' is missing walk bones ({string.Join(",", missing)}); '{go.name}' keeps its donor body.");
            Destroy(inst);
            Failed++;
            return null;
        }
        foreach (var anim in inst.GetComponentsInChildren<Animator>(true)) anim.enabled = false;

        float native = Mathf.Max(1e-4f, entry.nativeHeight);
        float figS = Mathf.Max(1e-4f, go.transform.lossyScale.y);
        float target = donorH * Mathf.Max(0.1f, entry.heightScale);
        inst.transform.localScale = Vector3.one * (target / (native * figS));
        // feet on the figure root: some exports carry the mesh above/below their origin
        // (measured once per model, in the model's own units, then scaled)
        if (!_footOffset.TryGetValue(entry.id, out float footLocal))
        {
            footLocal = 0f;
            if (BakedYRange(inst.GetComponentsInChildren<SkinnedMeshRenderer>(true), out float lo, out float hi) && hi > lo)
                footLocal = (lo - inst.transform.position.y) / Mathf.Max(1e-6f, inst.transform.lossyScale.y);
            _footOffset[entry.id] = footLocal;
            Debug.Log($"[peds-model] {entry.id}: feet at {footLocal:0.####} model units from its origin (native height {native:0.###}), scale x{inst.transform.localScale.x:0.###}");
        }
        float lift = -footLocal * inst.transform.lossyScale.y;
        if (Mathf.Abs(lift) < 0.5f * target) inst.transform.position += Vector3.up * lift;

        var sw = go.AddComponent<PedestrianModelSwap>();
        sw.modelId = entry.id;
        sw.boneSource = source;
        sw.donorHeight = donorH;
        sw.modelHeight = target;
        sw.modelRoot = inst.transform;
        sw.skins = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // materials: variant (per figure) or the entry default
        Material[] mats = entry.materials;
        if (entry.variants != null && entry.variants.Length > 0)
        {
            var v = entry.variants[(int)((h >> 3) % (uint)entry.variants.Length)];
            if (v != null && v.materials != null && v.materials.Length > 0) { mats = v.materials; sw.variantId = v.id; }
        }
        int slot = 0;
        var newRenderers = new List<Renderer>();
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            var cur = r.sharedMaterials;
            for (int i = 0; i < cur.Length; i++, slot++)
                if (mats != null && slot < mats.Length && mats[slot] != null) cur[i] = mats[slot];
            r.sharedMaterials = cur;
            if (sw.bodyMaterial == null && cur.Length > 0) sw.bodyMaterial = cur[0];
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            newRenderers.Add(r);
        }
        foreach (var smr in sw.skins) smr.updateWhenOffscreen = false;   // perf: see PedestrianAppearance

        // hide the donor (keep contact shadows)
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform.IsChildOf(inst.transform)) continue;
            if (r.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (!r.forceRenderingOff) { r.forceRenderingOff = true; sw._hidden.Add(r); }
        }
        var group = go.GetComponent<LODGroup>();
        if (group != null)
        {
            var old = group.GetLODs();
            float cut = old.Length > 1 ? old[1].screenRelativeTransitionHeight : 0.0045f;
            group.SetLODs(new[] { new LOD(cut, newRenderers.ToArray()) });
            group.RecalculateBounds();
        }

        actor.Rebind(inst.transform, map);
        // T-pose / wide A-pose rigs: lower the upper arms toward the donor's relaxed hang
        float drop = entry.armDropDegrees >= 0f ? entry.armDropDegrees : AutoArmDrop(map, go.transform);
        actor.armDropDegrees = Mathf.Max(actor.armDropDegrees, drop);

        Swapped++;
        _usage[entry.id] = (_usage.TryGetValue(entry.id, out var n) ? n : 0) + 1;
        return sw;
    }

    /// <summary>Degrees between the upper arm and hanging straight down, minus a small hip clearance.</summary>
    static float AutoArmDrop(Dictionary<string, Transform> map, Transform fig)
    {
        float worst = 0f;
        foreach (var (a, b) in new[] { ("LeftArm", "LeftForeArm"), ("RightArm", "RightForeArm") })
        {
            if (!map.TryGetValue(a, out var arm) || !map.TryGetValue(b, out var fore)) continue;
            var dir = fore.position - arm.position;
            if (dir.sqrMagnitude < 1e-8f) continue;
            worst = Mathf.Max(worst, Vector3.Angle(dir, -fig.up));
        }
        return Mathf.Max(0f, worst - 12f);
    }

    /// <summary>Standing height of the donor figure (world metres): renderer bounds top minus the figure root.</summary>
    public static float DonorHeight(MinatoCrowdActor actor)
    {
        var high = actor.transform.Find("LOD0 High Skinned");
        var root = high != null ? high : actor.transform;
        float top = float.MinValue;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || r.forceRenderingOff) continue;
            if (r.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (r.name.StartsWith("Ped ", StringComparison.Ordinal)) continue;
            top = Mathf.Max(top, r.bounds.max.y);
        }
        return top > float.MinValue ? top - actor.transform.position.y : 0f;
    }

    /// <summary>
    /// World min/max Y of the baked skins. BakeMesh's unit quirk (glTF armatures at 0.01) is only
    /// corrected when the raw bake disagrees with the skeleton's own span by 20x or more.
    /// </summary>
    public static bool BakedYRange(SkinnedMeshRenderer[] skins, out float lo, out float hi)
    {
        lo = float.MaxValue; hi = float.MinValue;
        foreach (var smr in skins)
        {
            if (smr == null || smr.sharedMesh == null) continue;
            var m = new Mesh();
            smr.BakeMesh(m, false);   // unscaled bake -> full localToWorld (parent scale included)
            var tr = smr.transform.localToWorldMatrix;
            float bLo = float.MaxValue, bHi = float.MinValue;
            foreach (var b in smr.bones) if (b != null) { bLo = Mathf.Min(bLo, b.position.y); bHi = Mathf.Max(bHi, b.position.y); }
            float rLo = float.MaxValue, rHi = float.MinValue;
            var verts = m.vertices;
            foreach (var v in verts) { float y = tr.MultiplyPoint3x4(v).y; rLo = Mathf.Min(rLo, y); rHi = Mathf.Max(rHi, y); }
            float span = bHi - bLo, raw = rHi - rLo;
            float fix = span > 1e-4f && raw / span > 20f ? MinatoCrowdActor.BakeUnitFix(smr, m) : 1f;
            if (fix != 1f)
            {
                rLo = float.MaxValue; rHi = float.MinValue;
                foreach (var v in verts) { float y = tr.MultiplyPoint3x4(v * fix).y; rLo = Mathf.Min(rLo, y); rHi = Mathf.Max(rHi, y); }
            }
            lo = Mathf.Min(lo, rLo); hi = Mathf.Max(hi, rHi);
            if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
        }
        return hi > lo;
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }
}
