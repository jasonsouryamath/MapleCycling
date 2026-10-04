using UnityEngine;

/// <summary>
/// One rider in an NPC race: position along the race, speed, stamina and the bonk timer, all
/// stepped by the brief's model (see <see cref="RaceMath"/>). Deliberately free of Unity
/// objects, so <see cref="RaceDirector"/> and the headless tests step the exact same code.
/// </summary>
public sealed class RaceRider
{
    public RaceMath.Stats stats;
    /// <summary>Metres from the start line.</summary>
    public float x;
    public float v;
    public float stamina;
    public float bonkTimer;
    /// <summary>Lateral line (metres right of the centreline, like RouteFollower's lane).</summary>
    public float lane;
    /// <summary>Speed multiplier applied to the target: the NPC's +/-2 % wobble. 1 for Kuro.</summary>
    public float wobble = 1f;
    public float finishTime = -1f;

    // what the rider did on the last step (HUD chips, animation)
    public bool pedaling, sprinting, drafting;

    public bool Bonked => bonkTimer > 0f;
    public bool Finished => finishTime >= 0f;
    public float StaminaFraction => stats.Stamina > 0f ? Mathf.Clamp01(stamina / stats.Stamina) : 0f;

    public RaceRider(RaceMath.Stats s)
    {
        stats = s;
        stamina = s.Stamina;
    }

    /// <summary>Advances speed and stamina (not position) by dt.</summary>
    public void Step(float dt, bool pedal, bool sprint, bool draft, float grade)
    {
        if (bonkTimer > 0f) { bonkTimer = Mathf.Max(0f, bonkTimer - dt); sprint = false; }
        if (!pedal || stamina <= 0f) sprint = false;
        pedaling = pedal;
        sprinting = sprint;
        drafting = draft && pedal;

        float target;
        if (!pedal)
        {
            target = 0f;
            stamina += RaceMath.RegenCoastPerS * dt;
        }
        else if (sprint)
        {
            target = stats.Cruise + stats.Boost;
            stamina -= RaceMath.SprintDrainPerS * dt;
            if (stamina <= 0f)
            {
                stamina = 0f;
                bonkTimer = RaceMath.BonkSeconds;
                sprinting = false;
            }
        }
        else
        {
            target = stats.Cruise;
            stamina += (drafting ? RaceMath.RegenDraftPerS : RaceMath.RegenPedalPerS) * dt;
        }
        stamina = Mathf.Min(stamina, stats.Stamina);

        target *= RaceMath.GradeFactor(grade, stats.GradeK) * wobble;
        if (bonkTimer > 0f) target *= RaceMath.BonkSpeedScale;
        if (drafting) target *= RaceMath.DraftBonus;

        v = Mathf.Max(0f, RaceMath.Accelerate(v, target, pedal, dt));
    }
}

/// <summary>
/// The rival's decisions (the brief's AI). Stateful only in whether a sprint is in progress,
/// which is what gives the "start above X, stop under Y" rules their hysteresis.
/// </summary>
public sealed class RaceRivalAi
{
    public readonly RaceType type;
    public readonly float raceLengthM;
    private bool _sprinting;

    public RaceRivalAi(RaceType type, float raceLengthM)
    {
        this.type = type;
        this.raceLengthM = raceLengthM;
    }

    /// <summary>True = sprint this step. The rival always pedals.</summary>
    public bool WantsSprint(RaceRider me, RaceRider kuro)
    {
        float toGo = raceLengthM - me.x;
        if (me.Bonked) { _sprinting = false; return false; }

        if (type == RaceType.TimeTrial)
        {
            // Ghost pacing: sprint above 40, rest under 8, empty the tank over the last 130 m.
            if (toGo <= 130f) return _sprinting = me.stamina > 0.5f;
            if (!_sprinting && me.stamina > 40f) _sprinting = true;
            else if (_sprinting && me.stamina < 8f) _sprinting = false;
            return _sprinting;
        }

        // The finishing kick: all-in, but never into a bonk.
        if (toGo <= RaceMath.KickM(type)) return _sprinting = me.stamina > 0.5f;

        float kuroAhead = kuro != null ? kuro.x - me.x : 0f;
        if (!_sprinting)
        {
            bool chase = kuroAhead > 9f && me.stamina > 45f;
            bool attack = type == RaceType.Elite && me.stamina > 0.92f * me.stats.Stamina;
            if (chase || attack) _sprinting = true;
        }
        else if (me.stamina < 22f)
        {
            _sprinting = false;
        }
        return _sprinting;
    }
}
