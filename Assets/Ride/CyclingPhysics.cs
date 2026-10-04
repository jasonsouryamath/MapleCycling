using System;
using UnityEngine;

/// <summary>
/// Watts to road speed.
///
/// The one rule this model must never break: virtual distance follows *this*, never a
/// trainer-reported speed. A trainer reports the speed of its own flywheel against its own
/// internal resistance curve; the world's gradient, the rider's mass and the air they are
/// pushing live here.
///
/// P = 0.5*rho*CdA*v^3 + Crr*m*g*cos(theta)*v + m*g*sin(theta)*v, all divided by drivetrain
/// efficiency. Integrated rather than solved, so the rider accelerates and coasts instead of
/// snapping to the terminal speed for the current watts.
///
/// EVERY VALUE HERE IS PROVISIONAL illustrative tuning (design handoff section 12: FTP setup,
/// rider mass and CdA are all unresolved). They are serialized so they can be tuned in the
/// inspector without a recompile.
/// </summary>
[Serializable]
public class CyclingPhysics
{
    [Header("Rider + bike (provisional)")]
    public float riderMassKg = 68f;
    public float bikeMassKg = 8.5f;

    [Header("Resistance (provisional)")]
    [Tooltip("Effective frontal area x drag coefficient, m^2. 0.32 is a hoods position.")]
    public float cdA = 0.32f;
    [Tooltip("Coefficient of rolling resistance. 0.005 is good tyres on decent asphalt.")]
    public float crr = 0.005f;
    public float airDensityKgM3 = 1.225f;
    [Range(0.85f, 1f)] public float drivetrainEfficiency = 0.975f;

    [Header("Limits (provisional)")]
    [Tooltip("Riders brake on descents; without this the model reaches absurd speeds.")]
    public float descentCapKph = 58f;
    [Tooltip("Below this the model would need infinite force to start from rest.")]
    public float forceReferenceSpeedMps = 2.0f;
    public float maxAccelMps2 = 4.0f;

    public float TotalMassKg => riderMassKg + bikeMassKg;

    /// <summary>Along-track wind, m/s, written by WeatherDirector each frame. Positive = headwind,
    /// negative = tailwind. 0 = calm, which reproduces the pre-weather model exactly. Wind changes
    /// ROAD SPEED only; measured trainer power is never altered.</summary>
    [System.NonSerialized] public float headwindMps;
    /// <summary>Aerodynamic drag multiplier from shelter x draft (1 = fully exposed, alone).</summary>
    [System.NonSerialized] public float dragMultiplier = 1f;
    /// <summary>Equipment aero (shop carbon wheels etc.): multiplies CdA. 1 = stock.</summary>
    [System.NonSerialized] public float equipmentCdaScale = 1f;

    private const float G = 9.80665f;

    /// <summary>Steady-state speed for a given power and grade. Used for ride-time estimates.</summary>
    public float TerminalSpeed(float watts, float grade)
    {
        float theta = Mathf.Atan(grade);
        float lo = 0.05f, hi = 30f;
        for (int i = 0; i < 48; i++)
        {
            float v = 0.5f * (lo + hi);
            float va = v + headwindMps;
            float need = (WindMath.AeroDragN(airDensityKgM3, cdA * dragMultiplier * equipmentCdaScale, va) * v
                          + crr * TotalMassKg * G * Mathf.Cos(theta) * v
                          + TotalMassKg * G * Mathf.Sin(theta) * v) / drivetrainEfficiency;
            if (need > watts) hi = v; else lo = v;
        }
        return 0.5f * (lo + hi);
    }

    /// <summary>One integration step. Returns the new speed in m/s.</summary>
    public float Step(float speedMps, float watts, float grade, float brakeInput, float dt)
    {
        float m = TotalMassKg;
        float theta = Mathf.Atan(grade);
        float v = Mathf.Max(speedMps, 0f);

        // P/v explodes at a standstill, so the drive force is evaluated against a reference
        // speed floor - which is also physically honest: a rider starting from rest is limited
        // by torque, not by power.
        float vRef = Mathf.Max(v, forceReferenceSpeedMps);
        float drive = watts * drivetrainEfficiency / vRef;

        // Drag from RELATIVE air speed (WindMath.AeroDragN): a headwind adds to v, a tailwind
        // subtracts and can push the rider when it exceeds road speed.
        float drag = WindMath.AeroDragN(airDensityKgM3, cdA * dragMultiplier * equipmentCdaScale, v + headwindMps);
        float roll = crr * m * G * Mathf.Cos(theta);
        float gravity = m * G * Mathf.Sin(theta);
        float brake = Mathf.Clamp01(brakeInput) * 0.55f * m * G;   // ~0.55 g of braking

        float accel = (drive - drag - roll - gravity - brake) / m;
        accel = Mathf.Clamp(accel, -maxAccelMps2 * 3f, maxAccelMps2);

        v += accel * dt;
        v = Mathf.Max(0f, v);

        // Riders do not free-fall down a 10 % descent to terminal velocity; they scrub speed.
        float cap = descentCapKph / 3.6f;
        if (v > cap) v = Mathf.Lerp(v, cap, 1f - Mathf.Exp(-3f * dt));
        return v;
    }
}
