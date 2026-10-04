# Azora Highlands — high-fidelity route and environment build guide

**For:** Copilot implementing MapleRide in Unity 6000.4.11f1 on Windows  
**Renderer already present:** HDRP package `17.4.0`, assigned in project Graphics Settings  
**Reference location:** `Assets/Environment/AzoraHighlands/`  
**Existing course:** `azora_ascent`, an open 24 km point-to-point ride  
**Visual goal:** Reproduce the seven renders as a coherent, traversable mountain ascent—not seven separate scenes or painted backdrops.

## 1. Read this before building

The PNGs are visual targets for terrain, atmosphere, route chapters, materials, vegetation, color, camera, and landmark hierarchy. They are **not exact geometry blueprints**. Image generation can depict roads that only appear connected from one camera. All production roads must be physically connected to the published course and correct from every viewpoint.

The current Azora implementation is not empty. It publishes `AzoraRoute.json` from `tools/blender/azora_route.py`, and `AzoraHighlandsEnvironment.cs` builds road, terrain, scenery, and route-dependent details from that centerline. The existing route has **6,001 samples**, **23,999.57 m** length, **one open course**, and **six checkpoints**. It climbs from about 900 m to Azora Col at about 1,980 m, then descends on the far side. Preserve the ride, trainer-grade behavior, lane placement, NPC logic, course ID, world-map/fast-travel connection, and known rider/road grounding. Art can be replaced; proven runtime contracts must be maintained.

The current route checkpoint names and approximate distances are:

| Checkpoint | Distance |
|---|---:|
| Meadow Gate | 0 km |
| Dry-Stone Switchbacks | 6.0 km |
| Pasture False-Flat | 10.8 km |
| Windward Ramp | 14.4 km |
| Azora Col — Panorama | 19.68 km |
| Highland Descent | 22.56 km |

The seven concepts can be distributed along this **existing** 24 km path. Do not silently create a longer route or change the course into a loop. If a visual landmark needs a route change, revise the Blender authoring source, regenerate the route JSON, rebake the route graph, then verify all dependent systems.

## 2. Image set and travel order

All links are relative to this Markdown file. The files are full-resolution original PNGs and must remain untouched. The same images also remain in `SC/cloud-forest-route/` as a source set.

| Ride order | Image | Proposed chainage | Purpose |
|---:|---|---:|---|
| 1 | [Riverhead Approach](01-riverhead-approach.png) | 0–3 km | Enter the wooded river gorge, first stone bridge, establish the river upstream |
| 2 | [Cloud-Forest Reference](00-cloud-forest-reference.png) | 3–6 km | Signature wet road, birch/pine forest, whitewater, bridge, waterfall tiers, mountain reveal |
| 3 | [Cascade Hairpins](02-cascade-hairpins.png) | 6–10 km | Climb beside stepped falls through connected retaining-wall turns and rock gallery |
| 4 | [Blue Meadow Plateau](03-blue-meadow-plateau.png) | 10–13 km | Forest opens to tarn, meadows, blue flowers, refuge and breathing room |
| 5 | [Avalanche Gallery](04-avalanche-gallery.png) | 13–17 km | Traverse the snowline under avalanche protection; amplify height/exposure |
| 6 | [Wind Pass and Sky Ridge](05-wind-pass-sky-ridge.png) | 17–20.5 km | Reach Azora Col, ride over a ridge above cloud-filled valleys |
| 7 | [Summit Basin Finale](06-summit-basin-finale.png) | 20.5–24 km | Observatory/tarn vista followed by the route's far-side descent and actual finish |

These ranges are **composition planning bands**, not replacement checkpoint data. The summit-basin render shows climbing toward an observatory; the real course descends after Azora Col. Place the observatory near the col, then continue visibly down the far side to the 24 km finish. Do not pretend the final section is all ascent.

## 3. Core visual target

One premium, stylized, true-3D alpine world with a cel-shaded/anime read. It must be materially richer than the current procedural mock without becoming photorealistic. Preserve:

- Wet dark asphalt and pale road-edge lines in lower sections, drying gradually at altitude.
- Gray stone parapets, bridges, retaining walls, and dry-stone structures as a recurring route family.
- Dense birch/pine cloud forest giving way to meadow, sparse conifers, rocky snowline, then high exposed ridge.
- Turquoise whitewater, tiered waterfalls, waterfall mist, a calm alpine tarn, and a partially frozen summit basin.
- Blue/violet flowers as controlled accents, not a uniform floral carpet.
- Bright cool highland morning light; clean cobalt sky; white snow peaks; warm sun shafts; blue-gray aerial perspective.
- Deep-focus composition: detailed road foreground, readable route/landmark midground, and legible distant peaks/valley silhouettes in the same image.
- A small black-clad chibi rider and road bike grounded on the actual pavement. The environment is the hero.

### Palette relationships

| Surface | Target color family | Material note |
|---|---|---|
| Asphalt | charcoal `#343B46` | Matte-damp; selective thin wet patches, not a mirror road |
| Road lines | warm near-white `#EDEDE8` | Sharp and readable from chase camera |
| Stonework | cool/warm gray `#827F7C` | Large strata/blocks; damp darkening near water |
| Forest | deep spruce `#244C3D` to lit sage `#6C9B60` | Distinct canopy and trunk silhouettes |
| Flowers | cobalt/violet `#557BD4`, `#8A70C7` | Sparse, clustered, distance-fading |
| River/foam | turquoise `#2FA9CC`, foam `#EAF7F8` | Flow/wave direction follows terrain |
| Tarn | glacier blue `#5DAFCB` | Quieter water than the river; stable reflection |
| Snow | cool off-white `#DCE7F2` | Shadows blue, not gray mud |
| Peaks | slate blue-gray `#677785` | Strong form in near ridges, lighter in far layers |
| Refuge/observatory | blue-white accents | Modest built landmarks; no castle or giant resort |

Judge displayed colors under one approved HDRP light/sky/exposure rig, not by raw texture hex alone.

## 4. Physical route and continuity

### The golden rule

One authoritative open route centerline controls **ride distance, rider position, grade, road mesh, collider, NPC path, chapter events, and composition anchors**. Decorative branches either rejoin or clearly end as small maintenance/service paths. Never imply a major road beyond a ridge that the player cannot actually reach.

### Route validation

- Road must enter and leave every chapter physically; no teleport or seam hidden by fog.
- Stone river bridges land on real terrain at both ends and have continuous pavement/collider.
- Hairpins are connected through actual turn geometry; do not compose stacked road tiers only for a screenshot.
- Avalanche gallery has a full entry, safe interior, exit, and route continuation.
- Sky-ridge saddle bridge has structural support and guardrails.
- Azora Col leads to the real far-side descent, not an inaccessible observatory cul-de-sac.
- Sightlines should support cycling speed; reduce art clutter in corner approach zones.
- Exposed road edges need continuous barrier/parapet coverage.
- Road mesh, painted markings, collider, gradient data, and rider lane height must agree within centimeters.

The current builder uses a **6 m road** (`3 m` half width), **0.7 m shoulder**, **0.06 m crown**, about **1.7 m lane offset**, and a solved road/rider height relationship. Treat these as existing runtime constraints. Any authored replacement road must be measured against the ride controller; do not simply eyeball the wheel contact from an image.

### Suggested chapter transitions

Place each transition behind a real terrain event: gorge widening, bridge, waterfall bend, tree line, tarn, avalanche gallery, col, and final summit basin. Landmarks should first appear as small distant goals, become clear, be reached, and later be visible behind from selected reverse-facing viewpoints.

## 5. Section-by-section implementation

### 5.1 Riverhead Approach — 0–3 km

Build a rideable river corridor rather than a uniform forest tube. Put the river below the road, support the pavement with stonework, and cross upstream once on the low bridge. The spillway and shelter are secondary lived-in details off the ride line. Use three forest bands: near birch trunks and flowers, mid gorge trees/river cascades, far upper mountains and first waterfall. Frame the distant cloud forest as the next reachable destination. Lower terrain should be mostly green with almost no snow.

### 5.2 Cloud-Forest Signature — 3–6 km

This is the user-selected hero reference. Match its low behind-rider view, black wet road, pale edge lines, heavy gray parapet posts with metal rails, multi-arch stone river crossing, blue roadside flowers, birch left foreground, pines throughout, strong whitewater and numerous waterfalls, shafts of sun, mist-filled gorge, and clear snowy peak skyline. Keep a few upper road tiers visible but reconstruct their actual continuity. Tune the scene from this section outward; it defines Azora's shared materials and lighting.

### 5.3 Cascade Hairpins — 6–10 km

Create an ascending chain of credible hairpins beside a tiered falls system. Use stone retaining walls that physically support each road level. The reference's upper gallery must connect to the lower road and exit onto the next level. Reveal the original river bridge far below in selected long views. Waterfall spray and fog may soften the gorge but must not obscure the upcoming turn or road edge. Increase exposed rock as forest thins.

### 5.4 Blue Meadow Plateau — 10–13 km

Give riders a visible recovery chapter: gentler grades, open meadow, tarn and mountain refuge. Keep the refuge modest and blue-roofed, with rideable access from the main road but no requirement for Kuro to walk. Use flower patches as compositional frames, not blanket scatter. The waterfall gorge should still be recognizable behind. Snowline switchbacks appear ahead as the next objective. Tarn water is calm; it must not reuse turbulent river behavior.

### 5.5 Avalanche Gallery — 13–17 km

The road enters a long open-sided gallery under snow chutes. Its concrete/stone structural rhythm creates movement parallax. Provide proper cyclist and camera clearance, continuous road/rail colliders, lighting and exposure blending. Snow fences above must follow actual slope. The refuge should be far behind, waterfall basin below, and upcoming ridge visible through gallery openings. Alpine trees become sparse. Wetness shifts from rain-dark road to melting-snow patches.

### 5.6 Wind Pass and Sky Ridge — 17–20.5 km

The route reaches the signed Azora Col around 19.68 km. Build broad, bicycle-safe ridge curves, a structurally credible short saddle crossing, stone/metal barriers, low wind-shaped vegetation and snow patches. Both valleys must show different layered geography; do not mirror one valley texture. Use cloud volume below the road without hiding the road. Make the rider feel genuinely high above earlier landmarks. Keep the radio/weather installation compact and original, never a tower-clutter theme park.

### 5.7 Summit Basin and Highland Descent — 20.5–24 km

Place a tarn and compact observatory near the col, then transition the primary route into the **actual open-course far-side descent**. Snow banks, little meltwater falls, limited blue flowers, and dark rock define the final chapter. Keep the partially frozen tarn clearly separate from the cycling line. Observatory terrace is a viewpoint/optional branch, not a forced dead end. Show the descent road leaving the basin and arriving at the Highland Descent checkpoint (~22.56 km), then clearly continuing to the 24 km finish. The final vista should look outward toward the next region, honoring the image while respecting route topology.

## 6. Highest practical HDRP fidelity

Use HDRP features where they improve the stylized outcome and survive real gameplay. A checkmark in a graphics setting is not fidelity; camera composition, authored geometry, stable exposure, material design, water placement, and recurring landmarks matter first.

### 6.1 Sky, sun and exposure

- Use a single physically based sky and one consistent sun direction/time across the open course.
- Set bright morning sun with cool skylight. Keep cloud detail near the sun.
- Use fixed or narrowly constrained gameplay exposure; local gallery/tunnel adaptation must be gradual.
- Support distinct sun shafts in the forest without whitewashing asphalt or rider.
- Use controlled volumetric clouds below the col and layered cloud shells at far distance.
- A light-probe/APV strategy must keep Kuro and NPC riders integrated with forest, gallery, and snowy pass lighting.

### 6.2 Deep focus, camera and atmospheric perspective

- Gameplay DoF: **off** or exceptionally mild. These images are deep-focus panoramas; blurring the distant route defeats the goal.
- Standard chase camera behind and slightly above rider, around 38–48° vertical FOV.
- Hero captures may use an elevated camera, but production must pass at real chase-camera height.
- Use at least five depth bands: 0–30 m road detail; 30–150 m hero rock/trees; 150–800 m waterfall/road tiers/refuge; 0.8–5 km ridge geometry; 5 km+ lightened mountain silhouettes/sky.
- Make distant peaks lighter, bluer and less texturally detailed while preserving their silhouette.
- Keep Azora's fog lighter than Shiosai/Sakura; the existing `RegionDirector.AzoraAmbience` intentionally prioritizes visibility and aerial perspective.
- Draw-distance ambitions must be served by HLOD/proxies, not full-detail objects to the horizon.

### 6.3 Volumetrics and weather

- Local HDRP fog volumes at river, waterfall basin, gallery exit, valleys and cloud shelf.
- Mist follows water and elevation; avoid uniform full-screen white fog.
- Lower road has carefully masked dampness and puddles. Keep traction/visual road edge readable.
- Waterfall spray is localized, wind-responsive and quality-scalable.
- Forest air may carry **wind-blown grass seed**: preserve Azora's existing regional VFX identity rather than importing Sakura petals.

### 6.4 Water

- River: authored spline/current direction, surface chop, turquoise body, turbulent whitewater around rocks, waterfalls following actual height changes.
- Falls: tiered geometry/VFX, coherent upstream source and downstream drainage, wet adjacent rock.
- Tarn: calmer, clearer HDRP-compatible lake material, controlled reflections, shoreline mask and partial ice only near summit.
- Use screen-space/probe reflections as baseline; expensive planar or ray-traced reflections only in selected Ultra/photo-mode views.
- Foam/waterline must attach to real shoreline or river rocks, not float as white decals.

### 6.5 Materials

- Build an HDRP-compatible stylized material family: road, dry/wet stone, dark timber, birch, conifer, meadow, blue flowers, snow, ice, refuge walls/roof, observatory, water and foam.
- Use physical light/shadow integration but graphic broad value bands, cool colored shadows and restrained specular shapes.
- Rock form is sculpted at large/medium scales. Avoid micro-noise that disappears at chase-camera distance.
- Road wetness uses a mask with sparse reflective patches; not a globally mirror-polished road.
- Snow uses cool-blue shadow and proper contact with rock/grass. No paper-white texture sheets.
- Vegetation needs stable alpha clipping, motion vectors, wind and shadow handling.
- Existing shaders named `MapleRide/SakuraCel`, `MapleRide/SakuraFoliage` and `MapleRide/SakuraTerrain` are used by the Azora builder; confirm they work under HDRP or replace them explicitly. No pink materials.

### 6.6 Shadows and GI

- High-resolution near sun shadows, stable cascades and contact shadows near rider/rails/rock.
- Use baked lighting where appropriate for static gallery/refuge/observatory, with probes for riders.
- Avoid high-frequency tree shadow flicker on the cycling line.
- Let far background shadows simplify; preserve form through macro texture/color and light direction.
- Treat ray tracing as optional Ultra/photo-mode enhancement, not a requirement for High preset correctness.

### 6.7 Post-processing

- HDR color grading: cool shaded rock/forest, warm sun edges, vivid but not neon water/flowers.
- TAA must be checked for rider smearing, foliage ghosting and shiny road flicker.
- Bloom low and thresholded, mostly sun/cloud/water highlights.
- AO subtle; do not dirty stylized skin or flatten snow.
- Motion blur low/off during cycling; no chromatic aberration or grain in normal gameplay.
- Photo mode may have shallow DoF, but benchmark/approval screenshots of the ride use deep focus.

## 7. Asset construction and data flow

Maintain the established generation contract wherever it keeps road/scenery aligned:

```text
tools/blender/azora_route.py
    → Assets/Environment/AzoraHighlands/AzoraRoute.json
    → RouteGraphBaker
    → player / trainer grade / NPC path / checkpoint distance
    → route-based road, barrier, collision and placement tools
```

Author unique hero assets in Blender or another DCC as normal source art: stone bridges, waterfall gorge, retaining walls, gallery, refuge, ridge crossing, observatory, ice/tarn shore. Place them against the published route and store source files. Procedural tools may distribute/fit repeatable parapets, flowers, trees and road details, but they should not replace sculpted hero geometry with flat corridor ribbons.

The current `AzoraHighlandsEnvironment.Apply()` destroys an exact-named root and rebuilds it. Before adding hand-authored production assets beneath that root, refactor the builder so it does not erase them on the next apply. Treat generated route infrastructure and authored landmark scenes/prefabs separately. Preserve saved mesh references, idempotence and repeatability.

Suggested organization under `AzoraHighlands/`:

```text
AzoraRoute.json
AZORA_HIGHLANDS_VISUAL_REBUILD.md
00-cloud-forest-reference.png
01-riverhead-approach.png
02-cascade-hairpins.png
03-blue-meadow-plateau.png
04-avalanche-gallery.png
05-wind-pass-sky-ridge.png
06-summit-basin-finale.png
Art/Meshes, Art/Materials, Art/Textures, Art/VFX
Prefabs/Road, Prefabs/Water, Prefabs/Vegetation, Prefabs/Landmarks
HDRP/Volumes, HDRP/ShaderGraphs
Scenes, Scripts, Editor, Tests
```

Do **not** apply the seven concept PNGs as billboards, terrain textures, skyboxes, or impostors. Reproduce them with actual scene geometry, materials, lighting and atmosphere.

## 8. LOD, streaming and performance

High fidelity must be observable from the rider, not merely in a still frame. Target **60 fps in a standalone Windows build** at the agreed High preset resolution, with 16.67 ms frame budget. Use separate lower settings for weaker Windows GPUs and Ultra/photo-mode for extra water/reflection/volumetric quality.

- Segment or grid streaming should keep the current area plus upcoming/behind transitions available at descent speed.
- Persist far proxies for waterfalls, bridge, refuge, snow ridge and observatory. They first appear distant, then resolve to full assets without silhouette change.
- Hero bridges/gallery/observatory: LOD0/1/2 plus HLOD/proxy.
- Forest: clustered HLOD/impostors beyond individual-tree distance; avoid unstable billboard transitions.
- Blue flowers: fewer geometry layers with distance; prevent alpha overdraw.
- Waterfall particles: quality-scaled, pooled and localized.
- Use authored occluders (ridges, forest, gallery) but do not accidentally occlude visible destination landmarks.
- Test floating-origin or camera-relative precision across the 24 km route, especially bike contact, shadows and water.
- Profile bridge/river/forest fog, cascade waterfall, gallery, sky-ridge vista and summit basin as separate stress cases.
- No streaming hitch, shader compilation stall or LOD pop at normal or downhill speed.

## 9. Reproduction workflow

1. **Audit:** capture the present course, gameplay camera, grade, rider contact and performance. Make a recoverable branch/backup.
2. **Lock the route:** retain 24 km open course; identify which rendered landmark belongs to each chainage band. Change route authoring only if necessary.
3. **Graybox:** place road, bridges, waterfall tiers, meadow/tarn, gallery, col, basin and far-side descent. Ride entire course before visual polish.
4. **Continuity pass:** from each chapter, prove the next destination exists in the correct direction; take backward-facing views to prove prior landmarks remain spatially coherent.
5. **Hero asset pass:** build sculpted bridge, falls, gallery, refuge, ridge crossing, observatory and water bodies.
6. **HDRP look pass:** create one shared sky/sun/exposure/atmosphere baseline and material masters; begin by matching the original Cloud-Forest Reference.
7. **Detail pass:** art-direct trees, flowers, stonework, water, wetness, snow, props and wind. Keep road visibility.
8. **Technical pass:** LOD/HLOD, streaming, probes, shadow stability, draw distance, shader audits and quality presets.
9. **Validation:** compare concept camera and real chase camera, run route tests, profile standalone Windows player and ride full 24 km.

For every image, save: reference, approximate matching Unity capture, standard chase view, grayscale/value comparison, edge/silhouette overlay, collision/route overlay and an approved deviation list. Match route topology and major shapes before tiny prop detail. A beauty screenshot alone is not acceptance.

## 10. Acceptance tests

- [ ] Seven chapter images are recognizable, with coherent shared road/stone/forest/water/snow language.
- [ ] The route is still `azora_ascent`, open, and approximately 24 km.
- [ ] All six existing checkpoint positions remain sensible or are explicitly migrated.
- [ ] Every visible major road is reachable and connected; no impossible hairpins/bridges.
- [ ] Rider wheels touch pavement correctly at the preferred lane offset, including bridges/gallery.
- [ ] Trainer grade matches filtered route slope, not art-only mesh banking.
- [ ] River/falls/tarn/ice have distinct credible behavior and physical sources.
- [ ] Main landmarks remain consistent as they appear, are reached, and recede.
- [ ] Road, midground destination and far peaks remain legible simultaneously.
- [ ] Azora keeps lighter fog and wind-blown seed identity.
- [ ] No Built-in-only/pink shader remains in production Azora views.
- [ ] Forest, gallery, water and summit benchmark cases meet the agreed Windows frame target.
- [ ] Full 24 km ride and automated flythrough pass with no load hitch, road seam or dead end.

## 11. Strict do-not-drift rules

1. Do not replace the real 24 km open course with seven disconnected dioramas.
2. Do not change route length/shape/checkpoints casually to imitate image-perspective artifacts.
3. Do not edit generated route JSON directly; change its authoring source and rebake.
4. Do not lose rider-grounding, lane-offset, grade, NPC or world-map contracts.
5. Do not put authored hero assets under a root that the existing builder will destroy.
6. Do not make the road or wet pavement a mirror; keep it readable and bicycle-safe.
7. Do not use strong gameplay DoF, dense fog or bloom to conceal unfinished far terrain.
8. Do not flatten mountains into sky cards at hero distance or use reference PNGs as scene art.
9. Do not turn the coast's turquoise/cream/coral identity into Azora; Azora is alpine blue/green/gray/white.
10. Do not make Azora photorealistic just because it uses HDRP.
11. Do not fill every meadow with identical flowers or every slope with uniform trees.
12. Do not add giant castles, resorts or fantasy structures; built landmarks are small and functional.
13. Do not create waterfalls without a visible upstream source and downstream drain.
14. Do not add snow to the lower forest or thick trees to the highest ridge without a climate rationale.
15. Do not substitute Sakura petals for Azora's wind-blown grass-seed VFX.
16. Do not declare completion from one camera angle or an Editor screenshot.

## Definition of done

Azora Highlands is complete when all seven images have become recognizable sections of **one physically continuous 24 km HDRP mountain ride**, their landscapes change naturally with elevation, the rider can travel from river gorge through the cloud forest and col to the far-side descent, the road and landmarks remain sharp across exceptional scenic depth, all established cycling and world systems work, and a standalone Windows build holds the agreed frame-rate target.
