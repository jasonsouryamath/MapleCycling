"""
build_all.py - one entry point for the whole Sakura Pass asset pipeline.

Runs, in dependency order:
    1. textures    seamless PBR set + atlases + ridge gradient
    2. route       publishes SakuraRoute.json for Unity to place props against
    3. terrain     valley terrain + distant ranges
    4. road        carriageway, markings, guardrail
    5. props       torii, lantern, retaining wall, rocks, ledge, marker, chevron,
                   summit sign, banner pole, overlook railing
    6. flora       sakura trees, sapling, grass, fern, conifers
    7. tunnel      the cliffside bore, route-baked at 300-342 m
    8. expand      timber guardrail, lake village, lake torii, hillside sign, petal drifts

Each stage runs in its own freshly reset scene. Stages can be selected:

    blender -b -P build_all.py                       # everything
    blender -b -P build_all.py -- flora props        # just those two
    blender -b -P build_all.py -- --skip textures    # everything but textures
"""

import os
import sys
import time
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sakura_lib as S


def stage_textures():
    import build_textures
    build_textures.build_all_textures()


def stage_route():
    import sakura_route
    sakura_route.write_route_json()


def stage_terrain():
    import build_terrain
    build_terrain.main()


def stage_road():
    import build_road
    build_road.main()


def stage_props():
    import build_props
    build_props.main()


def stage_flora():
    import build_flora
    build_flora.main()


def stage_tunnel():
    import build_tunnel
    build_tunnel.main()


def stage_expand():
    import build_expand
    build_expand.main()


STAGES = [
    ("textures", stage_textures),
    ("route", stage_route),
    ("terrain", stage_terrain),
    ("road", stage_road),
    ("props", stage_props),
    ("flora", stage_flora),
    ("tunnel", stage_tunnel),
    ("expand", stage_expand),
]


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    names = [n for n, _ in STAGES]

    skip = []
    want = []
    i = 0
    while i < len(argv):
        if argv[i] == "--skip":
            skip.append(argv[i + 1]); i += 2
        else:
            want.append(argv[i]); i += 1

    selected = want or names
    unknown = [n for n in selected + skip if n not in names]
    if unknown:
        raise SystemExit(f"unknown stage(s): {unknown}; valid: {names}")
    return [n for n in selected if n not in skip]


def main():
    selected = parse_args()
    print(f"[sakura] build_all: {', '.join(selected)}")

    failed = []
    for name, fn in STAGES:
        if name not in selected:
            continue
        print(f"\n[sakura] ===== stage: {name} =====")
        t0 = time.time()
        try:
            S.reset_scene()
            fn()
            print(f"[sakura] stage {name} ok ({time.time() - t0:.1f}s)")
        except Exception:
            traceback.print_exc()
            failed.append(name)
            print(f"[sakura] stage {name} FAILED ({time.time() - t0:.1f}s)")

    if failed:
        print(f"\n[sakura] build_all finished WITH FAILURES: {failed}")
        raise SystemExit(1)
    print("\n[sakura] build_all complete - all stages ok.")


if __name__ == "__main__":
    main()
