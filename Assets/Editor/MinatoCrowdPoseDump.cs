using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Exports the SEATED rider pose that <see cref="NpcCanonicalConformance"/> solves in Unity, so
/// Blender can bake it into a durable crowd GLB.
///
/// Why this exists
/// ---------------
/// The Minato boulevard cyclist archetype was approved from
/// <c>Assets/Kuro/NPC/MinatoNPC_01|07|08_Cyclist_*.png</c>, which
/// <see cref="MinatoNpcRenderSheet"/> rendered from the LIVE conformant fixture - not from a
/// GLB. The only pre-seated GLB on disk (<c>KuroNPC_Coral_OnBike.glb</c>) is a stale export with
/// an exploded head. So the solved seating exists only as Unity transforms, and Blender (which
/// owns decimation and export) has no way to reproduce it by hand.
///
/// This pass dumps, per donor, every deform bone's matrix relative to the body root in BOTH the
/// rest state (prefab as imported) and the conformance-solved state. <c>minato_crowd_export.py</c>
/// transfers that delta onto the Blender rig. Dumping rest as well as pose is what makes the
/// transfer basis-independent: Blender derives a per-bone correction from the rest pair and
/// never has to guess how glTFast mapped glTF joint frames into Unity.
///
/// CAPTURE-ONLY: it builds throwaway fixtures, writes JSON, and discards every scene change.
/// </summary>
public static class MinatoCrowdPoseDump
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>Donors approved as Minato boulevard cyclists, matching the approved PNG indices.</summary>
    public static readonly (string Index, string Donor, string BodyPath)[] Cyclists =
    {
        // Kuro-based crowd cyclists (must match MinatoCrowdPopulation.DonorRig).
        ("01", "Minori",  "Assets/Kuro/NPC/KuroRiders/KuroRider_bob.glb"),
        ("07", "Tatsuya", "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb"),
        ("08", "Ryoko",   "Assets/Kuro/NPC/KuroRiders/KuroRider_long.glb"),
    };

    public static string OutDir =>
        Path.Combine(MapleRidePaths.RepoRoot, "design_assets", "3d", "kuro", "minato_crowd_pose");

    [MenuItem("MapleRide/NPCs/Dump Minato Crowd Cyclist Pose", priority = 26)]
    public static void Dump()
    {
        Directory.CreateDirectory(OutDir);
        int written = 0;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var (index, donor, bodyPath) in Cyclists)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath);
                if (prefab == null)
                {
                    Debug.LogError($"[minato-crowd] missing donor {bodyPath}");
                    continue;
                }

                var rest = CaptureRest(prefab, out var restBikeless);
                var pose = CapturePose(donor, prefab, bodyPath);

                var json = new StringBuilder();
                json.Append("{\n");
                json.Append($"  \"donor\": \"{donor}\",\n");
                json.Append($"  \"index\": \"{index}\",\n");
                json.Append($"  \"source\": \"{bodyPath}\",\n");
                json.Append($"  \"bikeAsset\": \"{NpcCanonicalConformance.BikeAssetPath}\",\n");
                json.Append($"  \"bikeScale\": {F(NpcCanonicalConformance.BikeScale)},\n");
                json.Append($"  \"riderScale\": {F(NpcCanonicalConformance.RiderScale)},\n");
                json.Append("  \"bones\": [\n");
                var names = rest.Keys.ToList();
                for (int i = 0; i < names.Count; i++)
                {
                    string n = names[i];
                    if (!pose.TryGetValue(n, out var posed)) continue;
                    json.Append("    {\"name\": \"" + n + "\", \"rest\": ");
                    json.Append(Mat(rest[n]));
                    json.Append(", \"pose\": ");
                    json.Append(Mat(posed));
                    json.Append(i == names.Count - 1 ? "}\n" : "},\n");
                }
                json.Append("  ]\n}\n");

                string outFile = Path.Combine(OutDir, $"seated_{donor}.json");
                File.WriteAllText(outFile, json.ToString());
                written++;
                Debug.Log($"[minato-crowd] dumped {rest.Count} rest / {pose.Count} posed bones " +
                          $"for {donor} (restRootBones={restBikeless}) -> {outFile}");
            }
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
        Debug.Log($"[minato-crowd] wrote {written} seated-pose dumps to {OutDir}");
    }

    /// <summary>Bone matrices of the donor exactly as imported, relative to the prefab root.</summary>
    static Dictionary<string, Matrix4x4> CaptureRest(GameObject prefab, out int boneCount)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "~MinatoCrowdRest";
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        try
        {
            var map = BoneMatrices(instance.transform);
            boneCount = map.Count;
            return map;
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    /// <summary>Bone matrices after the canonical seated solve, relative to the body root.</summary>
    static Dictionary<string, Matrix4x4> CapturePose(
        string donor, GameObject bodyPrefab, string bodyPath)
    {
        var bikePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NpcCanonicalConformance.BikeAssetPath);
        if (bikePrefab == null)
            throw new InvalidOperationException(
                $"missing bike {NpcCanonicalConformance.BikeAssetPath}");

        var root = new GameObject("~MinatoCrowdFixture " + donor);
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;
        try
        {
            // The bike child MUST be named exactly "Bike" and nested under the rig, or
            // CoralBikeRig.Setup falls back to a global GameObject.Find("Bike").
            var bike = new GameObject("Bike");
            bike.transform.SetParent(root.transform, false);
            bike.transform.localScale = Vector3.one * NpcCanonicalConformance.BikeScale;
            var bikeModel = (GameObject)PrefabUtility.InstantiatePrefab(
                bikePrefab, bike.transform);
            bikeModel.name = "BikeMesh";

            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, root.transform);
            body.name = donor + "ArmatureAndMesh";

            var rig = root.AddComponent<CoralBikeRig>();
            rig.bikePrefab = bikePrefab;
            NpcCanonicalConformance.Configure(rig);
            NpcCanonicalConformance.FinalizeStagedPose(rig);

            return BoneMatrices(body.transform);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Every deform bone of every skinned renderer, as a matrix in the space of
    /// <paramref name="root"/>. Bones are keyed by exact name; the Kuro skeleton has 24 unique
    /// joint names, so a duplicate would indicate a malformed donor and is reported.
    /// </summary>
    static Dictionary<string, Matrix4x4> BoneMatrices(Transform root)
    {
        var map = new Dictionary<string, Matrix4x4>();
        var w2l = root.worldToLocalMatrix;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var bone in smr.bones)
            {
                if (bone == null || map.ContainsKey(bone.name)) continue;
                map[bone.name] = w2l * bone.localToWorldMatrix;
            }
        }
        if (map.Count == 0)
            throw new InvalidOperationException($"{root.name}: no skinned bones found.");
        return map;
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Row-major 4x4, which is how numpy will read it back.</summary>
    static string Mat(Matrix4x4 m)
    {
        var sb = new StringBuilder("[");
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
            {
                sb.Append(F(m[r, c]));
                if (!(r == 3 && c == 3)) sb.Append(", ");
            }
        sb.Append(']');
        return sb.ToString();
    }
}
