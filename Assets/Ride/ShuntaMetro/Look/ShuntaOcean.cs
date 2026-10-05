using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Vast photoreal night ocean for Shunta's coast, built on the HDRP water system (FFT waves, foam, fresnel, refraction, sun/moon glints,
/// screen-space reflections of the city neon). Replaces the old emissive box. A finite quad (not Infinite) so the sea never floods the
/// city: it starts at the coast and runs away from the city to the horizon. Needs supportWater on the active HDRP asset (High has it).
/// </summary>
public static class ShuntaOcean
{
    public const float SeaY = ShuntaLookScenery.GroundY + 1.0f;     // just above the ground slab so it shows over it

    /// <summary>coast = bounds of the route's seaward end; cityCentre = middle of the whole route (the sea runs away from it).</summary>
    public static WaterSurface Build(Transform parent, Bounds coast, Vector3 cityCentre)
    {
        var dir = new Vector3(coast.center.x - cityCentre.x, 0f, coast.center.z - cityCentre.z);
        if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
        dir.Normalize();
        float reach = 0.5f * Mathf.Max(coast.size.x, coast.size.z) + 1500f;      // near edge: just inland of the last road section
        const float length = 18000f, width = 24000f;
        var go = new GameObject("Shunta Ocean");
        go.transform.SetParent(parent, false);
        var centre = new Vector3(coast.center.x, SeaY, coast.center.z) - dir * reach + dir * (length * 0.5f);
        go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(dir, Vector3.up));
        go.transform.localScale = new Vector3(width, 1f, length);

        var w = go.AddComponent<WaterSurface>();
        w.surfaceType = WaterSurfaceType.OceanSeaLake;
        w.geometryType = WaterGeometryType.Quad;
        w.timeMultiplier = 0.8f;
        w.repetitionSize = 900f;
        w.largeWindSpeed = 26f; w.largeChaos = 0.85f; w.largeOrientationValue = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        w.largeBand0Multiplier = 1f; w.largeBand1Multiplier = 0.9f;
        w.ripples = true; w.ripplesWindSpeed = 9f; w.ripplesChaos = 0.8f;
        w.startSmoothness = 0.93f; w.endSmoothness = 0.78f; w.smoothnessFadeStart = 150f; w.smoothnessFadeDistance = 2500f;
        w.tessellation = true; w.maxTessellationFactor = 3f;
        // deep moonlit harbour water: dark blue-green body, teal back-scatter, no seabed so no caustics
        w.refractionColor = new Color(0.02f, 0.10f, 0.17f); w.maxRefractionDistance = 1.2f; w.absorptionDistance = 9f;
        w.scatteringColor = new Color(0.01f, 0.13f, 0.17f);
        w.ambientScattering = 0.25f; w.heightScattering = 0.18f; w.displacementScattering = 0.35f;
        w.directLightTipScattering = 0.5f; w.directLightBodyScattering = 0.35f;
        w.caustics = false;
        w.foam = true; w.foamColor = new Color(0.82f, 0.88f, 0.95f); w.foamTextureTiling = 0.25f; w.foamPersistenceMultiplier = 0.5f;
        return w;
    }
}
