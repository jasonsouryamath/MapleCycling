using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared KURO-BASED NPC body lookup for every region's roster (2026-09-24).
///
/// Every roster used to instance Coral (KuroNPC_Coral_Rigged.glb), a 21k-island Meshy triangle
/// soup that tears when posed. Minato moved first (MinatoNpcTraffic); this class lets the other
/// rosters make the same move with a two-line change: a rider whose NAME is in a KuroRiders
/// manifest (written by design_assets/3d/kuro/kuro_npc_liveries.py) gets
///   * the Kuro-based body GLB for their hair style (short = Kuro's own GLB),
///   * their KuroKit_&lt;Name&gt;.png atlas in place of the old CoralKit_&lt;Name&gt;.png,
///   * CelLit conversion of every material (fixes the unconverted "chrome" glTF shader the
///     Azora / Maple City / Taka / Fuji rosters never converted, and inherits the peach skin
///     tone from the Player_Material_0 template), and
///   * a per-rider hair tint on the greyscale NpcHair material.
/// Riders not in any manifest (bespoke heroes such as Hanakage) keep their own body and atlas,
/// but Finish still converts their materials to CelLit.
/// </summary>
public static class KuroRiderBodies
{
    public const string Dir = "Assets/Kuro/NPC/KuroRiders";
    const string ShortRig = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";

    [System.Serializable] sealed class Row { public string name, style, hair; }
    [System.Serializable] sealed class Manifest { public Row[] riders; }

    static Dictionary<string, Row> _rows;

    static Dictionary<string, Row> Rows
    {
        get
        {
            if (_rows != null) return _rows;
            _rows = new Dictionary<string, Row>();
            if (!Directory.Exists(Dir)) return _rows;
            foreach (var path in Directory.GetFiles(Dir, "*_riders.json"))
            {
                var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                if (m?.riders == null) continue;
                foreach (var r in m.riders) _rows[r.name] = r;   // later files win on a clash
            }
            return _rows;
        }
    }

    /// <summary>
    /// Re-bakes the greeting portrait of every Kuro-based rider in npc_riders.json (all regions
    /// except Minato, which has its own MinatoNpcTraffic.BakePortraits), one rider at a time via
    /// NpcPortraitBake.BakeSingle - which, unlike BakeAll, never deletes the portraits of riders
    /// that are parked inactive in the open scene. Riders not staged anywhere are skipped.
    /// Menu: MapleRide/NPCs/Bake Kuro-Rider Portraits (All Regions)
    /// </summary>
    [MenuItem("MapleRide/NPCs/Bake Kuro-Rider Portraits (All Regions)", priority = 64)]
    public static void BakeAllRegionPortraits()
    {
        const string scene = "Assets/Scenes/SakuraPass.unity";
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != scene)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene);
        var m = JsonUtility.FromJson<Manifest>(File.ReadAllText($"{Dir}/npc_riders.json"));
        var staged = new HashSet<string>();
        foreach (var g in Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include,
                                                                FindObjectsSortMode.None))
            if (!string.IsNullOrEmpty(g.riderName)) staged.Add(g.riderName);
        int ok = 0, skipped = 0;
        foreach (var r in m.riders)
        {
            if (!staged.Contains(r.name)) { skipped++; continue; }
            if (NpcPortraitBake.BakeSingle(r.name, exposureEv: -1.5f)) ok++;
        }
        Debug.Log($"[kuro-rider] baked {ok} portraits ({skipped} manifest riders not staged).");
    }

    /// <summary>Forget the cached manifests (call after regenerating kits).</summary>
    public static void Reload() => _rows = null;

    public static bool Has(string name) => name != null && Rows.ContainsKey(name);

    public static string RigPath(string name) =>
        Rows[name].style == "short" ? ShortRig : $"{Dir}/KuroRider_{Rows[name].style}.glb";

    public static string KitPath(string name) => $"{Dir}/KuroKit_{name}.png";

    /// <summary>The body prefab for this rider, or null if they are not a Kuro-based rider.</summary>
    public static GameObject Rig(string name)
    {
        if (!Has(name)) return null;
        var path = RigPath(name);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>
    /// After the roster's own ApplyKit: convert every body material to CelLit (idempotent) and,
    /// for Kuro-based riders, tint the hair piece. The conversion runs for EVERY rider, including
    /// bespoke-rig heroes (Akihiro, Shinobu, Akane): the Maple City, Azora and Taka rosters have
    /// no conversion of their own, so skipping it left those three on the raw glTF "chrome" shader.
    /// </summary>
    public static void Finish(GameObject body, string name, string materialDir,
                              Dictionary<Material, Material> sharedCache = null)
    {
        if (body == null) return;
        NpcCelLitConversion.ConvertRiderBody(body, materialDir, sharedCache: sharedCache);
        if (!Has(name))
        {
            NpcCelLitConversion.AssertNoGltfMaterials(body, $"bespoke-rider {name}");
            return;
        }
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.name != "NpcHair" || r.sharedMaterial == null) continue;
            ColorUtility.TryParseHtmlString(Rows[name].hair, out var hair);
            var path = $"{materialDir}/{name}_Hair.mat";
            var clone = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (clone == null)
            {
                clone = new Material(r.sharedMaterial) { name = name + "_Hair" };
                AssetDatabase.CreateAsset(clone, path);
            }
            else clone.CopyPropertiesFromMaterial(r.sharedMaterial);
            clone.shader = r.sharedMaterial.shader;
            if (clone.HasProperty("_Color")) clone.SetColor("_Color", hair);
            EditorUtility.SetDirty(clone);
            r.sharedMaterial = clone;
            EditorUtility.SetDirty(r);
        }
        NpcCelLitConversion.AssertNoGltfMaterials(body, $"kuro-rider {name}");
    }
}
