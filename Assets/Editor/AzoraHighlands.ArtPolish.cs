using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Azora village facade and open-fell readability pass (2026-09-30).</summary>
public static partial class AzoraHighlandsEnvironment
{
    private static Material _polishTrim, _polishFlower, _polishSign;

    private static void BuildAzoraArtPolish(Transform root, AzoraRoute route)
    {
        var facadeRoot = new GameObject("Azora Authored Facades").transform;
        facadeRoot.SetParent(root, false);
        _polishTrim = CelMaterial("Azora_Polish_FacadeTrim", new Color(0.23f, 0.13f, 0.07f), 0.08f, 0.04f, 0.10f, shade: GroundShade);
        _polishFlower = CelMaterial("Azora_Polish_FlowerBox", new Color(0.82f, 0.12f, 0.16f), 0.12f, 0.06f, 0.25f);
        _polishSign = CelMaterial("Azora_Polish_Sign", new Color(0.92f, 0.74f, 0.36f), 0.18f, 0.10f, 0.24f, shade: GroundShade);

        // The source chalet meshes already own the walls/roofs. These authored, shallow
        // attachments make the road-facing elevations legible without replacing their LODs.
        var bins = new Bins();
        for (int vi = 0; vi < VillageCentres.Length; vi++)
        {
            float dc = VillageCentres[vi];
            for (float d = dc - VillageHalfM; d <= dc + VillageHalfM; d += 26f)
            for (int side = -1; side <= 1; side += 2)
            {
                float seed = H01(d + side * 3.1f, vi + 4.7f);
                if (Mathf.Abs(d - dc) < 30f && side > 0) continue;
                if (Mathf.Abs(d - dc) < 24f && side < 0) continue;
                int ti = Mathf.Min(2, (int)(H01(d, 5.5f) * 3f));
                var ty = VillageChaletTypes[ti];
                float wall = VillageFrontWallM + seed * 1.5f;
                int i = route.IndexAt(d);
                DFoot(route, i, side * (wall + ty.D), out var centre);
                var s = route.SideFlat(i);
                var fwd = -new Vector3(s.x, 0f, s.z) * side;
                fwd = Quaternion.Euler(0f, (H01(d, 8.1f) - 0.5f) * 8f, 0f) * fwd;
                fwd.Normalize();
                var right = Vector3.Cross(Vector3.up, fwd).normalized;
                float datum = GH(centre.x, centre.z) + 0.15f;
                var front = centre + fwd * (ty.D + 0.10f);
                // Ground floor: door plus a pair of glazed windows. Upper floor: two windows.
                FacadeWindow(bins[_window], front + right * (-ty.W * 0.48f) + Vector3.up * 1.30f, right, fwd, 0.62f, 0.78f);
                FacadeWindow(bins[_window], front + right * (ty.W * 0.48f) + Vector3.up * 1.30f, right, fwd, 0.62f, 0.78f);
                FacadeWindow(bins[_window], front + right * (-ty.W * 0.52f) + Vector3.up * 3.00f, right, fwd, 0.58f, 0.70f);
                FacadeWindow(bins[_window], front + right * (ty.W * 0.52f) + Vector3.up * 3.00f, right, fwd, 0.58f, 0.70f);
                BoxB(bins[_door], front + right * (seed - 0.5f) * ty.W * 0.55f + Vector3.up * 0.82f + fwd * 0.035f,
                     right * 0.34f, fwd * 0.055f, 1.64f, true);
                // Strong dark timber crossbars and a flower box create authored silhouettes at 30 m.
                for (int level = 0; level < 2; level++)
                {
                    float y = level == 0 ? 1.30f : 3.00f;
                    FacadeTrim(bins[_polishTrim], front + Vector3.up * y, right, fwd, ty.W * 0.94f, 0.055f, 0.055f);
                }
                BoxB(bins[_polishFlower], front + fwd * 0.11f + Vector3.up * 0.92f + right * (-ty.W * 0.48f),
                     right * 0.44f, fwd * 0.13f, 0.13f, true);
                BoxB(bins[_polishFlower], front + fwd * 0.11f + Vector3.up * 0.92f + right * (ty.W * 0.48f),
                     right * 0.44f, fwd * 0.13f, 0.13f, true);
                if (seed > 0.58f)
                    BoxB(bins[_polishSign], front + fwd * 0.12f + Vector3.up * 2.15f, right * 0.70f, fwd * 0.035f, 0.22f, true);
                _ = datum; // retain explicit ground datum for future facade footing tuning.
            }
        }
        var meshRoot = new GameObject("Azora Facade Details (batched)").transform;
        meshRoot.SetParent(facadeRoot, false);
        bins.Flush(meshRoot, "Azora_FacadeDetails");

        // Terrain tiles should receive lighting but never cast kilometer-scale self-shadows;
        // those cascades were the source of the black void read in aerial diagnostics.
        var fell = root.Find("Azora Fell");
        if (fell != null)
            foreach (var mr in fell.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        var ground = GroundMaterial();
        ground.SetFloat("_GrassScale", 62f);
        ground.SetFloat("_ScreeScale", 73f);
        ground.SetFloat("_RockScale", 57f);
        ground.SetFloat("_MacroVariation", 1.0f);
        ground.SetFloat("_MossStrength", 0.30f);
        ground.SetFloat("_MossScale", 91f);
        ground.SetFloat("_ShadowAmbient", 0.68f);
        EditorUtility.SetDirty(ground);
        Debug.Log("[azora] art polish: authored facade details batched; fell self-shadow disabled; macro terrain breakup widened.");
    }

    private static void FacadeWindow(Bin b, Vector3 centre, Vector3 right, Vector3 fwd, float width, float height)
    {
        BoxB(b, centre + fwd * 0.035f, right * (width * 0.5f), fwd * 0.045f, height, true);
        BoxB(b, centre + fwd * 0.095f + Vector3.up * 0.02f, right * 0.035f, fwd * 0.025f, height * 0.94f, true);
        BoxB(b, centre + fwd * 0.098f + Vector3.up * 0.02f, right * (width * 0.46f), fwd * 0.020f, 0.035f, true);
    }

    private static void FacadeTrim(Bin b, Vector3 centre, Vector3 right, Vector3 fwd, float width, float depth, float height)
    {
        BoxB(b, centre + fwd * 0.09f, right * (width * 0.5f), fwd * depth, height, true);
    }
}
