# Sakura Pass NPC roster — design mockups (ROUND 2)

**DESIGN / REVIEW GATE ONLY. Nothing in this folder is rigged, seated, staged or spawned into
SakuraPass. No gameplay code is wired to any of it.**

Start with `NPC_00_ContactSheet.png`, then the per-rider sheets `NPC_01_Aoi.png` …
`NPC_10_Takumi.png`.

---

## What changed in round 2

Round 1 rendered all ten riders on the same female-presenting Coral sculpt, wearing the same
baked hair silhouette in ten different colours, with small additive proxies on top. Review
raised two problems and this round addresses both directly.

**1. The roster read as primarily all female.**
There is now an explicit **gender read** per rider and a **5 male / 4 female / 1 androgynous**
split. Male riders are no longer "the female sculpt in a hat" — they get a masculinised
**body silhouette** (shoulder yoke pushed out, hips pulled in, jaw widened), they lose the
baked bob's chin-length cheek tresses, and they wear short masculine hairstyles.

**2. Every rider wore the same hair.**
The shared baked bob is now **stripped off the sculpt**, and each rider's hair is built as
**real geometry on the measured skull** — a hair cap plus curved, tapered, solidified anime
"lock plates", fringes, sideburns, undercuts, tails, a braid and a bun. All ten are different
silhouettes, not recolours.

Because a helmet hides the crown, each sheet and each contact-sheet cell now also carries a
**helmet-off hair chip** so the hairstyles can be judged on silhouette alone.

**Names:** no rider needed renaming. **Nao** and **Sora** changed gender read to male; both are
genuinely unisex Japanese given names, so the names, roles and bios stand (pronouns updated).

---

## The roster

| # | Name | Gender read | Role | Hairstyle | Helmet | Accessory | Kit palette (kit / trim / hair) |
|---|------|-------------|------|-----------|--------|-----------|--------------------------------|
| 01 | **Aoi** | Female | Rival / all-round pace-setter | **Hime cut** — blunt fringe, two straight chin-length side curtains, long flat back length; the only hard geometric outline | aero | amber sports shades | `#22407E` / `#F2A03C` / `#1B2340` |
| 02 | **Haruka** | Female | Sprinter | **High twin-tails** — two thick bunches springing out above the ears; widest silhouette on the sheet | vented | teal arm warmers | `#E8408F` / `#25C3B8` / `#C77BD8` |
| 03 | **Mika** | Female | Tourist / route photographer | **Side braid** — one thick braid forward over the right shoulder with a swept side-parted fringe; asymmetric | capbrim | camera on a neck strap | `#F2B22E` / `#3E7FBF` / `#6B4326` |
| 04 | **Nao** | Male | Climber | **Cropped centre-split** — short crop with a centre-split curtain fringe, clipped at temples and nape; tidy, low volume | vented | race number bib | `#1FA877` / `#8A5CD6` / `#E8D79A` |
| 05 | **Ren** | Male | Breakaway specialist / cool rival | **Spikes + undercut** — swept-back blades front to back, bare above the ears; sharpest outline | visorshield | none | `#6B3FC4` / `#F0C23A` / `#3A2150` |
| 06 | **Daichi** | Male | Club veteran / mechanic | **Hard side part** — slicked flat and combed over from a low parting, trimmed sideburns; smoothest outline | bandana | canvas musette | `#8FBF3F` / `#2F5FD0` / `#3B2A1C` |
| 07 | **Sora** | Male | Descender / daredevil | **Faux-hawk** — a single raised centre crest, temples cut tight; tallest, narrowest crown | halfshell | coral scarf | `#2BC6D8` / `#F4548A` / `#F06A9B` |
| 08 | **Emi** | Female | Club rookie / junior | **Low messy bun** — gathered at the nape with loose temple wisps; volume all low and behind | rounded | school backpack | `#A45BD6` / `#54D69A` / `#E88AA8` |
| 09 | **Yuki** | Androgynous | Time-trialist | **Long centre-parted curtains** — straight past the jaw, dead flat, no crown volume; deliberately reads neither | ttlong | clear wraparound visor | `#DCE6F2` / `#5FB8D8` / `#F2F4F8` |
| 10 | **Takumi** | Male | Rouleur / gravel wanderer | **Shaggy mop** — overgrown layered flicks over the ears and collar; messiest outline | capbrim | orange arm warmers | `#3A3F47` / `#E2762C` / `#D2622A` |

Split: **5 male** (Nao, Ren, Daichi, Sora, Takumi) · **4 female** (Aoi, Haruka, Mika, Emi) ·
**1 androgynous** (Yuki).

Each per-rider sheet also carries the bike frame / bike accent swatches and the personality blurb.

---

## What is rig-compatible about this

Every rider is the same Coral chibi sculpt (`KuroNPC_Coral_Rigged.glb`), the same skeleton and
the same scale — **1.357 m standing / 1.185 m seated**, the same character-height class as Coral
and Kuro. The three full-body views are rendered at an **identical fixed camera distance** for
all ten, so the sheets are directly size-comparable and any size drift would be obvious.

Hair is additive geometry built on the skull in the sculpt's own rest-pose space; the body edit
is a vertex deformation of the existing mesh. Neither adds or moves a bone, so all ten remain
bindable to the shared Kuro skeleton later.

---

## Honest limitations — please read before judging fidelity

* **This is mockup geometry, not final hair.** The hair is procedurally built plates. It reads
  as the right silhouette and is deliberately crude next to the hand-painted sculpt. Final hair
  would be sculpted.
* **A short baked fringe is shared.** The sculpt's fringe is fused *into* the forehead surface,
  not a separate shell — cutting it opens real holes through into the head, and no additive
  patch sits convincingly on the curved chibi face (four attempts). So a short fringe remnant is
  left intact under every rider's own hair, recoloured to their hair colour by the atlas swap.
* **Male brows are not modelled.** The face atlas cannot be repainted (its UVs are shredded
  photogrammetry fragments scattered across 15 cells), and every additive brow attempt rendered
  as grey sunglasses. The male read is carried by the body silhouette and the haircut instead.
* **Profile artefacts at the nape.** In the SIDE views there are small leftover dark strand
  shards behind the head and a visible seam where the hair cap meets the baked helmet. Cutting
  those shards away is worse, not better — they are what hides the baked helmet's unlit
  interior.
* **All colours, roles, names, helmets and accessories remain PROVISIONAL** pending this review.

---

## Regenerating

```powershell
$BL = "C:\Users\jason\OneDrive\Desktop\MapleRide\tools\blender-4.5.10-windows-x64\blender.exe"
cd C:\Users\jason\OneDrive\Desktop\MapleRide

# 1. per-rider recoloured kit atlases  -> assets/3d/kuro/mockups/_kits/
python  assets\3d\kuro\mockups\gen_kits.py

# 2. render front / 3-4 / side + the helmet-off hair chip -> _views/   (add "-- Ren Aoi" for a subset)
& $BL -b -P assets\3d\kuro\mockups\render_mockups.py

# 3. compose the sheets + contact sheet into this folder
python  assets\3d\kuro\mockups\compose_sheets.py
```

Sources, all under `assets/3d/kuro/mockups/`:

| File | Owns |
|------|------|
| `roster_spec.py` | the single source of truth: names, gender, roles, hairstyle tokens, palettes, bios |
| `sculpt_variants.py` | stripping the baked hair/helmet off the shared sculpt, and the male body deformation |
| `hair_library.py` | the ten hairstyle builders (`scalp` shell + `lock` plate primitives) |
| `render_mockups.py` | rig import, kit atlas swap, surgery, hair build, the four renders |
| `compose_sheets.py` | the review sheets and the contact sheet |
| `probe_hair.py` | fast verification loop — renders all ten styles on a bare head into `_probe/` |
