"""Build three sculpted Nagisa Bay rock scatter variants with vertex-painted moss.

Each exported GLB contains LOD0/1/2 objects.  The red vertex-colour channel is a
deliberate art mask: moss gathers in low, noisy pockets and on shaded undersides,
while exposed ridges stay bare.  Run with Blender's background Python:
    blender -b --factory-startup -P tools/blender/build_nagisa_rocks_scatter.py
"""

import math
import os
import sys

import bpy
from mathutils import Vector, noise


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Models")


def fbm(p, octaves=4):
    total, amp, freq = 0.0, 1.0, 1.0
    for _ in range(octaves):
        total += amp * noise.noise_vector(p * freq).x
        amp *= 0.5
        freq *= 2.05
    return total


def material():
    m = bpy.data.materials.new("Nagisa_RockMoss")
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.37, 0.34, 0.29, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.86
    # Keep COLOR_0 live in the glTF export contract. Unity's RockMoss shader reads
    # the same channel directly; this harmless roughness modulation also prevents
    # Blender's exporter from dropping the authored vertex colour attribute.
    vc = m.node_tree.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Moss"
    m.node_tree.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    return m


def sculpt(tag, radius, height, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=1.0, location=(0, 0, 0))
    obj = bpy.context.object
    obj.name = tag + "_LOD0"
    off = Vector((seed * 1.37, seed * 0.71, seed * 2.13))
    elong = 0.72 + 0.18 * ((seed * 0.618) % 1.0)
    moss = []
    for v in obj.data.vertices:
        n = v.co.normalized()
        lobe = 1.0 + 0.24 * fbm(Vector((n.x, n.y, 0.0)) * 1.25 + off, 3)
        x, y = n.x * radius * lobe, n.y * radius * elong * lobe
        z = n.z
        if z > -0.05:
            crown = height * max(0.0, z) ** 0.72
            crown *= 1.0 + 0.25 * fbm(Vector((n.x, n.y, 0.4)) * 2.0 + off, 3)
            cliff = 1.0 + 0.18 * max(0.0, 1.0 - z * 3.0)
            x, y = x * cliff, y * cliff
            base_h = crown
        else:
            base_h = z * min(height * 0.35, 2.2)
        p = Vector((x, y, base_h))
        d = fbm(p * (3.0 / max(radius, 0.1)) + off, 4) * radius * 0.075
        d += noise.noise_vector(p * 0.28 + off).x * radius * 0.025
        v.co = p + Vector((n.x, n.y, max(n.z, 0.0))).normalized() * d
        # Crevice/underside mask.  High points and steep ridges are intentionally bare.
        cavity = 0.5 - 0.5 * noise.noise_vector(p * (1.6 / max(radius, 0.1)) + off).x
        underside = max(0.0, 0.42 - n.z) / 0.42
        low = max(0.0, 0.42 - n.z) * 0.9
        exposed = max(0.0, n.z - 0.62) * 1.25
        mask = max(0.0, min(1.0, 0.10 + cavity * 0.42 + underside * 0.52 + low - exposed))
        moss.append(mask)

    uv = obj.data.uv_layers.new(name="UVMap") if not obj.data.uv_layers else obj.data.uv_layers.active
    for loop in obj.data.loops:
        c = obj.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = ((c.x * 0.62 + c.y * 0.78) / max(radius * 2.1, 1.0),
                                  c.z / max(height * 1.5, 1.0) + 0.5)
    col = obj.data.color_attributes.new(name="Moss", type='FLOAT_COLOR', domain='POINT')
    for i, value in enumerate(moss):
        col.data[i].color = (value, value, value, 1.0)
    obj.data.materials.append(material())
    for poly in obj.data.polygons:
        poly.use_smooth = True

    # LOD copies preserve the same vertex-colour art direction.
    lods = [obj]
    for level, ratio in enumerate((0.34, 0.10), 1):
        lod = obj.copy()
        lod.data = obj.data.copy()
        lod.name = tag + "_LOD" + str(level)
        bpy.context.collection.objects.link(lod)
        mod = lod.modifiers.new("LOD%d_Decimate" % level, 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = ratio
        bpy.context.view_layer.objects.active = lod
        lod.select_set(True)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        lod.select_set(False)
        lods.append(lod)
    return lods


def export_variant(tag, radius, height, seed, filename):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    lods = sculpt(tag, radius, height, seed)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in lods:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = lods[0]
    path = os.path.join(OUT, filename)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True,
                               export_apply=True, export_materials='EXPORT',
                               export_vertex_color='MATERIAL', export_attributes=True,
                               export_lights=False, export_cameras=False)
    print("[nagisa-rocks] %s: %d objects -> %s" % (tag, len(lods), path))


def main():
    export_variant("Nagisa_RockMoss_A", 1.25, 1.55, 41, "Nagisa_RockMoss_A.glb")
    export_variant("Nagisa_RockMoss_B", 2.55, 3.45, 47, "Nagisa_RockMoss_B.glb")
    export_variant("Nagisa_RockMoss_C", 4.6, 6.4, 53, "Nagisa_RockMoss_C.glb")
    print("[nagisa-rocks] complete: 3 variants, vertex mask=Moss.r")


if __name__ == "__main__":
    main()
