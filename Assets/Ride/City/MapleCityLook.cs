using UnityEngine;

/// <summary>
/// MAPLE CITY LIFE - POPULATION RULES (copilot C8, 2026-09-25). Records how a Maple City Life
/// figure was dressed so the play test can prove "no helmeted pedestrians": every Minato donor
/// is a dismounted cyclist, so a pavement figure only passes when it wears a civilian (or
/// sportswear) atlas AND a hair cap over the baked-in helmet. Written by MapleCityLife.Dress.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapleCityLook : MonoBehaviour
{
    public enum Role { Walker, Runner, Sitter, CafeRegular, Chat, Other }

    public Role role;
    public string donor;
    /// <summary>Look id, e.g. "V2" (civilian) or "R1" (running kit); empty = still in cycling kit.</summary>
    public string look;
    /// <summary>The body atlas is a MapleLife recolour, not the donor's cycling kit.</summary>
    public bool civilianAtlas;
    /// <summary>The hair cap that covers the baked-in helmet.</summary>
    public Renderer hairCap;

    /// <summary>True when this figure cannot read as a helmeted, kitted cyclist.</summary>
    public bool Masked => civilianAtlas && hairCap != null && hairCap.enabled && hairCap.gameObject.activeInHierarchy;
}
