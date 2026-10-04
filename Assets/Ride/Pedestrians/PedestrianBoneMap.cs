using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PEDESTRIAN MODEL-SWAP PIPELINE (claude-peds, 2026-09-26): resolves any humanoid rig to the
/// canonical bone names that <see cref="MinatoCrowdActor"/>, <see cref="PedestrianBrain"/>,
/// <see cref="PedestrianAppearance"/> and MapleCityCafeGesture drive by name
/// (Hips, Spine, Spine01, Spine02, Neck, Head, Left/Right Shoulder, Arm, ForeArm, Hand, UpLeg, Leg, Foot, ToeBase).
///
/// Order of preference:
///   1. A valid Humanoid Avatar: Animator.GetBoneTransform(HumanBodyBones.X). Works for ANY rig the
///      Unity importer could map (Mixamo, VRoid/VRM, Meshy/Tripo auto-rigs, custom Blender rigs).
///   2. Name aliases: Mixamo "mixamorig:Hips" (any "prefix:" is stripped), VRoid "J_Bip_C_Hips" /
///      "J_Bip_L_UpperArm", Blender/Rigify-ish "upper_arm.L", and plain names ("Hips", "LeftUpLeg").
/// </summary>
public static class PedestrianBoneMap
{
    public static readonly string[] Canonical =
    {
        "Hips", "Spine", "Spine01", "Spine02", "Neck", "Head",
        "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
        "RightShoulder", "RightArm", "RightForeArm", "RightHand",
        "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
        "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase",
    };

    static readonly (string canon, HumanBodyBones bone)[] Human =
    {
        ("Hips", HumanBodyBones.Hips), ("Spine", HumanBodyBones.Spine), ("Spine01", HumanBodyBones.Chest),
        ("Spine02", HumanBodyBones.UpperChest), ("Neck", HumanBodyBones.Neck), ("Head", HumanBodyBones.Head),
        ("LeftShoulder", HumanBodyBones.LeftShoulder), ("LeftArm", HumanBodyBones.LeftUpperArm),
        ("LeftForeArm", HumanBodyBones.LeftLowerArm), ("LeftHand", HumanBodyBones.LeftHand),
        ("RightShoulder", HumanBodyBones.RightShoulder), ("RightArm", HumanBodyBones.RightUpperArm),
        ("RightForeArm", HumanBodyBones.RightLowerArm), ("RightHand", HumanBodyBones.RightHand),
        ("LeftUpLeg", HumanBodyBones.LeftUpperLeg), ("LeftLeg", HumanBodyBones.LeftLowerLeg),
        ("LeftFoot", HumanBodyBones.LeftFoot), ("LeftToeBase", HumanBodyBones.LeftToes),
        ("RightUpLeg", HumanBodyBones.RightUpperLeg), ("RightLeg", HumanBodyBones.RightLowerLeg),
        ("RightFoot", HumanBodyBones.RightFoot), ("RightToeBase", HumanBodyBones.RightToes),
    };

    /// <summary>Lower-cased, prefix-stripped source names -> canonical name.</summary>
    static readonly Dictionary<string, string> Aliases = BuildAliases();

    static Dictionary<string, string> BuildAliases()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void A(string canon, params string[] names) { foreach (var n in names) d[n] = canon; }
        foreach (var c in Canonical) d[c] = c;                       // plain names
        // Mixamo (after "mixamorig:" is stripped)
        A("Spine01", "Spine1"); A("Spine02", "Spine2");
        A("LeftToeBase", "LeftToe_End_Base", "LeftToes"); A("RightToeBase", "RightToes");
        // VRoid / VRM (J_Bip_<C|L|R>_<Name>)
        A("Hips", "J_Bip_C_Hips"); A("Spine", "J_Bip_C_Spine"); A("Spine01", "J_Bip_C_Chest");
        A("Spine02", "J_Bip_C_UpperChest"); A("Neck", "J_Bip_C_Neck"); A("Head", "J_Bip_C_Head");
        foreach (var (s, side) in new[] { ("L", "Left"), ("R", "Right") })
        {
            A(side + "Shoulder", $"J_Bip_{s}_Shoulder"); A(side + "Arm", $"J_Bip_{s}_UpperArm");
            A(side + "ForeArm", $"J_Bip_{s}_LowerArm"); A(side + "Hand", $"J_Bip_{s}_Hand");
            A(side + "UpLeg", $"J_Bip_{s}_UpperLeg"); A(side + "Leg", $"J_Bip_{s}_LowerLeg");
            A(side + "Foot", $"J_Bip_{s}_Foot"); A(side + "ToeBase", $"J_Bip_{s}_ToeBase");
            // generic Blender / Meshy / Rigify-ish names
            string l = s.ToLowerInvariant();
            A(side + "Arm", $"upper_arm.{s}", $"UpperArm.{s}", $"upperarm_{l}", $"{side}UpperArm");
            A(side + "ForeArm", $"forearm.{s}", $"lowerarm_{l}", $"{side}LowerArm");
            A(side + "Hand", $"hand.{s}", $"hand_{l}");
            A(side + "UpLeg", $"thigh.{s}", $"thigh_{l}", $"{side}UpperLeg", $"{side}Thigh");
            A(side + "Leg", $"shin.{s}", $"calf_{l}", $"{side}LowerLeg", $"{side}Shin", $"{side}Calf");
            A(side + "Foot", $"foot.{s}", $"foot_{l}");
            A(side + "Shoulder", $"shoulder.{s}", $"clavicle_{l}");
        }
        A("Hips", "pelvis", "hip", "Root_Hips"); A("Spine01", "chest", "spine_01"); A("Spine02", "upper_chest", "spine_02", "spine_03");
        A("Neck", "neck_01"); A("Head", "head");
        return d;
    }

    /// <summary>Canonical name for a raw bone name (or null). Strips "anything:" prefixes.</summary>
    public static string CanonicalOf(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        int colon = raw.LastIndexOf(':');
        string n = colon >= 0 ? raw.Substring(colon + 1) : raw;
        return Aliases.TryGetValue(n, out var c) ? c : null;
    }

    /// <summary>
    /// canonical name -> transform for the rig under <paramref name="root"/>. Humanoid Avatar first,
    /// then names. Missing chest bones fall back down the chain (UpperChest -> Chest -> Spine), so
    /// "Spine02" always exists when "Spine" does.
    /// </summary>
    public static Dictionary<string, Transform> Resolve(Transform root, out string source)
    {
        var map = new Dictionary<string, Transform>();
        source = "names";
        var animator = root.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
        {
            foreach (var (canon, bone) in Human)
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null) map[canon] = t;
            }
            source = "avatar";
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var c = CanonicalOf(t.name);
            if (c != null && !map.ContainsKey(c)) map[c] = t;
        }
        if (!map.ContainsKey("Spine01") && map.TryGetValue("Spine", out var sp)) map["Spine01"] = sp;
        if (!map.ContainsKey("Spine02") && map.TryGetValue("Spine01", out var ch)) map["Spine02"] = ch;
        return map;
    }

    /// <summary>Names of the canonical bones the walk needs that are still missing (empty = OK).</summary>
    public static List<string> MissingForWalk(Dictionary<string, Transform> map)
    {
        var need = new[] { "Hips", "Spine02", "Head", "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm",
                           "RightHand", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot" };
        var missing = new List<string>();
        foreach (var n in need) if (!map.ContainsKey(n) || map[n] == null) missing.Add(n);
        return missing;
    }
}
