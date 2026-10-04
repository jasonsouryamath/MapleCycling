using UnityEngine;

/// <summary>
/// The shared riding line, and the rule every rider in the world uses to get round somebody.
///
/// WHY THIS EXISTS
/// ---------------
/// Both regions used to place traffic as TWO FIXED LANES: every rider sat at
/// <c>side * (-dir * laneOffset)</c>, so riders travelling opposite ways landed on opposite
/// sides of the carriageway and simply never interacted. It was tidy, it was correct road law,
/// and it read on screen as two railway tracks - nobody ever moved, nobody ever had to.
///
/// What a real group ride looks like is the opposite: everybody rides ONE line, roughly where
/// the surface is best, and the only lateral movement in the whole scene is somebody stepping
/// out for a second to get past and then tucking straight back in. That movement is the thing
/// the player reads as "these are riders", so it is worth more than lane discipline.
///
/// So: one shared line (<see cref="SharedLineM"/>), and a transient step-out whenever two riders
/// would otherwise occupy the same piece of road. The step-out is:
///   - to the rider's own LEFT when meeting somebody head-on (both step left, so the gap opens
///     from both sides at once);
///   - to the rider's own RIGHT when overtaking somebody going the same way (you go round the
///     outside, which is where the road is).
///
/// WHO MOVES
/// ---------
/// Only the rider with somewhere to go moves. In a meeting BOTH riders see each other in front
/// of them, so both step left and the clearance is the sum of the two shifts. In an overtake
/// only the faster rider sees a conflict in front of it, so only the overtaker steps out and the
/// rider being passed holds its line - which is exactly the etiquette, and it also means the
/// player never gets swerved into by somebody they are calmly sitting behind.
///
/// ALL TUNING HERE IS PROVISIONAL. These are the numbers that make a 1.4 m chibi rider on a
/// ~5 m carriageway read correctly; none of them is a design requirement.
/// </summary>
public static class TrafficLine
{
    /// <summary>
    /// The one line everybody rides, in metres on the ROAD's own side axis (+ve = the segment's
    /// right). Slightly left of the crown rather than dead centre: it keeps the faint
    /// keep-left character of the world without splitting the traffic into two streams, and it
    /// leaves the wider half of the carriageway free on the side overtakes actually happen.
    /// PROVISIONAL.
    /// </summary>
    public const float SharedLineM = -0.35f;

    /// <summary>Random spread around the shared line so 30 riders are not on one rail. PROVISIONAL.</summary>
    public const float LineJitterM = 0.22f;

    /// <summary>Step-out when meeting an oncoming rider, metres to the rider's own left. PROVISIONAL.</summary>
    public const float MeetShiftM = 0.85f;

    /// <summary>Step-out when overtaking, metres to the rider's own right. PROVISIONAL.</summary>
    public const float PassShiftM = 1.15f;

    /// <summary>How fast a rider crosses the road, m/s. Slow enough to read as a decision. PROVISIONAL.</summary>
    public const float ShiftSpeedMps = 1.7f;

    /// <summary>
    /// How far in front a head-on rider is noticed, metres. Closing speed in a meeting is the
    /// sum of both paces (~15 m/s), so 30 m is under two seconds of warning - any less and the
    /// step-out happens too late to read as avoidance rather than a twitch. PROVISIONAL.
    /// </summary>
    public const float MeetLookAheadM = 30f;

    /// <summary>How far in front a same-direction rider is noticed before a pass. PROVISIONAL.</summary>
    public const float PassLookAheadM = 16f;

    /// <summary>
    /// How far behind a completed pass the step-out is held before tucking back in. Pulling in
    /// the instant the front wheel clears is how you take somebody's front wheel off. PROVISIONAL.
    /// </summary>
    public const float PassClearM = 7f;

    /// <summary>Lateral separation under which two riders count as being in each other's way. PROVISIONAL.</summary>
    public const float ConflictLateralM = 1.0f;

    /// <summary>Pace advantage needed before a rider commits to an overtake, m/s. PROVISIONAL.</summary>
    public const float OvertakeMarginMps = 0.2f;

    /// <summary>
    /// Works out the lateral step-out one rider wants this frame, in metres to its OWN RIGHT
    /// (negative = its own left), given one other road user.
    ///
    /// <paramref name="along"/> is metres in front of the subject along its own heading
    /// (negative = behind it), <paramref name="lateral"/> is metres to the subject's own right,
    /// <paramref name="oncoming"/> is true when the two are travelling against each other, and
    /// the paces are along each rider's own direction of travel.
    ///
    /// Returns 0 when this pair needs nothing, so a caller can simply take the largest-magnitude
    /// result across everybody nearby.
    /// </summary>
    public static float DesiredShift(float along, float lateral, bool oncoming,
                                     float ownPace, float otherPace, bool alreadyOut)
    {
        // A conflict only exists if the two lines are close enough to actually meet. Once a
        // rider IS out, the test widens: it is holding the step-out open across the pass, and
        // the whole point is that the gap is no longer small.
        float band = alreadyOut ? ConflictLateralM + PassShiftM : ConflictLateralM;
        if (Mathf.Abs(lateral) > band) return 0f;

        if (oncoming)
        {
            // Head-on. Only somebody in FRONT matters - an oncoming rider already behind you has
            // been passed and is receding at 15 m/s.
            if (along <= 0f || along > MeetLookAheadM) return 0f;
            return -MeetShiftM;                       // own left
        }

        // Same direction. Going past somebody in front...
        if (along > 0f && along < PassLookAheadM && ownPace > otherPace + OvertakeMarginMps)
            return PassShiftM;                        // own right

        // ...and stay out until properly clear of them behind.
        if (alreadyOut && along <= 0f && along > -PassClearM)
            return PassShiftM;

        return 0f;
    }

    /// <summary>
    /// Moves a current step-out towards its target at <see cref="ShiftSpeedMps"/>. Kept here so
    /// Sakura's roster riders and Shiosai's pooled traffic cross the road at the same rate -
    /// a difference in this one number is instantly legible as two different games.
    /// </summary>
    public static float Approach(float current, float target, float dt)
        => Mathf.MoveTowards(current, target, ShiftSpeedMps * dt);
}
