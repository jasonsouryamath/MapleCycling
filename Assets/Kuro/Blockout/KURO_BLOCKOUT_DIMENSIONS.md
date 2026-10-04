# Kuro + road bike — GRAYBOX BLOCKOUT (locked dimensions)

**Status:** reviewable silhouette/blockout milestone. *Not* the final model.
**Revision:** `ride_torso_lean` corrected 48° -> 20° (see “`ride_torso_lean` re-derivation” below) — the only constant changed in this pass; every other measurement in this document was already reference-accurate.
**Built by:** `tools/blender/build_kuro_blockout.py` (parametric — every number below is a
named constant in the `DIMS` dict at the top of that script).
**Compared by:** `tools/kuro_blockout_compare.py`.
**Sole target reference set:** `Assets/Kuro/ReferencePack`
(priority: `03_riding_left.png`, `04_riding_right.png`, `05_standing_front.png`;
supporting: `07_standing_right.png`, `08_character_wireframes.png`,
`09_bike_wireframes.png`, `00_design_sheet.png`).

> All values are **PROVISIONAL illustrative tuning** locked *for the blockout pass only*.
> They are named constants so the next milestone can retune them. Do not promote them to
> production requirements without review.

## Scale anchors (the two facts everything else hangs off)

| Anchor | Value | Source |
|---|---|---|
| Units | metres, 1.0 = real-world | `00_design_sheet.png` ("Units: Meters / Scale 1.0 = real-world") |
| Character standing height | **1.380 m** | design sheet says "~1.2–1.4 m"; matches existing `Assets/Kuro/kuro_cycle_fixed.glb` (1.384 m) |
| Bike overall length | **1.326 m** | matches existing `Assets/Kuro/kuro_on_bike.glb` footprint (1.326 m) |

Reference pixel→metre conversion used for body measurements:
`05_standing_front.png` subject spans **362 px** for 1.380 m → **3.812 mm/px**.

## Rider (measured off refs 05 / 07)

| Constant | Value (m) | Reference basis |
|---|---|---|
| `rider_standing_height` | 1.380 | crown → ground |
| `head_ball_w` / `_d` / `_h` | 0.360 / 0.390 / 0.345 | skull+hair mass (95 px wide, 109 px deep) |
| `helmet_extra_r` / `helmet_dome_h` / `helmet_lift` | 0.022 / 0.178 / 0.085 | crown lands at 1.380 |
| `jaw_height` | 0.945 | ref y=134 px |
| `neck_len` / `neck_dia` | 0.043 / 0.095 | chibi: head sits *on* the shoulders |
| `shoulder_height` / `shoulder_width` | 0.892 / 0.313 | ref y=148 px / 82 px |
| `chest_depth` | 0.240 | ref 07, 63 px |
| `waist_width` / `hip_width` | 0.187 / 0.229 | 49 px / 60 px |
| `crotch_height` | 0.496 | ref y=252 px (legs split) |
| `thigh_len` / `shank_len` | 0.255 / 0.245 | derived so hip→ankle is consistent with `crotch_height` |
| `thigh_dia` / `calf_dia` | 0.126 / 0.105 | 33 px / 28 px |
| `upper_arm_len` / `forearm_len` / `arm_dia` | 0.170 / 0.170 / 0.062 | hands land at ~0.36 of height |
| `shoe_len` / `_w` / `_h` | 0.260 / 0.110 / 0.070 | ref 07 reads 0.26–0.31; chunky chibi shoe |

## Bike (chibi-scaled road bike — ref 09 proportions, project footprint)

| Constant | Value (m) | Note |
|---|---|---|
| `wheel_dia` / `tire_r` | 0.500 / 0.024 | NOT 700c — the reference bike is chibi-scaled |
| `wheelbase` | 0.826 | 0.826 + 0.500 = **1.326 m** overall |
| `bb_height` | 0.200 | bottom-bracket centre |
| `rear_hub_dy` / `front_hub_dy` | −0.330 / +0.496 | from the BB, +Y = travel direction |
| `crank_len` | 0.100 | **scaled to the chibi inseam** (see caveat) |
| `saddle_top` / `saddle_dy` | 0.580 / −0.115 | solved so the leg reaches bottom dead centre |
| `hood_height` / `hood_dy` | 0.720 / 0.395 | refs 03/04: hoods sit ~0.14 m **above** the saddle |
| `head_tube_top` | 0.560 | small frame + riser stem, as the refs read |
| `bar_width` / `drop_depth` | 0.330 / 0.110 | ref 09 front/top views |
| `seat_tube_angle` / `head_tube_angle` | 72° / 71° | ref 09 side view |

## Riding pose (refs 03 / 04)

| Constant | Value | Note |
|---|---|---|
| `ride_hip_dy` / `ride_hip_z` | −0.105 / 0.635 | pelvis seated on the saddle |
| `ride_torso_lean` | **20°** above horizontal (was 48°) | hip → shoulder axis — re-derived, see below |
| `ride_head_pitch` / `ride_head_dy` / `ride_head_lift` | 22° / +0.130 / −0.070 | head carried forward over the cockpit (unchanged — falls into place once the lean is fixed) |
| `ride_crank_phase` | 15° | right foot forward, near horizontal — matches ref 04 |

### `ride_torso_lean` re-derivation (this pass’s fix)

The prior blockout pass measured the rider/bike anatomy correctly but locked
`ride_torso_lean = 48°`, an unvalidated pose guess. At 48° the hip→shoulder
vector is mostly *vertical* (cos 48°=0.669 forward, sin 48°=0.743 up), which put the
shoulder at **z=0.888 m** — almost back at standing-shoulder height (0.892 m) — so the
torso prism read as a tall vertical slab towering over the cockpit instead of a seated
cyclist's back, even though every individual body/bike measurement was correct.

**Method:** the only unambiguous hard geometric anchor visible in `03/04_riding_*.png` is
the **front wheel disc** (`wheel_dia = 0.500 m`, already locked). Its silhouette spans
rows ≈55.5% → 92.9% of the (caption-cropped) image height in `04_riding_right.png`
(410 px tall), giving a **3.218 mm/px** vertical scale independent of the standing-image
calibration. Reading the rider silhouette against that scale:

| Landmark | Row (% of image) | Height above ground |
|---|---|---|
| Helmet crown | 4.6% from top -> 95.4% up | **1.165 m** |
| Shoulder / collar top | ≈65% up | **≈0.73–0.76 m** |
| Saddle nose (hip contact) | ≈43.9% up | **0.579 m** (matches `saddle_top`=0.580 almost exactly) |

The saddle reading validates the existing `ride_hip_z`/`saddle_top` — no change needed
there. The shoulder reading (≈0.75 m) is **13–16 cm below** what the old 48° lean
produced (0.888 m), and is the actual defect: solving
`0.635 + sin(lean)×0.341 = 0.75` (`0.341 m` = the already-measured, unchanged
`shoulder_height − crotch_height` torso length) gives **lean ≈ 19.7°**, rounded to
**20.0°**.

**Why this single change is coherent and least-invasive:** `ride_head_pitch/_dy/_lift`
and the arm/leg IK targets are unchanged, yet the new lean alone cascades correctly:
* Head crown lands at **z=1.154 m** (ref: 1.165 m, −1.0%) — no head constants touched.
* Shoulder→hood arm span drops from 0.320 m to **0.180 m** (max reach is 0.340 m), which
  reads as a natural elbow bend instead of the old near-fully-stretched arm — matching
  the visibly bent elbows in refs 03/04.
* Hip/saddle/crank/leg IK are untouched, so the pedal-reach caveat below is unaffected.

## Kinematic caveat (an unresolved design decision, not a bug)

The reference art is **not kinematically consistent**: refs 03/04 imply a hip ~0.69 m above
ground, but this chibi's leg (hip→ankle ≈ 0.50 m) cannot reach a bottom-dead-centre pedal
from there with a real 0.1725 m crank. Two constants absorb this:

* `crank_len` = 0.100 m (proportional to the chibi inseam rather than real-world), and
* `saddle_top` = 0.580 m (lowered from the reference's apparent ~0.62–0.64 m).

The blockout therefore favours a pose that can actually complete a pedal stroke. If art
direction prefers the taller reference stance, raise `saddle_top`/`ride_hip_z` and accept a
cheated leg, or lengthen the legs — **this needs a decision before rigging.**

## Known deltas vs. the reference (accepted at blockout fidelity)

| Delta | Measured | Why accepted |
|---|---|---|
| Mounted height 1.159 m (was 1.343 m) vs ref apparent crown-to-ground 1.165 m | −0.5% | this pass's fix — previously −13% low-confidence, now within measurement noise |
| Mounted height 1.159 m vs the project's existing `kuro_on_bike.glb` 1.498 m | −23% | that legacy asset uses a different, more-upright pose on a differently-scaled bike; not a target for this pass |
| Front view widest point at 0.128 of height vs ref 0.215 | — | the ref's widest point is the **hair silhouette**, which is explicitly out of scope |
| Riding-view aspect (silhouette bbox) right 0.944 vs ref 0.825 (+14%), left 0.941 vs ref 0.682 (+38%) | +14–38% | refs 03/04 are cropped at the image edge (lower-bound only) **and** the corrected, more forward-leaning pose now spans further horizontally (head/hands reach further past the front hub) than the tightly-cropped photo shows; visually confirmed as a believable aero tuck in the renders, not a new proportion defect |

## Outputs

| Path | Contents |
|---|---|
| `Assets/Kuro/Blockout/kuro_blockout_standing.glb` | rider only, standing (1.381 × 0.404 × 0.434 m, feet at Y=0), 3 348 tris — unchanged, standing pose does not use `ride_torso_lean` |
| `Assets/Kuro/Blockout/kuro_blockout_riding.glb` | rider + bike, riding pose (**1.159** × 0.404 × 1.326 m, was 1.343 m tall), 6 164 tris |
| `Assets/Kuro/Blockout/kuro_blockout_bike.glb` | bike only (0.739 × 0.356 × 1.326 m), 2 816 tris — unchanged |
| `good_graphics/kuro_blockout/blockout_*.png` | grayscale orthographic renders (left / right / front / back) |
| `good_graphics/kuro_blockout/compare_*.png` | reference \| blockout \| silhouette-overlay sheets |

## Rebuild

```powershell
$BL = "C:\Users\jason\OneDrive\Desktop\MapleRide\tools\blender-4.5.10-windows-x64\blender.exe"
& $BL -b -P tools\blender\build_kuro_blockout.py -- --out "C:\Users\jason\OneDrive\Desktop\MapleRide"
python tools\kuro_blockout_compare.py
```

## Explicitly out of scope for this pass

Details, textures, decals/logos, hair, fingers, facial features, drivetrain
(chainrings/cassette/chain/derailleurs), spokes, bottle cages, and any rigging or skinning.
The renders are **orthographic**; the references are perspective renders, so outline overlays
are indicative rather than pixel-exact.
