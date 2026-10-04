/// <summary>
/// A live heart-rate feed, kept deliberately separate from <see cref="IRideTelemetrySource"/>.
///
/// A real Wahoo TICKR (or any BLE strap) only produces HEART RATE - never watts or cadence - so
/// it must NOT masquerade as a trainer. It layers on top of whatever is producing the rider's
/// effort: the mash model or, later, a real power meter / smart trainer. <see cref="DeviceManager"/>
/// polls this every tick and, only while it <see cref="IsConnected"/>, overrides the bpm in the
/// telemetry frame the HUD reads. Until a strap connects, the simulated HR stands in.
/// </summary>
public interface IHeartRateSource
{
    /// <summary>Human-readable status for the HUD / logs (e.g. "TICKR (BLE)" or "scanning").</summary>
    string Label { get; }

    /// <summary>True only while fresh beats are arriving. Goes false on drop or timeout.</summary>
    bool IsConnected { get; }

    /// <summary>Most recent heart rate in beats per minute. Undefined while disconnected.</summary>
    int BeatsPerMinute { get; }

    /// <summary>Human-readable diagnostics (status + devices seen while scanning), for the HUD.</summary>
    string Diagnostics { get; }

    /// <summary>Begin scanning / connecting. Safe to call again to force a reconnect.</summary>
    void Start();

    /// <summary>
    /// Called once per game tick on the main thread. BLE notifications arrive off-thread, so this
    /// is where staleness is evaluated and <see cref="IsConnected"/> is refreshed.
    /// </summary>
    void Poll(float dt);

    /// <summary>Disconnect and release the radio. Called on teardown.</summary>
    void Stop();
}
