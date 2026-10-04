using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pushes the chase-camera framing onto the SERIALIZED camera in SakuraPass.
///
/// WHY THIS EXISTS: the player reported riding with "a view of my character from the side", and
/// the code default on <see cref="KuroFollowCamera"/> - (0, 3.2, -7), a straight rear-and-above
/// chase - said nothing was wrong. The scene disagreed. The saved component carried
/// (2.55, 1.18, -3.35): 2.55 m of LATERAL offset against 3.35 m of pull-back, which is
/// atan2(2.55, 3.35) = 37 degrees off-axis - a three-quarter view, baked in. It came from
/// <c>MapleRideKuroSetup</c>, where it was a beauty-shot angle for a character render that
/// quietly became the ride camera.
///
/// Editing a field's code default does nothing to a scene that was saved with other values, so
/// the fix has to be written by a pass. This is that pass, and it is idempotent: it matches the
/// camera by exact name and only touches the framing fields.
/// </summary>
public static class RideCameraSetup
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string CameraName = "Sakura Camera";

    /// <summary>
    /// PROVISIONAL: the Zwift-style chase offset, in the rider's HEADING frame.
    ///
    /// x = 0 exactly: the camera sits on the rider's centreline. There is no artistic reason for
    /// a lateral bias here, and any non-zero value is precisely the defect being fixed.
    ///
    /// Hero-scale framing: Kuro and his bicycle are a principal game subject, not a distant
    /// navigation marker. Enough road remains visible above and around the rider for steering.
    /// </summary>
    public static readonly Vector3 ChaseOffset = new Vector3(0f, 1.15f, -2.20f);

    /// <summary>Gameplay FOV. Narrower than the old 52 degrees so Kuro and the bike read clearly.</summary>
    public const float FieldOfView = 40f;

    /// <summary>PROVISIONAL: damping on the heading. Matches the value the scene already used.</summary>
    public const float FollowSharpness = 6f;

    /// <summary>PROVISIONAL: see <see cref="KuroFollowCamera.maxHeadingLagDegrees"/>.</summary>
    public const float MaxHeadingLagDegrees = 8f;

    /// <summary>PROVISIONAL: see <see cref="KuroFollowCamera.pitchFollowFraction"/>.</summary>
    public const float PitchFollowFraction = 0.35f;

    [MenuItem("MapleRide/Ride/Apply Chase Camera Framing", priority = 63)]
    public static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        KuroFollowCamera follow = null;
        foreach (var f in Object.FindObjectsByType<KuroFollowCamera>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None))
        {
            if (!string.Equals(f.gameObject.name, CameraName, System.StringComparison.Ordinal))
            {
                Debug.LogWarning("[ride-cam] ignoring an unexpected KuroFollowCamera on '" +
                                 f.gameObject.name + "'.");
                continue;
            }
            follow = f;
        }
        if (follow == null)
        {
            Debug.LogError("[ride-cam] no '" + CameraName + "' with a KuroFollowCamera in the scene.");
            return;
        }

        var was = follow.offset;
        follow.offset = ChaseOffset;
        follow.followSharpness = FollowSharpness;
        follow.yawOnlyFrame = true;
        follow.pitchFollowFraction = PitchFollowFraction;
        follow.bankFollowFraction = 0f;
        follow.maxHeadingLagDegrees = MaxHeadingLagDegrees;
        follow.enforceGameplayPresentation = true;
        follow.gameplayOffset = ChaseOffset;
        follow.gameplayFieldOfView = FieldOfView;
        follow.addGameplayFill = true;
        follow.gameplayFillIntensity = 550f;

        var camera = follow.GetComponent<Camera>();
        if (camera != null) camera.fieldOfView = FieldOfView;

        EditorUtility.SetDirty(follow);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        float wasOffAxis = Mathf.Atan2(Mathf.Abs(was.x), Mathf.Max(0.01f, -was.z)) * Mathf.Rad2Deg;
        Debug.Log($"[ride-cam] chase framing applied: offset {was} -> {follow.offset} " +
                  $"(off-axis {wasOffAxis:0.0} deg -> 0.0 deg), yaw-only, " +
                  $"bank 0, FOV {FieldOfView:0}, max heading lag {MaxHeadingLagDegrees:0} deg.");
    }
}
