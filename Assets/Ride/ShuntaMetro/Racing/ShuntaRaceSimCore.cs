using System;
using System.Collections.Generic;

/// <summary>Course profile sampled every <see cref="StepM"/> metres, so the sim needs no engine types.</summary>
public sealed class ShuntaSimCourse
{
    public const float StepM = 10f;
    public float lengthM;
    public float[] gradePct = Array.Empty<float>();
    public byte[] zone = Array.Empty<byte>();            // 1..12
    public float[] zoneGrip = new float[13];
    public float[] zoneDraft = new float[13];

    public int Index(float posM) => Math.Clamp((int)(posM / StepM), 0, gradePct.Length - 1);
}

public sealed class ShuntaSimRider
{
    public string name = "";
    public ShuntaRacerProfile profile;
    // state
    public float posM, speed, wBalJ, form, finishS = -1f, powerNoise;
    public bool sitsIn;
    public int lastWetZone;
    public float draftNow;
}

/// <summary>
/// Deterministic seeded 1 Hz race simulation: power -> speed with grade, drag, rolling resistance and
/// draft, ShuntaPacing for power, corner caps from zone grip. Engine-free so it can be unit tested.
/// </summary>
public static class ShuntaRaceSimCore
{
    const float G = 9.80665f, Rho = 1.225f, Crr = 0.005f, Eff = 0.975f, BikeKg = 8.5f;
    const float DescentCapMps = 58f / 3.6f;
    const float DraftReachM = 15f;

    public static List<ShuntaSimRider> BuildField(int seed, int ridersPerArchetype, Func<ShuntaArchetype, ShuntaRacerProfile> profileOf)
    {
        var rng = new Random(seed * 7919 + 13);
        var list = new List<ShuntaSimRider>();
        for (int k = 0; k < ridersPerArchetype; k++)
            foreach (var a in ShuntaRacerArchetypes.All)
            {
                var p = profileOf(a);
                p.ftpW *= 1f + 0.04f * Gauss(rng);
                p.sprintPeakW *= 1f + 0.04f * Gauss(rng);
                p.massKg *= 1f + 0.02f * Gauss(rng);
                list.Add(new ShuntaSimRider { name = $"{a}{k + 1}", profile = p });
            }
        // random grid
        for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        return list;
    }

    /// <summary>Runs one race; returns riders ordered by finish. finishS holds each rider's time in seconds.</summary>
    public static List<ShuntaSimRider> Run(ShuntaSimCourse course, List<ShuntaSimRider> field, int seed)
    {
        var rng = new Random(seed);
        int n = field.Count;
        for (int i = 0; i < n; i++)
        {
            var r = field[i];
            r.posM = -(i / 3) * 2f; r.speed = 6f; r.wBalJ = r.profile.wPrimeJ; r.finishS = -1f;
            r.form = 1f + 0.04f * Gauss(rng);
            r.sitsIn = rng.NextDouble() < r.profile.draftUsage;
            r.lastWetZone = 0; r.powerNoise = 0f;
        }
        var order = new List<ShuntaSimRider>(field);
        int finished = 0;
        for (int t = 1; t <= 7200 && finished < n; t++)
        {
            order.Sort((a, b) => b.posM.CompareTo(a.posM));          // leader first
            for (int oi = 0; oi < n; oi++)
            {
                var r = order[oi];
                if (r.finishS >= 0f) continue;
                var p = r.profile;
                int idx = course.Index(r.posM);
                int zone = course.zone[idx];
                float grade = course.gradePct[idx] * 0.01f;
                float mass = p.massKg + BikeKg;

                // nearest unfinished rider ahead
                float draft = 0f, leadSpeed = 0f; float gap = 999f;
                for (int k = oi - 1; k >= 0; k--)
                {
                    var o = order[k];
                    if (o.finishS >= 0f) continue;
                    gap = o.posM - r.posM; leadSpeed = o.speed; break;
                }
                if (gap >= 0.3f && gap < DraftReachM)
                    draft = Math.Min(0.5f, 0.34f * (0.35f + 0.65f * p.draftUsage) * course.zoneDraft[zone] * (1f - gap / DraftReachM));
                r.draftNow = draft;

                var ctx = new ShuntaPacingContext
                {
                    kmDone = r.posM / 1000f, kmToGo = (course.lengthM - r.posM) / 1000f,
                    gradePercent = grade * 100f, zoneIndex = zone, wBalJ = r.wBalJ, drafting = draft > 0.10f
                };
                float watts = ShuntaPacing.TargetWatts(p, ctx) * r.form;
                bool sprinting = ctx.kmToGo * 1000f <= ShuntaPacing.SprintDistanceM * 1.1f;

                // sit-in riders do not go past the wheel unless spending/sprinting
                if (r.sitsIn && draft > 0.10f && !sprinting && ctx.kmToGo > 0.9f && !(ShuntaPacing.IsSpendZone(zone) && r.wBalJ > 0.3f * p.wPrimeJ && p.spendBias > 0.6f))
                {
                    float hold = PowerToHold(leadSpeed + 0.05f, p.CdA * (1f - draft), mass, grade);
                    if (hold < watts) watts = hold;
                }
                r.powerNoise = 0.8f * r.powerNoise + 0.2f * 0.03f * Gauss(rng);
                watts *= 1f + r.powerNoise;

                // W' bookkeeping
                float ftp = p.ftpW;
                if (watts > ftp)
                {
                    if (r.wBalJ <= 0f) watts = ftp * 0.95f;
                    else r.wBalJ = Math.Max(0f, r.wBalJ - (watts - ftp));
                }
                else r.wBalJ = Math.Min(p.wPrimeJ, r.wBalJ + (ftp - watts) * 0.35f);

                // physics
                float v = r.speed;
                float vd = Math.Max(v, 3f);
                float drive = watts * Eff / vd;
                float aero = 0.5f * Rho * p.CdA * (1f - draft) * v * v;
                float roll = Crr * mass * G;
                float slope = mass * G * grade;
                float vNew = v + (drive - aero - roll - slope) / mass;
                vNew = Math.Max(vNew, 1.5f);

                // corner / wet caps and mishaps
                float cap = DescentCapMps;
                float grip = course.zoneGrip[zone];
                if (grip < 0.999f)
                {
                    cap = Math.Min(cap, ShuntaPacing.CornerSpeedCapMps(p, grip));
                    if ((zone == 7 || zone == 8) && r.lastWetZone != zone)
                    {
                        r.lastWetZone = zone;
                        if (rng.NextDouble() < ShuntaPacing.WetMishapChance(p)) vNew = 2f;
                    }
                }
                if (vNew > cap) vNew = Math.Max(cap, v - 3f);
                r.speed = vNew;

                float prev = r.posM;
                r.posM += vNew;
                if (r.posM >= course.lengthM)
                {
                    float frac = (course.lengthM - prev) / Math.Max(0.01f, r.posM - prev);
                    r.finishS = t - 1 + frac;
                    finished++;
                }
            }
        }
        var res = new List<ShuntaSimRider>(field);
        res.Sort((a, b) => (a.finishS < 0 ? 1e9f : a.finishS).CompareTo(b.finishS < 0 ? 1e9f : b.finishS));
        return res;
    }

    static float PowerToHold(float v, float cdA, float mass, float grade)
    {
        float aero = 0.5f * Rho * cdA * v * v;
        float roll = Crr * mass * G;
        float slope = mass * G * grade;
        return Math.Max(0f, (aero + roll + slope) * v / Eff);
    }

    static float Gauss(Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
