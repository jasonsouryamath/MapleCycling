using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Opt-in preview helper for the Kuro graybox BLOCKOUT (milestone: silhouette pass).
///
/// This does NOT run automatically and does NOT modify any saved scene on its own - it only
/// drops the blockout GLBs into the scene that is already open when a menu item is clicked,
/// so the blockout can be eyeballed against the real Sakura Pass scale in the editor.
///
/// Source assets are built by tools/blender/build_kuro_blockout.py; locked dimensions live in
/// Assets/Kuro/Blockout/KURO_BLOCKOUT_DIMENSIONS.md. Every dimension there is PROVISIONAL.
/// </summary>
public static class KuroBlockoutPreview
{
    private const string BlockoutDir = "Assets/Kuro/Blockout";

    // Root object names are exact so repeat clicks replace rather than duplicate.
    private const string RidingRoot = "KuroBlockout_Riding";
    private const string StandingRoot = "KuroBlockout_Standing";
    private const string BikeRoot = "KuroBlockout_Bike";

    [MenuItem("MapleRide/Blockout/Spawn Kuro blockout (riding)")]
    public static void SpawnRiding() => Spawn("kuro_blockout_riding", RidingRoot, Vector3.zero);

    [MenuItem("MapleRide/Blockout/Spawn Kuro blockout (standing)")]
    public static void SpawnStanding() => Spawn("kuro_blockout_standing", StandingRoot, new Vector3(1.2f, 0f, 0f));

    [MenuItem("MapleRide/Blockout/Spawn bike blockout only")]
    public static void SpawnBike() => Spawn("kuro_blockout_bike", BikeRoot, new Vector3(-1.6f, 0f, 0f));

    [MenuItem("MapleRide/Blockout/Remove all blockout previews")]
    public static void RemoveAll()
    {
        foreach (var n in new[] { RidingRoot, StandingRoot, BikeRoot })
        {
            var existing = GameObject.Find(n);
            while (existing != null)
            {
                Object.DestroyImmediate(existing);
                existing = GameObject.Find(n);
            }
        }
        Debug.Log("[kuro-blockout] removed blockout previews");
    }

    private static void Spawn(string glbName, string rootName, Vector3 at)
    {
        string path = Path.Combine(BlockoutDir, glbName + ".glb").Replace('\\', '/');
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null)
        {
            Debug.LogError($"[kuro-blockout] missing {path} - run tools/blender/build_kuro_blockout.py first");
            return;
        }

        // idempotent: exact-name match, prune every duplicate before re-adding
        var stale = GameObject.Find(rootName);
        while (stale != null)
        {
            Object.DestroyImmediate(stale);
            stale = GameObject.Find(rootName);
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = rootName;
        go.transform.position = at;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;   // GLB is authored at 1.0 = metres

        var bounds = Measure(go);
        Debug.Log($"[kuro-blockout] spawned {rootName} at {at} - bounds size {bounds.size} " +
                  $"(expected ~1.38 m tall standing / 1.33 m long on the bike)");
        Selection.activeGameObject = go;
    }

    private static Bounds Measure(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }
}
