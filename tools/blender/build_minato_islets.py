"""
MINATO COAST - sea islets, rebuilt (2026-09-25).

Replaces the islet() family in build_minato_props.py, which stacked 34 axis-aligned boxes and
16 square cone "trees": at Unity scale 3.5-12.5x that read as brown wood-grain sandcastles and
the user could not tell what they were. These are single sculpted rock masses (noise-displaced
ico sphere, flattened below the waterline, steepened into low sea cliffs) under a rounded
canopy of foliage clumps - the wooded islands of the Minato concept boards.

Palette slots stay "rock" and "foliage" so MinatoCoastEnvironment.RetintBySlot maps them.
Canopy clumps carry clump-height UVs for Minato_Foliage_Gradient.png (dark below, sunlit crown).
Origin at sea level (y = 0). Writes Minato_Sea_IsletA/B.fbx over the old boxes.

Run:  blender -b --factory-startup -P build_minato_islets.py [-- --preview out.png]
"""

import math
import os
import sys

import bpy
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402


def fbm(p, octaves=4):
    total, amp, freq = 0.0, 1.0, 1.0
    for _ in range(octaves):
        total += amp * noise.noise(p * freq)
        amp *= 0.5
        freq *= 2.07
    return total


def rock_mass(tag, radius, height, seed):
    """One sculpted rock body. Blender Z is up (the u2b mirror is irrelevant for a
    rotationally random scatter prop)."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=5, radius=1.0, location=(0, 0, 0))
    o = bpy.context.active_object
    o.name = f"islet{tag}_rock"
    off = Vector((seed * 1.37, seed * 0.71, seed * 2.13))
    elong = 0.72 + 0.2 * ((seed * 0.618) % 1.0)
    for v in o.data.vertices:
        n = v.co.normalized()
        # Low-frequency lobes give a coastline with bays and points, not a disc.
        lobe = 1.0 + 0.28 * fbm(Vector((n.x, n.y, 0.0)) * 1.3 + off, 3)
        x, y = n.x * radius * lobe, n.y * radius * elong * lobe
        # Dome with a flattened, slightly asymmetric crown; sides steepen into sea cliffs.
        z = n.z
        if z > 0.0:
            h = height * (z ** 0.75) * (1.0 + 0.35 * fbm(Vector((n.x, n.y, 0.3)) * 2.1 + off, 3))
            cliff = 1.0 + 0.22 * max(0.0, 1.0 - z * 3.0)   # pushes the waterline out: a cliff foot
            x, y = x * cliff, y * cliff
        else:
            h = max(-6.0, z * 12.0)                           # flat foot hidden under the sea
        p = Vector((x, y, h))
        # Mid/high frequency rock breakup along the surface normal direction.
        d = fbm(p * (3.2 / radius) + off, 4) * radius * 0.07 + noise.noise(p * 0.22 + off) * 1.1
        v.co = p + Vector((n.x, n.y, max(n.z, 0.0))).normalized() * d
    # Planar-per-metre UVs so Taka granite texels stay ~square at any islet size.
    uv = o.data.uv_layers.active or o.data.uv_layers.new(name="UVMap")
    for loop in o.data.loops:
        c = o.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = ((c.x * 0.6 + c.y * 0.8) / 14.0, c.z / 14.0)
    for poly in o.data.polygons:
        poly.use_smooth = True
    M.paint(o, "rock")
    return o


def clump(name, centre, r, squash_z, seed_vec):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=centre)
    o = bpy.context.active_object
    o.name = name
    for v in o.data.vertices:
        n = v.co.normalized()
        v.co += n * r * 0.18 * noise.noise(n * 2.4 + seed_vec)
    uv = o.data.uv_layers.active or o.data.uv_layers.new(name="UVMap")
    u0 = (abs(seed_vec.x) * 7.13 + abs(seed_vec.z) * 3.7) % 1.0
    for loop in o.data.loops:
        z = o.data.vertices[loop.vertex_index].co.z
        uv.data[loop.index].uv = (u0, 0.25 + 0.75 * max(0.0, min(1.0, (z / r + 1.0) * 0.5)))
    o.scale = (1.0, 1.0, squash_z)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for poly in o.data.polygons:
        poly.use_smooth = True
    return o


def islet(tag, radius, height, seed, clumps):
    rock = rock_mass(tag, radius, height, seed)
    r = M.rng(seed)
    mw = rock.matrix_world
    # Canopy only on gentle upper ground - bare rock cliffs stay visible at the waterline.
    cands = [(mw @ v.co, v.normal) for v in rock.data.vertices
             if v.co.z > height * 0.22 and v.normal.z > 0.45]
    foliage = []
    scale = radius / 46.0
    for i in range(clumps):
        if not cands:
            break
        p, n = cands[int(r.integers(len(cands)))]
        cr = float(r.uniform(5.5, 10.5)) * scale ** 0.6
        centre = p + Vector((0, 0, cr * 0.35))
        foliage.append(clump(f"islet{tag}_c{i}", centre, cr, float(r.uniform(0.62, 0.85)),
                             Vector((p.x * 0.31, p.y * 0.57, i * 1.3))))
    # A few boulders at the waterline break the rock/sea line.
    boulders = []
    for i in range(10):
        a = float(r.uniform(0, math.tau))
        br = float(r.uniform(1.8, 4.2)) * scale ** 0.5
        d = radius * float(r.uniform(0.95, 1.2))
        b = clump(f"islet{tag}_b{i}", (math.cos(a) * d, math.sin(a) * d * 0.8, br * 0.1), br, 0.6,
                  Vector((a, i, 3.0)))
        boulders.append(b)
    M.paint(boulders, "rock")
    M.paint(foliage, "foliage")
    for b in boulders:   # boulders use the granite planar UVs too
        uv = b.data.uv_layers.active
        for loop in b.data.loops:
            c = b.data.vertices[loop.vertex_index].co
            uv.data[loop.index].uv = ((c.x * 0.6 + c.y * 0.8) / 14.0, c.z / 14.0)
    return S.join([rock] + boulders + foliage, f"Minato_Sea_Islet{tag}_LOD0")


CATALOG = [
    (lambda: islet("A", 46.0, 30.0, 81, 90), "Minato_Sea_IsletA.fbx", (0.30, 0.10)),
    (lambda: islet("B", 88.0, 56.0, 83, 200), "Minato_Sea_IsletB.fbx", (0.25, 0.08)),
]


def preview(path):
    """Render both islets side by side over a flat sea so they can be judged before export."""
    S.reset_scene()
    a = islet("A", 46.0, 30.0, 81, 90)
    b = islet("B", 88.0, 56.0, 83, 200)
    b.location = (170, 60, 0)
    M.mat("rock").node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.44, 0.43, 1)
    M.mat("foliage").node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.13, 0.30, 0.11, 1)
    bpy.ops.mesh.primitive_plane_add(size=3000, location=(0, 0, 0.5))
    sea = bpy.context.active_object
    sm = S.pbr_material("sea", base_color=(0.04, 0.22, 0.32, 1), roughness=0.15)
    S.assign_material(sea, sm)
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(62), 0, math.radians(-40)))
    bpy.context.active_object.data.energy = 4.0
    bpy.ops.object.camera_add(location=(-170, -330, 75))
    cam = bpy.context.active_object
    cam.data.lens = 38
    d = Vector((90, 40, 15)) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc = bpy.context.scene
    sc.camera = cam
    sc.world = bpy.data.worlds.new("w")
    sc.world.use_nodes = True
    sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.70, 0.90, 1)
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x, sc.render.resolution_y = 1280, 640
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--preview" in argv:
        preview(argv[argv.index("--preview") + 1])
        return
    for builder, filename, ratios in CATALOG:
        S.reset_scene()
        obj = builder()
        M.stat(obj, filename.replace(".fbx", ""))
        exports = [obj]
        base = obj.name.replace("_LOD0", "")
        for i, r in enumerate(ratios):
            exports.append(M.decimated_copy(obj, f"{base}_LOD{i + 1}", r))
        M.export_fbx(exports, filename)
    print("[minato] islets complete")


if __name__ == "__main__":
    main()
