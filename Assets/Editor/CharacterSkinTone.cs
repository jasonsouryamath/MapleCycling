using UnityEditor;
using UnityEngine;

/// <summary>
/// Natural PEACH skin for every character (player + every NPC body), 2026-09-24.
///
/// Skin read as whited-out because MapleRide/HDRP/CelLit's existing skin controls were all left at
/// their no-op defaults on the character materials: _SkinWarm (deepens + warms bright warm albedo
/// BEFORE lighting), _SkinTame (pulls lit skin that is blowing out back down) and
/// _HighlightRolloff (soft-clips highlights instead of clipping to white). Under each region's
/// bright key + ACES exposure lift, the atlas's pale peach therefore landed at near-white.
///
/// This turns them on for:
///   * Player_Material_0 - the TEMPLATE NpcCelLitConversion copies, so every NPC staged from now
///     on inherits it,
///   * every existing CelLit material under Assets/Kuro whose albedo is a CHARACTER atlas (texture
///     lives under Assets/Kuro) - player clones, all regions' NPC body clones - EXCEPT hair
///     pieces (a honey/pink hair tint would pass the warm-skin test) and bikes.
/// Environment materials are never touched (they live outside Assets/Kuro), and the shader's
/// warm+bright skin test leaves the black kit and neutral whites (socks, logos) exact.
/// ALL VALUES PROVISIONAL art tuning. Menu: MapleRide/Kuro/Apply Natural Skin Tone
/// </summary>
public static class CharacterSkinTone
{
    const string CelLit = "MapleRide/HDRP/CelLit";
    static readonly Color Peach = new Color(0.96f, 0.76f, 0.62f, 1f);

    [MenuItem("MapleRide/Kuro/Apply Natural Skin Tone")]
    public static void Apply()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Kuro" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null || m.shader.name != CelLit) continue;
            string lower = m.name.ToLowerInvariant();
            if (lower.Contains("hair") || lower.Contains("bike") || lower.Contains("aero") ||
                lower.Contains("colnago") || lower.Contains("frame") || lower.Contains("accent"))
                continue;
            var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            bool template = path.EndsWith("/Player_Material_0.mat");
            string texPath = tex != null ? AssetDatabase.GetAssetPath(tex) : "";
            if (!template && !texPath.StartsWith("Assets/Kuro")) continue;   // not a character atlas

            m.SetFloat("_SkinWarm", 1f);
            m.SetColor("_SkinWarmColor", Peach);
            m.SetFloat("_SkinTame", 1f);
            m.SetFloat("_SkinTameGain", 0.66f);
            m.SetFloat("_SkinTameKnee", 0.85f);
            if (m.GetFloat("_HighlightRolloff") < 0.6f) m.SetFloat("_HighlightRolloff", 0.6f);
            EditorUtility.SetDirty(m);
            n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[skin-tone] natural peach skin enabled on {n} character materials.");
    }
}
