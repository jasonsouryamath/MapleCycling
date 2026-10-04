"""
MINATO COAST - kit assembly preview.

Imports the exported bridge FBX modules back in, assembles three spans exactly the way
``MinatoCoastEnvironment`` will place them (same spec numbers, same pivots), and renders
orbit + on-deck views.

This exists because the module export log cannot tell you whether the pieces FIT. A pier cap
that sits 0.6 m too low, an arch that springs inside the deck edge, or a deck bay whose girders
miss the bearings all export perfectly happily. Round-tripping the real FBX (not the in-memory
Blender objects) also proves the import geometry is what Unity will see.

Renders to reference/good_graphics/minato_kit_*.png.
"""

import math
import os
import sys

import bpy
from mathutils import Vector, Euler

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402
from build_minato_bridge import SPEC as K  # noqa: E402
import build_minato_bridge as B  # noqa: E402


SPANS = 3
OUT = os.path.join(S.repo_root(), "reference", "good_graphics")


def imp(filename, lod=0, expect=None):
    """
    Import one module's LOD mesh from its FBX and return a fresh, Blender-native object.

    The FBX is written Y-up with ``bake_space_transform=True`` (that is what gives Unity an
    identity root), so Blender's importer adds a +90 deg X rotation to bring it back to its own
    Z-up world. That rotation must be APPLIED, not zeroed: zeroing it lays the whole arch on its
    side, which renders as a span sweeping vertically across the deck. Unity is Y-up and reads
    the baked data directly, so this correction is a preview-harness concern only.

    ``expect`` is a (x, y, z) Blender-space size checked against the import, so a scale or axis
    regression shows up as a printed number as well as a wrong-looking picture.
    """
    path = os.path.join(M.models_dir(), filename)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    want = f"_LOD{lod}"
    keep = None
    for o in new:
        if want in o.name:
            keep = o
        else:
            bpy.data.objects.remove(o, do_unlink=True)
    if keep is None:
        raise RuntimeError(f"{filename}: no {want} object among {[o.name for o in new]}")

    S.select_only(keep)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    d = keep.dimensions
    flag = ""
    if expect is not None:
        err = max(abs(d[i] - expect[i]) for i in range(3))
        flag = "  OK" if err < 0.35 else f"  <-- MISMATCH (expected {expect}, err {err:.2f} m)"
    print(f"[minato] import {filename:<34s} "
          f"{d.x:7.2f} x {d.y:7.2f} x {d.z:7.2f} m (blender){flag}")
    return keep


def clone(obj, pos_unity, name):
    """
    Place a linked-data copy at a Unity-space position.

    Linked instances on purpose - this is the same data-sharing the brief asks for, and it is
    how the assembly stays cheap enough to render 3 spans of 16 bays and 200 rail posts.

    The hide flags MUST be cleared explicitly: ``obj.copy()`` inherits them from the source,
    and the sources are hidden so the un-placed originals do not sit in shot at the origin.
    Miss this and every render comes out as empty ocean while the log happily reports "Saved"
    for all four frames.
    """
    c = obj.copy()
    c.name = name
    S.link(c)
    c.location = Vector(M.u2b(*pos_unity))
    c.hide_render = False
    c.hide_viewport = False
    return c


def place_member(src, a_unity, b_unity, name, nominal=10.0):
    """
    Place a straight member module between two Unity-space points.

    The module is authored from (0,0,0) to (0,0,nominal) in Unity coords, which ``u2b`` maps to
    Blender -Y, so that is the local axis rotated onto the target direction and scaled.
    This is how a bracing member follows a curving rib without a flat-plate artefact.
    """
    a = Vector(M.u2b(*a_unity))
    b = Vector(M.u2b(*b_unity))
    d = b - a
    L = d.length
    c = src.copy()
    c.name = name
    S.link(c)
    c.location = a
    c.rotation_mode = 'QUATERNION'
    c.rotation_quaternion = Vector((0.0, -1.0, 0.0)).rotation_difference(d.normalized())
    c.scale = (1.0, L / nominal, 1.0)
    c.hide_render = False
    c.hide_viewport = False
    return c


def main():
    S.reset_scene()

    # Expected Blender-space sizes, from the build log of build_minato_bridge.py. These are the
    # numeric half of the check; the renders are the half that actually decides.
    rib_main = imp("Minato_Bridge_ArchRibMain.fbx")
    rib_sec = imp("Minato_Bridge_ArchRibSecondary.fbx")
    hanger = imp("Minato_Bridge_Hanger.fbx")
    brace = imp("Minato_Bridge_CrossBrace.fbx")
    diag = imp("Minato_Bridge_BraceDiagonal.fbx")
    pedestal = imp("Minato_Bridge_ArchPedestal.fbx")
    bay = imp("Minato_Bridge_DeckBay.fbx")
    cap = imp("Minato_Bridge_PierCap.fbx")
    shaft = imp("Minato_Bridge_PierShaft.fbx", expect=(9.2, 7.0, 10.0))
    base = imp("Minato_Bridge_PierBase.fbx")
    post = imp("Minato_Bridge_RailPost.fbx")
    lamp = imp("Minato_Bridge_Lamp.fbx")
    joint = imp("Minato_Bridge_Joint.fbx")
    rail = imp("Minato_Bridge_Guardrail.fbx")
    walk = imp("Minato_Bridge_Walkway.fbx")
    drain = imp("Minato_Bridge_Drainage.fbx")

    srcs = (rib_main, rib_sec, hanger, brace, diag, pedestal, bay, cap, shaft, base,
            post, lamp, joint, rail, walk, drain)
    for o in srcs:
        o.hide_render = True
        o.hide_viewport = True

    span = K["spanLength"]
    total = span * SPANS
    soffit = -(K["deckSlabDepth"] + K["girderDepth"])   # deck-local underside of the girders
    deck_y = 48.0                                        # BRIDGE_DECK_Y from minato_route.py
    sea = 0.0

    parts = []

    # ---- arches, assembled from the SEPARATE modules -------------------------
    # The spec forbids one combined bridge mesh, so a span is now composed here exactly the way
    # MinatoCoastEnvironment will compose it: two mirrored ribs, vertical hangers scaled to
    # reach the rib, cross braces scaled to the rib gap, and four springing pedestals.
    for i in range(SPANS):
        zc = (i + 0.5) * span
        for sgn in (-1.0, +1.0):
            r = clone(rib_main, (0.0, deck_y, zc), f"Rib_{i}_{int(sgn)}")
            if sgn < 0:
                r.scale.x = -1.0            # Unity X mirror == Blender X mirror
            parts.append(r)

            # hangers: vertical, directly under the rib, scaled to reach it
            z = -span * 0.5 + K["hangerSpacing"]
            while z < span * 0.5 - K["hangerSpacing"] * 0.5:
                f = B._arch_f(z)
                y_arch = B._arch_y(f)
                x_arch = B._arch_x(f, sgn)
                if y_arch > 2.0:
                    h = clone(hanger, (x_arch, deck_y, zc + z), f"Hang_{i}_{int(sgn)}_{z:.0f}")
                    h.scale.z = (y_arch - 1.1) / 10.0   # module is 10 m tall along Unity +Y
                    parts.append(h)
                z += K["hangerSpacing"]

        # cross braces between the ribs, plus diagonals that follow the rib curve
        e_crown = K["ribOffsetCrown"] - K["ribWidthCrown"] * 0.25
        step = span / 12.0
        z = -span * 0.5 + span * 0.14
        bi = 0
        while z <= span * 0.5 - span * 0.14 + 0.01:
            f = B._arch_f(z)
            w, _ = B._arch_sec(f)
            e = B._arch_x(f) - w * 0.25
            y = B._arch_y(f)
            br = clone(brace, (0.0, deck_y + y, zc + z), f"Brace_{i}_{bi}")
            br.scale.x = e / e_crown
            parts.append(br)

            z2 = z + step
            if z2 <= span * 0.5 - span * 0.14 + 0.01:
                f2 = B._arch_f(z2)
                w2, _ = B._arch_sec(f2)
                e2 = B._arch_x(f2) - w2 * 0.25
                y2 = B._arch_y(f2)
                parts.append(place_member(
                    diag, (-e, deck_y + y, zc + z), (e2, deck_y + y2, zc + z2),
                    f"Diag_{i}_{bi}a"))
                parts.append(place_member(
                    diag, (e, deck_y + y, zc + z), (-e2, deck_y + y2, zc + z2),
                    f"Diag_{i}_{bi}b"))
            z += step
            bi += 1

        # springing pedestals, one per rib end
        for sgn in (-1.0, +1.0):
            for zs in (-span * 0.5, span * 0.5):
                p = clone(pedestal, (sgn * K["ribOffsetSpring"], deck_y, zc + zs),
                          f"Ped_{i}_{int(sgn)}_{int(zs)}")
                if zs > 0:
                    p.scale.y = -1.0        # face the cutwater the other way
                parts.append(p)


    # ---- deck bays, butted end to end ----------------------------------------
    n_bays = int(round(total / K["deckBayLength"]))
    for i in range(n_bays):
        parts.append(clone(bay, (0.0, deck_y, i * K["deckBayLength"]), f"Bay_{i}"))

    # ---- piers at every span joint -------------------------------------------
    # This stacking logic is deliberately the same one MinatoCoastEnvironment will use: the cap
    # hangs under the deck, tileable shafts run down from it, and the waterline base is placed
    # at a FIXED height relative to sea level so its splash band always straddles the water
    # (the lowest shaft simply buries into it - concrete into concrete).
    for i in range(SPANS + 1):
        z = i * span
        parts.append(clone(cap, (0.0, deck_y + soffit, z), f"Cap_{i}"))
        top = deck_y + soffit - K["pierCapDepth"] - 0.55
        base_top = sea + K["pierBaseDepth"] - 2.0
        n_shaft = max(1, int(math.ceil((top - base_top) / K["pierShaftLength"])))
        for s in range(n_shaft):
            parts.append(clone(shaft, (0.0, top - s * K["pierShaftLength"], z),
                               f"Shaft_{i}_{s}"))
        parts.append(clone(base, (0.0, base_top, z), f"Base_{i}"))

    # ---- railing posts and lamps ---------------------------------------------
    parapet_top = K["parapetHeight"]
    px = K["deckHalfWidth"] - K["footwayWidth"] - K["parapetWidth"] * 0.5
    z = 0.0
    k = 0
    while z <= total:
        for sgn in (-1.0, +1.0):
            parts.append(clone(post, (sgn * px, deck_y + parapet_top, z), f"Post_{k}_{int(sgn)}"))
        z += K["railPostSpacing"]
        k += 1

    z = K["lampSpacing"] * 0.5
    k = 0
    while z <= total:
        sgn = 1.0 if k % 2 == 0 else -1.0
        lp = clone(lamp, (sgn * (px + 0.1), deck_y, z), f"Lamp_{k}")
        if sgn < 0:
            lp.rotation_euler = Euler((0.0, 0.0, math.radians(180.0)))
        z += K["lampSpacing"]
        k += 1

    for i in range(SPANS + 1):
        parts.append(clone(joint, (0.0, deck_y, i * span), f"Joint_{i}"))

    # ---- stand-in carriageway + parapet -------------------------------------
    # The real ones are swept in C#; these exist so the on-deck shot shows what the rider sees
    # and so a mis-sized parapet or a floating rail post is visible NOW rather than in Unity.
    road = M.box("Road", (0.0, deck_y + 0.01, total * 0.5), (8.0, 0.04, total), uv_per_m=0.2)
    M.paint(road, "asphalt")
    parts.append(road)
    for sgn in (-1.0, +1.0):
        sec = [(0.0, 0.0), (K["parapetWidth"], 0.0),
               (K["parapetWidth"] * 0.86, K["parapetHeight"]), (0.0, K["parapetHeight"])]
        sec = [(sr * -sgn, su) for (sr, su) in sec]
        p = M.prism(f"Parapet{int(sgn)}", sec,
                    (sgn * (px - K["parapetWidth"] * 0.5), deck_y, 0.0),
                    (sgn * (px - K["parapetWidth"] * 0.5), deck_y, total),
                    up_hint=(0, 1, 0), uv_per_m=0.3)
        M.paint(p, "concrete")
        parts.append(p)
    ocean = M.box("Ocean", (0.0, sea, total * 0.5), (2200.0, 0.2, 2200.0), uv_per_m=0.02)
    M.paint(ocean, "hull_blue")
    parts.append(ocean)

    # ---- render --------------------------------------------------------------
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x, scn.render.resolution_y = 1600, 900
    scn.render.film_transparent = False
    scn.world = bpy.data.worlds.new("W")
    scn.world.use_nodes = True
    bg = scn.world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.42, 0.56, 0.72, 1.0)
    bg.inputs[1].default_value = 2.2

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = Euler((math.radians(58.0), 0.0, math.radians(-125.0)))
    S.link(sun)

    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    S.link(cam)
    scn.camera = cam

    os.makedirs(OUT, exist_ok=True)

    def shot(tag, eye_unity, look_unity, fov_deg):
        cam.location = Vector(M.u2b(*eye_unity))
        cam_data.angle = math.radians(fov_deg)
        cam_data.clip_end = 20000.0
        d = Vector(M.u2b(*look_unity)) - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(OUT, f"minato_kit_{tag}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[minato] kit render -> minato_kit_{tag}.png")

    mid = total * 0.5
    # Broadside: is the arch profile and pier stack believable at full scale?
    shot("side", (-420.0, deck_y + 60.0, mid), (0.0, deck_y + 12.0, mid), 42.0)
    # Cycling camera on the deck - the concept 02 / 03 viewpoint. THE test that matters.
    shot("deck", (0.0, deck_y + 2.3, 26.0), (0.0, deck_y + 2.6, 300.0), 58.0)
    # Three-quarter hero, like concept 04's springing.
    shot("hero", (-58.0, deck_y + 14.0, -70.0), (0.0, deck_y + 24.0, 240.0), 50.0)
    # From below: do the girders land on the bearings, is the soffit closed?
    shot("under", (-34.0, 16.0, span * 0.5), (0.0, deck_y - 4.0, span * 1.1), 55.0)

    M.save_blend("Minato_Bridge_KitPreview.blend")


if __name__ == "__main__":
    main()
