using UnityEngine;

/// <summary>
/// The one place that answers "is the player allowed to ride right now?".
///
/// MapleRide's controller is the bicycle, so "locking input" cannot mean "ignore the keyboard" -
/// a real trainer keeps streaming watts whether or not the game wants them. The gate is
/// therefore consulted at every point where effort becomes motion:
///
///   <see cref="DeviceManager"/>  - refuses to bank keyboard pedal strokes or read the effort axis
///   <see cref="RideSession"/>    - zeroes watts, brake and speed, and does not advance the ride
///   <see cref="RouteFollower"/>  - refuses lateral steer
///   <see cref="RideBootstrap"/>  - refuses the ride hotkeys (course, laps, restart, free roam)
///   <see cref="WorldMapHud"/>    - refuses the TAB fast-travel overlay
///
/// Deliberately STATIC and deliberately reset on every play session: the gate is a property of
/// "this run of the game", never of a serialized asset. A gate left latched in a saved scene
/// would be indistinguishable from a broken bike.
///
/// Nothing in the editor/batchmode capture paths ever locks it, so QA renders and self-tests are
/// unaffected - the gate is only ever engaged by <see cref="MapleRideFlowDirector"/>.
/// </summary>
public static class RideInputGate
{
    /// <summary>True while the player's ride input must be ignored (e.g. the 3-2-1 countdown).</summary>
    public static bool Locked { get; private set; }

    /// <summary>Why the gate is closed, for logs and the HUD. Empty when open.</summary>
    public static string Reason { get; private set; } = "";

    public static void Lock(string reason)
    {
        Locked = true;
        Reason = reason ?? "";
    }

    public static void Unlock(string reason = "")
    {
        Locked = false;
        Reason = "";
    }

    /// <summary>
    /// Static state does not survive a domain reload cleanly and MUST NOT survive a play session.
    /// Entering play mode with a latched gate would present as a bike that refuses to move with
    /// no visible cause.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        Locked = false;
        Reason = "";
    }
}
