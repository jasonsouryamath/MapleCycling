# MapleRide — Project Context

> A smart-trainer-controlled, anime cycling RPG in a 3D cel-shaded chibi world.
>
> Source: “Create Cycling Game,” conversation `6aa24cb1-7c6c-83e8-80ff-860f5f4cd1f6`, September 10–11, 2026 UTC.
> This is a design handoff, not an implemented game or a verified hardware specification.

## Archive scope and how to interpret it

All 35 turns exposed by the conversation reader were retrieved. Text is preserved in [the recovered transcript](sources/CONVERSATION_TEXT.md) and [the reader export](sources/conversation-reader-export.json). Image-generation responses were absent from that export; some turns contain only the user's request. No original image bytes were accessible. See [the image inventory](design_assets/images/README.md).

**Explicit direction** means stated or reinforced by the user. **Working concept** means a proposal developed in the conversation, sometimes broadly endorsed, without every detail being separately approved. All thresholds, drop rates, distances, reward quantities, role names, and schedules below are **illustrative tuning**, unless expressly identified otherwise. Do not turn them into final production requirements.

Later refinements take precedence: rival cyclists replace monster combat; club reputation replaces item fragments; 3D cel-shaded chibi riders replace realistic or overly painterly interpretations. Image-only details cannot be reconstructed reliably from this text.

## 1. Vision and pillars

The starting request was a cycling game similar to Zwift, connecting a trainer and heart-rate monitor. The user then explicitly requested cycling-anime inspiration and MapleStory-like chibi characters riding bicycles.

The resulting identity is **an original anime cycling RPG**:
- Your actual watts and cadence are the primary gameplay controls.
- Explore a scenic Japanese-inspired cycling world; climb, descend, draft, chase, and encounter rivals.
- Earn levels, reputation, cosmetics, Rider Journal entries, and relationships through riding.
- Capture MapleStory's recognizable populations, discovery, collection, and rare-drop excitement while respecting exercise fatigue.
- Borrow the emotional language of **Yowamushi Pedal**: specialties, attacks, rivalries, teamwork, dramatic camera work, expressive auras.
- Use original riders, clubs, designs, names, and stories rather than importing anime or MapleStory assets.
- Keep riding accessible: short sessions and direct route launches matter.

Core loop:

**Ride → discover or catch an NPC rider → cycling challenge → rewards → reputation / Journal / relationships → unlock goals → ride again.**

The design should work with CPU riders before a large online population exists.

## 2. Smart trainer, BLE, and FTMS architecture

### Hardware principle

Implement the standards exposed by supported devices; the proposal does not depend on access to Zwift's proprietary code.

Conversation architecture:
- Bluetooth **Fitness Machine Service (FTMS)** for smart-trainer telemetry and supported control.
- Heart Rate Service for the HR monitor.
- Cycling Power and Cycling Speed/Cadence services for optional independent sensors.
- Optional steering/control devices were mentioned, but steering is not required for the central gameplay.
- The user explicitly rides a **Wahoo trainer**; exact model, firmware, and supported capabilities remain unknown.

Wahoo KICKR, Zwift Hub, and Garmin/Tacx were suggested as possible initial device families. This is a candidate compatibility list, **not evidence of tested support**. Verify the exact device and its exposed capabilities during implementation.

### Recommended stack from the conversation

**Unity + C#**, with a device layer independent of Unity gameplay. Unity was recommended, not delivered or pinned to a version. No repository, BLE library, operating-system target, or backend was selected.

```text
Smart trainer ──BLE telemetry──┐
HR monitor ────BLE telemetry───┼─> DeviceManager
Optional sensors ─────────────┘         │
                                      ├─> HUD / cycling challenge evaluation
                                      └─> CyclingPhysics ─> route position / avatar
                                                               │
Route gradient / ride mode ─> TrainerController ──BLE control─────┘
                                     (back to trainer)
```

Proposed components:
- **DeviceManager:** scanner, trainer adapter, HR adapter, power meter, cadence sensor.
- **CyclingPhysics:** rider/bike model, aerodynamic drag, rolling resistance, gravity, acceleration, later drafting.
- **TrainerController:** simulation, ERG, and resistance modes where supported.
- **Game:** rider avatar, course, HUD, camera.
- **Networking, later:** positions, events, multiplayer.

Conceptual connection sequence: scan → connect → discover services and capabilities → subscribe to Indoor Bike Data → request control through the control point → process telemetry and control responses → send supported simulation parameters. The archived conversation did not specify packet layouts, UUIDs, timeouts, reconnection rules, or a complete control state machine. Those need authoritative protocol documentation and device testing before implementation.

### Physics and feedback

Telemetry watts feed a virtual cycling model; virtual distance should follow that model rather than blindly copying trainer-reported speed.

Inputs discussed: rider mass, bike mass, power, road gradient, air density, aerodynamic area, and rolling resistance. The simplified source model was:

```text
P = P_aero + P_rolling + P_gravity + P_acceleration
F_aero = 0.5 * rho * CdA * v^2
F_roll ≈ Crr * m * g
F_gravity = m * g * sin(theta)
P = F * v
```

These are conceptual relations, not a complete numerical solver. Starting from zero speed, coasting, wind, and stable integration were not resolved.

As virtual terrain changes, supported simulation parameters feed back to the trainer. Example: a road changing from flat to 7% produces a harder climb. ERG, resistance, and simulation are separate control options; the source did not decide a universal challenge-mode policy.

### First technical milestone

A graphics-free connection prototype:
1. Discover and connect the user's trainer.
2. Display watts, cadence, available speed, and HR.
3. Request trainer control and change a modest simulated gradient.
4. Verify the physical response before integrating graphics.

Follow with watts-to-speed physics, one rider/road, terrain feedback, HUD/segments/PRs/ghosts, visual polish, and eventually multiplayer. No hardware milestone has been completed in this archive.

## 3. Cycling as the controller

The user's defining mechanic: replace button mashing in a struggle meter with briefly holding or exceeding **watts or cadence**.

| Input | Game meaning |
|---|---|
| Watts | Strength, acceleration, attacks, sprint effort |
| Cadence | Spinning technique, agility, cadence challenges |
| W/kg | Climbing performance |
| Sustained relative power / %FTP | Endurance and threshold challenges |
| Drafting and positioning | Tactics |
| Heart rate | Physiological display / presentation context |

Abilities trigger through cycling performance, not repeated controller-button presses. Controllers, keyboards, or a phone interface remain appropriate for menus and choices.

Power targets should generally scale against the rider's FTP. An example of 280 W is a mockup value, not a fixed requirement for every user. Cadence targets need their own difficulty treatment; FTP does not normalize cadence.

Heart-rate-driven visual intensity appeared as an early idea. No HR threshold, fitness inference, or mandatory HR-triggered ability was finalized.

## 4. Classes and anime abilities

The developed class roster:

| Class | Primary input | Working signature | Encounter personality |
|---|---|---|---|
| Climber | W/kg + cadence | Wings / Tailwind | Uphill effort and climbing surges |
| Sprinter | Peak watts | Burst / Beast Mode | Short explosive finish |
| Attacker | %FTP surges | Breakaway | Repeated attacks and recoveries |
| Spinner | High cadence | Cyclone | Controlled fast spinning |
| Rouleur | Sustained watts | Iron Engine | Stay with the rider over time |
| All-Rounder | Mixed | Adapt | Varied demands and tactics |

Earlier ideas also mentioned Time Trialist and Domestique; these were not retained as separate rows in the later six-class proposal.

Examples:
- **Tailwind:** exceed 100 RPM and 105% FTP for six seconds; wind/wings VFX and an acceleration presentation.
- **High-Cadence Chase:** chase a rider 20 m ahead by reaching around 105 RPM.
- **Breakaway:** Kuro's aggressive surge, lowered camera, red speed streaks, subtle FOV widening.
- **Beast Mode:** sprint with exaggerated expression, aura, and speed lines.

The conversation initially proposed small tactical physics bonuses (weight, aero, drafting). Later it emphasized keeping real cycling performance separate from collection progression. **Whether class abilities change physics or only presentation / challenge outcomes remains unresolved.** Do not silently add speed bonuses to competitive riding.

## 5. Rival encounters and tug-of-war

### Latest direction

CPU rival / rogue riders are the primary encounter opponents. The user explicitly liked this direction and expanded it into common and rare riders. Legendary cyclists take the boss role.

Cute creatures may remain as scenery, quest-givers, shopkeepers, aid-station helpers, spectators, or optional nonviolent chase events. Earlier monster HP, grabbing, killing, and exploding-into-coins concepts are historical, not the default combat loop.

### Encounter flow

Spot a rider → approach or catch them → challenge → effort meter → win / lose / rider escapes → reward and dialogue → resume riding.

A moving rare rider need not wait. Kuro's example begins with an 84 m gap that closes as the player chases. Catching him starts the duel. Group encounters were proposed if several players catch him.

Automatic roadside challenges were suggested for ordinary CPU riders. Boss prompts and mutual acceptance for player-versus-player duels were also suggested. The precise opt-in / decline rules remain open.

### Continuous meter, not a binary threshold

For a power encounter:

```text
effortRatio = currentPower / targetPower
below target → marker moves toward rival
at target    → approximately balanced
above target → marker moves toward player
```

The source's illustrative bands:

| Relative effort | Meter response |
|---|---|
| Below 70% | Rival gains rapidly |
| 70–90% | Rival gains gradually |
| 90–100% | Nearly balanced, generally losing |
| At target | Deadlock |
| 100–110% | Player gradually gains |
| 110–130% | Strong player gain |
| Above 130% | Large counterattack, with a proposed cap |

No integration equation, smoothing interval, win boundary, tie behavior, or failure timer was finalized. The original table blurred the exact 100% boundary; use the explicit deadlock description when prototyping.

Power and cadence encounters can use different evaluation rules. Multi-stage battles can sequence a surge, hold, recovery, and cadence finish.

Examples: sprinter 10-second finish; climber 105% FTP for 30 seconds; spinner around 105 RPM for 15 seconds; endurance rider 90% FTP for 60 seconds. These illustrate workout-like variety, not a prescribed training plan.

Animation mirrors the meter: struggling → working hard → standing attack → dramatic aura / speed streaks. A “BREAK!” moment accompanies passing the rival.

### Rewards and multiplayer

Win presentation: XP, universal currency, reputation, a possible chest or cosmetic, and a rider's acknowledgement. The rival rides away or returns alongside for dialogue.

“Perfect Break” bonuses and a benefit cap near 130–140% of target were early proposals; exact reward multipliers were not approved.

PvP duels were proposed with both riders accepting and effort normalized against each rider's FTP. FTP validation, competitive fairness, synchronization, and group-battle rules remain design work.

## 6. NPC ecosystem, rarity, and Rider Journal

Common riders provide the baseline so unusual riders feel special. Rarity should be readable through silhouette, kit, bike, animation, aura, and behavior. Avoid relying solely on permanent rarity labels.

| Proposed tier | Typical rider | Frequency | Illustrative duration |
|---|---|---|---|
| Common | Rookie | Very frequent | 10–20 sec |
| Uncommon | Club rider | Frequent | 20–40 sec |
| Rare | Specialist | Occasional | 30–60 sec |
| Elite | Named rider | Rare | 1–3 min |
| Legendary | Famous rival | Extremely rare | Multi-stage |
| Boss | Route champion | Fixed / event | 3–10 min |

Club hierarchy and rarity are related but not identical. Early tables placed club riders in different tiers; do not hardcode rank = rarity.

Rookie visual: simple helmet, small backpack, basic aluminum-looking bike, slightly awkward chibi pedaling. Population variants: Rookie, Club Rider, Messenger / Delivery Rider, Hill Rookie, Junior Racer, Team Rider.

Riders should inhabit the roads: passing in both directions, joining roads, riding in groups, drafting. Rare riders may wait at a summit, beneath a cherry tree, or suddenly overtake the player. Avoid a road filled with static MMO spawn points.

Working special-rider examples:
- **Hana — Sakura Phantom:** Sakura Pass, sunset, accumulated climbing, chance-based appearance; “over 500 m” and “5%” were example conditions.
- **Night Messenger:** evening city appearance after a ride-distance condition.
- **Storm Rider:** rain on mountain roads.
- **Golden Rookie:** ultra-rare rookie variant with a large currency reward; 1/500 was illustrative.
- **Akihiko — Wing of Sakura:** proposed Sakura summit climber boss, with changing demands across a multi-minute battle.
- **Kuro:** Kage Collective legendary encounter; see below.

Rarity means more interesting mechanics, not simply more required watts.

**Rider Journal:** organize by region / club and rarity. Track discovery, wins, lore, clothing, bike paint, titles, and regional completion. Unknown riders may appear as silhouettes or question marks.

Milestone examples varied: first win unlocks an entry, later wins unlock lore and cosmetics, with larger totals for titles. Specific 5 / 10 / 25 / 50-win rewards are provisional. Once a common rider's meaningful rewards are completed, encourage exploration of another region or club.

## 7. Cycling clubs, relationships, and campaign

The user proposed cycling clubs as the game's equivalent of anime guilds or factions. The response developed **Cycling Clubs** as the normal game term.

| Club | Identity / look | Specialty |
|---|---|---|
| Maple Cycling Club / Maple CC | Friendly local beginner club | Balanced fundamentals |
| Kitsune Racing | Aggressive red/black racing club | Sprints, attacks, power surges |
| Aozora Climbing Society / Aozora CC | Calm technical riders, blue/white kits | Climbing, cadence, W/kg |
| Kage Collective | Mysterious black-clad riders, scarce encounters | Breakaways, threshold, attacks |
| Shiosai Velo | Coastal club | Endurance, distance, sustained power |
| Imperial Velo / Imperial Racing | Prestigious elite organization | Mixed elite racing |

Aozora and Imperial naming variants were never fully reconciled. Preserve that uncertainty.

Each club can have its own crest, kit, bikes, music, clubhouse, territory, and story. Proposed hierarchy: Captain → Vice Captain → Specialists → Club Riders → Rookies.

Kitsune example names: Daichi (Ace), Rei (Vice Captain), Kai (Captain), plus Akira “Red Fox” in a Journal example. These are working names.

Campaign: start unaffiliated near Maple Town / City, meet clubs, learn different cycling skills, and eventually choose affiliations without an immediate permanent lock. Earlier school-based teams and championship ladders were exploratory predecessors to the club-world direction.

Player-created cycling clubs were proposed later, with shared jerseys, members, club levels, distance totals, rankings, and possibly territory. None of their backend or competition rules were specified.

Individual rivalry ladder proposed:
**Stranger → Challenger → Rival → Respected Rival → Friend.**

Later club reputation ladder proposed:
**Unknown → Recognized → Respected → Trusted → Honored.**

Earlier reputation tiers differed. Treat the latest ladder as the current working wording. NPC recognition, dialogue, earned kit gifts, and familiar rivals make progress feel like becoming known in the cycling world.

## 8. Kuro and visual direction

> **Superseded in part.** The approved Kuro character model sheet has since been recovered and is
> stored at [`design_assets/images/kuro-model-sheet-01.png`](design_assets/images/kuro-model-sheet-01.png). For
> Kuro's appearance that sheet is the source of truth and overrides the text-derived description
> below, which was written when no image was accessible. Notable corrections: he **wears a vented
> black helmet with a white maple leaf**, the sheet states **3.5 heads** tall, he wears a **jacket
> and shorts with bare forearms and knees** rather than a skinsuit, he carries a **tattered cape and
> a KAGE backpack**, the **maple leaf** is the recurring accent motif, and the rendering is a
> **soft-lit figurine style rather than flat cel shading**. Working files derived from the sheet are
> in [`design_assets/3d/kuro/`](design_assets/3d/kuro/). The rest of this section remains useful as the reasoning
> and story context behind the design.

### Kuro's recoverable identity

- Name: **Kuro**, explicitly corrected from “Kurt.”
- Black-clad **Attacker**, associated with **Breakaway**.
- Developed as **Kage Collective Vice Captain** and a **legendary roaming rival**.
- Used as the player-visible hero/avatar in visual and hub examples as well.
- Whether he is a selectable hero, story avatar, NPC rival, or supports multiple roles is unresolved.
- Dark kit and red attack VFX are supported by text. Exact hair, face, kit detailing, bike geometry, and final palette cannot be verified without the missing images.

### Strong user requirements

**3D cel-shaded chibi**, preserving a big head, tiny body, small road bike, and exaggerated cycling animation. The user rejected losing the chibi anime design and requested detailed 3D renders.

**One image = one character.** For isolated character renders, no cluttered lineup, interface, scenery, or unrelated material. The user subsequently requested Kuro on his bike as he would appear in game.

World: real 3D roads, terrain, towns, and perspective. Camera: behind and slightly above the rider, with selective dramatic changes during attacks. Riders: simple true 3D models with toon/cel shading that reads like 2D artwork. UI and many effects: 2D anime/RPG styling.

Ragnarok Online and HD-2D were references for the 2D/3D feel, not a commitment to flat sprite riders. Cycling requires rear, side, three-quarter, leaning, standing, drafting, climbing, and celebration views. Earlier overly detailed / painterly mockups were to be simplified into a buildable game look.

### Meshy-ready Kuro reference

The user requested and approved a Meshy AI-ready reference:
- A single full-body Kuro.
- Neutral A-pose, clean background.
- No bike.
- Clearly separated limbs.
- Simplify cape/scarf overlaps and reconstruction-confusing details.
- Preserve the established chibi proportions and character design.

The final “Create it” turn is present, but its image output is absent from the reader. This archive contains no verified Meshy reference or 3D model. Do not generate a replacement and label it the original.

## 9. City hub behavior

Key constraint: the player remains on the Wahoo trainer. **“Kuro doesn't walk. Kuro rides.”** This was the proposed hub design rule.

Ride Mode → city gateway → gradually reduced resistance → **Hub Mode**:
- Assisted movement along predetermined streets or a loop.
- Gentle cadence controls cruising speed; zero cadence stops the avatar.
- No required physical steering or walking.
- Destination selection through controller, keyboard/mouse, or a proposed phone companion.
- Shops and clubhouses open focused interfaces rather than requiring interior navigation.

Proposed Maple City destinations: Central Plaza, Club District, Market, Workshop / Maple Cycle Works, Rider Journal, Training Center, Ride Gate toward Sakura Pass.

Trainer behavior: simulation on open roads; simulation or a challenge-specific option during encounters; light free riding in the hub (0–1% example). Smooth the transition rather than abruptly changing the load. There are **no performance rewards for hammering in town**.

The hub provides social presence, cosmetics, rewards, quests, upgrades, and active recovery. Other real players could be visible cycling or resting near the fountain.

Clubhouse examples: Aozora mountain lodge, Kitsune racing garage, Kage dark cycling studio. Kage UI examples include Kuro as Vice Captain, Ren as Attack Specialist, and Yumi as Team Rider.

**Do not make the city mandatory each session.** Entry choices proposed: Quick Ride, World, Maple City, Rival Hunt, Training. Quick Ride should launch directly into Sakura Pass with minimal friction.

## 10. Sakura Pass and world map

**Sakura Pass** is the established showcase region, repeatedly liked by the user in visualization requests. Recoverable motifs: cherry blossoms, forested roads, hills and mountain climbing, a summit, cyclists at different distances, a nearby city connection, and rare-rider discovery.

Use it for the first coherent slice: Kuro riding a road with depth, a rival ahead, the cycling struggle UI, rewards, and a return to the hub. Hana and Akihiko are proposed regional encounters.

The user's world-map request explicitly includes **mountainous passes and a scenic ocean coast**.

One proposed connected itinerary:

```text
Maple City
   → Sakura Pass
      → Aozora Mountains
         → Coastal Highway
            → Shiosai Port
               → Kage City
                  → Imperial Highlands
```

This is a **textual route concept**, not a recovered map image, geographic topology, scale, or finalized road network. Cities serve as recovery zones between major regions. Mountain terrain supports climbing; the coast supports scenic endurance riding.

Earlier generic course lengths and a fictional Mt. Akagi race were prototype examples, not finalized Sakura Pass dimensions. No final route distances, grades, elevation profile, junction graph, map labels beyond the text, or spawn coordinates are recoverable.

## 11. Progression and grind philosophy

The user's goal: grindy and rewarding like MapleStory, without excessive grind. They explicitly rejected **fragments** as too MapleStory-like while agreeing with the underlying guaranteed-progress idea.

Latest principle:

**RNG = shortcut and excitement. Progression = guarantee.**

Every ride should contribute through multiple channels: XP, universal currency, club reputation, Journal, quests, cosmetics, personal records, and relationships. Avoid a ride feeling wasted because a drop did not occur.

**Replace fragment crafting with club reputation.** Defeat club riders → earn reputation → unlock access to club gear at the clubhouse. A random drop may give that gear early. Proposed currency labels evolved from Maple Coins to Credits; settle the name later. One universal currency plus club reputation is preferred over many fantasy currencies.

Riders may recognize repeated encounters and eventually gift signature kit after an important victory.

Illustrative reward cadence:
- Encounters: every few minutes.
- Drops: roughly 5–10 minutes.
- Daily-sized quests: a 20–40-minute ride.
- Levels / common cosmetics: a few rides.
- Club levels: several rides.
- Regional collections: weeks.
- Legendary sets / prestige: weeks to months.

These are aspirations for pacing, not a promise or finalized economy.

### Exercise-aware progression

- Rested progression was proposed, e.g. bonus XP for the next short ride after time off.
- Diminishing fitness-sensitive rewards after a long daily session were proposed.
- Avoid incentives to repeatedly overextend for essential progression.
- Keep rare riders exciting but unnecessary for essential power.
- Long grinds should primarily confer visible prestige, not major competitive speed.

Prestige examples: Sakura Master for repeated pass completions and a blossom trail; Kitsune Legend for club completion and captain wins; Shadow Rider for Kage rare discoveries. Exact counts, cosmetic bundles, and whether companions ship are unconfirmed.

Optional ultra-rare cosmetic world drops without pity were also proposed. This is an exception restricted to nonessential bonus cosmetics, not a replacement for guaranteed access to meaningful progression.

### Collection versus performance

Real cycling performance should determine the riding / racing foundation. RPG progression provides zones, quests, cosmetics, pets, story, and collection goals. Grinding should not grant a large speed advantage over a stronger cyclist.

Potential cosmetics span hair, helmets, glasses, kit, shoes, backpacks, bike paint/components, wheels, bottle details, lights, pets, wings, auras, and trails. This is a possibility list rather than launch scope.

## 12. Superseded ideas and unresolved decisions

| Earlier idea | Later direction |
|---|---|
| Realistic “Gran Turismo for cycling” environment | Original anime chibi world |
| Pure side-scrolling / flat sprite interpretation | Real 3D world and cel-shaded chibi models |
| Roadside monster combat and coin explosions | Rival cyclists and cycling-native rewards |
| Jersey fragments / many tokens | Universal currency plus club reputation |
| School teams as primary organizations | Cycling clubs with territories and hierarchies |
| Dense static MMO enemy placement | Cyclists inhabiting and moving through roads |

Outstanding decisions:
- Exact trainer, OS, BLE implementation, compatibility and control behavior.
- Unity version/render pipeline and actual repository layout.
- FTP setup, cadence accessibility, reward pacing, recovery and challenge opt-in.
- Meter integration, target scaling, timers, disconnect / stale telemetry handling.
- Class physics bonuses versus visual identity, and fair competition rules.
- Kuro's playable/NPC role and exact visual design.
- Canonical club/currency names, NPC roster and lore.
- Map topology, route metrics, art scale, world events and spawn rules.
- Multiplayer scope, player clubs, progression storage, and economy.
- Recovery of the original generated images.

## 13. Suggested implementation handoff

This ordering consolidates the conversation's proposed milestones; it is not evidence of completed work:

1. **Hardware proof:** trainer telemetry + HR + supported, verified resistance control.
2. **Ride foundation:** watts-to-speed, one road, route gradient feedback, HUD.
3. **Playable slice:** Kuro-style chibi rider, one Sakura Pass route, a small set of CPU rivals, tug-of-war and rewards.
4. **Persistent goals:** Journal, one currency, club reputation, cosmetic rewards.
5. **Hub:** assisted travel, low resistance, destination menus, direct Quick Ride.
6. **Expand:** class variety, rare encounters, club stories, mountains/coast, then multiplayer.

The early proposed fun test was a 10–15-minute mountain race, one player, three AI rivals, one trainer, HR, resistance, and attacks/counterattacks. Adapt it to Sakura Pass if that becomes the chosen first course.

For a coding agent: inspect the actual repository first, treat this file as design context, preserve the explicit direction, expose unresolved assumptions, and implement one reviewable milestone at a time. Consult current authoritative device documentation before implementing protocol details.

## 13.1 Implementation status (updated 2026-09-13)

Everything above is **design context**. This section is the only part of this document that records
what is **actually built in the repository**. It is maintained by the build agent; if it disagrees
with the rest of the file, this section is the one describing reality.

### Built and verified

| Milestone | State | Where |
|---|---|---|
| 1. Hardware proof | **Partial — mockable device layer only.** `DeviceManager` + `IRideTelemetrySource` + `SimulatedTrainerSource` produce watts/cadence/HR and accept a simulated-gradient channel back. **No BLE/FTMS code exists and no trainer has been tested: NOT VERIFIED.** `DeviceManager.SourceKind.BluetoothFtms` is a named placeholder that warns and falls back. | `Assets/Ride/DeviceManager.cs` |
| 2. Ride foundation | **Built.** Watts→speed physics (integrated, not solved), route-gradient feedback, telemetry HUD (power/cadence/speed/HR, %FTP, W/kg), route-following rider. | `Assets/Ride/CyclingPhysics.cs`, `RideSession.cs`, `RouteFollower.cs`, `Assets/Ride/UI/RideHud.cs` |
| Sakura Pass road network | **Built.** 4 segments / 6.70 km unique: `pass` 1 377 m, `s1` Kawabe Lakeshore Return 1 902 m, `aozora` Aozora Ascent 2 596 m, `maple` Maple City Road 824 m. Terrain, carriageway, markings, guardrail and dressing regenerated for all of them. | `tools/blender/sakura_route.py`, `build_terrain.py`, `build_road.py`, `build_expand.py` |
| Climb re-cut | **Built.** The 37–40 % wall between 400 and 475 m is gone; the climb now peaks at **10.8 %** with the 36.5 m summit and every x/z unchanged, so no landmark moved. | `sakura_route.py` `CONTROL_POINTS` |
| Runtime route data | **Built.** `SakuraRoute.json` now publishes segments + courses + checkpoints; `RouteGraphBaker` bakes it into `Assets/Resources/SakuraRouteGraph.asset`, which loads at play time. `RouteCourse` flattens a course into one ridable polyline with `PositionAt` / `GradeAt` / checkpoints. | `Assets/Ride/RouteGraph.cs`, `RouteCourse.cs`, `Assets/Editor/RouteGraphBaker.cs` |
| Courses + ride plan | **Built.** 4 Sakura Pass courses; laps are derived at run time from the target duration and the rider's FTP, never from a fixed course length. | `RideSession.EstimateLapMinutes` / `RecommendedLaps` |
| GPS / map window | **Built.** Course line, ridden-progress overlay, rest-of-network context roads, lake body, checkpoint pins with the next one named in gold, live rider chevron, distance / ascent / to-go / next-CP readouts, elevation band with a live gradient badge, lap counter, whole-course and follow zoom. | `Assets/Ride/UI/RouteMapHud.cs`, `RouteGraphics.cs`, `HudKit.cs` |
| Checkpoints | **Built.** `RouteDirector` fires arrival and section events off arc length and drives the banner. These are the intended award hooks for the Journal and club reputation, which are **not** built yet. | `Assets/Ride/RouteDirector.cs` |
| Streaming / performance | **Partial.** Dressing is chunked per ~110 m of route (63 chunks, 5 428 props) and range-activated around the rider. **LODGroups and GPU-instanced flora are still not implemented, and true runtime frame time has not been measured: NOT VERIFIED.** | `Assets/Ride/RouteDressingStreamer.cs`, `SakuraPassEnvironment.ChunkDressing` |

### Measured ride lengths (`RideValidation.Run`, FTP 220 W provisional)

| Course | km/lap | Gain | Minutes/lap | 30-min plan | 60-min plan |
|---|---|---|---|---|---|
| Pass Sprint | 1.38 | 37 m | 4.5 | 7 laps | 13 laps |
| **Sakura Circuit** | 3.28 | 69 m | 9.7 | **3 laps · 9.8 km · 24.8 min** | **7 laps · 22.9 km · 57.7 min** |
| **Aozora Skyline Loop** | 8.48 | 230 m | 27.7 | **1 lap · 8.5 km · 24.2 min** | 2 laps · 17.0 km · 48.3 min |
| **Sakura Gran Fondo** | 10.12 | 242 m | 31.2 | 1 lap · 10.1 km · 27.8 min | **2 laps · 20.3 km · 55.4 min** |

### Not built yet (still design context, not code)

Rival encounters and the tug-of-war meter · classes and abilities · Rider Journal · currency · club
reputation · rewards and cosmetics · Maple City hub interior and destination menus · multiplayer ·
real BLE/FTMS hardware · LOD/instancing · FTP setup flow.

### Deliberate deviations from the expansion plan

- The plan's **S2b "Skyline Traverse"** is not built. Getting it back to the summit crest meant
  either crossing the descent hairpins or running an elevated road across the open lake view. The
  Aozora climb is delivered as an **out-and-back spur** to the Cloudline Shrine instead; because a
  course leg can be ridden in either direction this still lands on the plan's 8.4 km / ~27 min
  budget with 2.5 km less geometry and no road crossings.
- The **Lake Village** was re-sited. It used to be a silhouette town on its own private bench at
  lake level, 200 m across the water. The lakeshore return now rides straight through there, so it
  is rebuilt as a frontage on the new road, seated on the real terrain heightfield.
- The **Aozora junction** leaves the pass at 150 m, not 250 m: at 250 m the switchback field had
  to start far enough north that its top limbs collided with the built descent.

## 14. Image references

No original images are included because the available reader exposed none and the browser was logged out. The following relative references point to inventory records, so this document has no broken image embeds:

- [Kuro character renders](design_assets/images/README.md#kuro-character-renders)
- [Kuro on his bike](design_assets/images/README.md#kuro-on-his-bike)
- [Sakura Pass gameplay and map mockups](design_assets/images/README.md#sakura-pass)
- [World map](design_assets/images/README.md#world-map)
- [Meshy-ready Kuro reference](design_assets/images/README.md#meshy-ready-kuro)

The inventory gives proposed local filenames and Markdown embeds to activate once originals are recovered.
