# Shiosai Coast HDRP Rebuild Specification

**Project:** MapleRide  
**Engine:** Unity 6000.4.11f1  
**Platform:** Windows 10/11, dedicated-GPU PC  
**Target renderer:** High Definition Render Pipeline (HDRP)  
**Region:** Shiosai Coast  
**Primary course:** Shiosai Grand Coast, approximately 42 km  
**Document role:** Standalone implementation contract for Copilot and human reviewers  
**Status:** Replaces the provisional visual mock; preserves validated gameplay contracts  
**Reference directory:** `Assets/Environment/ShiosaiCoast/SC/`

---

## 0. Executive directive

Rebuild Shiosai Coast as a premium Windows-only HDRP environment using every PNG in this directory as a coordinated visual reference set. The eight images describe successive chapters of one continuous ride, not isolated dioramas. The finished coast must preserve their radiant stylized color, strong landmarks, readable road, rich near-to-horizon layering, and sense of scale while correcting any generative-image geometry that would not work in a real game.

The current Shiosai implementation is a deliberately provisional approximately 2.892 km C# ribbon mock. Its own source comments state that a real art pass should move authored geometry to the Blender/content pipeline and leave the editor script as a staging pass. This rebuild fulfills that intent.

The target is a seamless approximately 42 km ride:

```text
Mountain Gateway
→ Upper Switchbacks
→ Hydrangea Descent
→ Fishing Village
→ Red Bridge
→ Lighthouse Climb
→ Sea-Arch Road
→ Coastal Highway
→ Island Overlook / next-region connection
```

### Definition of success

- One unbroken, rideable route with no unexplained dead ends or teleports.
- Eight visually distinct chapters whose landscape and landmarks prove they occupy one coherent coastline.
- HDRP lighting, atmosphere, water, shadows, reflections, and material response used deliberately—not merely enabled.
- Deep scenic focus: foreground road, midground landmarks, and distant mountains/islands remain readable together.
- MapleRide's stylized 3D anime/cel-shaded identity remains intact; HDRP must not turn the game photorealistic.
- Stable 60 fps at the selected target resolution on the defined minimum Windows PC tier.
- Existing route-following, trainer-grade, rider grounding, NPC traffic, world-map, and fast-travel contracts remain functional or are migrated explicitly.

---

## 1. Source-of-truth hierarchy

When sources conflict, use this priority:

1. Working rider/trainer/route runtime behavior and safety constraints.
2. This HDRP rebuild specification.
3. The eight PNG references as a complete sequence.
4. `SHIOSAI_GRAND_COAST_ENVIRONMENT_SPEC.md` for compatible route detail.
5. Existing Shiosai route data and C# mock only as migration evidence—not final scale or appearance.

Do not copy visual artifacts from generated images when they violate physics, road continuity, scale, or safe cycling. Preserve the intended composition instead.

---

## 2. Visual reference atlas

| Ref | File | Chapter | Extract these qualities |
|---|---|---|---|
| SC01 | [01-mountain-gateway.png](01-mountain-gateway.png) | 0–6 km | Arrival gate, wooded ridge road, hydrangea foreground, first deep ocean/bridge/lighthouse reveal |
| SC02 | [02-upper-switchbacks.png](02-upper-switchbacks.png) | 6–11 km | Traceable stacked hairpins, retaining walls, destination visible far below, coastal scale |
| SC03 | [03-hydrangea-descent.png](03-hydrangea-descent.png) | 11–16 km | Flower-framed speed, shrine overlook, tunnel threshold, harbor reveal |
| SC04 | [04-fishing-village.png](04-fishing-village.png) | 16–20 km | Lived-in harbor, cream/timber architecture, calm water, clear route to bridge |
| SC05 | [05-red-bridge.png](05-red-bridge.png) | 20–23 km | Iconic coral-red crossing, strong frame geometry, village behind, lighthouse ahead |
| SC06 | [06-lighthouse-climb.png](06-lighthouse-climb.png) | 23–28 km | Promontory climbing loop, tall white lighthouse, backward/forward route visibility |
| SC07 | [07-sea-arch-road.png](07-sea-arch-road.png) | 28–34 km | Exposed rolling road, real rock arch passage, spray, rugged coastal drama |
| SC08 | [08-coastal-highway.png](08-coastal-highway.png) | 34–42 km | Fast open finale, island horizon, golden light, overlook and onward connection |

### Required shared language across all references

- True 3D anime-stylized world.
- Bright turquoise near water graduating to cobalt distance water.
- Warm cream village walls and slate-blue roofs.
- Coral/vermilion landmark accents.
- Lush fresh greens with windswept pine silhouettes.
- Warm late-afternoon sunlight and cool blue shadows.
- Clean, dark, readable asphalt with clear pale edge lines.
- Strong foreground framing, layered midground, and distant mountain/island silhouettes.
- One compact black-clad chibi rider establishes scale but never dominates the environmental composition.

---

## 3. Project audit and migration facts

At the time of this spec:

- Unity version is `6000.4.11f1`.
- `Packages/manifest.json` contains neither URP nor HDRP.
- `ProjectSettings/GraphicsSettings.asset` has no custom render pipeline assigned.
- The existing Shiosai route is about `2892.006 m` one way and is reused in reverse as a closed course.
- The existing course is named `Shiosai Breeze` and targets about 30 minutes.
- Current generation flows through `tools/blender/shiosai_route.py` → `ShiosaiRoute.json` → `RouteGraphBaker` → runtime `RouteGraph`.
- `ShiosaiCoastEnvironment.cs` builds ribbon land, road, ocean, guardrails, landmarks, scatter, and distant depth procedurally into `Assets/Scenes/SakuraPass.unity`.
- The C# comments explicitly identify this as a mock and prescribe moving the real art pass to Blender/authored assets.
- Shiosai directly uses Built-in shaders `MapleRide/ShiosaiArchitecture`, `MapleRide/ShiosaiShallows`, `MapleRide/SakuraCel`, `MapleRide/SakuraWater`, and `MapleRide/SakuraTerrain`.
- Project scripts also request Built-in shader names such as `Standard`, `Particles/Standard Unlit`, legacy particles, and `Unlit/Texture`.
- Existing road alignment contracts include a 7 m carriageway, 0.55 m shoulder, 0.06 m crown, lane offset around 1.72 m, and carefully solved rider/road vertical alignment.

The rebuild must not erase these facts. It must replace the mock deliberately, update dependents, and retain validated alignment behavior.

---

## 4. HDRP migration strategy

### 4.1 Safety rule

Do not convert the only working project state in place without a recoverable checkpoint. Create a version-control branch or full project backup before installing HDRP. Do not delete Built-in shaders until HDRP replacements have passed visual and runtime tests.

### 4.2 Package and pipeline setup

1. Install the Unity-version-compatible **High Definition RP** package from Package Manager.
2. Open the HDRP Wizard and resolve required configuration through its supported fixes.
3. Create pipeline/global settings under `Assets/Settings/HDRP/`.
4. Create separate HDRP assets for Low, Medium, High, and Ultra Windows presets.
5. Assign the intended default asset in Graphics Settings and each override in Quality Settings.
6. Set Windows graphics API order deliberately, with DirectX 12 as the primary target after compatibility testing.
7. Enable linear color space, HDR rendering, motion vectors, depth buffer features, and required normal/opaque buffers.
8. Configure frame settings for camera, reflection, and custom-pass needs.
9. Add packages such as Cinemachine only if justified and recorded. Do not add unrelated dependencies.

### 4.3 Conversion policy

- Convert compatible standard materials in a controlled scope first.
- Do not assume the automatic converter can translate custom surface shaders.
- Expect custom Built-in shaders to turn pink until replaced.
- Rebuild custom Shiosai and shared Sakura shaders in HDRP Shader Graph or HDRP-compatible hand-authored HLSL.
- Re-author lighting because HDRP light units and response do not match Built-in lighting one-for-one.
- Replace old post-processing with HDRP Volumes.
- Replace legacy particle materials with HDRP-compatible particle/VFX materials.
- Audit every `Shader.Find(...)` call and supply explicit HDRP mappings or serialized material references.

### 4.4 Mandatory shader migration matrix

| Existing dependency | HDRP replacement | Acceptance requirement |
|---|---|---|
| `MapleRide/ShiosaiArchitecture` | `MapleRide/HDRP/ShiosaiArchitecture` | Cream stucco, timber, slate roof, cel-controlled shadow response |
| `MapleRide/ShiosaiShallows` | HDRP shallow-water layer integrated with main ocean | Turquoise depth gradient, foam, shoreline blend, no z-fighting |
| `MapleRide/SakuraCel` | shared `MapleRide/HDRP/CelLit` | Stable two/three-band light, shadow, specular and rim controls |
| `MapleRide/SakuraWater` | HDRP Water System or dedicated HDRP ocean shader | Stable horizon, waves, foam, underwater/shore behavior as required |
| `MapleRide/SakuraTerrain` | HDRP terrain master | Macro variation, slope blending, stylized banding, distance stability |
| `Standard` | HDRP/Lit or project wrapper | No runtime missing shader |
| `Particles/Standard Unlit` | HDRP particle/VFX material | Correct transparency, sorting, fog integration |
| Legacy particle shaders | HDRP fallback mappings | No pink effects or build stripping failure |
| `Unlit/Texture` | HDRP/Unlit or project wrapper | Correct face/UI/character behavior where used |

### 4.5 Rollout gate

Before converting all MapleRide regions, build a representative HDRP vertical slice containing:

- Kuro and bicycle.
- 500–1000 m of Shiosai road.
- One cliff, hydrangea cluster, pine cluster, house, guardrail, ocean surface, foam edge, fog layer, and hero landmark proxy.
- A tunnel entrance and local exposure transition.
- Daylight, shadow, reflection, and post-processing stack.

Proceed only after the slice is visually approved, free of missing shaders, and meets the selected frame-rate target.

---

## 5. Route redesign: 2.892 km mock to 42 km production course

### 5.1 Authoritative topology

The current out-and-back mock must not be stretched uniformly. Author a new centerline with eight distinct route chapters and meaningful elevation rhythm.

| Chapter | Chainage | Length | Primary effort |
|---|---:|---:|---|
| Mountain Gateway | 0–6 km | 6 km | rolling warm-up into sustained climb |
| Upper Switchbacks | 6–11 km | 5 km | long technical descent |
| Hydrangea Descent | 11–16 km | 5 km | fast descending bends and tunnel |
| Fishing Village | 16–20 km | 4 km | flat recovery and encounter space |
| Red Bridge | 20–23 km | 3 km | open crossing and midpoint event |
| Lighthouse Climb | 23–28 km | 5 km | short steep climb, summit, reconnection |
| Sea-Arch Road | 28–34 km | 6 km | exposed rolling effort |
| Coastal Highway | 34–42 km | 8 km | fast sustained finale |

### 5.2 Elevation targets

- Start elevation: approximately 40–80 m above sea level.
- Mountain Gateway summit: approximately 350–500 m.
- Village road: approximately 3–15 m.
- Bridge deck: approximately 25–55 m depending on final structure.
- Lighthouse summit: approximately 120–220 m.
- Sea-Arch Road: oscillates approximately 20–100 m.
- Final overlook: approximately 50–120 m.
- No instantaneous grade discontinuities.
- Recommended sustained grades: within ±8%; short lighthouse ramps may reach 10%.
- Trainer grade is sampled/smoothed from route data and is independent of decorative road banking.

### 5.3 Road engineering

- Two-way standard carriageway: preserve approximately 7 m width where possible.
- Shoulder: 0.55–1.0 m according to risk and environment.
- Village constriction: no narrower than approximately 4.5–5.0 m unless gameplay uses controlled single-lane logic.
- Maintain the existing road crown and solved rider-contact principle; recalculate offsets from the final authored cross-section rather than copying arbitrary vertical constants.
- Minimum normal high-speed curve radius: 24 m.
- Absolute minimum deliberate hairpin radius: 18 m with reduced design speed.
- All cliff edges near the player receive guardrails, parapets, or terrain buffers.
- Bridge, tunnel, sea arch, and retaining structures must have continuous road render mesh and collider.
- No decorative road segment may appear to be a major path unless it connects or is clearly a minor access road.

### 5.4 Route data contract

Keep the established pipeline shape:

```text
authoring source
→ exported ShiosaiRoute.json
→ RouteGraphBaker
→ RouteGraph
→ RouteFollower / trainer grade / traffic / checkpoints / world map
```

Update the Blender route-authoring tool rather than manually editing the generated JSON. Extend the schema only when necessary and keep backwards compatibility where practical.

Required data per sample or derived lookup:

- Position, tangent, side, up, banking, cumulative distance.
- Smoothed grade for trainer control.
- Segment/chapter ID.
- Lane widths and preferred rider lane offset.
- Surface and tunnel flags.
- Safety/respawn classification.
- Streaming cell and landmark-visibility hints.
- Optional camera and ambience metadata.

### 5.5 Required anchors

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

---

## 6. World layout and scene architecture

### 6.1 Do not keep the production coast embedded as one generated root in SakuraPass

Create a persistent Shiosai scene and additive segment scenes. Maintain compatibility with `RegionDirector` and world-map travel, but stop rebuilding production art destructively every time the mock builder runs.

Recommended scenes:

```text
SC_Persistent.unity
SC_01_MountainGateway.unity
SC_02_UpperSwitchbacks.unity
SC_03_HydrangeaDescent.unity
SC_04_FishingVillage.unity
SC_05_RedBridge.unity
SC_06_LighthouseClimb.unity
SC_07_SeaArchRoad.unity
SC_08_CoastalHighway.unity
SC_Backdrop_OceanIslands.unity
SC_Lighting_HDRP.unity
```

`SC_Persistent` owns route runtime, stream manager, origin shifting, spawn anchors, audio state, shared ocean coordination, and persistent landmark proxies. Segment scenes own production terrain, road meshes, colliders, local structures, vegetation, probes, and chapter triggers.

### 6.2 Streaming envelope

- Keep current segment plus next and previous transition cells loaded.
- Preload earlier on fast descents and before tunnel/bridge exits.
- Persistent far proxies represent red bridge, lighthouse, village mass, sea arches, and islands.
- Full landmark assets replace proxies before their silhouette difference is noticeable.
- Use asynchronous loading and activation staggering.
- Never hitch when the player exceeds expected downhill speed.

### 6.3 Coordinate precision

A 42 km course requires explicit precision control. Prefer camera-relative rendering plus floating-origin/world-shift logic, or compact route layout with segment-local coordinates. Test rider physics, wheel contact, camera smoothing, shadow stability, water, VFX, and AI after large coordinate shifts.

### 6.4 Depth bands

Every major view uses five controlled bands:

1. **0–30 m:** road, markings, guardrail, drainage, flowers, nearby rock and foliage.
2. **30–150 m:** hero cliff faces, buildings, large trees, bridge members, tunnel portals.
3. **150–800 m:** village blocks, bays, terrain terraces, full/proxy landmarks.
4. **0.8–5 km:** simplified ridges, coast geometry, large island and town masses.
5. **5–30 km apparent:** horizon silhouettes, cloud layers, distant islands and mountains.

At least three bands must be readable in routine chase-camera views and all five in hero vistas.

---

## 7. Segment production briefs

## 7.1 SC01 Mountain Gateway — 0–6 km

**Reference:** `01-mountain-gateway.png`

- Begin in forest shade with rolling grades, then climb toward a framed ocean reveal.
- Build a unique timber-and-stone gateway that reads from both directions.
- Use warm retaining stone, pine trunks, layered canopy, and hydrangea banks in the foreground.
- Reveal the village, red bridge, lighthouse, ocean, and islands at believable relative positions.
- Road continues around the ridge beyond the hero composition.
- Maintain two overlook pull-offs outside the active lane.
- Use volumetric light sparingly through trees; never wash out the asphalt.
- Landmark proxies must match later full-model silhouettes.

## 7.2 SC02 Upper Switchbacks — 6–11 km

**Reference:** `02-upper-switchbacks.png`

- Build one traceable chain of at least four major hairpins.
- Every visible tier connects through actual geometry.
- Use authored retaining walls with drainage, rock cuts, guardrails, mirrors, and restrained chevrons.
- Alternate exposure and vegetation so the player repeatedly loses and regains the sea view.
- Keep village/bridge/lighthouse far below as navigation proof.
- Place HLOD vegetation between road tiers without hiding all connectivity.
- Design corners around bicycle speed and chase-camera sightlines.

## 7.3 SC03 Hydrangea Descent — 11–16 km

**Reference:** `03-hydrangea-descent.png`

- Create a fast continuous descent framed by blue and pink hydrangeas.
- Place one small shrine overlook and one restrained coral-red torii beside the route.
- Lead the eye toward a complete stone-lined tunnel.
- Tunnel includes approach lighting, portal wetness, emissive fixtures, interior probes, reverb, exposure transition, exit bloom control, and continuous collider.
- Harbor light and the next road remain visible through or immediately after the exit.
- Flowers are clustered and art-directed, not uniformly scattered.

## 7.4 SC04 Fishing Village — 16–20 km

**Reference:** `04-fishing-village.png`

- Build a modular cream-stucco/timber village with slate-blue tile roofs and varied silhouettes.
- Keep the main road flat and continuously rideable beside the quay.
- Place nets, crates, ropes, floats, boats, and market props outside the rider envelope.
- Use calm harbor water distinct from the open ocean.
- Utility wires and poles reinforce perspective but remain controlled.
- Grow the red bridge progressively larger throughout the chapter.
- Create optional recovery/encounter space without forcing Kuro to walk.

## 7.5 SC05 Red Bridge — 20–23 km

**Reference:** `05-red-bridge.png`

- Create an original coral-red steel arch bridge with structurally coherent members.
- Road approaches, crosses the inlet once, and lands on the lighthouse-promontory road.
- Preserve clear sightlines backward to village/switchbacks and forward to lighthouse.
- Use boats and water movement below for speed parallax.
- Add crosswind audio/VFX but do not create arbitrary steering forces.
- Place midpoint checkpoint/event trigger without blocking the road.
- Configure bridge reflection, contact shadows, and LODs so it remains iconic from kilometers away.

## 7.6 SC06 Lighthouse Climb — 23–28 km

**Reference:** `06-lighthouse-climb.png`

- Route rises from bridge exit and circles the promontory in at least two connected bends.
- Lighthouse is tall, white, consistent in every LOD, and visible throughout the region.
- Include a small keeper building and overlook outside the ride line.
- Summit offers backward view to bridge/village and forward view to sea arches.
- Far-side road descends and reconnects toward SC07; no cul-de-sac.
- Use exposed rock, windswept pine, localized spray, and stronger wind.
- Short ramps may reach 10% but transitions remain trainer-safe.

## 7.7 SC07 Sea-Arch Road — 28–34 km

**Reference:** `07-sea-arch-road.png`

- Carve the primary rideable passage through a broad natural headland arch.
- Secondary offshore arches remain scenic and cannot be mistaken for the route.
- Support road geometry with believable rock or engineered structures.
- Combine short climbs/dips, clear barriers, wet shoreline rock, foam, and impact spray.
- Asphalt stays dry and readable.
- Integrate volumetric mist close to wave impacts without obscuring the road.
- Show the final highway beyond the arch.

## 7.8 SC08 Coastal Highway — 34–42 km

**Reference:** `08-coastal-highway.png`

- Open into broad sweepers and sustained high-speed sightlines.
- Keep the ocean consistently on the established side of the outbound route.
- Retain the lighthouse behind at selected views.
- Make the island chain the final horizon objective.
- Build a large rideable overlook/turnaround with an original sail-like marker.
- Show a credible tunnel/road connection toward the next region.
- Transition late afternoon toward golden hour through one continuous lighting state, not scene-by-scene discontinuity.
- Reserve final 2 km for a clear sustained acceleration/sprint presentation.

---

## 8. HDRP lighting system

### 8.1 Lighting target

Radiant late-afternoon coast: warm directional sun, cool skylight/shadows, bright but controlled water sparkle, soft bounced color, and high visibility across the complete depth stack.

### 8.2 Physically based sky

- Use HDRP Physically Based Sky or a validated HDRI/sky combination.
- Fix one sun azimuth that remains spatially consistent across the coastline.
- Time may advance slightly across the 42 km course, but avoid rapid color/angle shifts.
- Use cloud layers or volumetric clouds according to quality tier.
- Preserve cloud detail near the sun and avoid clipped white sky.
- Horizon color must merge naturally with distance fog and ocean.

### 8.3 Exposure

- Use fixed or tightly constrained exposure for normal riding.
- Use localized tunnel adaptation with controlled speed and min/max values.
- Never let automatic exposure turn black kit gray or blow out foam/lighthouse.
- Validate forest-to-ocean, tunnel-entry, tunnel-exit, and sunset transitions.

### 8.4 Global illumination

- Prefer baked/mixed GI for static village and landmark architecture where it improves stability.
- Use light probes or Adaptive Probe Volumes for dynamic riders and moving props.
- Use Screen Space Global Illumination only if validated against performance and temporal artifacts.
- Do not depend on ray-traced GI for the baseline High preset.
- Ultra may add selected ray-traced features only with clean fallbacks.

### 8.5 Shadows

- High-resolution directional shadows near rider.
- Four cascades for High/Ultra; tune transition and distance.
- Contact shadows for rider, guardrails, bridge members, building edges, and nearby foliage.
- Micro Shadowing for vegetation/material grounding where stable.
- Bake or simplify distant shadows.
- Avoid leaf-shadow flicker and cascade crawling on long coastal views.

---

## 9. HDRP atmosphere and deep focus

The PNGs achieve “extreme depth of field” primarily through **deep focus and atmospheric layering**, not strong optical background blur.

### 9.1 Gameplay camera policy

- Depth of Field is off by default during riding.
- If enabled, use a very deep focus range that keeps road, rider, and primary landmark sharp.
- Shallow DoF is restricted to photo mode, menus, and controlled cinematics.
- Never blur the upcoming road, route edge, rival, bridge, lighthouse, tunnel exit, or next destination.

### 9.2 Fog and aerial perspective

- Use HDRP Volumetric Fog for low-frequency atmosphere.
- Use height fog subtly in coves, forest valleys, and between distant ridges.
- Near objects retain contrast and saturation.
- Midground shifts slightly cooler and lower contrast.
- Far mountains/islands become lighter blue-gray but retain clean silhouettes.
- Avoid uniform fog that flattens all depth bands.
- Use local fog volumes only for intentional tunnel, cove, sea-spray, and valley effects.

Suggested visual retention:

| Distance band | Contrast | Saturation | Haze contribution |
|---|---:|---:|---:|
| 0–150 m | 95–100% | 95–100% | 0–5% |
| 150–800 m | 82–95% | 85–95% | 5–18% |
| 0.8–5 km | 58–80% | 62–82% | 18–42% |
| 5 km+ | 35–60% | 42–68% | 40–70% |

### 9.3 Camera baseline

- Behind and slightly above cyclist.
- Vertical FOV approximately 42°, tunable 38–48°.
- Near clip 0.1–0.2 m.
- Far clip 15–30 km only when supported by HLOD/proxies; full geometry must not remain at full cost.
- Use camera-relative rendering and test origin shifting.
- Frame road as primary leading line and a reachable landmark as secondary target.

---

## 10. HDRP water system

### 10.1 Open ocean

- Evaluate HDRP Water System as the primary solution for open ocean.
- Establish large swell direction consistent with wind and coastline.
- Layer medium/small surface detail without noisy temporal shimmer.
- Tune absorption/scattering from turquoise shallows to deep cobalt.
- Maintain stable horizon at long view distances.
- Use physically plausible but art-directed sun reflection.

### 10.2 Shallows and shoreline

- Rebuild `ShiosaiShallows` behavior using water masks, decals, custom-pass support, or a dedicated HDRP shoreline material.
- Depth-based turquoise transition follows actual bathymetry.
- Foam follows shore contours and wave impacts.
- Wet rock darkening and specular response are localized.
- No floating foam cards, z-fighting shelves, or hard water-color seams.

### 10.3 Harbor

- Harbor surface is calmer, darker, and more reflective.
- Add gentle boat wake and quay interaction.
- Use planar reflection selectively only if the performance budget allows.
- SSR/reflection probes provide fallbacks.

### 10.4 Water quality tiers

- Low: simplified ocean mesh/shader, reduced foam and reflection.
- Medium: HDRP water with limited simulation and SSR.
- High: full art-directed water, foam, selected local reflections.
- Ultra: increased water resolution, higher reflection quality, optional advanced effects.

---

## 11. Stylized HDRP material system

### 11.1 Cel-lit master

Create `MapleRide/HDRP/CelLit` as a reusable project shader. Required controls:

- Base color/texture.
- Normal map and strength.
- Mask map or remapped metallic/ambient occlusion/smoothness.
- Two/three-band diffuse ramp.
- Shadow tint and threshold/softness.
- Stylized specular band size/intensity/tint.
- Fresnel/rim intensity, threshold, and tint.
- Emission.
- Wind support for vegetation variants.
- Alpha clipping and double-sided mode only where necessary.
- Fog, shadow, lightmap/probe, motion-vector, and instancing compatibility.

Use HDRP light data but quantize art response intentionally. Do not discard HDRP's environment integration by making every surface fully unlit.

### 11.2 Master material families

Create shared masters/instances for:

- Asphalt and painted markings.
- Warm retaining stone.
- Hero/distant cliff rock.
- Cream stucco and dark timber.
- Slate roof tile.
- Coral bridge/torii metal and wood.
- Guardrail/structural metal.
- Pine, broadleaf, grass, hydrangea.
- Wet shoreline rock.
- Tunnel stone/concrete.
- Window and lamp glass.
- Boat hull/trim.
- Terrain layers.
- Water/foam/spray.

### 11.3 Color anchors

| Material role | Base target |
|---|---|
| Near water | turquoise `#20AFC1` |
| Far water | cobalt `#2377B9` |
| Forest | fresh green `#3F7C3D` |
| Forest shadow | cool green `#244B3C` |
| Rock | warm gray `#8B8173` |
| Rock shadow | blue-gray `#4C5360` |
| Asphalt | charcoal `#353940` |
| Stucco | warm cream `#E9DFC8` |
| Roof | slate blue `#344D62` |
| Landmark | coral red `#D95748` |
| Hydrangea blue | `#668ED6` |
| Hydrangea pink | `#D47DA9` |

Use these as relationship anchors under the target HDRP lighting, not as immutable displayed pixels.

### 11.4 Texture policy

- Reference PNGs remain reference-only and are never used as billboards, skyboxes, or surface textures.
- Hero structures may use 2K–4K sets based on screen coverage.
- Road/terrain use tiled detail plus macro color variation.
- Midground architecture uses 1K–2K atlases.
- Far proxies use baked 512–1K atlases or simplified vertex color.
- Normal detail fades with distance.
- Enable mipmaps and anisotropic filtering appropriate to grazing road views.
- Avoid high-frequency realism that undermines the stylized image.

---

## 12. Terrain, cliffs, and shoreline construction

- Use Unity Terrain for broad land masses where it improves iteration.
- Use authored Blender meshes for hero cliffs, road cuts, sea arches, tunnel portals, promontory, breakwaters, bridge foundations, and shoreline silhouettes.
- Blend terrain/mesh boundaries with rock skirts, vegetation, walls, and foam.
- Use triplanar or terrain-layer projection on steep faces.
- Add large-scale strata and shape breaks before small texture detail.
- Build believable retaining structures beneath stacked switchbacks.
- Ensure every bridge/tunnel/road support reaches ground or rock.
- Create bathymetry near shore so water color and foam correspond to geometry.
- Distant cliffs use HLOD meshes with preserved silhouette and baked macro color.

---

## 13. Vegetation

### 13.1 Art direction

- Windswept pines define the coastal silhouette.
- Hydrangeas dominate SC01–SC04 and recur sparingly later for continuity.
- Broadleaf canopy creates cool shade in mountain sections.
- Low grasses/shrubs dominate exposed SC06–SC08.
- Keep deliberate open slopes and rock faces; do not cover everything uniformly.

### 13.2 Technical requirements

- HDRP-compatible foliage shader with alpha clipping, two-sided normals, transmission/subsurface approximation as appropriate, wind, instancing, shadow controls, and stable motion vectors.
- Mesh LODs followed by stable impostor/billboard where visually acceptable.
- Reduce alpha overdraw in dense hydrangea and tree masses.
- Use GPU instancing or batch-compatible placement.
- Disable or simplify distant real-time shadows.
- Wind amplitude decreases with distance to avoid horizon shimmer.
- Keep foliage outside rider/camera safety envelope.

---

## 14. Architecture and hero assets

### 14.1 Village kit

- Modular cream stucco walls, dark timber, slate-blue tiled roofs, windows, balconies, awnings, foundations, quay edges, steps, and utility attachments.
- Vary footprint, height, roofline, and setback.
- Avoid rows of identical houses.
- Props imply fishing activity without blocking the road.
- Use HLOD village clusters for mountain and bridge views.

### 14.2 Red bridge

- Original design; do not reproduce a real protected structure exactly.
- Structurally coherent arch, cross-bracing, deck, rails, foundations, drainage, expansion joints, and lamps.
- Hero LOD supports close crossing.
- Proxy LOD preserves exact overall arch silhouette.
- HDRP material balances coral paint, metal response, salt-weather variation, and cel shading.

### 14.3 Lighthouse

- One canonical silhouette across every scene and LOD.
- White tower, dark/coral roof accent, lantern gallery, keeper building, retaining base.
- Controlled emissive beacon for appropriate conditions; do not overexpose daylight view.
- Visible from SC01 through SC08 according to the landmark plan.

### 14.4 Sea arches

- Primary road arch is physically connected to the headland.
- Secondary arches sit offshore or below and cannot confuse navigation.
- Silhouette is readable at distance and dramatic from the road.
- Water, foam, mist, wetness, and contact shadows ground them.

---

## 15. Volumes and post-processing

Use one global baseline Volume plus local override Volumes.

### Global baseline

- Tonemapping: ACES or validated custom curve.
- White balance: slightly warm daylight.
- Color adjustments: restrained saturation/contrast.
- Bloom: low, thresholded, supporting sun path, foam, lamps, and highlights.
- TAA: baseline anti-aliasing; validate foliage and cyclist motion.
- Ambient occlusion: subtle, prevent dirty anime faces/materials.
- Fog/sky/exposure configured coherently.
- Motion blur: off or very low for gameplay; avoid smearing rider/road.
- Chromatic aberration: off.
- Film grain: off.
- Lens distortion: off unless a specific cinematic requires it.
- Vignette: nearly zero in normal riding.
- DoF: off in normal riding.

### Local volumes

- Forest shade: minor exposure/color adjustment only.
- Tunnel entry/interior/exit: smooth exposure, fog, reverb coordination.
- Bridge: wind/sky emphasis without exposure jump.
- Lighthouse summit: subtle clarity and atmosphere adjustment.
- Sea arches: localized mist/fog and wetness environment.
- Sunset finale: gradual warmth and sky transition.

Volume blends must be long enough to remain invisible at cycling speed.

---

## 16. Reflections and ray tracing

- Baseline High preset uses SSR, reflection probes, planar reflections only at selected hero areas, and baked/static strategies.
- Do not require ray tracing for correct visuals.
- Ultra may add ray-traced reflections or selected shadows after profiling.
- Every ray-traced effect requires a non-ray-traced fallback.
- Probe boundaries must not visibly pop while riding.
- Use local probes in village, bridge, tunnel mouths, lighthouse, and wet sea-arch areas.
- Avoid reflecting distant full-detail geometry unnecessarily.

---

## 17. HLOD, LOD, occlusion, and streaming

### 17.1 Landmark persistence plan

| Landmark | First visible | Hero chapter | Visible behind |
|---|---|---|---|
| Village | SC01 | SC04 | SC05–SC06 |
| Red bridge | SC01 | SC05 | SC06 |
| Lighthouse | SC01 | SC06 | SC07–SC08 |
| Sea arches | SC05 | SC07 | SC08 |
| Islands/overlook | SC01 distant | SC08 | n/a |

### 17.2 LOD rules

- Preserve landmark silhouette before surface detail.
- Use at least LOD0/1/2 plus HLOD/proxy for bridge, lighthouse, large cliffs, village clusters, and sea arches.
- Cross-fade only where HDRP/TAA does not create objectionable dithering.
- Avoid LOD transitions at vista reveal moments.
- Use distance and projected screen size with hysteresis.
- Far village proxies bake roof/wall color blocks and shadow mass.
- Replace individual distant trees with clustered HLOD silhouettes.

### 17.3 Occlusion

- Use terrain ridges, tunnels, village blocks, and headlands as natural occluders.
- Do not rely on occlusion culling for open-ocean views where little is occluded.
- Bake/test occlusion per segment if beneficial.
- Never occlude persistent landmark proxies that should remain visible.

---

## 18. Windows quality tiers

Targets are provisional and must be benchmarked on actual hardware.

| Preset | Intended class | Output target | Major features |
|---|---|---|---|
| Low | GTX 1660 / RTX 2060 class | 1080p60 | simplified water/fog/clouds, shorter shadows, aggressive HLOD |
| Medium | RTX 3060 class | 1080p–1440p60 | HDRP water, TAA, moderate volumetrics/SSR |
| High | RTX 3070 / RTX 4060 Ti class | 1440p60 | high water/shadows/fog, richer reflections, full landmark range |
| Ultra | RTX 4070+ class | 1440p–4K60 with upscaling | enhanced volumetrics/reflections/clouds, optional ray tracing |

### Required scalability controls

- Render scale and dynamic resolution.
- Upscaler selection where supported.
- Shadow resolution, cascade distance, and contact shadows.
- Volumetric resolution and distance.
- Cloud quality.
- Water simulation/reflection quality.
- SSR/ray-tracing state.
- Vegetation density/distance.
- Terrain detail distance.
- HLOD transition distances.
- Particle/spray density.

No preset may remove the road, barriers, required route landmarks, or navigation readability.

---

## 19. Performance budgets and profiling

- Primary frame target: 16.67 ms total at 60 fps.
- Profile in standalone Windows player, not only Editor.
- Use GPU and CPU frame timing separately.
- Capture representative worst cases:
  - SC02 view containing multiple switchbacks, village, bridge, water, and lighthouse.
  - SC04 dense village/harbor.
  - SC05 bridge with water/reflections.
  - SC07 waves, mist, cliffs, foliage, and road arch.
  - SC08 maximum draw-distance vista.
- No segment activation hitch visible during normal or maximum downhill speed.
- Pool repeated particles, birds, boats, riders, and ambience emitters.
- Stagger expensive activation and probe updates.
- Never use per-frame allocations in streaming/route/quality systems.
- Build shader-variant collection/stripping deliberately to prevent missing build shaders and excessive build times.

---

## 20. Gameplay integration requirements

- Preserve `RegionCatalog.ShiosaiCoast` behavior.
- Preserve or deliberately migrate world-map fast travel.
- Retain a valid Shiosai start state and checkpoints.
- Extend course data rather than silently changing every consumer of the old 2.892 km route.
- Ensure `RouteFollower` and NPC traffic use the same final route/lane convention.
- Recalculate rider lift/contact from the final road cross-section and verify wheels visually touch asphalt.
- Trainer grade uses filtered route slope; decorative bank/crown must not modify it.
- NPC riders stream and LOD with the environment.
- Hub/ride rules remain unchanged: Kuro remains on the bicycle.
- Existing Shiosai diagnostics and performance capture tools must be updated, not abandoned.

---

## 21. Folder architecture

```text
Assets/Environment/ShiosaiCoast/
├── SC/                              # immutable visual references + specs
├── Art/
│   ├── Materials/
│   ├── Meshes/
│   ├── Textures/
│   ├── Vegetation/
│   └── VFX/
├── BlenderAssets/
├── Data/
├── HDRP/
│   ├── CustomPasses/
│   ├── ShaderGraphs/
│   ├── Shaders/
│   └── Volumes/
├── Prefabs/
│   ├── Architecture/
│   ├── Landmarks/
│   ├── Props/
│   ├── Road/
│   └── Vegetation/
├── Scenes/
├── Scripts/
├── Tests/
└── ShiosaiRoute.json
```

Editor tools remain under the project's established `Assets/Editor/` convention unless assembly definitions justify a local Editor folder. Never place editor-only APIs in runtime assemblies.

Naming:

```text
SC_<Chapter>_<Type>_<Description>_<Variant>
```

Examples:

```text
SC_02_PF_RetainingWall_A
SC_05_PF_RedBridge_LOD0
SC_06_PF_Lighthouse_Proxy
SC_07_MAT_WetCliff
SC_08_VOL_SunsetFinale
```

---

## 22. Reference-match process

For each chapter:

1. Match an approximate reference camera and FOV.
2. Block route, ocean, terrain silhouette, and hero landmark only.
3. Prove road continuity and correct grade in play mode.
4. Match foreground/midground/background composition.
5. Establish HDRP sun, sky, fog, exposure, and water.
6. Build primary materials and architecture/vegetation masses.
7. Add secondary props, foam, spray, birds, boats, and audio.
8. Build LOD/HLOD/proxies and verify landmark persistence.
9. Compare reference, Unity capture, edge overlay, and grayscale values.
10. Review at real gameplay camera and target resolution.
11. Profile standalone build.
12. Record intentional deviations.

Approval images per chapter:

- Matched concept camera.
- Standard chase camera.
- Reverse-look continuity view.
- Low/Medium/High comparison.
- Fog disabled/enabled diagnostic.
- LOD/HLOD distance diagnostic.
- Collision/route overlay.

---

## 23. Automated validation

Add or update tests/tools to validate:

- HDRP package and pipeline assets are assigned.
- No Built-in-only Shiosai shader is referenced in HDRP production scenes.
- No pink/missing shader renderer exists in loaded Shiosai scenes.
- Route length is 42 km ±2%.
- Route distances are monotonic and segment boundaries ordered.
- Road collider gaps stay below tolerance.
- Smoothed grade contains no impossible spikes.
- All required anchors and checkpoints exist.
- Bridge deck connects both shores.
- Tunnel has connected entry/interior/exit.
- Lighthouse route reconnects toward SC07.
- Sea-arch route is structurally continuous.
- Final overlook supports turnaround and/or onward connection.
- All streaming zones have preload overlap.
- Landmark proxies map to correct full assets.
- Water, fog, and lighting state remain consistent across segment loads.
- Reference PNG hashes/dimensions remain unchanged.
- Standalone Windows benchmark captures CPU/GPU frame time and memory.

Create an HDRP route flythrough/debug mode that can jump to chainage, show grade/chapter/stream state, freeze time/exposure, toggle fog/HLOD/volumes, and capture standardized screenshots.

---

## 24. Acceptance criteria

### HDRP migration

- [ ] HDRP is installed and configured for Windows quality tiers.
- [ ] No missing or pink shaders in Shiosai or shared required content.
- [ ] Built-in custom shaders have explicit HDRP replacements.
- [ ] Lighting and post-processing are rebuilt with HDRP Volumes.
- [ ] Baseline visuals do not require ray tracing.

### Route

- [ ] One continuous approximately 42 km centerline.
- [ ] All eight chapter transitions are physically rideable.
- [ ] Road, collider, AI lane, rider placement, and trainer grade agree.
- [ ] No major decorative road is disconnected.
- [ ] Bridge, tunnel, lighthouse loop, sea arch, and final overlook connect correctly.

### Visuals

- [ ] Each PNG is recognizably represented in its corresponding chapter.
- [ ] Shared palette and cel-shaded identity remain consistent.
- [ ] Red bridge and lighthouse retain one silhouette across all LODs.
- [ ] Water progresses from turquoise shallows to cobalt distance.
- [ ] At least three depth bands are readable routinely and five at hero vistas.
- [ ] Road remains the primary navigational shape.

### Deep focus

- [ ] Gameplay DoF is off or extremely restrained.
- [ ] Foreground road, midground destination, and distant silhouette remain readable together.
- [ ] Atmospheric perspective separates distance without erasing landmarks.
- [ ] Far geometry uses proxies/HLOD rather than being blurred or culled too early.

### Performance

- [ ] High preset meets 1440p60 on the agreed target hardware or documented adjusted target.
- [ ] Low preset offers a credible 1080p60 path.
- [ ] Streaming has no perceptible riding hitch.
- [ ] Maximum-vista, harbor, bridge, water, and sea-arch stress tests pass.
- [ ] Quality changes do not break gameplay or route visibility.

---

## 25. Strict do-not-drift rules

1. Do not build eight disconnected scenes without a continuous route contract.
2. Do not stretch the old 2.892 km mock uniformly to 42 km.
3. Do not edit generated `ShiosaiRoute.json` by hand; update its authoring source.
4. Do not destroy validated rider/road alignment, lane offset, trainer grade, NPC, or world-map behavior.
5. Do not leave production Shiosai as destructive ribbon generation inside the Sakura scene.
6. Do not assume automatic material conversion handles custom shaders.
7. Do not ship any pink/missing shader fallback.
8. Do not use HDRP as permission to become photorealistic.
9. Do not make every material glossy, noisy, or physically literal.
10. Do not use strong gameplay DoF; the requested depth is deep scenic focus.
11. Do not hide missing scenery with blur, fog, darkness, or bloom.
12. Do not blur the upcoming road or landmarks.
13. Do not create decorative roads that float, end, or teleport.
14. Do not flip the ocean side or landmark positions arbitrarily between chapters.
15. Do not change the bridge/lighthouse silhouette between proxy and full model.
16. Do not scatter vegetation uniformly over every surface.
17. Do not obstruct the cycling line with village props, foliage, traffic, or VFX.
18. Do not require ray tracing for baseline visuals.
19. Do not add HDRP features without scalability and fallback behavior.
20. Do not use the reference PNGs as environment textures or impostors.
21. Do not modify, crop, recompress, upscale, or overwrite the eight reference PNGs.
22. Do not overuse shrines, torii, or stereotyped decorative motifs.
23. Do not copy a real bridge, team brand, or protected environment asset.
24. Do not declare completion from still screenshots alone.
25. Do not declare completion before a full ride, automated flythrough, route validation, shader audit, and standalone performance capture.

---

## 26. Copilot implementation plan

### Phase A — Preserve and branch

- Record current project state, route length, visuals, tests, and performance.
- Create recoverable migration branch/backup.
- Preserve reference images and both specifications.

### Phase B — HDRP vertical slice

- Install/configure HDRP.
- Create quality assets and baseline Volume.
- Rebuild cel, architecture, terrain, foliage, water, particle, and unlit shader paths needed by the slice.
- Produce one playable 500–1000 m coast slice.
- Approve appearance/performance before global conversion.

### Phase C — Route foundation

- Redesign the authoring spline for the full 42 km.
- Export route data and update graph/course/checkpoints.
- Preserve lane, grounding, physics, trainer grade, and NPC behavior.
- Implement floating-origin/segment-local precision solution.
- Prove primitive road can be ridden end to end.

### Phase D — Additive world blockout

- Create persistent, lighting, backdrop, and eight segment scenes.
- Block terrain, ocean, road, tunnel, bridge, lighthouse, sea arch, village, and overlook.
- Add persistent landmark proxies.
- Validate streaming and cross-segment continuity.

### Phase E — Art production

- Produce chapters in route order.
- Complete hero asset, materials, terrain, vegetation, architecture, water interaction, lighting, volumes, audio, collision, LOD, and HLOD for each.
- Compare against corresponding reference PNG after every major pass.

### Phase F — Depth and cohesion

- Tune sky, aerial perspective, volumetric fog, landmark persistence, horizon, and color grade globally.
- Confirm all scenes share one sun/time/weather state.
- Confirm foreground/midground/far depth matches references without shallow DoF.

### Phase G — Optimization and QA

- Implement all quality tiers.
- Profile standalone Windows builds at stress points.
- Fix streaming hitches, shader variants, overdraw, water/reflection cost, shadows, and HLOD transitions.
- Run full-route automated and manual tests.
- Produce approval captures and deviation log.

---

## 27. Final definition of done

The HDRP rebuild is complete only when Shiosai Coast is a continuous approximately 42 km Windows PC ride; each reference render has become a recognizable, traversable chapter; the coast preserves MapleRide's stylized 3D anime identity; HDRP provides stable water, atmosphere, lighting, shadows, reflections, and scalable quality; recurring landmarks maintain spatial continuity across kilometers; foreground road, midground destinations, and distant horizons remain simultaneously readable; all route/trainer/rider/NPC/world-map contracts work; and a standalone build sustains the agreed 60 fps target without visible streaming or shader failures.

