# MapleRide Shared Asset & Constants Catalog

Purpose: a single place to point future agent prompts at instead of having an agent
re-discover these facts by exploring the whole repo. Cite this file directly in prompts
("see ASSET_CATALOG.md") to cut token/exploration cost on every new build/fix task.
Keep this file short and factual — link to the real source file for anything that changes.

## Shared shaders (do not fork per-zone; add textures/materials instead)

| Shader | Used by | Notes |
|---|---|---|
| `MapleRide/SakuraCel` | Sakura Pass + Shiosai Coast | flat/simple cel shader, per-material `_ShadeColor`/`_RimColor` |
| `MapleRide/SakuraTerrain` | Sakura Pass + Shiosai Coast | tri-planar terrain/rock shader, reads `uv2_RockTex` (needs UV1 authored or it silently falls back to UV0 and can paint the whole mesh with one texture — see troubleshooting) |
| `MapleRide/SakuraWater` | Sakura Pass + Shiosai Coast (deep water) | fresnel-based shallow/deep blend only, no real depth notion |
| `Assets/Environment/ShiosaiCoast/Shaders/ShiosaiShallows.shader` | Shiosai Coast only | Shiosai-specific translucent shoreline shelf, vertex-alpha shore→sea gradient; do not port into `SakuraWater` |

## Blender build pipeline

- Shared builder API: `tools/blender/sakura_lib.py` (`u2b` coord helper, `recalc_normals`,
  `cap_open_boundaries`, `export_glb`, mesh helpers). Always start new builders from this,
  never hand-roll axis swaps or export code.
- Stage scripts: `build_terrain.py`, `build_road.py`, `build_props.py`, `build_flora.py`,
  `build_textures.py`, `build_tunnel.py` (Sakura Pass); `build_shiosai.py`,
  `build_shiosai_textures.py`, `shiosai_route.py` (Shiosai Coast).
- Orchestrator: `build_all.py` (stages: textures, route, terrain, road, props, flora).
- Copy-paste starting points (in the `sakura-environment-assets` skill, not the repo):
  `templates/new_prop.py`, `templates/new_texture.py`.
- Full command reference: skill file `reference/pipeline.md`.

## Unity staging

- Orchestrators: `Assets/Editor/SakuraPassEnvironment.cs` (Sakura, GLB-staging pattern — the
  target pattern for any zone that gets a real art pass), `Assets/Editor/ShiosaiCoastEnvironment.cs`
  (Shiosai — mostly migrated off the old "mock" C# procedural road/guardrail/trees; asphalt,
  guardrail, pines, torii/rock, harbour houses, distant mountain bands are now real authored
  assets as of the 2026-09-13 art pass).
- Diagnostics (editor-camera renders, NOT gameplay-representative — see gotcha below):
  `Assets/Editor/SakuraPassDiagnostics.cs`, `Assets/Editor/ShiosaiCoastDiagnostics.cs`.
- Gameplay-camera verification (what actually matches what the player sees — use this, not
  diagnostics, before calling any visual fix "verified"): `ShiosaiPlaymodeCapture.Run` and
  its `TrafficAudit()` HUD-free elevated pass. Renders land in `good_graphics/shiosai_play/`.

## Kuro-based NPC riders (replacing Coral — Minato done 2026-09-24, other regions pending)

Coral's body mesh is a 21,477-island Meshy triangle soup that shears open when posed (the torn
helmet/jersey/shoes seen from the chase camera — visible in a plain Blender render, so it is
geometry, not a shader). Four in-place repairs failed; don't try a fifth. Riders are now Kuro's
clean body (same 24-bone skeleton, so `CoralBikeRig`/`NpcCanonicalConformance` work unchanged):

| Step | Script | Output |
|---|---|---|
| 1. Label Kuro's atlas regions by geometry (bone + position) | `design_assets/3d/kuro/kuro_npc_segment.py` (Blender) | `kuro_npc/kuro_segments.npz` |
| 2. Per-rider kit atlas + manifest (style, hair colour) | `kuro_npc_liveries.py` (system python) | `Assets/Kuro/NPC/KuroRiders/KuroKit_<Name>.png`, `<region>_riders.json` |
| 3. Hair-style GLBs (bob/ponytail/twintails/long; "short" = Kuro's own GLB) | `kuro_npc_build.py` (Blender) | `KuroRiders/KuroRider_<style>.glb` |

Hair colour is a per-rider `_Color` tint on the greyscale `NpcHair` material (staging does it).
Staged by `Assets/Editor/MinatoNpcTraffic.cs`; close-up check: `MinatoKuroRiderCloseups.Capture`.
Gotcha: Kuro's GLB `images[0]` is the NORMAL map — resolve the material's baseColorTexture.

## Legacy NPC rider constants (Coral rig — still used by every region except Minato traffic)

Source of truth: `Assets/Editor/SakuraNpcRoster.cs`. Every rider in both zones should reuse
these verbatim rather than re-deriving them:

| Constant | Value | Meaning |
|---|---|---|
| Rig | `Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb` | the only sculpt that survives decimation — do not use the per-name `KuroNPC_<name>.glb` variants (they read as "crumpled foil" at distance) |
| Bike | `Assets/Kuro/kuro_bike_colnago.glb` | Coral's Colnago; hand grip is baked to this frame at this scale |
| `BikeScale` | `0.9` | required for the chibi arm chain to reach the brake hoods |
| `UnscaledWheelRadius` | `0.175` m | |
| `CoralSeatedHeight` | `1.185` m | measured from baked skinned mesh at scale 1 — `Renderer.bounds` on a SkinnedMeshRenderer lies, don't re-measure that way |
| `GroundOffset` | `0.065` m | clears the road's 6 cm parabolic crown |
| `RouteSpacing` | `3.0` m | must match the route resampling spacing |
| Bike child object name | `"Bike"` (exact) | staging code matches by exact name |
| Riders told apart by | recolored **texture**, not material tint | whole rider is one atlas on `Material_0`; a tint would recolor skin/face too. Recolor via a palette-variant script (see below), never tint the shared material. |

Livery/name generation: `design_assets/3d/kuro/npc_palette_variants.py` (Sakura's 10 named riders +
Coral) and `design_assets/3d/kuro/shiosai_palette_variants.py` (Shiosai's 12 named riders — *imports*
the Sakura script rather than duplicating it; add new zones the same way, never edit
`npc_palette_variants.py` itself for a different zone's names).

## Region / ambience system (one shared scene, not one scene per zone)

Source of truth: `Assets/Ride/RegionCatalog.cs`, `Assets/Ride/RegionDirector.cs`.
- All regions live in **one scene** (`Assets/Scenes/SakuraPass.unity`) as sibling roots keyed
  by exact name (`RegionCatalog.Region.EnvironmentRoot`, e.g. `"Sakura Pass Environment"`,
  `"Shiosai Coast Environment"`). `RegionDirector.ApplyEnvironmentVisibility()` shows/hides by
  exact name — new per-region objects (NPC traffic roots, etc.) must be parented under (or
  named to match) their region's root so they get hidden automatically when not active.
- Per-region time-of-day/grade lives in `RegionDirector.Ambience` structs (`SakuraAmbience`,
  `ShiosaiAmbience`): fog, ambient trilight, key/fill light euler+color+intensity, and the
  `SakuraPostFX` grade (bloom, exposure, saturation, contrast, lift/gain, vignette, DOF).
  Add a new zone's look here, don't touch the other zone's struct.
- DOF fields (`dofFocusDistance`, `dofFocusRange`, `dofStrength`, and the newer `dofFalloff`,
  `dofIterations`) are real and wired through `SakuraPostFX` — check they're non-trivial
  (Shiosai's original 240/1400/0.42 was ~1.5% blur at 400m, effectively dead) before assuming
  DOF "already exists" just because the fields are populated.

## Ambient traffic (randomized, pooled — the pattern for any zone that wants ambient NPCs)

Source of truth: `Assets/Ride/ShiosaiTrafficDirector.cs`, staged by
`Assets/Editor/ShiosaiNpcTraffic.cs`. Pattern to reuse for a future zone's ambient traffic:
- Work in **segment/centreline space**, not course arc-length, if the course is an
  out-and-back (arc length alone doesn't identify a road position on a there-and-back loop).
- Pool riders, recycle by relevance window around the player (ahead/behind distance), respawn
  on a **randomized** delay — never a fixed per-rider schedule (that's the Sakura pattern,
  intentionally different from ambient traffic).
- Assign direction + lane side per spawn so oncoming riders mirror onto the opposite
  carriageway automatically from the same lane-offset math the player's `RouteFollower` uses.
- "Zone 2" ambient pace ≈ 6.5–8.0 m/s (23–29 km/h), randomized per rider, not a single fixed
  speed — reserve higher speeds for race/sprint contexts elsewhere in the game.

## Known gotchas (check `reference/troubleshooting.md` in the skill first for more)

- Diagnostic-camera renders (`good_graphics/diag_*.png`) can look correct while the actual
  Play-mode scene still shows old/placeholder geometry if the Unity stage pass was never
  re-run after a code change. Always rebuild → re-stage → re-capture **gameplay** screenshots
  before calling a visual fix verified.
- `Cone()`'s third argument in `ShiosaiCoastEnvironment.cs` is a **radius**, not a width/diameter.
- A cross-section/profile builder that reaches a fixed distance in every direction regardless
  of local turn radius can self-fold at tight hairpins, producing a dark, unlit "floating
  plane" artifact (backfacing normals). Clamp offsets *and* proportionally scale the height
  rise together (preserve slope), not just the horizontal reach.
