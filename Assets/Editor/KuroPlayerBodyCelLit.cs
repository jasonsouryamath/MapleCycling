using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reimport-safe, idempotent repair for the recurring "player Kuro renders chrome / blown-out
/// metallic blob" defect on Sakura Pass / Shiosai Coast.
///
/// ROOT CAUSE (confirmed by QaKuroChromeProbe render + material inventory, not guessed):
/// The player body mesh <c>Mesh_0.001</c> ("Kuro on Sakura Pass") carries two materials,
/// <c>Material_0.002</c> and <c>Material_0.003</c>, that are the RAW glTF import materials
/// embedded in <c>KuroNPC_KuroAnime_Rigged.glb</c>, still bound to
/// <c>Shader Graphs/glTF-pbrMetallicRoughness</c> with metallicFactor = 1 and roughnessFactor = 1.
/// A metallic = 1 surface mirrors its surroundings, so under Sakura/Shiosai daylight the body
/// reads as chrome / wet plastic while the (already-CelLit) bicycle looks correct. The
/// <c>SmileDecal</c> mouth (<c>KuroSmile</c>) is on the same unconverted glTF shader.
///
/// Every re-instantiation of the character GLB (see <see cref="KuroPlayerModelSwap"/>,
/// ShiosaiRealisticRiderPoc) re-binds the body renderers to those embedded glTF materials, which
/// is why this has now reverted THREE times. The correct matte CelLit body recipe lives on disk
/// as <c>Assets/Kuro/Materials/Player_Material_0.mat</c> (MapleRide/HDRP/CelLit, _SpecStrength
/// 0.04, _Gloss 0.01, flat 3-step ramp, NO metallic) but is no longer assigned to any renderer.
///
/// THE FIX: for each player renderer material still on the glTF shader, build a PERSISTED CelLit
/// .mat that clones <c>Player_Material_0</c>'s approved matte recipe and swaps in that material's
/// OWN base-colour texture (Mesh_0.001 has two distinct albedo atlases -
/// kuro_bodyK_basecolor and Image_0 - so a single shared material would lose one), then re-point
/// the renderer at the clone. Because the scene now references standalone .mat assets, a plain
/// GLB reimport can no longer revert the body; only a re-instantiate can, which is why
/// <see cref="KuroPlayerModelSwap"/> calls <see cref="ConvertPlayerBody"/> after its swap and
/// <see cref="AssertNoGltfBody"/> fails the build loudly if the body is ever left on glTF metallic.
/// </summary>
public static class KuroPlayerBodyCelLit
{
    const string ScenePath   = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName  = "Kuro on Sakura Pass";
    internal const string MatDir       = "Assets/Kuro/Materials";
    const string TemplatePath = "Assets/Kuro/Materials/Player_Material_0.mat";
    internal const string CelLitName   = "MapleRide/HDRP/CelLit";
    internal const string GltfShader   = "Shader Graphs/glTF-pbrMetallicRoughness";
    const string DecalRendererName = "SmileDecal";

    // ---- Player-exclusive skin tone -----------------------------------------------------------
    // The body atlas material (Image_0, which carries the bare SKIN together with the kit) is
    // SHARED by ~8 Kuro-visual NPCs (verified: 11 refs in SakuraPass.unity), so its highlight
    // white-point blowout (skin clipping to near-white under any bright map) can only be corrected
    // on a PLAYER-EXCLUSIVE clone. This step runs after the CelLit conversion so a fresh re-stage
    // always leaves the player on the skin-corrected clone while the NPCs keep the shared asset.
    const string SkinAtlasMatName   = "PlayerBody_Image_0_CelLit";
    const string PlayerOnlySkinPath = "Assets/Kuro/Materials/PlayerOnly_Body_Image_0_CelLit.mat";

    [MenuItem("MapleRide/Kuro/Fix Player Body CelLit (reimport-safe)")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find(PlayerName);
        if (player == null)
        {
            Debug.LogError($"[player-body-cellit] player '{PlayerName}' not found.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        LogInventory("BEFORE", player);
        int converted = ConvertPlayerBody(player, save: true);
        Debug.Log($"[player-body-cellit] converted {converted} material(s) to matte CelLit.");
        LogInventory("AFTER ", player);

        bool ok = AssertNoGltfBody(player);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// Idempotent. Converts every renderer material under <paramref name="player"/> that is still
    /// on the raw glTF metallic shader onto a persisted matte CelLit clone (per-material albedo
    /// preserved). Safe to call from a staging pass after re-instantiating the character GLB.
    /// Returns the number of source materials converted.
    /// </summary>
    public static int ConvertPlayerBody(GameObject player, bool save)
    {
        var celLit = Shader.Find(CelLitName);
        if (celLit == null) { Debug.LogError($"[player-body-cellit] shader '{CelLitName}' not found."); return 0; }

        var template = AssetDatabase.LoadAssetAtPath<Material>(TemplatePath);
        if (template == null) { Debug.LogError($"[player-body-cellit] recipe template not found at {TemplatePath}."); return 0; }

        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Kuro", "Materials");

        var cache = new Dictionary<Material, Material>();
        int converted = 0;
        bool touchedScene = false;
        var bike = player.transform.Find("Bike");

        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (bike != null && r.transform.IsChildOf(bike)) continue;
            var mats = r.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null) continue;
                if (m.shader.name != GltfShader) continue; // already CelLit / intentionally something else

                if (!cache.TryGetValue(m, out var clone))
                {
                    clone = BuildCelLitBody(m, template, celLit, r.name == DecalRendererName);
                    cache[m] = clone;
                    converted++;
                }
                if (mats[i] != clone) { mats[i] = clone; touched = true; }
            }
            if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); touchedScene = true; }
        }

        // Player-exclusive skin tone: swap the shared body atlas material for the skin-corrected
        // clone on the player's renderers only (never on the NPCs that share the atlas material).
        touchedScene |= AssignPlayerSkin(player);

        if (touchedScene)
        {
            AssetDatabase.SaveAssets();
            if (save)
            {
                var scene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        return converted;
    }

    /// <summary>
    /// Builds (or refreshes, so re-runs are idempotent) a persisted CelLit .mat that carries
    /// Player_Material_0's approved matte recipe and this source material's OWN albedo texture
    /// and base-colour factor. Textures are read BEFORE any shader swap - assigning .shader on the
    /// source would drop glTF-named properties.
    /// </summary>
    static Material BuildCelLitBody(Material src, Material template, Shader celLit, bool isDecal)
    {
        // Read the source's own albedo + base colour while it is still on the glTF shader.
        Texture baseMap = null;
        foreach (var n in new[] { "baseColorTexture", "_BaseColorMap", "_BaseMap", "_MainTex", "_BaseColorTexture" })
            if (src.HasProperty(n)) { var t = src.GetTexture(n); if (t != null) { baseMap = t; break; } }

        Color baseColor = Color.white;
        foreach (var n in new[] { "baseColorFactor", "_BaseColorFactor", "_BaseColor", "_Color" })
            if (src.HasProperty(n)) { baseColor = src.GetColor(n); break; }

        string key = Sanitize(baseMap != null ? baseMap.name : src.name.Replace(" (Instance)", ""));
        string path = $"{MatDir}/PlayerBody_{key}_CelLit.mat";

        // Start from the approved matte recipe, then override only albedo/base-colour/decal-cull.
        var built = new Material(template);
        built.CopyPropertiesFromMaterial(template);
        built.shader = celLit;
        built.name = $"PlayerBody_{key}_CelLit";

        // The decal has no atlas (flat baseColorFactor); everything else keeps its own atlas. Do
        // not leave the template's body atlas on a texture-less decal, or the mouth samples skin.
        if (built.HasProperty("_MainTex")) built.SetTexture("_MainTex", baseMap);
        if (built.HasProperty("_Color")) built.SetColor("_Color", baseColor);
        if (isDecal && built.HasProperty("_Cull")) built.SetFloat("_Cull", 0f); // winding-agnostic decal

        // Same fix as NpcCelLitConversion.ApplyCelLitRecipe for the Minato helmet/jersey "tear"
        // finding: Player_Material_0 is built on MapleRide/HDRP/CelLit, a shader authored for
        // static world-anchored props, and carries that shader's non-zero procedural
        // detail/tint-variation/wear/moss/grime/dapple weathering (MR_Weather in
        // MapleRideCelLit.shader), driven by absolute world position and world-space normal.
        // The player body clones this same template, so it silently carries the identical
        // defect (never previously reported/looked at closely on the player, but present).
        // Zero it here too so the player and every NPC get the same fix.
        if (built.HasProperty("_DetailAmount")) built.SetFloat("_DetailAmount", 0f);
        if (built.HasProperty("_TintVariation")) built.SetFloat("_TintVariation", 0f);
        if (built.HasProperty("_WearAmount")) built.SetFloat("_WearAmount", 0f);
        if (built.HasProperty("_MossAmount")) built.SetFloat("_MossAmount", 0f);
        if (built.HasProperty("_GrimeAmount")) built.SetFloat("_GrimeAmount", 0f);
        if (built.HasProperty("_DappleStrength")) built.SetFloat("_DappleStrength", 0f);

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = celLit;
            existing.CopyPropertiesFromMaterial(built);
            existing.name = built.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(built);
            Debug.Log($"[player-body-cellit] refreshed {path} (albedo='{(baseMap != null ? baseMap.name : "none")}')");
            return existing;
        }

        AssetDatabase.CreateAsset(built, path);
        Debug.Log($"[player-body-cellit] created {path} (albedo='{(baseMap != null ? baseMap.name : "none")}')");
        return built;
    }

    /// <summary>
    /// Player-only. Replaces the shared body atlas material (skin+kit) on the player's renderers
    /// with a persisted, player-EXCLUSIVE clone carrying the skin white-point recipe, so the bare
    /// skin reads as a natural warm tone (not blown white) on any map. NPCs that reference the
    /// shared atlas material are never touched. Idempotent. Returns true if any slot changed.
    /// </summary>
    static bool AssignPlayerSkin(GameObject player)
    {
        var skinMat = EnsurePlayerSkinMat();
        if (skinMat == null) return false;
        bool changed = false;
        var bike = player.transform.Find("Bike");
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (bike != null && r.transform.IsChildOf(bike)) continue;
            var mats = r.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name == SkinAtlasMatName)
                { mats[i] = skinMat; touched = true; }
            if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); changed = true; }
        }
        return changed;
    }

    /// <summary>Create (or refresh) the player-exclusive, skin-corrected clone of the body atlas material.</summary>
    static Material EnsurePlayerSkinMat()
    {
        var shared = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{SkinAtlasMatName}.mat");
        if (shared == null) return null; // player never used the Image_0 atlas; nothing to correct
        var existing = AssetDatabase.LoadAssetAtPath<Material>(PlayerOnlySkinPath);
        if (existing == null)
        {
            var clone = new Material(shared);
            clone.CopyPropertiesFromMaterial(shared);
            clone.name = "PlayerOnly_Body_Image_0_CelLit";
            ApplySkinRecipe(clone);
            AssetDatabase.CreateAsset(clone, PlayerOnlySkinPath);
            Debug.Log($"[player-body-cellit] created skin-corrected {PlayerOnlySkinPath}");
            return clone;
        }
        existing.shader = shared.shader;
        existing.CopyPropertiesFromMaterial(shared);
        existing.name = "PlayerOnly_Body_Image_0_CelLit";
        ApplySkinRecipe(existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    /// <summary>
    /// The skin white-point recipe: the CelLit highlight soft-clip pushes bright skin past the
    /// scene tonemap's 1.0 ceiling and it reads white. Lowering the white point restores the
    /// atlas's own warm skin while the near-black kit (no highlight) stays black; a region-
    /// independent warm character self-light gives the skin form + warmth on any map.
    /// </summary>
    static void ApplySkinRecipe(Material m)
    {
        if (m.HasProperty("_HighlightRolloff")) m.SetFloat("_HighlightRolloff", 0.80f);
        if (m.HasProperty("_HighlightKnee"))    m.SetFloat("_HighlightKnee", 0.52f);
        if (m.HasProperty("_HighlightWhite"))   m.SetFloat("_HighlightWhite", 1.06f);
        if (m.HasProperty("_ShadowAmbient"))    m.SetFloat("_ShadowAmbient", 0.55f);
        if (m.HasProperty("_MatteFloor"))       m.SetColor("_MatteFloor", new Color(0.165f, 0.165f, 0.177f, 1f));
        if (m.HasProperty("_CharacterLight"))    m.SetFloat("_CharacterLight", 0.85f);
        if (m.HasProperty("_CharKeyDir"))        m.SetVector("_CharKeyDir", new Vector4(0.35f, 0.60f, 0.55f, 0f));
        if (m.HasProperty("_CharKeyColor"))      m.SetColor("_CharKeyColor", new Color(1f, 0.98f, 0.94f, 1f));
        if (m.HasProperty("_CharKeyIntensity"))  m.SetFloat("_CharKeyIntensity", 1.25f);
        if (m.HasProperty("_CharFillColor"))     m.SetColor("_CharFillColor", new Color(0.62f, 0.72f, 0.95f, 1f));
        if (m.HasProperty("_CharFillIntensity")) m.SetFloat("_CharFillIntensity", 0.45f);
        if (m.HasProperty("_CharAmbient"))       m.SetFloat("_CharAmbient", 0.34f);
        if (m.HasProperty("_EdgeRimColor"))      m.SetColor("_EdgeRimColor", new Color(0.72f, 0.80f, 1.0f, 1f));
        if (m.HasProperty("_EdgeRimStrength"))   m.SetFloat("_EdgeRimStrength", 0.18f);
        if (m.HasProperty("_EdgeRimPower"))      m.SetFloat("_EdgeRimPower", 3.0f);
        ApplyDetailRecipe(m);
    }

    /// <summary>
    /// PLAYER DEPTH (2026-09-26): Kuro's GLB ships a baked 2048 tangent-space normal map (fabric
    /// folds, seams, embossed logos, face relief) that CelLit used to ignore. CelLit now takes it
    /// opt-in; only this player-exclusive material turns it on, so the ~8 Kuro-visual NPCs that
    /// share the Image_0 atlas material are unchanged.
    /// </summary>
    public const float PlayerNormalStrength = 0.75f;
    public static void ApplyDetailRecipe(Material m)
    {
        if (m == null || !m.HasProperty("_NormalMap")) return;
        Texture2D normal = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb"))
            if (o is Texture2D t && t.name == "normal") { normal = t; break; }
        if (normal == null) { Debug.LogWarning("[player-body-cellit] no 'normal' texture in Kuro's GLB - detail recipe skipped"); return; }
        m.SetTexture("_NormalMap", normal);
        m.SetFloat("_NormalStrength", PlayerNormalStrength);
        EditorUtility.SetDirty(m);
    }

    /// <summary>Applies only the detail recipe to the existing player-only material (no re-stage).</summary>
    public static void ApplyDetailToPlayerMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(PlayerOnlySkinPath);
        if (m == null) { Debug.LogError($"[player-body-cellit] {PlayerOnlySkinPath} missing"); return; }
        ApplyDetailRecipe(m);
        AssetDatabase.SaveAssets();
        Debug.Log($"[player-body-cellit] detail recipe on {PlayerOnlySkinPath}: normal map " +
                  $"{(m.GetTexture("_NormalMap") != null ? m.GetTexture("_NormalMap").name : "none")} @ {m.GetFloat("_NormalStrength"):0.00}");
    }

    /// <summary>
    /// Loud guard: returns false (and logs an error) if ANY renderer material under the player is
    /// still on the raw glTF metallic shader. A promo/kit/build pass should treat false as a hard
    /// failure rather than silently shipping a chrome Kuro.
    /// </summary>
    public static bool AssertNoGltfBody(GameObject player)
    {
        var bad = new List<string>();
        var bike = player.transform.Find("Bike");
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (bike != null && r.transform.IsChildOf(bike)) continue;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name == GltfShader)
                    bad.Add($"{r.name}/{m.name}");
        }

        if (bad.Count > 0)
        {
            Debug.LogError($"[player-body-cellit] ASSERTION FAILED: {bad.Count} player material(s) still on " +
                           $"'{GltfShader}' (metallic glTF import -> chrome): {string.Join(", ", bad)}. " +
                           "Run MapleRide/Kuro/Fix Player Body CelLit (reimport-safe).");
            return false;
        }
        Debug.Log("[player-body-cellit] assertion OK: no player material on the glTF metallic shader.");
        return true;
    }

    static void LogInventory(string label, GameObject player)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[player-body-cellit] {label} inventory for '{player.name}':");
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name != "Mesh_0.001" && r.name != DecalRendererName) continue; // character body only
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { sb.AppendLine($"    {r.name} -> <NULL>"); continue; }
                string spec = m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength").ToString("F3") : "-";
                string gloss = m.HasProperty("_Gloss") ? m.GetFloat("_Gloss").ToString("F3") : "-";
                string mf = m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor").ToString("F2")
                          : (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("F2") : "-");
                string rf = m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor").ToString("F2") : "-";
                sb.AppendLine($"    {r.name} | mat={m.name} | shader={m.shader.name} | spec={spec} gloss={gloss} metallicFactor={mf} roughnessFactor={rf}");
            }
        }
        Debug.Log(sb.ToString());
    }

    static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }
}
