"""
M1 - MapleRide world geography master plan.

Authoring source for the canonical world layout. Run with system Python:

    python tools/blender/worldmap/author_world_layout.py

It writes Assets/Resources/World/world_layout.json, which is THE canonical world: the
terrain generator (world_terrain.py), the Blender relief render (build_world_relief.py) and the
Unity World Map (RegionCatalog / WorldMapHud) all read that JSON, never this file. Edit here,
re-run and commit the JSON.

Conventions
  * Units are kilometres. Origin is the SW corner, +x = east, +y = north (Unity x / z).
  * Elevations are metres above sea level.
  * Polylines and polygons are FLAT float arrays [x0, y0, x1, y1, ...] so Unity's JsonUtility
    can read them.
  * Built regions' route polylines are the REAL shipped course centrelines (Assets/Environment/
    <Region>/<Region>Route.json), drawn at true scale and centred on the region's site.
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "World", "world_layout.json")

W, H = 380.0, 250.0


def flat(pts):
    return [round(float(v), 3) for p in pts for v in p]


# ---------------------------------------------------------------------------------- coast
# Main island (Hondo), clockwise from Kaze Cape. The terrain generator fractal-warps this
# outline, so the vertices only need to carry the large shapes (capes, bights, peninsulas).
MAIN_COAST = [
    (8, 102), (16, 122), (14, 140), (20, 158), (16, 174), (26, 188), (38, 202), (52, 214),
    (70, 226), (92, 236), (116, 242), (140, 246), (164, 244), (186, 248), (210, 247),
    (236, 244), (260, 246), (284, 242), (306, 236), (326, 226), (344, 212), (358, 196),
    (368, 178), (374, 160), (366, 146), (358, 134), (362, 120), (356, 106), (348, 94),
    (354, 82), (340, 72), (322, 68), (306, 74), (292, 70), (278, 76), (262, 72), (248, 78),
    (236, 76), (224, 84), (212, 82), (200, 84), (188, 78), (178, 70), (168, 63),
    (158, 59), (146, 62), (134, 68), (122, 74), (108, 78), (94, 80), (80, 86),
    (68, 90), (54, 88), (40, 86), (26, 90), (14, 96),
]

# Water cut out of the main island: Minato Bay and its north-east arm, the Maple City inner
# harbour. The bay opens to the sea between the Minato peninsula and Nagisa.
SEA_CUTS = [
    {"name": "Minato Bay", "poly": flat([
        (158, 54), (172, 60), (170, 66), (168, 76), (176, 88), (179, 100), (180, 110),
        (184, 120), (194, 130), (206, 138), (214, 142), (210, 148), (198, 144), (184, 138),
        (168, 134), (150, 136), (132, 136), (118, 132), (108, 124), (104, 112), (108, 100),
        (120, 92), (136, 86), (150, 80), (156, 72), (156, 62)])},
    # smaller inlets that break the outline into capes and bays
    {"name": "Kita Fjord", "poly": flat([(94, 252), (99, 232), (103, 228), (106, 236), (110, 252)])},
    {"name": "Mizu Bay", "poly": flat([(160, 254), (166, 240), (174, 236), (182, 242), (186, 254)])},
    {"name": "Kage Bay", "poly": flat([(380, 206), (354, 198), (350, 192), (358, 186), (380, 186)])},
    {"name": "Higashi Inlet", "poly": flat([(380, 124), (362, 127), (360, 132), (366, 136), (380, 138)])},
    {"name": "Kitsune Ria", "poly": flat([(328, 58), (331, 74), (336, 82), (341, 78), (346, 58)])},
    {"name": "Nagisa Sound", "poly": flat([(204, 64), (207, 78), (212, 84), (218, 80), (222, 64)])},
    {"name": "Momo Estuary", "poly": flat([(238, 64), (243, 78), (247, 82), (252, 77), (256, 64)])},
    {"name": "Shiosai Cove", "poly": flat([(84, 66), (86, 80), (91, 85), (96, 80), (100, 66)])},
    {"name": "Kaze Bight", "poly": flat([(0, 126), (11, 127), (14, 134), (10, 142), (0, 144)])},
    {"name": "Akane Gulf", "poly": flat([(0, 176), (18, 172), (24, 178), (22, 186), (0, 192)])},
]

# Offshore islands as ellipses (centre, radii, rotation deg), fractal-warped like the coast.
ISLANDS = [
    {"name": "Tsuki-jima", "c": [298, 46], "r": [8.5, 5.5], "rot": 20},
    {"name": "Ko-Tsuki", "c": [314, 38], "r": [4.5, 3.0], "rot": -10},
    {"name": "Hoshi-jima", "c": [282, 34], "r": [3.8, 2.6], "rot": 35},
    {"name": "Nami-jima", "c": [311, 57], "r": [3.0, 2.2], "rot": 0},
    {"name": "Yoru-jima", "c": [328, 48], "r": [2.6, 1.9], "rot": 50},
    {"name": "Kumo-jima", "c": [60, 40], "r": [12.5, 9.5], "rot": -15},
    {"name": "Kumo Islet", "c": [80, 32], "r": [2.4, 1.6], "rot": 10},
    {"name": "Shio Rocks", "c": [44, 74], "r": [2.2, 1.5], "rot": 0},
    {"name": "Minato Breakwater Isle", "c": [176, 60], "r": [2.4, 1.8], "rot": 0},
    {"name": "Nishi-jima", "c": [10, 62], "r": [4.0, 2.6], "rot": 20},
    {"name": "Oki-jima", "c": [28, 230], "r": [5.0, 2.6], "rot": 30},
    {"name": "Kaze Stacks", "c": [4, 118], "r": [2.0, 3.2], "rot": 0},
    {"name": "Higashi-jima", "c": [378, 140], "r": [1.8, 3.6], "rot": 15},
    {"name": "Minami Reef", "c": [230, 52], "r": [3.4, 1.8], "rot": -20},
    {"name": "Hanare-jima", "c": [120, 40], "r": [3.0, 2.0], "rot": 30},
]

# ---------------------------------------------------------------------------------- relief
# Ranges: crest polyline with a crest height per vertex (m) and a half-width (km).
RANGES = [
    {"name": "Kita Alps", "width": 24,
     "pts": flat([(30, 186), (50, 196), (72, 203), (98, 205), (120, 205), (142, 208),
                  (164, 212)]),
     "crest_m": [1900, 2800, 3200, 3350, 3150, 2800, 2300]},
    {"name": "Higashi Range", "width": 20,
     "pts": flat([(270, 214), (294, 200), (306, 182), (312, 162), (316, 142), (318, 124),
                  (320, 108)]),
     "crest_m": [2000, 2450, 2250, 1950, 1850, 1500, 1000]},
    {"name": "Chuo Ridge", "width": 10,
     "pts": flat([(222, 166), (232, 180), (240, 200), (248, 214)]),
     "crest_m": [800, 1250, 1400, 1100]},
    {"name": "Nagisa Hills", "width": 9,
     "pts": flat([(194, 100), (204, 110), (214, 116), (226, 118)]),
     "crest_m": [300, 420, 380, 300]},
    {"name": "Minato Heights", "width": 6,
     "pts": flat([(120, 82), (134, 76), (146, 70)]),
     "crest_m": [180, 260, 160]},
]

# Stratovolcanoes / island cones: peak (m), base radius (km), crater radius (km) and depth (m).
VOLCANOES = [
    {"name": "Kamifuji", "c": [200, 222], "peak_m": 3340, "r": 24, "crater_r": 1.5,
     "crater_m": 170, "lake_m": 3180},
    {"name": "Kumo-dake", "c": [58, 41], "peak_m": 920, "r": 10, "crater_r": 0.8,
     "crater_m": 60, "lake_m": 0},
]

# Calderas: a ring rim around a sunken floor with a lake.
CALDERAS = [
    {"name": "Hinode Caldera", "c": [360, 158], "rim_r": 7.5, "outer_r": 17, "rim_m": 1100,
     "floor_m": 360, "lake_m": 380},
]

# Plateaus: raised table land with a soft edge (km).
PLATEAUS = [
    {"name": "Azora Plateau", "c": [124, 170], "r": [26, 17], "rot": 10, "level_m": 1650,
     "edge": 7},
    {"name": "Akane Mesa", "c": [46, 152], "r": [30, 42], "rot": 0, "level_m": 720,
     "edge": 6, "canyons": True},
    {"name": "Mizuumi Basin", "c": [140, 234], "r": [22, 9], "rot": -5, "level_m": 700,
     "edge": 6},
]

# Hill zones: rolling-hill amplitude (m) inside an ellipse. amp < 60 flattens (plains, delta,
# city basins).
HILL_ZONES = [
    {"name": "Nagisa uplands", "c": [206, 110], "r": [18, 16], "amp_m": 300},
    {"name": "Bamboo hills", "c": [262, 132], "r": [26, 20], "amp_m": 480},
    {"name": "Chaen hills", "c": [292, 112], "r": [18, 14], "amp_m": 560},
    {"name": "Kage uplands", "c": [334, 190], "r": [24, 28], "amp_m": 850},
    {"name": "Northern Wilds", "c": [284, 226], "r": [34, 18], "amp_m": 750},
    {"name": "Shiosai bluffs", "c": [72, 100], "r": [30, 14], "amp_m": 230},
    {"name": "Kitsune plain", "c": [338, 100], "r": [22, 16], "amp_m": 50},
    {"name": "Hotaru delta", "c": [112, 138], "r": [18, 12], "amp_m": 8},
    {"name": "Maple basin", "c": [212, 154], "r": [13, 10], "amp_m": 25},
    {"name": "Minato waterfront", "c": [151, 68], "r": [6, 4], "amp_m": 20},
]

# ---------------------------------------------------------------------------------- water
# Rivers run source -> mouth. "width" is the channel width at the mouth (km).
RIVERS = [
    {"name": "Kawa", "width": 0.9,
     "pts": flat([(150, 190), (166, 180), (184, 170), (198, 160), (208, 152), (212, 145)])},
    {"name": "Shirakawa", "width": 0.5,
     "pts": flat([(204, 200), (211, 182), (213, 166), (210, 153)])},
    {"name": "Hotaru", "width": 1.1,
     "pts": flat([(100, 192), (103, 178), (106, 160), (110, 146), (113, 134)])},
    {"name": "Hotaru West Mouth", "width": 0.6,
     "pts": flat([(110, 146), (106, 138), (107, 128)])},
    {"name": "Hotaru East Mouth", "width": 0.6,
     "pts": flat([(110, 146), (118, 140), (123, 135)])},
    {"name": "Momo", "width": 0.8,
     "pts": flat([(306, 176), (294, 160), (277, 146), (262, 136), (254, 120), (250, 100),
                  (246, 79)])},
    {"name": "Akane", "width": 0.7,
     "pts": flat([(62, 180), (50, 162), (38, 142), (27, 122), (15, 107)])},
    {"name": "Mizu", "width": 0.5,
     "pts": flat([(146, 230), (143, 240), (140, 247)])},
    {"name": "Kita", "width": 0.5,
     "pts": flat([(288, 222), (284, 236), (282, 244)])},
    {"name": "Kitsune", "width": 0.6,
     "pts": flat([(326, 132), (336, 114), (344, 98), (350, 88)])},
    {"name": "Seta", "width": 0.6,
     "pts": flat([(262, 180), (268, 168), (272, 156), (277, 147)])},
    {"name": "Kage", "width": 0.5,
     "pts": flat([(330, 200), (346, 196), (358, 194)])},
]

LAKES = [
    {"name": "Lake Aoi", "c": [118, 176], "r": [5.0, 2.6], "rot": 20, "level_m": 1560},
    {"name": "Lake Shiro", "c": [134, 166], "r": [3.0, 1.8], "rot": -15, "level_m": 1640},
    {"name": "Mizuumi-ko", "c": [128, 236], "r": [6.0, 2.5], "rot": -10, "level_m": 690},
    {"name": "Kagami-ko", "c": [143, 232], "r": [4.0, 2.0], "rot": 15, "level_m": 700},
    {"name": "Hoshi-ko", "c": [153, 238], "r": [3.5, 1.8], "rot": 0, "level_m": 680},
    {"name": "Kita-ko", "c": [276, 230], "r": [5.0, 3.0], "rot": 25, "level_m": 620},
    {"name": "Ookami-ko", "c": [294, 222], "r": [3.0, 2.0], "rot": -20, "level_m": 700},
    {"name": "Hinode-ko", "c": [360, 158], "r": [5.2, 4.2], "rot": 10, "level_m": 380},
    {"name": "Oumi-ko", "c": [256, 186], "r": [12.0, 5.5], "rot": 28, "level_m": 95},
]

# ---------------------------------------------------------------------------------- biomes
# Soft biome zones (ellipse + feather km). Anything not covered is temperate: grass lowland,
# broadleaf forest, conifers higher, rock and snow above the snowline (2,000 m +/- noise).
BIOME_ZONES = [
    {"biome": "desert", "c": [44, 150], "r": [30, 44], "feather": 10},
    {"biome": "winter", "c": [124, 172], "r": [30, 22], "feather": 8},
    {"biome": "sakura", "c": [306, 152], "r": [22, 26], "feather": 8},
    {"biome": "sakura", "c": [212, 152], "r": [10, 8], "feather": 5},
    {"biome": "autumn", "c": [338, 104], "r": [26, 22], "feather": 10},
    {"biome": "bamboo", "c": [262, 134], "r": [18, 14], "feather": 7},
    {"biome": "tea", "c": [292, 112], "r": [14, 11], "feather": 5},
    {"biome": "paddy", "c": [112, 138], "r": [16, 11], "feather": 5},
    {"biome": "boreal", "c": [284, 226], "r": [40, 20], "feather": 10},
    {"biome": "boreal", "c": [140, 234], "r": [26, 12], "feather": 8},
    {"biome": "darkforest", "c": [334, 190], "r": [22, 26], "feather": 8},
    {"biome": "volcanic", "c": [200, 222], "r": [8, 8], "feather": 4},
    {"biome": "volcanic", "c": [360, 158], "r": [9, 9], "feather": 4},
    {"biome": "tropical", "c": [298, 46], "r": [40, 20], "feather": 6},
]

SNOWLINE_M = 2000

# Settlements: towns drawn as building clusters. "towers" = a high-rise core.
SETTLEMENTS = [
    {"name": "Maple City", "c": [212, 151], "r": 9.5, "density": 1.0, "towers": True},
    {"name": "Minato", "c": [150, 68], "r": 4.0, "density": 0.9, "towers": False},
    {"name": "Nagisa Marina", "c": [185, 99], "r": 3.0, "density": 0.85, "towers": False},
    {"name": "Shiosai", "c": [70, 92], "r": 2.6, "density": 0.7, "towers": False},
    {"name": "Hotaru", "c": [114, 140], "r": 2.2, "density": 0.55, "towers": False},
    {"name": "Kitsune", "c": [340, 94], "r": 2.2, "density": 0.55, "towers": False},
    {"name": "Kaze", "c": [18, 104], "r": 1.6, "density": 0.5, "towers": False},
    {"name": "Azora village", "c": [122, 162], "r": 1.4, "density": 0.5, "towers": False},
    {"name": "Sakura village", "c": [304, 148], "r": 1.4, "density": 0.5, "towers": False},
    {"name": "Bamboo village", "c": [262, 136], "r": 1.3, "density": 0.45, "towers": False},
    {"name": "Tsuki harbour", "c": [296, 42], "r": 1.4, "density": 0.55, "towers": False},
    {"name": "Taka lodge", "c": [98, 195], "r": 0.9, "density": 0.35, "towers": False},
    {"name": "Fuji shrine town", "c": [200, 206], "r": 1.2, "density": 0.4, "towers": False},
    {"name": "Mizuumi", "c": [138, 232], "r": 1.2, "density": 0.4, "towers": False},
    {"name": "Kage village", "c": [332, 186], "r": 1.0, "density": 0.35, "towers": False},
    {"name": "Wilds outpost", "c": [282, 226], "r": 0.9, "density": 0.3, "towers": False},
    {"name": "Hinode", "c": [352, 160], "r": 1.0, "density": 0.35, "towers": False},
    {"name": "Chaen", "c": [290, 110], "r": 1.1, "density": 0.4, "towers": False},
    {"name": "Canyon post", "c": [48, 150], "r": 0.9, "density": 0.3, "towers": False},
    {"name": "Kumo port", "c": [66, 32], "r": 1.0, "density": 0.4, "towers": False},
    {"name": "Oumi", "c": [247, 179], "r": 2.0, "density": 0.55, "towers": False},
]

# ---------------------------------------------------------------------------------- regions
# pos      = the site the map pin marks (km).
# summit   = [x, y, m]: the region's defining elevation, which the terrain is locally corrected
#            to (so the world agrees with the course and the RegionCatalog subtitle).
# route    = the ride polyline (km). Built regions use the real course (see header).
REGIONS = [
    # ---- built -------------------------------------------------------------------------
    {"id": "sakura_pass", "name": "Sakura Pass", "tagline": "Petals on the Climb",
     "subtitle": "1,320 m", "status": "built", "biome": "sakura", "pos": [312, 152],
     "summit": [313, 153, 1320], "route_src": "SakuraPass/SakuraRoute.json",
     "route_scale": 6.0},
    {"id": "shiosai_coast", "name": "Shiosai Coast", "tagline": "Ride the Breeze",
     "subtitle": "20.0 km", "status": "built", "biome": "coast", "pos": [72, 94],
     "summit": [76, 97, 170], "route_src": "ShiosaiCoast/ShiosaiRoute.json",
     "route_rot": -90},
    {"id": "maple_city", "name": "Maple City", "tagline": "The Heart",
     "subtitle": "40 m", "status": "built", "biome": "city", "pos": [212, 152],
     "summit": [212, 152, 40], "route_src": "MapleCity/MapleRoute.json"},
    {"id": "azora_highlands", "name": "Azora Highlands", "tagline": "Earn the View",
     "subtitle": "1,980 m", "status": "built", "biome": "winter", "pos": [126, 170],
     "summit": [130, 176, 1980], "route_src": "AzoraHighlands/AzoraRoute.json"},
    {"id": "taka_mountains", "name": "Taka Mountains", "tagline": "The High Road",
     "subtitle": "2,842 m", "status": "built", "biome": "alpine", "pos": [98, 198],
     "summit": [100, 201, 2842], "route_src": "TakaMountains/TakaRoute.json"},
    {"id": "fuji_ridge", "name": "Fuji Ridge", "tagline": "Clouds Above",
     "subtitle": "1,776 m", "status": "built", "biome": "volcanic", "pos": [200, 209],
     "summit": [200, 213.5, 1776], "route_src": "FujiRidge/FujiRoute.json"},
    {"id": "minato_port", "name": "Minato Port", "tagline": "Beyond the Horizon",
     "subtitle": "19.0 km", "status": "built", "biome": "port", "pos": [153, 66],
     "summit": [153, 66, 6], "route_src": "MinatoCoast/MinatoRoute.json",
     "route_anchor": [157, 63], "route_scale": 1.2},
    # ---- B5: promoted by the B5 package (COORDINATION.md) --------------------------------
    {"id": "nagisa_bay", "name": "Nagisa Bay", "tagline": "Where the City Meets the Sea",
     "subtitle": "420 m", "status": "planned", "biome": "coast", "pos": [200, 108],
     "summit": [204, 110, 420],
     "route": flat([(190, 103), (193, 107), (198, 110), (203, 111), (206, 108), (204, 104),
                    (208, 100), (214, 101), (218, 106), (222, 112), (226, 117)])},
    # ---- locked (v1 map) ------------------------------------------------------------------
    {"id": "northern_wilds", "name": "Northern Wilds", "tagline": "Untamed", "subtitle": "",
     "status": "locked", "biome": "boreal", "pos": [284, 226], "summit": [284, 226, 900]},
    {"id": "kage_forest", "name": "Kage Forest", "tagline": "Shadows in the Trees",
     "subtitle": "", "status": "locked", "biome": "darkforest", "pos": [334, 188],
     "summit": [334, 188, 720]},
    {"id": "kitsune_fields", "name": "Kitsune Fields", "tagline": "Speed Lives Here",
     "subtitle": "", "status": "locked", "biome": "autumn", "pos": [338, 100],
     "summit": [338, 100, 60]},
    {"id": "bamboo_valley", "name": "Bamboo Valley", "tagline": "Find Your Flow",
     "subtitle": "", "status": "locked", "biome": "bamboo", "pos": [262, 136],
     "summit": [262, 136, 180]},
    {"id": "tsuki_islands", "name": "Tsuki Islands", "tagline": "Hidden Routes",
     "subtitle": "", "status": "locked", "biome": "tropical", "pos": [298, 46],
     "summit": [300, 47, 160]},
    {"id": "sunset_canyon", "name": "Sunset Canyon", "tagline": "Golden Miles",
     "subtitle": "", "status": "locked", "biome": "desert", "pos": [46, 150],
     "summit": [46, 150, 720]},
    # ---- NEW locked regions (M1) ----------------------------------------------------------
    {"id": "kaze_cape", "name": "Kaze Cape", "tagline": "Where the Wind Turns",
     "subtitle": "", "status": "locked", "biome": "coast", "pos": [16, 104],
     "summit": [18, 106, 90]},
    {"id": "mizuumi_lakes", "name": "Mizuumi Lakes", "tagline": "Mirror Waters",
     "subtitle": "", "status": "locked", "biome": "boreal", "pos": [140, 233],
     "summit": [140, 229, 760]},
    {"id": "hinode_caldera", "name": "Hinode Caldera", "tagline": "First Light",
     "subtitle": "", "status": "locked", "biome": "volcanic", "pos": [360, 158],
     "summit": [360, 166, 1100]},
    {"id": "chaen_terraces", "name": "Chaen Terraces", "tagline": "Steps of Green",
     "subtitle": "", "status": "locked", "biome": "tea", "pos": [292, 112],
     "summit": [292, 112, 540]},
    {"id": "hotaru_delta", "name": "Hotaru Delta", "tagline": "Lanterns on the Water",
     "subtitle": "", "status": "locked", "biome": "paddy", "pos": [113, 139],
     "summit": [113, 139, 5]},
    {"id": "kumo_isle", "name": "Kumo Isle", "tagline": "Island in the Mist",
     "subtitle": "", "status": "locked", "biome": "volcanic", "pos": [60, 40],
     "summit": [58, 41, 920]},
]

# Road network (region id pairs). world_terrain.py routes each link with A* over a slope cost,
# so roads follow valleys, cross ranges at passes and never cross open sea. "bridge" links
# are allowed to cross water in a straight line.
LINKS = [
    ("maple_city", "nagisa_bay"), ("maple_city", "bamboo_valley"),
    ("bamboo_valley", "sakura_pass"), ("sakura_pass", "kage_forest"),
    ("kage_forest", "northern_wilds"), ("sakura_pass", "chaen_terraces"),
    ("chaen_terraces", "kitsune_fields"), ("kitsune_fields", "hinode_caldera"),
    ("kage_forest", "hinode_caldera"), ("maple_city", "fuji_ridge"),
    ("fuji_ridge", "mizuumi_lakes"), ("mizuumi_lakes", "taka_mountains"),
    ("maple_city", "hotaru_delta"), ("hotaru_delta", "azora_highlands"),
    ("azora_highlands", "taka_mountains"), ("hotaru_delta", "shiosai_coast"),
    ("shiosai_coast", "kaze_cape"), ("shiosai_coast", "sunset_canyon"),
    ("sunset_canyon", "taka_mountains"), ("hotaru_delta", "minato_port"),
    ("fuji_ridge", "northern_wilds"), ("bamboo_valley", "chaen_terraces"),
    ("nagisa_bay", "chaen_terraces"),
]
BRIDGES = [
    {"name": "Minato Crossing", "pts": flat([(157, 64), (163, 67), (169, 71)])},
    {"name": "Tsuki Bridge", "pts": flat([(290, 72), (293, 60), (296, 52)])},
]
FERRIES = [
    {"name": "Kumo Ferry", "pts": flat([(148, 58), (120, 48), (92, 40), (72, 36)])},
    {"name": "Tsuki Ferry", "pts": flat([(162, 56), (200, 50), (250, 44), (288, 42)])},
]


def load_route(rel, scale=1.0, rot_deg=0.0):
    """Real course centreline -> km polyline relative to its own centroid (x east, y north),
    rotated CCW by rot_deg and scaled. Also returns the start point in the same frame."""
    import math
    path = os.path.join(ROOT, "Assets", "Environment", rel)
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    pts = [(s["p"][0] / 1000.0, s["p"][2] / 1000.0) for s in d["samples"]]
    step = max(1, len(pts) // 90)
    pts = pts[::step] + [pts[-1]]
    cx = (min(p[0] for p in pts) + max(p[0] for p in pts)) * 0.5
    cy = (min(p[1] for p in pts) + max(p[1] for p in pts)) * 0.5
    length = d["samples"][-1].get("d", 0.0) / 1000.0
    ca, sa = math.cos(math.radians(rot_deg)), math.sin(math.radians(rot_deg))
    out = []
    for x, y in pts:
        x, y = (x - cx) * scale, (y - cy) * scale
        out.append((x * ca - y * sa, x * sa + y * ca))
    return out, length


def main():
    regions = []
    for r in REGIONS:
        r = dict(r)
        src = r.pop("route_src", None)
        scale = r.pop("route_scale", 1.0)
        rot = r.pop("route_rot", 0.0)
        anchor = r.pop("route_anchor", None)
        if src:
            rel, length = load_route(src, scale, rot)
            if anchor:   # place the course START on the anchor instead of centring it
                ox, oy = anchor[0] - rel[0][0], anchor[1] - rel[0][1]
            else:
                ox, oy = r["pos"]
            r["route"] = flat([(ox + x, oy + y) for x, y in rel])
            r["route_km"] = round(length, 2)
            if scale != 1.0:
                r["route_note"] = f"drawn x{scale:g}: the shipped course is {length:.2f} km"
        r.setdefault("route", [])
        regions.append(r)

    layout = {
        "_comment": "MapleRide canonical world layout (M1). Generated by "
                    "tools/blender/worldmap/author_world_layout.py - edit there and re-run. "
                    "Units km (origin SW, +x east, +y north), elevations m. See docs/WORLD_GEOGRAPHY.md.",
        "version": 1,
        "name": "Hondo - the MapleRide world",
        "size_km": [W, H],
        "sea_level_m": 0,
        "snowline_m": SNOWLINE_M,
        "coast": flat(MAIN_COAST),
        "sea_cuts": SEA_CUTS,
        "islands": ISLANDS,
        "ranges": RANGES,
        "volcanoes": VOLCANOES,
        "calderas": CALDERAS,
        "plateaus": PLATEAUS,
        "hill_zones": HILL_ZONES,
        "rivers": RIVERS,
        "lakes": LAKES,
        "biome_zones": BIOME_ZONES,
        "settlements": SETTLEMENTS,
        "regions": regions,
        "links": [{"a": a, "b": b} for a, b in LINKS],
        "bridges": BRIDGES,
        "ferries": FERRIES,
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(layout, f, indent=1)
    print(f"wrote {OUT}: {len(regions)} regions, {len(RIVERS)} rivers, {len(RANGES)} ranges")


if __name__ == "__main__":
    main()
