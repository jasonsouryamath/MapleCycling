using UnityEngine;

/// <summary>
/// Tiny runtime animator for the Shunta Metro landmark set: spins the Odaiba ferris wheel (gondolas stay upright),
/// pulses the Tokyo Tower beacon, and runs a chase of lamps under the zone-8 railway arches.
/// Only touches materials owned by <see cref="ShuntaLandmarkSet"/>. Static when not playing.
/// </summary>
[ExecuteAlways]
public sealed class ShuntaLandmarkMotion : MonoBehaviour
{
    public Transform wheel;
    public Quaternion wheelBase = Quaternion.identity;
    public float wheelDegPerSec = 3.5f;
    public Transform[] gondolas = new Transform[0];
    public Quaternion gondolaWorld = Quaternion.identity;

    public Material beacon; public Color beaconRgb = new Color(1f, 0.25f, 0.2f); public float beaconRel = 6f;
    public Material rim; public float rimRel = 1.8f;
    public Material[] chase = new Material[0]; public Color chaseRgb = new Color(0.55f, 1f, 0.85f); public float chaseRel = 3f;

    void Update()
    {
        float t = Application.isPlaying ? Time.time : 0f;
        if (wheel != null) wheel.localRotation = wheelBase * Quaternion.Euler(0f, 0f, -t * wheelDegPerSec);
        if (gondolas != null) foreach (var g in gondolas) if (g != null) g.rotation = gondolaWorld;
        if (beacon != null)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(t * 2.4f);
            ShuntaLookKit.SetEmissive(beacon, beaconRgb, beaconRel * (0.45f + 0.55f * k));
        }
        if (rim != null)
        {
            Color c = Color.HSVToRGB(Mathf.Repeat(0.52f + 0.12f * Mathf.Sin(t * 0.35f), 1f), 0.55f, 1f);
            ShuntaLookKit.SetEmissive(rim, c, rimRel);
        }
        if (chase != null && chase.Length > 0)
            for (int i = 0; i < chase.Length; i++)
            {
                if (chase[i] == null) continue;
                float ph = 0.5f + 0.5f * Mathf.Cos(Mathf.PI * 2f * (t * 0.7f - i / (float)chase.Length));
                ShuntaLookKit.SetEmissive(chase[i], chaseRgb, chaseRel * (0.12f + 0.88f * ph * ph * ph));
            }
    }
}
