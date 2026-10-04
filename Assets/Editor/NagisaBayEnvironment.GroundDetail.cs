// Nagisa Bay grass-detail overlay for the art-critic placeholder pass.
// Deliberately owns only new overlay geometry/material/texture; NB9's ground material and flora
// files remain reserved. The overlay breaks the road-side verge into irregular soil, lawn and
// sun-bleached patches while preserving the route corridor and existing terrain.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    // Keep the overlay on the outer verge edge: the shared CanPlace corridor reserves the
    // carriageway + shoulder + 3 m, so the inner edge must sit just beyond 7.1 m.
    // Three staggered strips cover the authored verge instead of reading as one
    // narrow decal at the road seam.  They stay outside the road bench and stop
    // before the promenade keep-out, so NB4/NB6 occupants remain untouched.
    private static readonly Vector2[] DetailBands =
    {
        new Vector2(4.24f, 5.18f),
        new Vector2(5.34f, 6.22f),
        new Vector2(6.36f, 7.28f)
    };
    private const float DetailLiftM = 0.018f;

    [NagisaStage(42, "GroundDetail")]
    private static void BuildGroundDetail(Transform group)
    {
        EnsureRouteGround();
        var tex = Tex(NagisaTex, "Nagisa_Ground_Splat.png");
        if (tex == null)
        {
            Debug.LogWarning("[nagisa-ground] missing Nagisa_Ground_Splat.png; run make_nagisa_ground_splat.py first.");
            return;
        }

        var shader = Shader.Find("MapleRide/HDRP/Foliage");
        if (shader == null) { Debug.LogError("[nagisa-ground] MapleRide/HDRP/Foliage shader missing."); return; }
        var mat = LoadOrCreate("Nagisa_GroundDetail", "MapleRide/HDRP/Foliage");
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_Color", Color.white);
        mat.SetFloat("_Cutoff", 0.36f);
        mat.SetFloat("_Translucency", 0f);
        mat.SetFloat("_RimStrength", 0.02f);
        mat.SetFloat("_WindStrength", 0f);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);

        int sides = 0, quads = 0;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int[] previous = { -1, -1, -1 };
            for (int i = 0; i < _route.Count; i++)
            {
                if (_route.OnBridge(i)) { previous[0] = previous[1] = previous[2] = -1; continue; }
                var p = _route.Position[i];
                var side = _route.SideFlat(i) * sign;
                for (int band = 0; band < DetailBands.Length; band++)
                {
                    var span = DetailBands[band];
                    var a = p + side * span.x;
                    var b = p + side * span.y;
                    if (!CanPlace((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f, 0.01f, out _, 2f))
                    { previous[band] = -1; continue; }
                    float ya = _ground.Height(a.x, a.z) + DetailLiftM;
                    float yb = _ground.Height(b.x, b.z) + DetailLiftM;
                    int baseIndex = verts.Count;
                    verts.Add(new Vector3(a.x, ya, a.z));
                    verts.Add(new Vector3(b.x, yb, b.z));

                    // Pick one of the four irregular atlas cells per route
                    // segment/band.  This breaks repetition at ride speed while
                    // keeping the texture's alpha edge doing the actual breakup.
                    int cell = Mathf.Abs((i / 11) + band * 3 + (sign < 0 ? 1 : 0)) & 3;
                    float cellX = (cell & 1) * 0.5f;
                    float cellY = (cell >> 1) * 0.5f;
                    float u = Mathf.Repeat(_route.Distance[i] / 5.5f, 1f) * 0.5f + cellX;
                    uvs.Add(new Vector2(u, cellY));
                    uvs.Add(new Vector2(u, cellY + 0.5f));
                    if (previous[band] >= 0)
                    {
                        tris.Add(previous[band]); tris.Add(baseIndex); tris.Add(baseIndex + 1);
                        tris.Add(previous[band]); tris.Add(baseIndex + 1); tris.Add(previous[band] + 1);
                        quads++;
                    }
                    previous[band] = baseIndex;
                }
            }
            if (tris.Count == 0) continue;
            var mesh = Finish($"Nagisa_GroundDetail_{(sign < 0 ? "L" : "R")}", verts, uvs, tris);
            var go = AddMesh(group, mesh.name, mesh, mat, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            sides++;
        }
        Debug.Log($"[nagisa-ground] irregular verge overlay: {quads} quads across {sides} sides; road corridor untouched.");
    }

}
