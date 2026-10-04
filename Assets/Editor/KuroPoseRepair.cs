using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Repairs the Sakura player without replacing the bike, materials, or world scene.</summary>
public static class KuroPoseRepair
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PlayerName = "Kuro on Sakura Pass";

    [MenuItem("MapleRide/Kuro/Repair player riding pose")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = scene.GetRootGameObjects().SingleOrDefault(root => root.name == PlayerName);
        if (player == null) throw new InvalidOperationException("Kuro player root is missing.");
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig == null) throw new InvalidOperationException("KuroBikeRig is missing.");

        RestoreSockets(player);
        // These are the last visually verified seated values from the hands-on-bars capture.
        rig.hipTiltDegrees = 0f;
        rig.spineLeanDegrees = 47f;
        rig.neckLiftDegrees = -22f;
        rig.headLiftDegrees = -28f;
        rig.shoulderDropDegrees = 7f;
        rig.handTargetLocalOffset = new Vector3(0f, 0.01f, 0f);
        rig.gripSpreadMetres = 0f;
        rig.handSeatOffsetL = Vector3.zero;
        rig.handSeatOffsetR = Vector3.zero;
        rig.armElbowPoleSign = -1f;
        rig.useAnatomicalArmSolver = true;
        rig.ankleHeight = 0.113f;
        rig.keepPedalsLevel = true;
        KuroCyclingPostureSetup.ConfigurePlayer(player, true);
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[kuro-pose-repair] restored saddle/hood sockets and seated player pose.");
    }

    public static void RestoreSockets(GameObject player)
    {
        foreach (var name in new[] { "SaddleTop", "Hood_L", "Hood_R" })
        {
            var matches = player.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == name).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one player {name}, found {matches.Length}.");
            var socket = matches[0];
            var source = PrefabUtility.GetCorrespondingObjectFromSource(socket);
            if (source == null)
                throw new InvalidOperationException($"Cannot find authored prefab socket for {name}.");
            socket.localPosition = source.localPosition;
            socket.localRotation = source.localRotation;
            EditorUtility.SetDirty(socket);
        }
    }
}
