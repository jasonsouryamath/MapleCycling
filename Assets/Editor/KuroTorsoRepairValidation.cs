using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Launches a real play-mode acceptance capture of the active SakuraPass player.
/// This deliberately has no model or pose override hooks: it verifies exactly what gameplay loads.
/// </summary>
[InitializeOnLoad]
public static class KuroTorsoRepairValidation
{
    private const string PrefKey = "mapleride.kuro.torso-runtime-validation";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PlayerName = "Kuro on Sakura Pass";
    private const string ModelPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
    private const string ExpectedModelSha256 =
        "6F304CE5A4711C5CE2BB580C504DEDA08CB354F7AEECD99CB6CB0A9D2354C54D";
    private const double TimeoutSeconds = 600.0;
    private static double _deadline;
    private static bool _captureStarted;

    static KuroTorsoRepairValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += WatchForRuntimeScene;
    }

    [MenuItem("MapleRide/Kuro/Validate Runtime Face And Torso", priority = 68)]
    public static void Run()
    {
        VerifyCanonicalModel();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = scene.GetRootGameObjects().SingleOrDefault(root => root.name == PlayerName);
        if (player == null)
            throw new InvalidOperationException("Kuro player root is missing from SakuraPass.");

        var skin = player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .OrderByDescending(renderer =>
                renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0)
            .FirstOrDefault();
        if (skin == null)
            throw new InvalidOperationException("The active SakuraPass player has no skinned body.");

        var source = PrefabUtility.GetCorrespondingObjectFromSource(skin.gameObject);
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (!string.Equals(sourcePath, ModelPath, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The active SakuraPass player is not sourced from the canonical model: " +
                (string.IsNullOrEmpty(sourcePath) ? "<embedded or unknown>" : sourcePath));

        var pose = player.GetComponent<KuroRidePose>();
        var rig = player.GetComponent<KuroBikeRig>();
        if (pose == null ||
            rig == null ||
            pose.selectedPosture != KuroRidePose.CyclingPosture.RoadRacerAggressive ||
        !pose.lockGameplayLowAeroDefault ||
        pose.rememberSelection ||
        pose.enableKeyboardSelection ||
        pose.enableClimbOverlay ||
        Mathf.Abs(rig.spineLeanDegrees - 11f) > 0.001f ||
            Mathf.Abs(pose.roadRacerAggressive.extraHipTiltDegrees - 29f) > 0.001f ||
        Mathf.Abs(pose.roadRacerAggressive.extraSpineLeanDegrees - 52f) > 0.001f ||
        Mathf.Abs(pose.roadRacerAggressive.extraNeckLiftDegrees - (-10f)) > 0.001f ||
        Mathf.Abs(pose.roadRacerAggressive.extraHeadLiftDegrees - (-12f)) > 0.001f)
            throw new InvalidOperationException(
                "The active player must default to the coupled low-aero road-racer refit.");

        Debug.Log("[kuro-runtime-face] preflight canonical source=" + sourcePath +
                  ", vertices=" + skin.sharedMesh.vertexCount +
                  ", bones=" + skin.bones.Length +
                  ", defaultPosture=" + pose.selectedPosture +
                  ", baseSpine=" + rig.spineLeanDegrees.ToString("F1") +
                  ", aggressiveHip/Spine=" +
                  pose.roadRacerAggressive.extraHipTiltDegrees.ToString("F1") + "/" +
                  pose.roadRacerAggressive.extraSpineLeanDegrees.ToString("F1"));

        EditorPrefs.SetBool(PrefKey, true);
        _captureStarted = false;
        EditorApplication.EnterPlaymode();
    }

    private static void VerifyCanonicalModel()
    {
        string absolutePath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../" + ModelPath));
        if (!File.Exists(absolutePath))
            throw new FileNotFoundException("Canonical Kuro model is missing.", absolutePath);

        using var stream = File.OpenRead(absolutePath);
        using var sha = SHA256.Create();
        string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!string.Equals(actual, ExpectedModelSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Canonical Kuro model checksum mismatch. Expected " + ExpectedModelSha256 +
                ", got " + actual + ".");

        Debug.Log("[kuro-runtime-face] canonical model SHA-256=" + actual);
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            _captureStarted = false;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[kuro-runtime-face] left play mode with the scene restored from disk.");
            if (Application.isBatchMode)
                EditorApplication.Exit(KuroGameplayPoseRunner.Failed ? 1 : 0);
        }
    }

    private static void WatchForRuntimeScene()
    {
        if (!EditorPrefs.GetBool(PrefKey, false) || !EditorApplication.isPlaying)
            return;

        if (_deadline <= 0.0)
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;

        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[kuro-runtime-face] timed out waiting for runtime captures.");
            KuroTorsoRuntimeRunner.Failed = true;
            EditorApplication.ExitPlaymode();
            return;
        }

        if (_captureStarted)
        {
            if (KuroGameplayPoseRunner.Finished)
                EditorApplication.ExitPlaymode();
            return;
        }

        if (Time.frameCount < 5 ||
            GameObject.Find(PlayerName) == null)
            return;

        _captureStarted = true;
        KuroGameplayPoseRunner.Finished = false;
        KuroGameplayPoseRunner.Failed = false;
        var runnerObject = new GameObject("~Kuro Gameplay Pose Validation");
        var runner = runnerObject.AddComponent<KuroGameplayPoseRunner>();
        runner.outDir = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../reference/good_graphics/kuro_gameplay_pose/runtime_verified"));
    }
}
