using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// READ-ONLY probe of the SakuraPass player rig, written for the four-posture work.
///
/// Answers three questions the serialized constants in KuroBikeRefit cannot: what the scene
/// ACTUALLY carries on the player's KuroBikeRig right now, whether anything (Animator, extra
/// pose driver) is in a position to stomp an authored pose, and what the cockpit hierarchy
/// under the player's bike looks like (so an aero-bar prop can be parented to a real socket
/// instead of a guessed one).
///
/// Opens the scene read-only and never marks it dirty.
/// </summary>
public static class KuroPostureProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PlayerName = "Kuro on Sakura Pass";

    [MenuItem("MapleRide/Kuro/Probe posture state", priority = 61)]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject player = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == PlayerName) { player = root; break; }
        if (player == null) { Debug.LogError("[posture-probe] player root not found."); return; }

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig == null) { Debug.LogError("[posture-probe] no KuroBikeRig."); return; }

        Debug.Log(string.Format(
            "[posture-probe] rig hipTilt={0} spineLean={1} neckLift={2} headLift={3} shoulderDrop={4} " +
            "handTargetLocalOffset={5} gripSpread={6} seatL={7} seatR={8} elbowPoleSign={9} " +
            "ankleHeight={10} pedalsLevel={11} wheelRadius={12}",
            rig.hipTiltDegrees, rig.spineLeanDegrees, rig.neckLiftDegrees, rig.headLiftDegrees,
            rig.shoulderDropDegrees, rig.handTargetLocalOffset.ToString("F4"), rig.gripSpreadMetres,
            rig.handSeatOffsetL.ToString("F4"), rig.handSeatOffsetR.ToString("F4"),
            rig.armElbowPoleSign, rig.ankleHeight, rig.keepPedalsLevel, rig.wheelRadius));

        Debug.Log(string.Format(
            "[posture-probe] runtime modifiers spineExtra={0} standRise={1} standFwd={2} rock={3} " +
            "headYaw={4} headRoll={5}",
            rig.poseExtraSpineLeanDegrees, rig.poseStandRiseM, rig.poseStandForwardM,
            rig.poseStandRockDegrees, rig.poseHeadYawDegrees, rig.poseHeadRollDegrees));

        foreach (var a in player.GetComponentsInChildren<Animator>(true))
            Debug.Log("[posture-probe] ANIMATOR on '" + Path(a.transform) + "' enabled=" + a.enabled +
                      " controller=" + (a.runtimeAnimatorController != null
                                        ? a.runtimeAnimatorController.name : "<none>"));

        foreach (var p in player.GetComponents<MonoBehaviour>())
            if (p != null) Debug.Log("[posture-probe] player component: " + p.GetType().Name);

        var ridePose = player.GetComponentInChildren<KuroRidePose>(true);
        Debug.Log("[posture-probe] KuroRidePose present=" + (ridePose != null));

        // Cockpit hierarchy: everything under the bike's steerer, so the aero-bar prop has a
        // real parent and real dimensions to work from.
        Transform bike = null, steer = null, hoodL = null, hoodR = null, saddle = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Bike" && bike == null) bike = t;
            if (t.name == "SteerPivot" && steer == null) steer = t;
            if (t.name == "Hood_L" && hoodL == null) hoodL = t;
            if (t.name == "Hood_R" && hoodR == null) hoodR = t;
            if (t.name == "SaddleTop" && saddle == null) saddle = t;
        }
        Debug.Log("[posture-probe] bike=" + (bike ? bike.name + " scale=" + bike.localScale.ToString("F3") : "null") +
                  " steer=" + (steer ? "yes" : "no"));
        if (steer != null)
        {
            var sb = new StringBuilder();
            foreach (Transform c in steer) sb.Append(c.name).Append(" lp=").Append(c.localPosition.ToString("F4")).Append("; ");
            Debug.Log("[posture-probe] SteerPivot children: " + sb);
        }
        if (hoodL != null && hoodR != null && steer != null)
        {
            Debug.Log("[posture-probe] Hood_L local(in steer)=" +
                      steer.InverseTransformPoint(hoodL.position).ToString("F4") +
                      " Hood_R local=" + steer.InverseTransformPoint(hoodR.position).ToString("F4") +
                      " world gap=" + Vector3.Distance(hoodL.position, hoodR.position).ToString("F4"));
        }
        if (saddle != null && hoodL != null)
            Debug.Log("[posture-probe] saddle world=" + saddle.position.ToString("F4") +
                      " hoodL world=" + hoodL.position.ToString("F4"));

        // Solved bone geometry, so the posture deltas can be reasoned about in real numbers.
        rig.ForceSolveOnce();
        Transform hips = null, chest = null, head = null, handL = null, footL = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Hips" && hips == null) hips = t;
            if (t.name == "Spine02" && chest == null) chest = t;
            if (t.name == "Head" && head == null) head = t;
            if (t.name == "LeftHand" && handL == null) handL = t;
            if (t.name == "LeftFoot" && footL == null) footL = t;
        }
        Debug.Log("[posture-probe] bones hips=" + (hips ? hips.position.ToString("F4") : "?") +
                  " chest=" + (chest ? chest.position.ToString("F4") : "?") +
                  " head=" + (head ? head.position.ToString("F4") : "?") +
                  " handL=" + (handL ? handL.position.ToString("F4") : "?") +
                  " footL=" + (footL ? footL.position.ToString("F4") : "?"));
        if (hips != null && head != null)
        {
            Vector3 v = head.position - hips.position;
            Vector3 f = player.transform.forward, up = Vector3.up;
            float along = Vector3.Dot(v, f), vert = Vector3.Dot(v, up);
            Debug.Log(string.Format("[posture-probe] TORSO vector fwd={0:F4} up={1:F4} -> angle from vertical={2:F1} deg",
                                    along, vert, Mathf.Atan2(along, vert) * Mathf.Rad2Deg));
        }

        // Read-only: discard anything ForceSolveOnce touched.
        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[posture-probe] done (scene left clean).");
    }

    private static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
