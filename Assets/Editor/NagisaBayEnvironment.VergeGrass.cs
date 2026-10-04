// Nagisa Bay VERGE GRASS (Claude, 2026-10-01; user: "the grass needs to be more realistic and finer detail").
// The old verge was a smeared ground texture plus one 3-triangle card every 32 m (VisualPolish). This stage
// scatters dense clumps of real grass blades (tools/textures/make_nagisa_grass_cards.py: a 4x2 atlas of
// 34-60 individually drawn, tapered, curved blades with dark bases and sun-bleached tips) along both verges.
//   * each clump is two crossed cards (8 vertices, 4 triangles), 0.45-0.9 m wide, varied height / lean;
//   * three bands: short lawn right at the shoulder, taller tufts further out, a few tall wild stems;
//   * placed only on grass / forest ground classes (never sand, rock, sea) with CanPlace's road keep-out,
//     pads, slopes under ~42 deg, and not on the beach's sea side;
//   * merged into one mesh per 48 m of route per side, with a LODGroup that culls beyond ~150 m;
//   * foliage shader (wind sway, alpha cutout), shadows off, three tints for colour variety.
// Stage order 45 (after VisualPolish 44). Single-stage run: MR_NB_STAGES=VergeGrass + ApplyOverhaul.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const float GrassChunkM = 48f;
    private const float GrassStepM = 1.15f;
    private const int GrassPerStep = 12;
    private const float GrassMinOffsetM = 4.7f, GrassMaxOffsetM = 14.0f;
    private const float GrassCullTransition = 0.30f;     // LODGroup relative height: ~150 m for a 48 m chunk

    [NagisaStage(45, "VergeGrass")]
    private static void BuildVergeGrass(Transform group)
    {
        EnsureRouteGround();
        const string texPath = NagisaTex + "/Nagisa_GrassCards.png";
        var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (ti == null) { Debug.LogError("[nagisa-grass] Nagisa_GrassCards.png missing - run tools/textures/make_nagisa_grass_cards.py"); return; }
        if (!ti.alphaIsTransparency || !ti.mipMapsPreserveCoverage || ti.wrapMode != TextureWrapMode.Clamp)
        {
            ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.4f;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var shader = Shader.Find("MapleRide/HDRP/Foliage");
        if (shader == null) { Debug.LogError("[nagisa-grass] MapleRide/HDRP/Foliage shader missing."); return; }

        var tints = new[] { new Color(0.66f, 0.80f, 0.54f), new Color(0.80f, 0.86f, 0.60f), new Color(0.58f, 0.74f, 0.48f) };
        var mats = new Material[tints.Length];
        for (int m = 0; m < mats.Length; m++)
        {
            mats[m] = LoadOrCreate($"Nagisa_VergeGrass_{m}", "MapleRide/HDRP/Foliage");
            mats[m].SetTexture("_MainTex", tex);
            mats[m].SetColor("_Color", tints[m]);
            mats[m].SetFloat("_Cutoff", 0.40f);
            mats[m].SetFloat("_Translucency", 0.22f);
            mats[m].SetFloat("_RimStrength", 0.04f);
            mats[m].SetFloat("_WindStrength", 0.14f);
            mats[m].enableInstancing = true;
            EditorUtility.SetDirty(mats[m]);
        }

        var rng = new System.Random(45045);
        float R() => (float)rng.NextDouble();
        int chunks = 0; long clumps = 0, verts = 0;
        int nChunks = Mathf.CeilToInt(_route.Length / GrassChunkM);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            for (int c = 0; c < nChunks; c++)
            {
                float d0 = c * GrassChunkM, d1 = d0 + GrassChunkM;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (float d = d0; d < d1; d += GrassStepM)
                {
                    int i = _route.IndexAt(d);
                    if (_route.OnBridge(i)) continue;
                    var p = _route.Position[i];
                    var sideDir = _route.SideFlat(i) * sign;
                    var tan = Vector3.Cross(Vector3.up, sideDir);
                    bool beach = d >= _route.BeachStartM && d <= _route.BeachEndM;
                    if (beach && Vector3.Dot(sideDir, _route.SideFlat(i) * SeaSign(i)) > 0.5f) continue;   // sand / sea side
                    for (int k = 0; k < GrassPerStep; k++)
                    {
                        float u = R();
                        float o = GrassMinOffsetM + (GrassMaxOffsetM - GrassMinOffsetM) * Mathf.Pow(u, 1.55f);
                        var q = p + sideDir * o + tan * ((R() - 0.5f) * GrassStepM * 1.4f);
                        // CanPlace's corridor keep-out (7.1 m) would leave the whole shoulder strip bare; the carriageway
                        // + shoulder end at RoadHalfWidth + ShoulderWidth, so grass may start just beyond it.
                        if (_ground.Coast(q.x, q.z) < 7f) continue;
                        float y = _ground.Height(q.x, q.z);
                        if (y < 0.25f) continue;
                        if (_route.PlanDistance(q.x, q.z, out _) < RoadHalfWidth + ShoulderWidth + 0.9f) continue;
                        int cls = _ground.ClassAt(q.x, q.z);
                        if (cls == 0 || cls == 1) continue;
                        if (InAnyPad(q.x, q.z, 2.5f)) continue;
                        float sx = _ground.Height(q.x + 1.2f, q.z) - _ground.Height(q.x - 1.2f, q.z);
                        float sz = _ground.Height(q.x, q.z + 1.2f) - _ground.Height(q.x, q.z - 1.2f);
                        float slope = Mathf.Sqrt(sx * sx + sz * sz) / 2.4f;
                        if (slope > 1.25f || (cls == 5 && slope > 0.6f)) continue;                // > ~51 deg, or bare rock
                        // thin out with distance from the road, keep the shoulder lush
                        if (R() > Mathf.Lerp(1f, 0.40f, Mathf.InverseLerp(GrassMinOffsetM, GrassMaxOffsetM, o))) continue;
                        float band = R();
                        float w = band < 0.55f ? Mathf.Lerp(0.45f, 0.7f, R()) : band < 0.93f ? Mathf.Lerp(0.6f, 0.9f, R()) : Mathf.Lerp(0.8f, 1.1f, R());
                        float h = band < 0.55f ? Mathf.Lerp(0.22f, 0.38f, R()) : band < 0.93f ? Mathf.Lerp(0.34f, 0.55f, R()) : Mathf.Lerp(0.55f, 0.85f, R());
                        AddClump(v, n, uv, t, new Vector3(q.x, y - 0.02f, q.z), R() * 180f, w, h, R() * 14f - 7f,
                                 rng.Next(8), R());
                        clumps++;
                    }
                }
                if (v.Count == 0) continue;
                var mesh = new Mesh { name = $"Nagisa_VergeGrass_{(sign < 0 ? "L" : "R")}_{c:D3}" };
                mesh.indexFormat = IndexFormat.UInt16;
                if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(group, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mats[(c + (sign < 0 ? 1 : 0)) % mats.Length];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(GrassCullTransition, new Renderer[] { mr }) });
                lod.fadeMode = LODFadeMode.None;
                lod.RecalculateBounds();
                string path = $"{MeshDir}/{mesh.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                chunks++; verts += v.Count;
            }
        }
        Debug.Log($"[nagisa-grass] {clumps:N0} blade clumps in {chunks} chunks ({verts:N0} verts); verge lawn + tufts + wild stems.");
    }

    /// <summary>Two crossed, slightly leaning cards using one of the 8 atlas cells (4 x 2).</summary>
    private static void AddClump(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 at,
                                 float yawDeg, float width, float height, float leanDeg, int cell, float seed)
    {
        float cx = (cell & 3) * 0.25f, cy = (cell >> 2) * 0.5f;
        float inset = 0.004f;
        for (int card = 0; card < 2; card++)
        {
            var rot = Quaternion.Euler(0f, yawDeg + card * 90f + (seed - 0.5f) * 18f, 0f);
            var right = rot * Vector3.right * (width * 0.5f);
            var lean = rot * Vector3.forward * (Mathf.Tan(leanDeg * Mathf.Deg2Rad) * height * (card == 0 ? 1f : -1f));
            int b = v.Count;
            v.Add(at - right); v.Add(at + right);
            v.Add(at + right + Vector3.up * height + lean); v.Add(at - right + Vector3.up * height + lean);
            var nrm = new Vector3(0f, 1f, 0f);
            n.Add(nrm); n.Add(nrm); n.Add(nrm); n.Add(nrm);
            uv.Add(new Vector2(cx + inset, cy)); uv.Add(new Vector2(cx + 0.25f - inset, cy));
            uv.Add(new Vector2(cx + 0.25f - inset, cy + 0.5f - inset)); uv.Add(new Vector2(cx + inset, cy + 0.5f - inset));
            t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
        }
    }
}
