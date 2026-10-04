"""
Single source of truth for the SHIOSAI COAST route - the 42 km "Shiosai Grand Coast".

Publishes ``Assets/Environment/ShiosaiCoast/ShiosaiRoute.json`` in exactly the
schema ``sakura_route.write_route_json()`` uses, so ``RouteGraphBaker`` can merge every region
into the one runtime ``RouteGraph`` asset and every ride system (physics, HUD, GPS window,
follower, dressing streamer) works on the coast with no new code paths.

Deliberately standalone: it needs no ``bpy`` and no ``sakura_lib`` (which does import ``bpy``),
so it runs under Blender's Python *or* any Python with numpy::

    blender -b -P shiosai_route.py
    python shiosai_route.py

All coordinates are **Unity** world space (Y up, metres).


AUTHORING MODEL
---------------
The 2.892 km mock parameterised everything on world ``z``: ``x = meander(z)``, ``y =
elevation(z)``. That model cannot express the route the spec asks for, because a switchback
doubles back on itself - two different points of the road share one ``z`` - and because the
chapter chainages (spec 5.1) and the 17 required anchors (spec 5.5) are defined in *distance
along the road*, not in world ``z``.

So the centreline is now integrated as a **driven path**: a curvature schedule ``kappa(s)`` in
road-distance ``s``, integrated into a heading and then into a plan position::

    heading(s)  = heading(0) + integral kappa ds
    position(s) = position(0) + integral (sin h, 0, cos h) ds

This buys exactly the three properties the spec constrains:

* **Arc length is exact by construction** - each schedule entry contributes its own length, so
  chapter chainages and the 42 km total are authored directly rather than discovered.
* **Curve radius is exact by construction** - an entry's radius IS the design radius, so the
  24 m high-speed minimum and 18 m hairpin floor (spec 5.3) are enforced at author time.
* **Elevation is a function of chainage**, so every elevation target in spec 5.2 is pinned at
  the chainage the anchor table gives it.

Curvature is smoothed before integration (``KAPPA_SMOOTH_M``), which turns every abrupt schedule
change into a transition spiral. That is both what real road engineering does and what keeps
banking continuous - a step change in curvature would step the bank angle.

Elevation runs through a **monotone cubic (PCHIP)** fit of the chainage/height keys. Monotone
matters: a natural cubic overshoots between keys, and an overshoot at the Gateway summit or the
tunnel mouth would invent a grade reversal that nobody authored.


WHAT IS STILL PROVISIONAL
-------------------------
Route metrics remain an open design decision in the project handoff. The chapter boundaries, the
anchor chainages and the alignment contracts (7 m carriageway, 0.55 m shoulder) are spec
requirements and are treated as fixed. The individual curvature schedules, the elevation keys
between the spec's stated targets, and ``TARGET_MINUTES`` are authored tuning: they satisfy the
spec's stated bands, but the exact values are ours and are expected to move with playtesting.
"""

import json
import math
import os

import numpy as np

# --------------------------------------------------------------------------- identity

REGION_ID = "shiosai_coast"
SEGMENT_ID = "shiosai"
SEGMENT_NAME = "Shiosai Grand Coast"
COURSE_ID = "shiosai_breeze"
COURSE_NAME = "Shiosai Grand Coast"

# --------------------------------------------------------------------------- alignment contract
# Validated gameplay contracts. These are NOT free tuning - rider grounding, traffic lane
# placement and the road mesh sweep are all solved against them (spec 4, spec 5.3).

ROAD_HALF_WIDTH = 3.5          # 7 m carriageway
SHOULDER_WIDTH = 0.55
SEA_LEVEL_Y = 0.0

MIN_CRUISE_RADIUS = 24.0       # spec 5.3 normal high-speed minimum
MIN_HAIRPIN_RADIUS = 18.0      # spec 5.3 absolute deliberate-hairpin floor

# PROVISIONAL: the compressed SC02 stack. 21 m is a design floor, so the shortened chapter
# buys fewer hairpins rather than tighter ones. Even count keeps the exit heading parity.
SWITCHBACK_COUNT = 6
SWITCHBACK_STACK_M = 1150.0

# PROVISIONAL: where the port town sits on the compressed route. See ANCHORS for why this is
# authored rather than remapped - it is the reveal distance from the tunnel mouth (4,804 m).
VILLAGE_CENTRE_M = 5200.0

# --------------------------------------------------------------------------- integration

INTEGRATION_STEP = 1.0         # metres, path integration
SAMPLE_SPACING = 3.0           # metres, published sample spacing (matches SakuraRoute.json)
KAPPA_SMOOTH_M = 26.0          # transition-spiral length

# Where the coast sits in world space. Sakura Pass occupies x -285..190, z -295..900 and the
# other regions sit in their own boxes; the coast is pushed far east and long in z so a 42 km
# route never touches them. Fast travel stays a transform move, not a scene load.
START_X = 6600.0
START_Z = -11000.0
START_HEADING_DEG = -40.0      # heading 0 = +Z (north); positive turns toward +X (east)

TARGET_MINUTES = 45            # PROVISIONAL: ~20 km at a scenic endurance pace

# --------------------------------------------------------------------------- chapters
# Spec 5.1. Chainage is authored, not discovered: each chapter's schedule sums to its length.
#
# ROUTE LENGTH REVISION (PROVISIONAL, user decision): the course was 42 km and the user could
# not physically reach the coastal chapters, so the whole route is compressed to ~20 km. All
# eight chapters are KEPT - the story is unchanged - but they are compressed UNEVENLY:
#
#   * the three inland chapters (Gateway / Switchbacks / Hydrangea) are compressed hardest
#     (to ~30% of their old length) so the dark tunnel and the harbour reveal land EARLY,
#   * the five coastal chapters keep proportionally more of their length (~53-70%), because
#     the coast is the content the user actually wants to ride.
#
# The result: tunnel entry at ~4.65 km and tunnel exit at ~4.80 km, i.e. 23-24% into the
# course, inside the "first 20-25%" the user asked for.
#
# Each chapter's curvature schedule is scaled GEOMETRICALLY by CHAPTER_SCALE below (lengths
# x f, curvature / f), so every chapter is a faithful scaled copy of its authored shape: the
# heading change per element (k * L) is invariant, so chapter hand-offs still line up and the
# plan view keeps the same macro geography. Radii shrink with f, which is why the switchback
# stack - whose 21 m hairpins are already at the design floor - is re-authored with FEWER
# hairpins at f = 1.0 instead of being scaled into an unrideable 6 m radius.

CHAPTERS = [
    ("gateway",     "Mountain Gateway",  0,     1800),
    ("switchbacks", "Upper Switchbacks", 1800,  3300),
    ("hydrangea",   "Hydrangea Descent", 3300,  4800),
    ("village",     "Fishing Village",   4800,  6900),
    ("bridge",      "Red Bridge",        6900,  8500),
    ("lighthouse",  "Lighthouse Climb",  8500,  11200),
    ("seaarch",     "Sea-Arch Road",     11200, 14400),
    ("highway",     "Coastal Highway",   14400, 20000),
]

TOTAL_LENGTH = CHAPTERS[-1][3]

# Old (42 km) chapter boundaries, kept so anchors and elevation keys authored on the old scale
# can be remapped piecewise-linearly onto the new one instead of being re-guessed by hand.
LEGACY_CHAPTER_BOUNDS = [0, 6000, 11000, 16000, 20000, 23000, 28000, 34000, 42000]
CHAPTER_BOUNDS = [CHAPTERS[0][2]] + [c[3] for c in CHAPTERS]

# Per-chapter geometric compression factor, derived (never hand-typed) from the two tables.
CHAPTER_SCALE = {
    CHAPTERS[i][0]: (CHAPTER_BOUNDS[i + 1] - CHAPTER_BOUNDS[i]) /
                    float(LEGACY_CHAPTER_BOUNDS[i + 1] - LEGACY_CHAPTER_BOUNDS[i])
    for i in range(len(CHAPTERS))
}


def remap(legacy_m):
    """Maps a chainage authored on the old 42 km scale onto the new ~20 km scale."""
    legacy_m = float(legacy_m)
    for i in range(len(CHAPTERS)):
        a, b = LEGACY_CHAPTER_BOUNDS[i], LEGACY_CHAPTER_BOUNDS[i + 1]
        if legacy_m <= b or i == len(CHAPTERS) - 1:
            t = (legacy_m - a) / float(b - a)
            na, nb = CHAPTER_BOUNDS[i], CHAPTER_BOUNDS[i + 1]
            return na + t * (nb - na)
    return float(TOTAL_LENGTH)


def straight(length):
    return (length, 0.0)


def arc(length, radius, turn):
    """`turn` is +1 for a right-hand (eastward) bend, -1 for left."""
    return (length, turn / float(radius))


def bend(angle_deg, radius, turn):
    """An arc specified by the angle it sweeps - how a road is actually laid out."""
    return arc(math.radians(abs(angle_deg)) * radius, radius, turn)


def hairpin(radius, turn):
    """A full 180 degree switchback."""
    return bend(180.0, radius, turn)


def _switchback_stack(total, count, radius, leg_radius):
    """
    Stacked hairpins (SC02): alternating 180 degree turns joined by traversing legs, so the road
    visibly stacks up the mountain face and the rider can trace the line below them.
    """
    hp_len = math.radians(180.0) * radius
    legs = count + 1
    leg = (total - count * hp_len) / legs
    if leg <= 0:
        raise ValueError("switchback stack over-subscribed")
    out = []
    turn = +1
    for _ in range(count):
        # A gentle counter-bend on the traverse keeps the legs from reading as pure straights.
        out.append(arc(leg, leg_radius, -turn))
        out.append(hairpin(radius, turn))
        turn = -turn
    out.append(arc(leg, leg_radius, -turn))
    return out


class _Path:
    """
    Accumulates the curvature schedule while tracking heading, so a chapter can be authored
    against the *direction it should be travelling* rather than by hoping its bends happen to
    balance. The first draft of this route was authored without heading tracking and the plan
    view showed it doubling back on itself three times - a coast road with the sea first to the
    west, then the east, then the south.
    """

    def __init__(self, heading_deg):
        self.items = []
        self.heading = math.radians(heading_deg)
        self.scale = 1.0

    def chapter(self, chapter_id):
        """
        Enters a chapter, so everything added from here is geometrically scaled by that
        chapter's compression factor: length x f, curvature / f. That pair is a similarity
        transform of the authored shape, so the heading each element contributes (k * L) is
        unchanged and the chapter still hands the next one the direction it promised.
        """
        self.scale = CHAPTER_SCALE[chapter_id]
        return self

    def add(self, *entries):
        f = self.scale
        for length, k in entries:
            length, k = length * f, (k / f if k else 0.0)
            self.items.append((length, k))
            self.heading += k * length

    def add_unscaled(self, *entries):
        """For elements whose RADIUS is a hard design floor (the 21 m hairpins)."""
        f, self.scale = self.scale, 1.0
        try:
            self.add(*entries)
        finally:
            self.scale = f

    def length(self):
        return sum(L for L, _ in self.items)

    def aim(self, target_deg, radius):
        """Turns by the shortest arc that leaves the path travelling on `target_deg`."""
        delta = (math.radians(target_deg) - self.heading + math.pi) % (2 * math.pi) - math.pi
        if abs(delta) < 1e-4:
            return
        self.add(arc(abs(delta) * radius, radius, 1 if delta > 0 else -1))

    def fit(self, target_total):
        """
        Pads with a straight so the chapter ends exactly on its contract chainage.

        Over-running RAISES rather than trimming: chapter chainages and anchor positions are
        spec values (5.1 / 5.5), so a chapter that does not fit its budget is an authoring
        error to correct, not something to silently truncate - truncation would eat the aiming
        arc and leave the next chapter pointing the wrong way.
        """
        delta = target_total - self.length()
        if delta < -1e-6:
            raise ValueError(f"chapter overruns its budget by {-delta:.1f} m "
                             f"(authored {self.length():.1f} m, budget {target_total:.1f} m)")
        if delta > 1e-6:
            # Padding is an ABSOLUTE chainage correction, so it must not be re-scaled.
            self.add_unscaled(straight(delta))


def _schedule():
    """
    The full curvature schedule, chapter by chapter (spec 5.1).

    Macro geography: the rider starts high and inland in the mountains to the EAST, descends
    west through the switchbacks to the coast, and then travels broadly NORTH along it for the
    remaining six chapters with the sea always on their left. Each chapter states the heading it
    hands to the next one, which is what keeps that true.
    """
    b = _Path(START_HEADING_DEG)

    # --- Mountain Gateway: arrival gate, wooded ridge road, sweeping climb (SC01).
    # Broad radii - this is a warm-up, the effort is the gradient, not the cornering. The road
    # curls around the mountain's shoulder to arrive at the summit facing back down the range.
    b.chapter("gateway")
    b.add(straight(420), bend(38, 320, -1), bend(30, 280, +1), straight(360),
          bend(52, 400, -1), bend(44, 240, +1), straight(300), bend(36, 360, -1),
          bend(58, 260, +1), straight(280), bend(48, 300, -1), bend(40, 220, +1),
          straight(340), bend(62, 380, -1), bend(34, 260, +1), straight(300))
    b.aim(200, 420)                 # hand over travelling SSW, along the range
    b.fit(CHAPTER_BOUNDS[1])

    # --- Upper Switchbacks: the stacked hairpin descent (SC02).
    # The hairpins are at 21 m - above the 18 m floor, below the 24 m cruise minimum, which is
    # the point: these are deliberate reduced-design-speed corners. 21 m is a DESIGN FLOOR, so
    # this chapter is NOT geometrically scaled like the others; instead the compressed budget
    # buys FEWER hairpins (6, was 12) on shorter traverse legs. Six keeps the count even, so
    # the stack still exits travelling the same way it did at 42 km.
    #
    # The traverse legs are near-straight ON PURPOSE. The first draft curved them at a 300 m
    # radius, which turned each leg through 55 degrees; successive hairpins then rotated
    # instead of offsetting and the whole chapter wound itself into a knot. With near-straight
    # legs the alternating hairpins offset by 2R each time, which is exactly the traceable
    # stacked ladder SC02 shows.
    b.chapter("switchbacks")
    b.add_unscaled(straight(60))
    b.add_unscaled(*_switchback_stack(SWITCHBACK_STACK_M, SWITCHBACK_COUNT, 21.0, 700.0))
    b.aim(-70, 120)                 # hand over travelling WNW, down toward the sea
    b.fit(CHAPTER_BOUNDS[2])

    # --- Hydrangea Descent: flower-framed speed, shrine overlook, tunnel (SC03).
    b.chapter("hydrangea")
    b.add(bend(40, 220, +1), straight(360), bend(55, 180, -1), bend(48, 200, +1),
          straight(420), bend(62, 150, -1), straight(300), bend(44, 240, +1),
          bend(50, 170, -1), straight(380), bend(36, 260, +1))
    b.aim(-8, 260)
    b.fit(remap(15500))
    # The last 500 legacy metres of this chapter are the TUNNEL (anchors SC_KM_155 / SC_KM_160):
    # a gentle constant bend, so the far portal frames the harbour reveal instead of staring
    # down a straight bore.
    b.add(bend(26, 1100, +1))
    b.fit(CHAPTER_BOUNDS[3])

    # --- Fishing Village: lived-in harbour, calm water, clear route to the bridge (SC04).
    # Tighter radii through the village lanes, opening out along the harbour front.
    b.chapter("village")
    b.add(bend(34, 130, +1), straight(240), bend(46, 90, -1), bend(38, 110, +1),
          straight(300), bend(52, 140, -1), straight(420), bend(30, 180, +1),
          bend(44, 120, -1), straight(360), bend(36, 160, +1))
    b.aim(6, 300)
    b.fit(CHAPTER_BOUNDS[4])

    # --- Red Bridge: the iconic crossing (SC05).
    # Approach curve, then a near-straight deck centred on the midpoint anchor, then the far
    # abutment turns away toward the lighthouse promontory.
    b.chapter("bridge")
    b.add(bend(30, 260, -1), straight(180))
    b.aim(2, 900)
    b.fit(remap(20850))
    b.add(bend(8, 4000, +1))                      # the deck's very slight curve
    b.fit(remap(22150))
    b.add(bend(40, 190, -1), straight(220))
    b.aim(-46, 240)                               # turn out onto the promontory
    b.fit(CHAPTER_BOUNDS[5])

    # --- Lighthouse Climb: promontory climbing loop (SC06).
    # Rolls out along the far shore, spirals up the headland so the rider can see the road both
    # below and above them, then drops back to rejoin the coast.
    b.chapter("lighthouse")
    b.add(straight(340), bend(50, 240, +1), bend(42, 200, -1), straight(380),
          bend(38, 260, +1))
    b.aim(-74, 300)
    b.fit(remap(25500))
    b.add(bend(95, 95, +1), bend(80, 80, +1), bend(110, 110, +1), bend(85, 95, +1))
    b.fit(remap(27500))
    b.add(bend(70, 85, -1))
    b.aim(12, 120)                                # reconnect, travelling north again
    b.fit(CHAPTER_BOUNDS[6])

    # --- Sea-Arch Road: exposed rolling coastal effort (SC07).
    b.chapter("seaarch")
    b.add(bend(44, 170, -1), straight(320), bend(56, 140, +1), bend(38, 190, -1),
          straight(400), bend(62, 160, +1), straight(280), bend(40, 210, -1))
    b.aim(4, 300)
    b.fit(remap(30900))
    b.add(straight(200))                          # through the arch itself (anchor SC_KM_310)
    b.fit(remap(31100))
    b.add(bend(48, 180, +1), straight(360), bend(54, 150, -1), bend(36, 230, +1),
          straight(420), bend(44, 200, -1), straight(300))
    b.aim(0, 320)
    b.fit(CHAPTER_BOUNDS[7])

    # --- Coastal Highway: fast open finale, island horizon, overlook (SC08).
    # Large radii throughout - this chapter is meant to be ridden hard.
    b.chapter("highway")
    b.add(bend(30, 700, -1), straight(600), bend(42, 900, +1), straight(700),
          bend(36, 650, -1), straight(800), bend(48, 800, +1), straight(650),
          bend(28, 750, -1), straight(700), bend(40, 850, +1), straight(540))
    b.aim(8, 700)
    b.fit(remap(41400))
    # The final overlook: the road swings inland and up onto the headland to finish facing the
    # islands, which is also the hand-off point to the next region.
    b.add(bend(62, 260, +1))
    b.aim(34, 340)
    b.fit(TOTAL_LENGTH)
    return b.items


# --------------------------------------------------------------------------- elevation
# Spec 5.2 targets are the fixed points; the keys between them are authored tuning, shaped to
# give each chapter the "primary effort" its row in spec 5.1 calls for.

ELEVATION_KEYS = [
    # --- inland: compressed to ~30% of its old plan length, so the OLD summit (420 m) would
    # now demand a ~20% grade. The mountain is therefore lowered to keep the authored grades
    # inside the spec 5.2 10% ceiling. PROVISIONAL: spec 5.2's "350-500 m gateway summit"
    # is not achievable on a 20 km course and is superseded by the user's length decision.
    (0,      62.0),    # start
    (450,    88.0),
    (900,   118.0),
    (1350,  148.0),
    (1800,  170.0),    # Gateway summit (was 420 m at 6 km)
    (2200,  148.0),
    (2750,  118.0),
    (3300,   95.0),    # switchback exit
    (3750,   72.0),
    (4200,   50.0),    # shrine overlook
    (4650,   24.0),    # tunnel entry
    (4800,   14.0),    # tunnel exit - the harbour reveal, just above the town
    # --- coastal: elevations here are ABSOLUTE, not scaled, because they are measured against
    # sea level (y = 0). A harbour at 44 m is not a harbour.
    (5300,    9.0),
    (5850,    7.0),    # was the village centre; the town now sits at 5,200 m
    (6400,   10.0),
    (6900,   16.0),    # bridge approach
    (7300,   38.0),
    (7700,   42.0),    # bridge deck, spec 25-55 m
    (8100,   38.0),
    (8500,   20.0),    # bridge exit
    (9000,   34.0),
    (9850,   52.0),    # lighthouse climb start
    (10400,  88.0),
    (10930, 125.0),    # lighthouse summit, spec 120-220 m
    (11200, 108.0),    # far-side reconnect
    (11700,  70.0),
    (12200,  42.0),
    (12800,  24.0),    # sea arch, spec oscillates 20-100 m
    (13400,  58.0),
    (14000,  82.0),
    (14400,  55.0),    # highway start
    (15400,  40.0),
    (16600,  62.0),
    (17800,  45.0),
    (18600,  68.0),    # island reveal
    (19300,  80.0),
    (20000,  92.0),    # final overlook, spec 50-120 m
]


def _pchip(xs, ys, q):
    """
    Monotone cubic (Fritsch-Carlson) interpolation.

    Used instead of a natural cubic because a natural cubic overshoots between keys: it would
    invent a bump above the authored Gateway summit and a dip below the tunnel mouth, i.e. grade
    reversals nobody authored. PCHIP cannot overshoot and is C1, so the grade is continuous -
    which is exactly what spec 5.2's "no instantaneous grade discontinuities" asks for.
    """
    xs = np.asarray(xs, dtype=np.float64)
    ys = np.asarray(ys, dtype=np.float64)
    h = np.diff(xs)
    delta = np.diff(ys) / h

    m = np.zeros_like(ys)
    m[0] = delta[0]
    m[-1] = delta[-1]
    for i in range(1, len(ys) - 1):
        if delta[i - 1] * delta[i] <= 0:
            m[i] = 0.0                      # local extremum: flat, so no overshoot
        else:
            w1 = 2 * h[i] + h[i - 1]
            w2 = h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i])

    q = np.asarray(q, dtype=np.float64)
    idx = np.clip(np.searchsorted(xs, q) - 1, 0, len(xs) - 2)
    hh = h[idx]
    t = (q - xs[idx]) / hh
    t2, t3 = t * t, t * t * t
    h00 = 2 * t3 - 3 * t2 + 1
    h10 = t3 - 2 * t2 + t
    h01 = -2 * t3 + 3 * t2
    h11 = t3 - t2
    return (h00 * ys[idx] + h10 * hh * m[idx] +
            h01 * ys[idx + 1] + h11 * hh * m[idx + 1])


# --------------------------------------------------------------------------- anchors
# Spec 5.5. The name encodes chainage in hundreds of metres: SC_KM_155 is 15.5 km.

ANCHORS = [(remap(km), name) for km, name in [
    (0,     "SC_KM_000_Start"),
    (6000,  "SC_KM_060_GatewaySummit"),
    (11000, "SC_KM_110_SwitchbackExit"),
    (14000, "SC_KM_140_ShrineOverlook"),
    (15500, "SC_KM_155_TunnelEntry"),
    (16000, "SC_KM_160_TunnelExit"),
    (20000, "SC_KM_200_BridgeApproach"),
    (21500, "SC_KM_215_BridgeMidpoint"),
    (23000, "SC_KM_230_BridgeExit"),
    (25500, "SC_KM_255_LighthouseClimbStart"),
    (27500, "SC_KM_275_LighthouseSummit"),
    (28000, "SC_KM_280_FarSideReconnect"),
    (31000, "SC_KM_310_SeaArch"),
    (34000, "SC_KM_340_HighwayStart"),
    (40000, "SC_KM_400_IslandReveal"),
    (42000, "SC_KM_420_OverlookFinish"),
]] + [
    # THE REVEAL. The village centre is deliberately NOT remapped proportionally (which would
    # put it at 5,855 m). The whole point of the compressed route is that the rider bursts out
    # of the dark bore and sees the port town, and at 5,855 m the town sat ~1 km beyond the
    # mouth, behind a headland and a screen of pines - the exit framed empty coast.
    # 400 m past the exit puts the harbour frontage roughly 200 m from the portal, i.e. in the
    # reveal frame. PROVISIONAL.
    (VILLAGE_CENTRE_M, "SC_KM_180_VillageCenter"),
]
ANCHORS.sort(key=lambda a: a[0])


def repo_root():
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", ".."))


def assets_root():
    """
    The Unity ``Assets`` folder, probed rather than assumed.

    The repository was restructured on 2026-09-15: the Unity project moved out of
    ``MapleRide/`` up to the repo root. Hard-coding either layout means this tool
    silently publishes ``ShiosaiRoute.json`` into an orphaned folder that Unity never imports -
    the generator would then keep building the PREVIOUS route while every number this script
    prints describes the new one.
    """
    root = repo_root()
    for candidate in (os.path.join(root, "Assets"),
                      os.path.join(root, "Assets")):
        if os.path.isdir(candidate):
            return candidate
    raise SystemExit("[shiosai] cannot locate the Unity Assets folder under " + root)


def renders_root():
    """Reviewable render folder; ``reference/good_graphics`` post-restructure."""
    root = repo_root()
    for candidate in (os.path.join(root, "reference", "good_graphics"),
                      os.path.join(root, "good_graphics")):
        if os.path.isdir(candidate):
            return candidate
    return os.path.join(root, "reference", "good_graphics")


# --------------------------------------------------------------------------- centreline

def _curvature_profile():
    """Expands the schedule to a per-metre curvature array, then smooths it into spirals."""
    sched = _schedule()
    n = int(round(sum(L for L, _ in sched) / INTEGRATION_STEP))
    kappa = np.zeros(n)
    at = 0.0
    for length, k in sched:
        a = int(round(at / INTEGRATION_STEP))
        b = min(n, int(round((at + length) / INTEGRATION_STEP)))
        kappa[a:b] = k
        at += length

    win = max(3, int(round(KAPPA_SMOOTH_M / INTEGRATION_STEP)) | 1)
    pad = np.pad(kappa, win // 2, mode="edge")
    return np.convolve(pad, np.ones(win) / win, mode="valid")[:n]


def plan_path():
    """Integrates the curvature profile into a plan polyline at INTEGRATION_STEP spacing."""
    kappa = _curvature_profile()
    h0 = math.radians(START_HEADING_DEG)
    heading = h0 + np.concatenate(([0.0], np.cumsum(kappa)[:-1] * INTEGRATION_STEP))

    dx = np.sin(heading) * INTEGRATION_STEP
    dz = np.cos(heading) * INTEGRATION_STEP
    x = START_X + np.concatenate(([0.0], np.cumsum(dx)[:-1]))
    z = START_Z + np.concatenate(([0.0], np.cumsum(dz)[:-1]))
    s = np.arange(len(x), dtype=np.float64) * INTEGRATION_STEP
    return x, z, s, kappa


def centreline(spacing=SAMPLE_SPACING):
    """
    The published centreline: the plan path resampled at `spacing`, with elevation applied as a
    function of chainage and the frame (tangent / side / up / bank) solved from the result.
    """
    px, pz, ps, _ = plan_path()

    q = np.arange(0.0, ps[-1] + 1e-6, spacing)
    x = np.interp(q, ps, px)
    z = np.interp(q, ps, pz)
    y = _pchip([k for k, _ in ELEVATION_KEYS], [v for _, v in ELEVATION_KEYS], q)

    p = np.stack([x, y, z], axis=1)
    n = len(p)

    tangents = np.zeros_like(p)
    tangents[1:-1] = p[2:] - p[:-2]
    tangents[0] = p[1] - p[0]
    tangents[-1] = p[-1] - p[-2]
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)

    world_up = np.array([0.0, 1.0, 0.0])
    # cross(up, tangent) points to the rider's RIGHT - the same convention the pass uses.
    sides = np.cross(world_up, tangents)
    sides /= np.maximum(np.linalg.norm(sides, axis=1, keepdims=True), 1e-9)

    curvature = np.zeros(n)
    for i in range(1, n - 1):
        cross = np.cross(tangents[i - 1], tangents[i + 1])
        seg = np.linalg.norm(p[i + 1] - p[i - 1])
        curvature[i] = np.dot(cross, world_up) / max(seg, 1e-6)
    kernel = np.ones(9) / 9.0
    curvature = np.convolve(curvature, kernel, mode="same")

    banks = np.clip(curvature * 0.55 * 180.0, -5.0, 5.0)
    banks = np.convolve(banks, kernel, mode="same")

    ups = np.zeros_like(p)
    for i in range(n):
        roll = math.radians(banks[i])
        sd = sides[i]
        u = np.cross(tangents[i], sd)
        u /= max(np.linalg.norm(u), 1e-9)
        ups[i] = u * math.cos(roll) + sd * math.sin(roll)
        sides[i] = sd * math.cos(roll) - u * math.sin(roll)

    arc_len = np.zeros(n)
    arc_len[1:] = np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))
    return p, tangents, sides, ups, banks, arc_len


# --------------------------------------------------------------------------- validation

def _self_conflicts(p, arc_len, clearance=9.0, vertical=6.0, ignore_span=80.0):
    """
    Finds places where the road runs into itself.

    Every alignment contract can pass while the centreline still crosses its own path - and a
    road that crosses itself with no junction is exactly the "unexplained dead end or teleport"
    the executive directive forbids. Switchback stacks make this a live risk: twelve hairpins
    offset by only 2R are one authoring slip away from overlapping.

    A conflict is two samples that are far apart ALONG the road (`ignore_span`) but close
    together in plan (`clearance`) and not vertically separated enough (`vertical`) to be a
    legitimate viaduct or tunnel crossing. Uses a uniform grid, so it stays linear in samples.
    """
    cell = clearance
    grid = {}
    for i, q in enumerate(p):
        grid.setdefault((int(q[0] // cell), int(q[2] // cell)), []).append(i)

    conflicts = []
    for i, q in enumerate(p):
        gx, gz = int(q[0] // cell), int(q[2] // cell)
        for ax in (-1, 0, 1):
            for az in (-1, 0, 1):
                for j in grid.get((gx + ax, gz + az), ()):
                    if j <= i or arc_len[j] - arc_len[i] < ignore_span:
                        continue
                    if abs(p[j][1] - q[1]) >= vertical:
                        continue
                    if math.hypot(p[j][0] - q[0], p[j][2] - q[2]) < clearance:
                        conflicts.append((float(arc_len[i]), float(arc_len[j])))
    return conflicts


def validate(p, arc_len):
    """
    Re-derives every contract the section 23 gate checks, here at author time, so a violation is
    caught in the tool that can fix it rather than three batchmode launches later.
    """
    issues = []
    length = float(arc_len[-1])

    if abs(length - TOTAL_LENGTH) > TOTAL_LENGTH * 0.02:
        issues.append(f"length {length:.1f} m is outside {TOTAL_LENGTH / 1000:.0f} km +/-2%")
    if not np.all(np.diff(arc_len) > 0):
        issues.append("distances are not strictly monotonic")

    # Smoothed grade exactly as ShiosaiMigrationAudit samples it: a 10-sample (30 m) window.
    window = 10
    d = arc_len[window:] - arc_len[:-window]
    g = np.where(d > 0.01, (p[window:, 1] - p[:-window, 1]) / np.maximum(d, 1e-6), 0.0)
    max_grade = float(np.max(np.abs(g)))
    max_step = float(np.max(np.abs(np.diff(g))))
    if max_grade > 0.10:
        issues.append(f"max smoothed grade {max_grade * 100:.1f}% exceeds 10%")
    if max_step > 0.02:
        issues.append(f"max grade step {max_step * 100:.2f}% exceeds 2%")

    # Plan curve radius, taken from the authored schedule rather than the resampled polyline.
    _, _, _, kappa = plan_path()
    peak = float(np.max(np.abs(kappa)))
    min_radius = 1.0 / peak if peak > 1e-9 else float("inf")
    if min_radius < MIN_HAIRPIN_RADIUS:
        issues.append(f"min plan radius {min_radius:.1f} m is below the "
                      f"{MIN_HAIRPIN_RADIUS} m floor")

    conflicts = _self_conflicts(p, arc_len)
    if conflicts:
        spans = ", ".join(f"{a / 1000:.2f}/{b / 1000:.2f} km" for a, b in conflicts[:6])
        issues.append(f"{len(conflicts)} self-conflict sample pair(s) - road runs into "
                      f"itself near {spans}")

    return {
        "length": length,
        "samples": len(p),
        "min_y": float(p[:, 1].min()),
        "max_y": float(p[:, 1].max()),
        "max_grade": max_grade,
        "max_step": max_step,
        "min_radius": min_radius,
        "conflicts": len(conflicts),
        "issues": issues,
    }


# --------------------------------------------------------------------------- publish

def write_route_json():
    out_dir = os.path.join(assets_root(), "Environment", "ShiosaiCoast")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "ShiosaiRoute.json")

    p, t, s, u, bank, arc_len = centreline()
    report = validate(p, arc_len)
    length = float(arc_len[-1])

    samples = [{
        "p": [round(float(v), 4) for v in p[i]],
        "t": [round(float(v), 4) for v in t[i]],
        "s": [round(float(v), 4) for v in s[i]],
        "u": [round(float(v), 4) for v in u[i]],
        "bank": round(float(bank[i]), 3),
        "d": round(float(arc_len[i]), 3),
    } for i in range(len(p))]

    # Anchor chainages are authored in nominal metres; the published distance array is the true
    # 3D arc length, very slightly longer because of the climbing. Anchors are placed
    # proportionally so each one still lands on the feature it names.
    scale = length / float(TOTAL_LENGTH)

    payload = {
        "_comment": "Generated by tools/blender/shiosai_route.py - do not edit by hand.",
        "regionId": REGION_ID,
        "roadHalfWidth": ROAD_HALF_WIDTH,
        "shoulderWidth": SHOULDER_WIDTH,
        "seaLevel": SEA_LEVEL_Y,
        "samples": samples,
        "segments": [{"id": SEGMENT_ID, "name": SEGMENT_NAME, "samples": samples}],
        # ONE UNBROKEN ONE-WAY ROUTE. The mock was a closed out-and-back that rode the same
        # 2.892 km backwards to make up its distance - which is why the gate reported 5.784 km
        # for a 2.892 km road. The Grand Coast is a point-to-point journey through eight
        # distinct chapters, so it is open: no lap, no reversal, no teleport.
        "courses": [{
            "id": COURSE_ID,
            "name": COURSE_NAME,
            "closed": False,
            "targetMinutes": TARGET_MINUTES,
            "legs": [{"segment": SEGMENT_ID, "from": 0.0, "to": round(length, 3)}],
        }],
        "chapters": [{
            "id": cid,
            "name": cname,
            "from": round(a * scale, 2),
            "to": round(b * scale, 2),
        } for cid, cname, a, b in CHAPTERS],
        "checkpoints": [
            {"segment": SEGMENT_ID, "distance": round(min(km * scale, length), 2), "name": name}
            for km, name in ANCHORS
        ],
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=1)

    print(f"[shiosai] wrote route definition -> {path}")
    print(f"[shiosai]   {report['samples']} samples, {length / 1000.0:.3f} km one way "
          f"({len(CHAPTERS)} chapters, {len(ANCHORS)} anchors)")
    print(f"[shiosai]   elevation {report['min_y']:.1f} - {report['max_y']:.1f} m")
    print(f"[shiosai]   max smoothed grade {report['max_grade'] * 100:.2f}%, "
          f"max grade step {report['max_step'] * 100:.3f}%, "
          f"min plan radius {report['min_radius']:.1f} m")
    if report["issues"]:
        for issue in report["issues"]:
            print(f"[shiosai]   CONTRACT VIOLATION: {issue}")
    else:
        print("[shiosai]   all alignment contracts satisfied")
    return path, report


if __name__ == "__main__":
    write_route_json()
