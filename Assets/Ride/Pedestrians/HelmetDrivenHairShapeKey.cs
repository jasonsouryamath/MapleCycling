using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps authored hair clear of a moving helmet without moving the hair object off the Head
/// bone. The PedHair FBX files contain a small crown-compression shape named
/// <c>HelmetClearance</c>; this component drives it from the helmet bone's local height.
/// </summary>
[DisallowMultipleComponent]
public sealed class HelmetDrivenHairShapeKey : MonoBehaviour
{
    public const string ShapeName = "HelmetClearance";

    [Tooltip("Optional exact helmet bone. If unset, the rig is searched by conventional names.")]
    public Transform helmetBone;
    [Tooltip("Weight at the authored helmet rest pose. Prevents static interpenetration.")]
    [Range(0f, 100f)] public float restWeight = 18f;
    [Tooltip("Additional weight when the helmet bone moves down by this many metres.")]
    [Min(0.001f)] public float compressionDistance = 0.025f;
    public bool allowHeadFallback = true;

    Transform _rigRoot;
    Vector3 _restLocalPosition;
    bool _bound;
    readonly List<SkinnedMeshRenderer> _renderers = new List<SkinnedMeshRenderer>();
    readonly List<int> _shapeIndices = new List<int>();

    /// <summary>Bind after the hair prefab is parented so bone search uses the live donor rig.</summary>
    public void Bind(Transform rigRoot, Transform preferredHelmetBone = null)
    {
        _rigRoot = rigRoot;
        helmetBone = preferredHelmetBone != null && IsHelmetName(preferredHelmetBone.name)
            ? preferredHelmetBone : FindHelmetBone(rigRoot);
        if (helmetBone == null && allowHeadFallback)
            helmetBone = preferredHelmetBone != null ? preferredHelmetBone : FindByName(rigRoot, "Head");
        if (helmetBone == null)
        {
            _bound = false;
            Debug.LogWarning("[hair-shape] no helmet/head bone found for " + name);
            return;
        }

        _restLocalPosition = helmetBone.localPosition;
        _renderers.Clear();
        _shapeIndices.Clear();
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            int index = smr.sharedMesh.GetBlendShapeIndex(ShapeName);
            if (index < 0) continue;
            _renderers.Add(smr);
            _shapeIndices.Add(index);
        }
        _bound = _renderers.Count > 0;
        if (!_bound)
            Debug.LogWarning("[hair-shape] " + name + " has no " + ShapeName + " blend shape");
        Apply();
    }

    void LateUpdate()
    {
        if (_bound) Apply();
    }

    void Apply()
    {
        float drop = _restLocalPosition.y - helmetBone.localPosition.y;
        float weight = Mathf.Clamp(restWeight + Mathf.Max(0f, drop) / compressionDistance * (100f - restWeight), 0f, 100f);
        for (int i = 0; i < _renderers.Count; i++)
        {
            var smr = _renderers[i];
            if (smr != null && smr.sharedMesh != null) smr.SetBlendShapeWeight(_shapeIndices[i], weight);
        }
    }

    static bool IsHelmetName(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        n = n.Replace("_", "").Replace("-", "").ToLowerInvariant();
        return n == "helmet" || n == "helmetbone" || n == "helmetroot" || n == "helmetsocket";
    }

    static Transform FindHelmetBone(Transform root)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (IsHelmetName(t.name)) return t;
        return null;
    }

    static Transform FindByName(Transform root, string wanted)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, wanted, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }
}
