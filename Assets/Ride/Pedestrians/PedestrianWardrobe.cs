using System;
using UnityEngine;

/// <summary>
/// AMBIENT PEDESTRIANS (copilot, 2026-09-26). Runtime index of the civilian looks that already
/// exist on disk for the Minato crowd donors (made by MapleCityLife.Dress /
/// tools/blender/build_maple_city_life_textures.py): per donor, its helmet-sunk live-skin mesh,
/// its hair-cap meshes and every civilian ("V") look's body / hair / distance / hair-cap
/// material. It only REFERENCES those assets (GUID-stable); nothing is generated here.
///
/// Built by <c>PedestrianWardrobeBuilder.Build</c> (editor) into
/// Assets/Resources/Pedestrians/PedestrianWardrobe.asset, loaded by <see cref="PedestrianAppearance"/>
/// so figures on EVERY map (Minato too, which had none) can be dressed and re-rolled at runtime
/// without re-staging the 527 MB SakuraPass scene.
/// </summary>
public sealed class PedestrianWardrobe : ScriptableObject
{
    public const string ResourcePath = "Pedestrians/PedestrianWardrobe";

    [Serializable]
    public sealed class Look
    {
        public string id;          // "V0".."V5"
        public Material body;      // LOD0 atlas
        public Material hair;      // LOD0 NpcHair strip (null if the donor has none)
        public Material dist;      // LOD1 atlas
        public Material hairCap;   // solid hair colour for the cap
    }

    [Serializable]
    public sealed class Donor
    {
        public string key;         // e.g. "05_Walk_Rina"
        public Mesh helmetless;    // Mesh_0 with the helmet shell pulled inside the cap
        public Mesh[] caps = Array.Empty<Mesh>();    // hair-cap styles (Head-bone space)
        public Look[] looks = Array.Empty<Look>();
    }

    public Donor[] donors = Array.Empty<Donor>();

    public Donor Find(string key)
    {
        if (donors == null) return null;
        foreach (var d in donors) if (d != null && d.key == key) return d;
        return null;
    }

    static PedestrianWardrobe _cached;
    static bool _tried;
    public static PedestrianWardrobe Load()
    {
        if (_tried) return _cached;
        _tried = true;
        _cached = Resources.Load<PedestrianWardrobe>(ResourcePath);
        if (_cached == null) Debug.LogWarning("[peds] no PedestrianWardrobe in Resources - figures keep their staged look.");
        return _cached;
    }
}
