using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Private refined visuals; the existing NPC skeleton, bicycle and gameplay stay live.</summary>
public sealed class RefinedNpcCast : ScriptableObject
{
    [Serializable] public sealed class Part
    {
        public Mesh mesh;
        public Material[] materials;
        public string[] bones;
        public string rootBone;
    }
    [Serializable] public sealed class Rider
    {
        public string name;
        public Part body;
        public Part[] extras;
    }
    public Rider[] riders = Array.Empty<Rider>();
    public Rider Find(string name) => Array.Find(riders, r => r.name == name);

    public static bool Apply(GameObject root, Rider rider)
    {
        if (root == null || rider == null || root.GetComponent<RefinedNpcApplied>() != null) return false;
        SkinnedMeshRenderer body = null;
        foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skin.sharedMesh != null && (body == null || skin.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = skin;
        if (body == null) return false;
        var skeleton = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (var bone in body.bones) if (bone != null) skeleton[bone.name] = bone;
        // Validate all parts before changing any renderer. Never partly replace a mismatched rig.
        if (!Compatible(rider.body, skeleton)) return false;
        foreach (var part in rider.extras) if (!Compatible(part, skeleton)) return false;
        var obsolete = new List<Renderer>();
        if (rider.name == "Hanakage")
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.StartsWith("HanakageHelm", StringComparison.Ordinal))
                { renderer.enabled = false; renderer.forceRenderingOff = true; obsolete.Add(renderer); }
        var originalBounds = body.localBounds;
        Install(body, rider.body, skeleton);
        originalBounds.Encapsulate(body.localBounds); body.localBounds = originalBounds;
        var extraSkins = new List<SkinnedMeshRenderer>();
        foreach (var part in rider.extras)
        {
            var extra = new GameObject("Refined " + rider.name + " " + part.mesh.name);
            extra.transform.SetParent(body.transform, false);
            var skin = extra.AddComponent<SkinnedMeshRenderer>();
            skin.shadowCastingMode = body.shadowCastingMode;
            skin.receiveShadows = body.receiveShadows;
            skin.updateWhenOffscreen = body.updateWhenOffscreen;
            Install(skin, part, skeleton);
            skin.localBounds = body.localBounds; skin.enabled = body.enabled;
            skin.forceRenderingOff = body.forceRenderingOff; extraSkins.Add(skin);
        }
        var marker = root.AddComponent<RefinedNpcApplied>(); marker.identity = rider.name;
        marker.primary = body; marker.extras = extraSkins.ToArray();
        marker.obsolete = obsolete.ToArray();
        return true;
    }
    static bool Compatible(Part part, Dictionary<string, Transform> skeleton)
    {
        if (part == null || part.mesh == null || part.bones == null || part.mesh.bindposes.Length != part.bones.Length) return false;
        foreach (string name in part.bones) if (string.IsNullOrEmpty(name) || !skeleton.ContainsKey(name)) return false;
        return true;
    }
    static void Install(SkinnedMeshRenderer skin, Part part, Dictionary<string, Transform> skeleton)
    {
        var bones = new Transform[part.bones.Length];
        for (int i=0; i<bones.Length; i++) bones[i] = skeleton[part.bones[i]];
        skin.sharedMesh = part.mesh; skin.bones = bones; skin.sharedMaterials = part.materials;
        if (!string.IsNullOrEmpty(part.rootBone) && skeleton.TryGetValue(part.rootBone, out var root)) skin.rootBone = root;
        skin.localBounds = part.mesh.bounds;
    }
}
