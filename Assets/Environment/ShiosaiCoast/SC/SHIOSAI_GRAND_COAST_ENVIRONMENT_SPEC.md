# Shiosai Grand Coast — Unity Environment Production Specification

**Project:** MapleRide  
**Region:** Shiosai Coast  
**Route:** Shiosai Grand Coast  
**Target length:** approximately 42 km  
**Engine:** Unity 6000.4.11f1  
**Current renderer:** Built-in Render Pipeline  
**Purpose:** Authoritative Copilot implementation handoff  
**Reference fidelity:** Full-resolution PNG source renders, 1536 × 1024

---

## 1. Mission

Build Shiosai Grand Coast as one continuous, believable, visually dense cycling route. The eight reference renders define the desired mood, landmark language, terrain layering, vegetation, architecture, materials, color treatment, and sense of scale. They are not eight disconnected dioramas. The implementation must connect them into a seamless 42 km ride with an unbroken paved centerline.

The result must feel like a polished 3D cel-shaded anime cycling world with unusually deep scenery: near roadside detail, readable mid-distance destinations, distant mountain and island silhouettes, and landmarks that the player can first see, gradually approach, pass, and later recognize behind them.

### Non-negotiable outcome

- One continuous route from Mountain Gateway to Coastal Highway.
- Every road shown to the player has a believable origin and destination.
- The red bridge, village, lighthouse, tunnel, sea arches, and final overlook are physically connected.
- The player can ride the entire route without teleporting or encountering an unexplained dead end.
- Long views remain sharp and layered. Do not apply strong shallow depth-of-field blur during gameplay.
- Visual richness comes from composition, terrain layers, landmark recurrence, atmospheric perspective, parallax, materials, and prop density—not random clutter.
- Maintain stable performance while preserving the hero views.

---

## 2. Reference image index

All paths below are relative to this document.

| ID | Route chapter | Kilometer range | Reference |
|---|---|---:|---|
| SC01 | Mountain Gateway | 0–6 km | [01-mountain-gateway.png](01-mountain-gateway.png) |
| SC02 | Upper Switchbacks | 6–11 km | [02-upper-switchbacks.png](02-upper-switchbacks.png) |
| SC03 | Hydrangea Descent | 11–16 km | [03-hydrangea-descent.png](03-hydrangea-descent.png) |
| SC04 | Fishing Village | 16–20 km | [04-fishing-village.png](04-fishing-village.png) |
| SC05 | Red Bridge | 20–23 km | [05-red-bridge.png](05-red-bridge.png) |
| SC06 | Lighthouse Climb | 23–28 km | [06-lighthouse-climb.png](06-lighthouse-climb.png) |
| SC07 | Sea-Arch Road | 28–34 km | [07-sea-arch-road.png](07-sea-arch-road.png) |
| SC08 | Coastal Highway | 34–42 km | [08-coastal-highway.png](08-coastal-highway.png) |

The references are concept targets, not literal engineering drawings. Preserve their visual hierarchy and emotional beats while correcting any impossible road geometry, scale ambiguity, or generative-image inconsistency.

---

## 3. Core visual language

### 3.1 Style

- True 3D environment with clean, stylized geometry.
- Anime-inspired, cel-shaded material response.
- Saturated but controlled colors.
- Large readable shape groups; limited micro-noise.
- Real spatial depth and perspective, not a flat painted backdrop.
- Architecture and infrastructure feel Japanese-inspired without copying a specific real place.
- Natural terrain is dramatic but traversable and structurally plausible.
- The environment is the hero; the rider remains visually readable against it.

### 3.2 Palette anchors

| Element | Base family | Highlight | Shadow/accent |
|---|---|---|---|
| Near ocean | turquoise `#20AFC1` | foam `#F4FCFB` | deep water `#176C91` |
| Far ocean | cobalt `#2377B9` | sun path `#F6D79B` | horizon `#466E9C` |
| Forest | green `#3F7C3D` | leaf light `#79A94E` | cool canopy `#244B3C` |
| Rock | warm gray `#8B8173` | face light `#BAAA91` | cool crevice `#4C5360` |
| Asphalt | charcoal `#353940` | warm sun `#55575A` | cool shade `#252C36` |
| Buildings | cream `#E9DFC8` | stucco light `#FFF2D8` | timber `#69513F` |
| Roofs | slate blue `#344D62` | ridge light `#64798A` | eave `#202D38` |
| Landmark red | coral vermilion `#D95748` | sun edge `#EF7965` | shade `#9B302D` |
| Hydrangea blue | `#668ED6` | `#9BB7EE` | `#485D9E` |
| Hydrangea pink | `#D47DA9` | `#F0A7C9` | `#98527D` |

Match relationships before exact hex values. The ocean must dominate cool color mass, vegetation must frame rather than bury the roads, cream buildings must punctuate the coast, and coral red must identify route landmarks.

### 3.3 Value hierarchy

- Road surface stays readable at all times.
- Rider silhouette must not disappear into asphalt or forest shadows.
- Distant mountains step progressively lighter and cooler.
- White foam and lighthouse are the brightest environmental features, but neither should clip.
- Red bridge and torii are high-chroma landmarks, not the dominant color of every scene.

---

## 4. Route master plan

### 4.1 Continuous topology

```text
Neighboring Region
  → Mountain Gateway
  → Ridge Climb
  → Upper Switchback Descent
  → Hydrangea Descent
  → Shrine Overlook
  → Cliff Tunnel
  → Fishing Village Harbor Road
  → Red Bridge Approach
  → Red Bridge Crossing
  → Lighthouse Promontory Climb
  → Far-Side Reconnection
  → Sea-Arch Road
  → Coastal Highway
  → Island Overlook Turnaround / Next-Region Portal
```

There is exactly one primary route spline. Optional roads may form short loops, service access, or visible background connections, but must never imply inaccessible major routes or broken navigation.

### 4.2 Suggested route metrics

| Segment | Length | Elevation behavior | Suggested average grade | Suggested max grade |
|---|---:|---|---:|---:|
| Mountain Gateway | 6.0 km | rolling then sustained climb | +3.5% | 8% |
| Upper Switchbacks | 5.0 km | long descent | −4.8% | −9% |
| Hydrangea Descent | 5.0 km | technical descent | −3.2% | −8% |
| Fishing Village | 4.0 km | flat recovery | 0–1% | 3% |
| Red Bridge | 3.0 km | approach, crossing, exit | +0.5% | 4% |
| Lighthouse Climb | 5.0 km | short steep climb and descent | mixed | 10% |
| Sea-Arch Road | 6.0 km | exposed rolling terrain | mixed ±2.5% | 7% |
| Coastal Highway | 8.0 km | fast rolling finish | −0.5% net | 5% |

These are visual/gameplay targets. Integrate with MapleRide's trainer and route-gradient systems rather than deriving grade from decorative mesh alone.

### 4.3 Road geometry rules

- Standard paved width: 6.0–7.0 m for two-way segments.
- Narrow village/coastal lane: 4.5–5.5 m where deliberately constrained.
- Shoulder: 0.4–1.0 m based on terrain.
- Minimum rideable curve radius: 18 m; use 24 m or greater for high-speed descents.
- Vertical curve transitions must be smooth enough for the bike physics and chase camera.
- Avoid sudden spline banking, mesh twists, collider seams, or camera-visible gaps.
- Switchbacks use connected hairpins with credible retaining walls and drainage.
- All cliff edges near the ride line require a guardrail, stone parapet, or equivalent barrier.
- Tunnels require a complete approach, interior, exit, lighting transition, and continuing road.
- Bridge deck must land on terrain at both ends and use a continuous road collider.
- The lighthouse route passes the lighthouse and reconnects; it is not a dead-end driveway.
- The final overlook includes a rideable turnaround plus a clearly staged onward portal.

### 4.4 Kilometer and event anchors

Create named route anchors at least every 250 m and major anchors at each kilometer. Required major transforms:

```text
SC_KM_000_Start
SC_KM_060_GatewaySummit
SC_KM_110_SwitchbackExit
SC_KM_140_ShrineOverlook
SC_KM_155_TunnelEntry
SC_KM_160_TunnelExit
SC_KM_180_VillageCenter
SC_KM_200_BridgeApproach
SC_KM_215_BridgeMidpoint
SC_KM_230_BridgeExit
SC_KM_255_LighthouseClimbStart
SC_KM_275_LighthouseSummit
SC_KM_280_FarSideReconnect
SC_KM_310_SeaArch
SC_KM_340_HighwayStart
SC_KM_400_IslandReveal
SC_KM_420_OverlookFinish
```

Use these for streaming, respawn, rival encounters, trainer-gradient sampling, ambience, music, camera cues, and automated route validation.

---

## 5. Segment specifications

## 5.1 SC01 — Mountain Gateway, 0–6 km

**Reference:** `01-mountain-gateway.png`

### Experience

Arrival into Shiosai. The ocean, village, red bridge, and lighthouse appear as distant promises. The player begins among shaded woodland rollers and earns the panorama through a sustained ridge climb.

### Required composition

- Road enters from the previous region behind the player.
- Original timber/stone gateway straddles or frames the road near the major reveal.
- Foreground includes stone walls, pine trunks, hydrangea clusters, and roadside depth cues.
- Midground shows layered wooded slopes and the road continuing along the ridge.
- Background shows village, red bridge, lighthouse, ocean, and islands.
- The road visibly continues around the mountain rather than ending at the overlook.

### Gameplay

- First 2 km: rolling ±2% warm-up.
- Next 4 km: sustained 3–6% climb with two short 7–8% ramps.
- Provide at least two safe overlook widenings without interrupting route flow.
- Avoid dense props inside the rider's braking sight distance.

### Assets

Gateway kit, warm-gray stone retaining wall, timber rail, mountain guardrail, pine set, deciduous canopy set, hydrangea clusters, distant village proxy, bridge proxy, lighthouse proxy, island cards/meshes.

## 5.2 SC02 — Upper Switchbacks, 6–11 km

**Reference:** `02-upper-switchbacks.png`

### Experience

A long, legible descent where the player sees multiple future hairpins and the destination far below. This section proves the route is physically connected.

### Required composition

- At hero vista points, show four or more traceable connected hairpins.
- Each road tier must meet the next through an actual hairpin outside or inside the frame.
- Village, bridge, harbor, and lighthouse remain below as orientation landmarks.
- Use rock cuts on the uphill side and retaining walls/guardrails downhill.
- Vegetation separates road tiers without hiding all connections.

### Gameplay

- Average descent near −5% with alternating 200–500 m straights and hairpins.
- Protect sightlines at corner entries.
- Add braking/cornering visual cues without placing gameplay-obstructing traffic.
- Camera should reveal lower switchbacks gradually, not expose the whole route continuously.

### Assets

Hairpin spline modules, tall retaining walls, drainage channels, chevrons, guardrails, rock-cut modules, cliff vegetation, switchback overlook props, distant landmark proxies.

## 5.3 SC03 — Hydrangea Descent, 11–16 km

**Reference:** `03-hydrangea-descent.png`

### Experience

Fast, colorful, technically engaging descent. Hydrangeas and shrine architecture create a strong identity before the route compresses through a cliff tunnel.

### Required composition

- Hydrangea banks frame the road but do not invade the collision line.
- A small shrine overlook and coral-red torii sit beside the route, not across it as an obstacle.
- Road curves toward a visible stone-lined tunnel mouth.
- The far tunnel exit provides a glimpse of harbor light and descending continuation.
- Use dappled canopy light, cool tunnel contrast, and ocean flashes through trees.

### Gameplay

- Curves are technical but readable.
- Tunnel is approximately 250–450 m, long enough to create contrast but not monotony.
- Grade transition and trainer resistance must remain smooth through tunnel portals.
- Provide a wide enough tunnel bore for chase camera and safe two-way riding.

### Assets

Hydrangea color variants, shrine overlook kit, original torii, stone tunnel portals, tunnel interior modules, lamps, wet-darkened portal stone, drainage, cliff mesh, tree canopy volumes.

## 5.4 SC04 — Fishing Village, 16–20 km

**Reference:** `04-fishing-village.png`

### Experience

Active recovery and environmental storytelling. The route flattens along a lived-in harbor before feeding directly toward the red bridge.

### Required composition

- Cream plaster/timber houses with slate-blue tile roofs.
- Quay wall and turquoise harbor water on the seaward side.
- Fishing boats, nets, crates, floats, and ropes remain outside the active riding line.
- Utility poles and wires reinforce depth but must not create visual tangles.
- Red bridge remains visible ahead and grows larger across the chapter.
- Road visibly becomes the bridge approach.

### Gameplay

- Mostly flat, 0–1%, with no forced dismount.
- Gentle cadence/recovery encounter pacing.
- Keep a clear ride corridor despite dense set dressing.
- Optional harbor loop may rejoin before the bridge; it must not replace the main spine.

### Assets

Modular house kit, shopfront/awning variants without readable branded text, tile roofs, sea walls, docks, boat set, fishing props, utility poles, lamps, planters, cats/birds as low-cost ambience, harbor reflection probes.

## 5.5 SC05 — Red Bridge, 20–23 km

**Reference:** `05-red-bridge.png`

### Experience

The region's unmistakable midpoint landmark. The crossing opens the view in all directions and transfers the player from village shore to lighthouse promontory.

### Required composition

- Coral-red steel arch structure with rhythmic beams.
- Road approaches, crosses the inlet exactly once, and lands visibly on the far road.
- Village and mountain switchbacks are readable behind.
- Lighthouse climb is readable ahead.
- Fishing boats and water parallax below emphasize height and motion.

### Gameplay

- Wide unobstructed bridge deck and safe rails.
- Mild approach/exit grades only.
- Strong but controlled crosswind presentation; do not move the rider unpredictably.
- Use bridge midpoint as checkpoint, music transition, and potential rival trigger.

### Assets

Modular arch bridge kit, structural beams, deck, expansion joints, rails, foundations, bridge lamps, inlet water, boat paths, seabirds, far-shore road connection.

## 5.6 SC06 — Lighthouse Climb, 23–28 km

**Reference:** `06-lighthouse-climb.png`

### Experience

A compact heroic climb around a rocky promontory. The player sees both where they came from and the wild sea-arch road waiting beyond.

### Required composition

- Bridge exit flows directly into lower climb.
- Two or more connected bends wrap around the promontory.
- Tall white lighthouse dominates the summit silhouette.
- Small overlook platform sits off the route.
- Road passes the lighthouse, descends on the far side, and reconnects toward the sea arches.
- Red bridge and village appear behind; sea arches appear ahead.

### Gameplay

- Short ramps up to 10%, separated by recovery bends.
- Summit payoff should occur around 27.5 km.
- Far-side descent must be visible before the summit ends.
- Avoid a fake summit cul-de-sac.

### Assets

Hero lighthouse, keeper building, promontory stone walls, overlook deck, windswept pines, exposed rock kit, bridge-distance LOD, sea-arch-distance LOD, spray emitters.

## 5.7 SC07 — Sea-Arch Road, 28–34 km

**Reference:** `07-sea-arch-road.png`

### Experience

The wildest coastal chapter: rolling road, strong wind, sea caves, spray, and a memorable passage through a broad natural arch.

### Required composition

- Road is carved into or supported by solid rock.
- One primary broad arch carries the road through a headland.
- Secondary offshore arches remain scenic, not confused with the ride path.
- Road is visible continuing beyond the arch toward the final highway.
- Use surf impact, spray, grasses, and twisted pines to sell exposure.

### Gameplay

- Repeating short rises and dips, generally within ±7%.
- Maintain dry, clear asphalt.
- Wind effects are visual/audio unless gameplay explicitly supports them.
- Rock overhang and tunnel clearance must accommodate the chase camera.

### Assets

Hero road arch, offshore arch variants, sea caves, wave-impact VFX, foam decals, cliff supports, coastal rail kit, grasses, low pines, rockfall netting, distant highway proxy.

## 5.8 SC08 — Coastal Highway, 34–42 km

**Reference:** `08-coastal-highway.png`

### Experience

A fast, open finale with long sightlines and golden light. It should feel both conclusive and connected to a larger world.

### Required composition

- Broad flowing road with ocean consistently on the established side.
- Lighthouse and prior coastline remain visible far behind at selected viewpoints.
- Distant islands grow into the main horizon objective.
- Route curves into a large circular overlook/turnaround.
- A visible road/tunnel beyond the overlook suggests the next region.
- Late-afternoon light moves toward sunset without changing the series' art direction.

### Gameplay

- Long fast straights and broad sweepers.
- Rolling profile with mild net descent.
- Final 2 km provides a clear acceleration/sprint opportunity.
- Finish trigger sits after the overlook reveal, not before it.
- Turnaround remains rideable if the next region is not implemented.

### Assets

Highway spline kit, wide guardrail, overlook platform, original sail-shaped monument, low coastal vegetation, distant island chain, final tunnel/portal, sunset sky setup, finish trigger dressing without intrusive UI.

---

## 6. Terrain and world construction

### 6.1 Scale and partitioning

- Unity scale: 1 unit = 1 meter.
- Use a route-centered world rather than building a uniformly detailed 42 × 42 km square.
- Partition into eight primary segment scenes plus persistent systems.
- Recommended additive scene layout:

```text
SC_Persistent
SC_01_MountainGateway
SC_02_UpperSwitchbacks
SC_03_HydrangeaDescent
SC_04_FishingVillage
SC_05_RedBridge
SC_06_LighthouseClimb
SC_07_SeaArchRoad
SC_08_CoastalHighway
SC_Backdrop_OceanIslands
```

- Keep two neighboring route scenes loaded around the player.
- Distant hero landmarks use persistent proxy LODs so they can be seen several chapters away.
- Swap proxies for full assets invisibly before approach.

### 6.2 Terrain layers

Build scenery in five depth bands:

1. **Ride envelope, 0–30 m:** collision-accurate road, barriers, drainage, flowers, signs, walls.
2. **Near environment, 30–150 m:** cliffs, houses, major trees, harbor props, structural details.
3. **Midground, 150–800 m:** terrain terraces, village blocks, coves, landmark full/proxy models.
4. **Far terrain, 0.8–5 km:** simplified mountain ridges, coast, islands, large building clusters.
5. **Horizon, 5–30 km apparent:** very low-cost silhouettes, haze-controlled islands/mountains, sky and ocean horizon.

Every hero camera should contain at least three distinct depth bands. The references feel deep because foreground occlusion, midground destinations, and distant silhouettes overlap clearly.

### 6.3 Terrain mesh strategy

- Use Unity Terrain where broad sculpting/vegetation painting is efficient.
- Use authored cliff meshes for hero rock cuts, sea arches, tunnel portals, promontories, and shoreline silhouettes.
- Hide terrain/mesh transitions with rock skirts, vegetation, retaining walls, or shoreline foam.
- Avoid repeating cliff textures at obvious uniform scale.
- Use macro color variation and triplanar projection on steep rock faces.

### 6.4 Floating origin

A 42 km route risks transform precision issues if laid out linearly far from origin. Use one of:

- Floating-origin/world-shift system centered on rider, preferred if existing systems support it.
- Curved/compacted world layout keeping playable geometry within roughly ±8 km while preserving route distance along the spline.
- Segment-local coordinates with controlled scene transitions.

Do not allow wheel jitter, shadow shimmer, physics instability, or camera shake from large coordinates.

---

## 7. Road spline and cycling integration

- One authoritative route spline provides position, tangent, banking, grade, distance, segment ID, and respawn data.
- Road render mesh, collider mesh, AI path, camera hints, and trainer grade sampling derive from the same source or verified synchronized data.
- Grade is calculated over a smoothing distance appropriate for trainer control; do not send raw triangle-to-triangle slope changes.
- Place distance markers and event zones using spline distance, not manually guessed world positions.
- Detect reverse travel and support safe turnaround where intended.
- Validate total spline length within ±2% of 42 km.
- Validate there is no gap greater than 2 cm between sequential road collision sections.
- Road banking should improve visual cornering but must not corrupt trainer grade.
- AI riders need lane offsets and overtaking space throughout the route.

---

## 8. Deep-focus rendering and scenic depth

The request for “extreme depth of field like the renders” should be implemented as **extreme depth/deep focus**, not extreme shallow optical blur. The references show sharp foreground roads, readable midground landmarks, and detailed far scenery simultaneously.

### 8.1 Camera focus policy

- Gameplay depth of field: off, or extremely restrained.
- If a post effect is added later, focus follows the rider/road and preserves mid/far landmarks.
- Never blur the upcoming road, bridge, lighthouse, or route objective.
- Reserve cinematic shallow DoF for menus, photo mode, or controlled cutscenes.
- Use composition and atmospheric perspective—not blur—to separate depth planes.

### 8.2 Camera baseline

- Perspective chase camera behind and slightly above rider.
- Vertical FOV: 42°, adjustable within 38–48°.
- Near clip: 0.1–0.2 m.
- Far clip: target 12–20 km, supported by LOD/proxy design; do not rely on full-detail meshes at that range.
- Dynamic clipping-plane changes must not visibly pop mountains or ocean.
- Camera height and distance should keep road curvature and distant landmark visible.
- Use Cinemachine only if already present or intentionally added; do not make the environment depend on an uninstalled package.

### 8.3 Atmospheric perspective

- Near scene retains saturation, contrast, and warm sunlight.
- Midground shifts slightly cooler and lower contrast.
- Far mountains/islands shift blue-gray, lighten, and lose texture detail.
- Preserve silhouettes; do not fog the entire coast into white.
- Height fog should pool subtly in coves and between ridges.
- Ocean horizon remains clean and stable.
- Use fog colors tied to sky/sun rather than neutral gray.

Suggested starting ranges:

| Layer | Contrast retention | Saturation retention | Haze blend |
|---|---:|---:|---:|
| 0–150 m | 100% | 100% | 0–5% |
| 150–800 m | 85–95% | 88–96% | 5–15% |
| 0.8–5 km | 60–80% | 65–85% | 18–40% |
| 5 km+ | 35–60% | 45–70% | 40–70% |

### 8.4 Landmark persistence

- Red bridge: first proxy glimpse in SC01; dominant in SC04–SC05; visible behind in SC06.
- Lighthouse: first proxy glimpse in SC01; grows through SC02–SC05; dominant in SC06; remains behind in SC07–SC08.
- Village: visible below in SC01–SC03; dominant in SC04; recedes in SC05–SC06.
- Sea arches: hinted in SC05; visible ahead in SC06; dominant in SC07; recede in SC08.
- Islands/final overlook: distant throughout; become objective in SC08.

Landmarks must not teleport, rotate implausibly, or change silhouette between proxy and hero versions.

---

## 9. Lighting and sky

### 9.1 Time and direction

- Late-afternoon coastal sun, progressing gently toward golden hour.
- Choose one consistent sun direction across all segment scenes.
- Primary light is warm; skylight and shadows are cool blue.
- Avoid resetting the sun angle when additive scenes change.

### 9.2 Built-in pipeline setup

- One directional sun with soft shadows.
- High-quality cascaded shadows near the rider.
- Baked or mixed lighting for static village/landmark architecture when suitable.
- Light probes along the entire route for riders and dynamic props.
- Reflection probes at harbor, bridge, tunnel mouths, lighthouse, and sea arches.
- Use a consistent skybox and ambient mode across scenes.
- Tunnel uses local lights and exposure-friendly portal transitions.

### 9.3 Shadow priorities

- Highest: rider, bike, guardrails, near trees, bridge beams, tunnel mouth.
- Medium: houses, retaining walls, hero cliffs, lighthouse.
- Low/baked: distant vegetation, proxy villages, far mountains.
- Prevent cascade seams and shimmering across long views.
- Do not let foliage shadows turn the road into high-frequency visual noise.

### 9.4 Exposure and tone

- Lock or tightly constrain exposure during gameplay.
- Preserve cloud and foam detail.
- Retain separation inside black rider materials.
- Keep ocean sparkle controlled rather than covering the water in clipped white.
- Use a filmic/custom anime color curve with gentle highlight rolloff.

---

## 10. Water and coastline

- Ocean is a large stable surface with layered normal scales, directional swell, and distance-aware detail.
- Near shore uses foam lines, impact bursts, wet rock darkening, and cove color variation.
- Far water simplifies to broad value/color motion.
- Reflections must not shimmer excessively during chase-camera motion.
- Use planar reflection only for selected hero views if performance permits; otherwise use probes and screen-space approximations available in the current renderer.
- Fishing harbor water is calmer, darker, and more reflective than open sea.
- Bridge and cliff foundations must visibly meet water/rock.
- Shoreline foam should follow actual coastline contours rather than floating decals.
- Sea spray is localized and wind-directed.

---

## 11. Materials and shaders

### 11.1 Toon response

- Use two main diffuse value bands plus a softened transition.
- Use cooler colored shadows instead of pure black multiplication.
- Keep specular highlights graphic and material-specific.
- Reduce high-frequency normal maps that undermine the cel-shaded look.
- Use distance fading for small normal/detail textures.

### 11.2 Material families

Create shared master materials for:

- Asphalt dry / shaded / tunnel.
- Painted road markings.
- Warm retaining stone.
- Hero cliff rock.
- Distant cliff rock.
- Cream stucco.
- Dark timber.
- Slate roof tile.
- Coral painted steel/wood.
- Guardrail metal.
- Pine bark/needles.
- Broadleaf foliage.
- Hydrangea foliage/flowers.
- Ocean surface.
- Harbor water.
- Wet shoreline rock.
- Tunnel stone/concrete.

Use MaterialPropertyBlock or instancing-compatible parameters for variation. Do not create hundreds of nearly identical material assets.

### 11.3 Texture fidelity

- Hero structures: up to 2K–4K texture sets where screen coverage warrants it.
- Repeating terrain/road materials: tiled 2K–4K with macro variation.
- Midground buildings: 1K–2K atlases.
- Far proxies: 512–1K atlases or baked color.
- Preserve full-resolution reference PNGs as references; do not use them as environment textures.
- Enable mipmaps and sensible anisotropic filtering for roads viewed at grazing angles.

---

## 12. Vegetation and prop density

### 12.1 Density hierarchy

- Highest curated density within 30 m of hero camera corridors.
- Medium density on visible midground slopes.
- Clustered silhouette vegetation on far terrain.
- Leave deliberate open areas so roads and landmarks breathe.

### 12.2 Vegetation rules

- Use windswept coastal pines as recurring silhouettes.
- Use hydrangeas strongly in SC01–SC04, then sparingly as a visual continuity motif.
- Vary scale, rotation, hue, and clustering without breaking stylization.
- Avoid uniformly scattering trees across every slope.
- Protect camera and rider collision envelopes.
- Use impostors/billboards only where transition is visually stable.
- Wind motion must be subtle enough not to cause full-screen shimmer.

### 12.3 Story props

Use props to imply life and occupation: fishing equipment, harbor lamps, mooring posts, drainage, benches, shrine lanterns, maintenance barriers, road mirrors, small planters, and utility poles. Do not turn the route into a prop museum. Every cluster should support location, scale, navigation, or story.

---

## 13. Architecture and landmarks

- Village buildings use a modular kit with varied widths, heights, rooflines, balconies, and setbacks.
- Avoid copy-paste rows of identical houses.
- Keep interiors mostly unmodeled unless directly visible.
- Red bridge must be an original design with believable structural rhythm.
- Lighthouse must retain one consistent silhouette in every LOD/proxy.
- Shrine/torii remain secondary accents; Shiosai is a cycling coast, not a theme park of repeated gates.
- Utilities should follow roads and buildings logically.
- Building foundations must meet slopes/quays cleanly.
- No inaccessible door floating above terrain or stairs leading nowhere in hero areas.

---

## 14. Audio and environmental motion

Per-segment ambience:

- SC01: forest insects, wind through pines, distant surf.
- SC02: increasing wind, tire/guardrail reflections, distant harbor.
- SC03: rushing descent, leaves, tunnel reverb transition.
- SC04: boats, gulls, ropes, water against quay, subdued village life.
- SC05: crosswind, structural hum, open-water ambience.
- SC06: stronger exposed wind, surf impacts, lighthouse machinery at low level.
- SC07: crashing waves, cave resonance, gusts.
- SC08: open wind, rhythmic surf, expansive finale music layer.

Environmental motion includes water, foam, boats, seabirds, foliage, flags/ropes, and subtle cloud travel. Motion should reinforce direction and wind without overwhelming the cyclist.

---

## 15. Scene and asset organization

Recommended Unity structure:

```text
Assets/Environment/ShiosaiCoast/SC/
├── SHIOSAI_GRAND_COAST_ENVIRONMENT_SPEC.md
├── 01-mountain-gateway.png
├── 02-upper-switchbacks.png
├── 03-hydrangea-descent.png
├── 04-fishing-village.png
├── 05-red-bridge.png
├── 06-lighthouse-climb.png
├── 07-sea-arch-road.png
├── 08-coastal-highway.png
├── Art/
│   ├── Materials/
│   ├── Meshes/
│   ├── Textures/
│   ├── Vegetation/
│   └── VFX/
├── Audio/
├── Data/
├── Editor/
├── Prefabs/
│   ├── Architecture/
│   ├── Landmarks/
│   ├── Props/
│   ├── Road/
│   └── Vegetation/
├── Scenes/
├── Scripts/
└── Tests/
```

Naming:

```text
SC_<Segment>_<AssetType>_<Description>_<Variant>
```

Examples:

```text
SC_02_PF_RetainingWall_A
SC_05_PF_RedBridge_Hero
SC_06_PF_Lighthouse_LOD0
SC_07_MAT_WetCliff
SC_08_SCN_CoastalHighway
```

Keep editor-only generation tools inside `Editor/`. Do not place runtime scripts in editor assemblies.

---

## 16. LOD, streaming, and performance

### 16.1 Visual priority

Spend budget in this order:

1. Road and immediate collision envelope.
2. Rider-readable foreground lighting.
3. Hero landmarks.
4. Midground terrain/buildings.
5. Water and shoreline interaction.
6. Distant terrain and islands.
7. Small decorative props.

### 16.2 LOD targets

- Hero landmarks: LOD0/1/2 plus proxy/impostor.
- Architecture: LOD0/1/2; merge far village blocks.
- Trees: mesh LODs plus stable billboard/impostor.
- Rocks: 3 LODs; preserve silhouette.
- Guardrails/road furniture: distance culling after they stop affecting route read.
- Hydrangeas: reduce flower clusters and transparency layers aggressively with distance.
- Keep red bridge and lighthouse visible through persistent far proxies.

### 16.3 Streaming

- Preload next segment before the player reaches its transition.
- Never hitch during high-speed descents.
- Use object pooling for birds, spray, boats, and repeated ambience.
- Stagger expensive activation across frames.
- Keep persistent sky/ocean transitions visually stable while scenes stream.

### 16.4 Performance target

- Primary: stable 60 fps at 1080p on the project's minimum supported PC.
- No sustained spikes above 16.67 ms during normal riding.
- No scene-load hitch perceptible at cycling speed.
- Use profiler captures at SC02 descent, SC04 village, SC05 bridge, SC07 surf, and SC08 long vista.
- Test worst-case long view with multiple landmarks and water visible.

---

## 17. Reference-matching workflow

For each segment:

1. Lock the route spline and gameplay grade.
2. Match the reference camera approximately.
3. Block only road, terrain masses, ocean, and hero landmark.
4. Verify road continuity in editor and play mode.
5. Match foreground/midground/background silhouette layers.
6. Add architecture and vegetation clusters.
7. Match palette and sunlight.
8. Add secondary props and VFX.
9. Review at gameplay camera/FOV.
10. Profile, create LODs, and validate streaming.

Comparison views must include:

- Reference image.
- Unity capture from approximate reference camera.
- Edge/silhouette overlay.
- Grayscale value comparison.
- Gameplay-camera capture.
- Reverse-look capture proving landmarks remain spatially consistent.

Do not chase tiny decorative differences before topology, silhouette, lighting, and landmark placement are approved.

---

## 18. Automated validation

Create editor or play-mode validation for:

- Route spline is continuous and approximately 42 km.
- All eight segment boundaries connect.
- No road collider gap exceeds tolerance.
- Grade samples contain no impossible one-frame spikes.
- All required kilometer anchors exist and are ordered.
- Bridge deck connects both shores.
- Tunnel has entry, exit, interior, and road continuation.
- Lighthouse loop reconnects toward SC07.
- Final overlook supports turnaround.
- Streaming triggers cover the full route with overlap.
- Persistent landmark proxies are assigned.
- Missing materials/prefabs are reported.
- Reference images remain present and unmodified.

Create a route flythrough/debug mode that advances along the spline, displays kilometer/grade/segment, and can capture standardized screenshots.

---

## 19. Acceptance criteria

### Route

- [ ] A rider can traverse the entire route without teleportation.
- [ ] Total route length is 42 km ±2%.
- [ ] Each segment's grade and gameplay identity match this spec.
- [ ] No hero road is disconnected or ends without explanation.
- [ ] Optional branches reconnect or are clearly minor access roads.

### Visual fidelity

- [ ] All eight references are recognizable in their corresponding route chapters.
- [ ] Red bridge, lighthouse, village, tunnel, sea arches, and overlook retain consistent positions and silhouettes.
- [ ] Every hero view contains foreground, midground, and background depth.
- [ ] Ocean, vegetation, rock, architecture, and coral-red landmarks match the shared palette.
- [ ] Scene reads as polished 3D cel-shaded anime game art, not photorealism.

### Deep focus

- [ ] Upcoming road is sharp and readable.
- [ ] Midground landmarks remain legible.
- [ ] Far mountain/island silhouettes remain visible through controlled haze.
- [ ] Gameplay does not use strong shallow DoF blur.
- [ ] Long views show atmospheric layering without fogging away navigation targets.

### Technical

- [ ] Stable 60 fps/1080p target on minimum PC.
- [ ] No visible streaming hitch at expected maximum cycling speed.
- [ ] No precision jitter across the 42 km route.
- [ ] LOD transitions do not change landmark identity.
- [ ] Road, bike, camera, and trainer-grade systems remain synchronized.
- [ ] Tunnel, bridge, cliffs, and guardrails have correct collision.

---

## 20. Strict do-not-drift rules

1. Do not treat the eight renders as eight isolated scenes.
2. Do not create decorative roads that vanish, float, or fail to connect.
3. Do not shorten the route by visually repeating the same 500 m environment.
4. Do not use teleportation between route chapters.
5. Do not use extreme shallow depth of field during gameplay; the requested depth is deep scenic visibility.
6. Do not blur or fog away the bridge, lighthouse, road, or next destination.
7. Do not replace stylized cel-shaded art with photoreal assets.
8. Do not fill every slope with uniform vegetation scatter.
9. Do not bury the ride line under props, flowers, shadows, or VFX.
10. Do not change the red bridge or lighthouse silhouette between segments/LODs.
11. Do not turn the village into a dense modern city or tropical resort.
12. Do not overuse torii, shrines, or stereotyped motifs.
13. Do not create impossible cliff supports, bridge landings, tunnels, or road grades.
14. Do not let ocean direction flip arbitrarily between connected segments.
15. Do not add cars that block the cycling route.
16. Do not use the reference PNGs as skyboxes or billboard replacements for real geometry.
17. Do not modify, recompress, crop, upscale, or overwrite the reference PNGs.
18. Do not add render-pipeline dependencies silently. The current project does not list URP or HDRP.
19. Do not optimize away the foreground/midground/background layer structure.
20. Do not declare completion without a full 42 km automated flythrough and manual ride test.

---

## 21. Copilot implementation order

### Phase 1 — Audit and route skeleton

1. Inspect existing Shiosai systems, scene-loading conventions, road tools, camera, rider controller, and trainer-grade integration.
2. Preserve working project architecture and avoid duplicating systems.
3. Create the authoritative 42 km route spline and kilometer anchors.
4. Block all eight segment scenes with primitive terrain and continuous road/collider.
5. Add floating-origin or coordinate mitigation if required.
6. Prove the complete route can be ridden end to end.

### Phase 2 — Landmark and depth blockout

1. Block ocean, five depth bands, and all major terrain silhouettes.
2. Place proxy/full red bridge, village, tunnel, lighthouse, sea arches, islands, and overlook.
3. Verify cross-segment landmark persistence from standardized cameras.
4. Establish consistent sun, sky, haze, and color grading.

### Phase 3 — Segment production

Implement in route order. For each segment, complete road safety, terrain, hero landmark, materials, vegetation, architecture, props, VFX, audio, collisions, and LODs before final polish.

### Phase 4 — Deep-focus polish

1. Tune far clip, LODs, proxies, atmospheric perspective, and horizon.
2. Preserve sharp route/landmark visibility.
3. Add macro terrain color variation and foreground parallax frames.
4. Match reference captures without using shallow DoF as a shortcut.

### Phase 5 — Optimization and QA

1. Profile hero stress points.
2. Fix streaming hitches and memory spikes.
3. Run automated route validation.
4. Perform full 42 km ride in both directions where supported.
5. Capture before/after/reference comparison set.
6. Document remaining deviations and obtain approval.

---

## 22. Definition of done

Shiosai Grand Coast is done when it is a continuous approximately 42 km ride, each of the eight chapters has a distinct gameplay and visual identity, the recurring landmarks prove spatial continuity, the environment preserves the renders' extraordinary near-to-horizon depth without blurring the route, the player can travel through every depicted transition, and the scene holds the project's performance target under real gameplay conditions.

