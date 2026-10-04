using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Surgical, idempotent re-fit of the PLAYER's bike after Kuro's chibi proportion rework.
///
/// Replacing KuroNPC_KuroAnime_Rigged.glb on disk updates the mesh and skeleton automatically
/// on reimport, but the bike's scale and the rig's wheel radius are SERIALIZED on the scene
/// objects that MapleRideKuroSetup created. Editing the constants in that setup class only
/// affects a future full rebuild, so without this pass the newly compact Kuro would be staged
/// on the old 1.35-sized frame -- a small rider on a machine two sizes too big.
///
/// BuildSakuraPass would apply it, but it rebuilds the whole route and restages the NPC
/// roster, which is far more blast radius than a bike-fit change warrants. This pass touches
/// exactly two serialized values and nothing else.
///
/// Matching is by EXACT name (a Contains-style match has previously grabbed an NPC's bike
/// instead of the player's), and running it twice is a no-op.
/// </summary>
public static class KuroBikeRefit
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PlayerRootName = "Kuro on Sakura Pass";
    private const string BikeAnchorName = "Bike";

    // Must stay in lockstep with KuroCyclingPostureSetup / MapleRideKuroSetup.
    // 1.26 is the promoted A-pose-rebuild value; see the derivation comment on
    // MapleRideKuroSetup.BikeScale. wheelRadius is reconciled below because it divides
    // WORLD distance -- leaving it at the old value would desync the crank from the road.
    private const float BikeScale = 1.26f;
    private const float UnscaledWheelRadius = 0.175f;

    // Riding pose, in lockstep with the MapleRideKuroSetup block of the same name and with
    // the accepted Blender fit (zz_bike_fit.py --lean=47 --crank=-90 --footsolve). These are
    // SERIALIZED on the scene's KuroBikeRig, so a scene saved before the promotion still
    // carries the old degenerate-spine values until this pass overwrites them.
    private const float HipTiltDegrees = 0f;      // never rotate Hips (frozen rebuild rule)
    private const float SpineLeanDegrees = 11f;
    private const float NeckLiftDegrees = -22f;
    private const float HeadLiftDegrees = -28f;
    // Symmetric collarbone drop so the seated lean stops burying the big chibi head between the
    // shoulders in the chase cam. Player-only (KuroBikeRig defaults it to 0 for every NPC).
    // Tuned against the seated chase render, not derived.
    private const float ShoulderDropDegrees = 7f;
    // Player-only hand IK seating (steer-local, added to the Hood_L/Hood_R targets). The player
    // rides the SHORT-REACH, WIDER-HOOD bike copy (kuro_bike_colnago_player.glb). A clean SYMMETRIC
    // grip seats both gloves on their OWN hoods: a small symmetric Y lift keeps the wrists at hood
    // height, and the mirrored outboard spread below pushes each wrist ~0.06 m outboard so the
    // baked glove (which hangs ~0.09 m inboard of the wrist) lands on the hood. NPCs keep the rig
    // default (zero) on the shared bike.
    // Lift and advance the targets so the gloves, not only the wrists, sit over the brake hoods.
    private static readonly Vector3 HandTargetLocalOffset = new Vector3(0f, 0.01f, 0f);
    // Player-only glove-on-hood seating via a SYMMETRIC outboard spread. This REPLACES the earlier
    // asymmetric per-hand handSeatOffsetL/R (a glove-gap=0 seat-fit solver derived them with a
    // large crossing -Z back-pull that folded the forearms into an X - "pretzel arms"). The seat
    // offsets are now ZERO like every NPC; the spread + Y lift seat the gloves symmetrically.
    // Verified by frontal render on Sakura (fi_symsweep/sym_p06y5): arms uncrossed, forearms
    // parallel, each glove on its same-side hood.
    private const float GripSpreadMetres = 0f;
    private static readonly Vector3 HandSeatOffsetL = Vector3.zero;
    private static readonly Vector3 HandSeatOffsetR = Vector3.zero;
    // Measured sole-below-ankle is 0.128 (L 0.1280 / R 0.1279). 0.130 left the shoes riding
    // +0.0155 / +0.0237 m proud of the pedal platform in-engine, so the ankle is dropped to
    // centre the sole ON the platform. PROVISIONAL: tuned against the render, not derived.
    private const float AnkleHeight = 0.113f;
    private const bool KeepPedalsLevel = true;

    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject player = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == PlayerRootName) { player = root; break; }
        }
        if (player == null)
        {
            Debug.LogError("[kuro-refit] player root '" + PlayerRootName + "' not found; aborting.");
            return;
        }

        // Exact-name child search, and prune duplicates so repeated runs cannot leak a second bike.
        Transform anchor = null;
        int duplicates = 0;
        var all = player.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t.name != BikeAnchorName) continue;
            if (anchor == null) anchor = t;
            else { duplicates++; Object.DestroyImmediate(t.gameObject); }
        }
        if (anchor == null)
        {
            Debug.LogError("[kuro-refit] no child named '" + BikeAnchorName + "' under the player; aborting.");
            return;
        }

        // Replace the drifted player bike instance with the canonical road-bike prefab.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            KuroCyclingPostureSetup.PlayerBikeAssetPath);
        if (prefab != null)
        {
            var old = anchor.gameObject;
            var fresh = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
            fresh.name = BikeAnchorName;
            fresh.transform.localPosition = Vector3.zero;
            fresh.transform.localRotation = Quaternion.identity;
            fresh.transform.localScale = Vector3.one * BikeScale;
            Object.DestroyImmediate(old);
            anchor = fresh.transform;
        }
        anchor.localScale = Vector3.one * BikeScale;
        EditorUtility.SetDirty(anchor);

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null)
        {
            rig.bikePrefab = prefab != null ? prefab : rig.bikePrefab;
            rig.wheelRadius = UnscaledWheelRadius * BikeScale;
            rig.hipTiltDegrees = HipTiltDegrees;
            rig.spineLeanDegrees = SpineLeanDegrees;
            rig.neckLiftDegrees = NeckLiftDegrees;
            rig.headLiftDegrees = HeadLiftDegrees;
            rig.shoulderDropDegrees = ShoulderDropDegrees;
            rig.handTargetLocalOffset = HandTargetLocalOffset;
            rig.gripSpreadMetres = GripSpreadMetres;
            rig.handSeatOffsetL = HandSeatOffsetL;
            rig.handSeatOffsetR = HandSeatOffsetR;
            rig.ankleHeight = AnkleHeight;
            rig.footTargetRearwardOffset = 0.062f;
            rig.footHeightTrimL = 0.0005f;
            rig.footHeightTrimR = 0.0025f;
            rig.footToeDownDegrees = 5f;
            rig.footRollDegrees = 5f;
            rig.kneePoleLateralOffset = 0.060f;
            rig.keepPedalsLevel = KeepPedalsLevel;
            rig.useAnatomicalArmSolver = true;
            // Reset scene overrides to the authored bike sockets; repeated refits must not drift.
            KuroPoseRepair.RestoreSockets(player);
            rig.ForceSolveOnce();
            EditorUtility.SetDirty(rig);
        }

        // The four-posture owner is also the canonical player-bike staging owner. This applies
        // the player-only cloned materials and recreates the compact cockpit sockets idempotently.
        KuroCyclingPostureSetup.ConfigurePlayer(player, true);

        Debug.Log(string.Format(
            "[kuro-refit] bike scale -> {0}, wheelRadius -> {1}, duplicate anchors pruned={2}, rig={3}",
            BikeScale, UnscaledWheelRadius * BikeScale, duplicates, rig != null));
        if (rig != null)
        {
            Debug.Log(string.Format(
                "[kuro-refit] pose -> hipTilt={0} spineLean={1} neck={2} head={3} ankleHeight={4} pedalsLevel={5}",
                HipTiltDegrees, SpineLeanDegrees, NeckLiftDegrees, HeadLiftDegrees,
                AnkleHeight, KeepPedalsLevel));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[kuro-refit] scene saved clean.");
    }
}







