"""
MAPLE CITY EAST - "Harbour & Market District": the second district that doubles Maple City (user, 2026-10-03).

Publishes ``Assets/Environment/MapleCity/MapleRouteEast.json`` as its OWN closed 5.0 km loop (segment "maplecity_east",
course "maple_city_east_crit") using the exact same generator as the shipped circuit (maple_city_route.py), which is
reused unchanged apart from one overridable output filename. The shipped ``MapleRoute.json`` is never touched (verified
byte-identical by tools/blender/maple_city_east_check.py).

    python tools/blender/maple_city_east_route.py

LAYOUT
------
The original loop is centred on x = -3000 (bounds x -3655..-2271, z -874..855). The district sits 1650 m east of it at
x = -1350 (bounds ~ -2000..-680): ~230 m clear of the old Gate Plaza side and ~365 m clear of Sakura Pass (x >= -285), which
leaves room for a connector avenue between the two loops (a later Unity-side step: route graph link + environment build).

CHARACTER (different from the Old Town / canal circuit)
  0 Harbour Gate Plaza   wide start/finish plaza on the water
  1 Quay head            long straight along the fish quays
  2 Fish Market corner   tight cobbled turn between stalls
  3 Warehouse Row        second tight corner, brick warehouses
  4 Hill Park ramp       the climb: a short, steep rise to the park lookout
  5 Lantern Avenue       wide fast sweep back down
  6-8 Ferry Road         three long bends along the ferry terminal back to the gate
PROVISIONAL: proportions are art/gameplay tuning, like the shipped circuit.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import maple_city_route as base  # noqa: E402

# ----------------------------------------------------------------------------- identity
base.REGION_ID = "maple_city"
base.SEGMENT_ID = "maplecity_east"
base.SEGMENT_NAME = "Maple City East Circuit"
base.COURSE_ID = "maple_city_east_crit"
base.COURSE_NAME = "Maple City East Crit"
base.OUT_FILE = "MapleRouteEast.json"

# ----------------------------------------------------------------------------- placement
base.ORIGIN_X = -1350.0
base.ORIGIN_Z = 0.0

# ----------------------------------------------------------------------------- plan (draft metres, rescaled to 5.0 km)
# Mirrored relative to the Old Town circuit so the two loops read as different districts from the air.
base.CIRCUIT = [
    (   0.0,    0.0, 130.0),   # Harbour Gate Plaza
    ( -40.0,  820.0,  90.0),   # Quay head
    (-300.0, 1100.0,  45.0),   # Fish Market corner
    (-760.0, 1110.0,  40.0),   # Warehouse Row
    (-1000.0, 820.0,  36.0),   # Hill Park ramp corner
    (-1080.0, 380.0, 140.0),   # Lantern Avenue sweep
    (-900.0, -200.0, 105.0),   # Ferry Road I
    (-500.0, -480.0, 115.0),   # Ferry Road II
    (-140.0, -380.0, 125.0),   # Ferry Road III
]
base.START_CORNER = 0

# ----------------------------------------------------------------------------- profile
# Flat harbour, one real climb to Hill Park, a fast descent, then a flat run-in (mean grade is removed to close the loop).
base.GRADE_PHASES = [
    (0.000, 0.300, 0.0, 0.8),
    (0.300, 0.440, 5.0, 8.0),
    (0.440, 0.560, -5.5, -5.5),
    (0.560, 1.000, -0.6, 0.0),
]
base.CHECKPOINTS = [
    (0.00, "Harbour Gate Plaza"),
    (0.14, "Quay Sprint"),
    (0.34, "Fish Market"),
    (0.46, "Hill Park Lookout"),
    (0.72, "Lantern Avenue"),
    (0.92, "Ferry Terminal Finish"),
]

if __name__ == "__main__":
    base.write_route_json()
