using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public sealed partial class ShuntaNeoTokyo
{
    public const float StreetlightSpacingKm = .03f;

    void BuildStreetInfrastructure(float start, float end, ShuntaLookKit.MeshBag bag, Block block)
    {
        // Continuous strips share the route's vertices and elevation, rather than flat boxes
        // spaced along a hill. Keep the carriageway clear and preserve bridge/tunnel volumes.
        for (float km = start; km < end - .000001f; km += .005f)
        {
            float next = Mathf.Min(km + .005f, end);
            var z = route.Course.ZoneAtKm((km + next) * .5f);
            bool street = z.index == 1 || z.index == 2 || z.index == 7 || z.index == 8 || z.index >= 11;
            for (int side = -1; side <= 1; side += 2)
            {
                float edge = route.roadWidth * .5f;
                Strip(bag, pavement, km, next, side, edge, edge + 2.8f, .10f);
                if (z.index!=4 && z.index!=10) Strip(bag, concrete, km, next, side, edge + 2.8f, GroundReach((km+next)*.5f, side), street?-.12f:-12.12f);
            }
        }
        // Use a course-wide grid, so streaming chunk boundaries never double or omit lamps.
        int first = Mathf.CeilToInt((start - .000001f) / StreetlightSpacingKm);
        for (int i = first; i * StreetlightSpacingKm < end - .000001f; i++)
        {
            float km = i * StreetlightSpacingKm;
            Vector3 road = route.PositionAtKm(km), f = route.TangentAtKm(km);
            f.y = 0f; f.Normalize();
            Vector3 right = new Vector3(f.z, 0f, -f.x);
            bool tunnel = route.Course.ZoneAtKm(km).index == 4;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 baseP = road + right * side * (route.roadWidth * .5f + .85f);
                float height = tunnel ? 4f : 7f;
                Vector3 top = baseP + Vector3.up * height;
                Vector3 lamp = top - right * side * 1.5f;
                var q = Quaternion.LookRotation(f);
                bag.Box(concrete, baseP + Vector3.up * .17f, q, new Vector3(.44f,.34f,.44f));
                bag.Beam(metal, baseP, top, .16f);
                bag.Beam(metal, top, lamp, .12f);
                bag.Box(metal, lamp, q, new Vector3(.72f,.18f,1.15f));
                bag.Box(streetLamp, lamp - Vector3.up * .105f, q, new Vector3(.82f,.05f,1.25f));
                var go = new GameObject("Shunta streetlight " + i + " " + side) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(block.root.transform, false);
                go.transform.position = lamp - Vector3.up * .15f;
                go.transform.rotation = Quaternion.LookRotation(Vector3.down, f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot; light.spotAngle = 150f;
                light.range = 42f; light.color = new Color(1f,.84f,.62f);       // 2026-10-04: brighter, longer-reaching streetlights (user: more illumination)
                light.shadows = LightShadows.None;
                var hd = go.GetComponent<HDAdditionalLightData>() ?? go.AddComponent<HDAdditionalLightData>();
                hd.SetIntensity(650000f, LightUnit.Lumen); hd.affectsVolumetric = false;      // volumetric scattering off: per-light froxel cost, and it was a suspect in the 2026-10-04 out-of-memory crash
                block.lights.Add(light); block.streetlights++;
            }
        }
    }

    void ValidateRoadMaterials()
    {
        if (route == null) return;
        var ribbon = route.transform.Find("Shunta Road Ribbon");
        if (ribbon == null) return;
        var renderer = ribbon.GetComponent<MeshRenderer>();
        if (renderer == null) return;
        // Procedural HDRP materials need the same pass/keyword setup as the inspector.
        foreach (var material in renderer.sharedMaterials)
            if (material != null && material.shader.name == "HDRP/Lit") HDMaterial.ValidateMaterial(material);
    }

    float GroundReach(float km, int side)
    {
        float edge = route.roadWidth*.5f+2.8f;
        float reach = 335f;
        if (route.Course.ZoneAtKm(km).index >= 11 && side > 0) return edge + 4f;
        var p = route.PositionAtKm(km);
        var f0 = route.TangentAtKm(Mathf.Max(0,km-.005f)); f0.y=0; f0.Normalize();
        var f1 = route.TangentAtKm(km+.005f); f1.y=0; f1.Normalize();
        float turn = Mathf.Atan2(Vector3.Cross(f0,f1).y,Vector3.Dot(f0,f1));
        if (turn*side > .0001f)
        {
            float chord = Vector3.Distance(route.PositionAtKm(Mathf.Max(0,km-.005f)),route.PositionAtKm(km+.005f));
            reach = Mathf.Min(reach,chord/Mathf.Abs(turn)*.6f);
        }
        // Don't raise land over another route section at bends or parallel streets.
        for (int i=0;i<route.Positions.Length;i+=4)
        {
            if (Mathf.Abs(route.Km[i]-km)<.15f) continue;
            var other = route.transform.TransformPoint(route.Positions[i]);
            var delta=other-p; var right=new Vector3(f0.z,0,-f0.x);
            float lateral=Vector3.Dot(delta,right)*side;
            // Only another ribbon across this side strip can limit its land. Collinear road
            // samples ahead/behind are not parallel streets and must not collapse city width.
            if(lateral>edge && Mathf.Abs(Vector3.Dot(delta,f0))<15f) reach=Mathf.Min(reach,lateral*.45f);
        }
        return Mathf.Max(edge+.5f,reach);
    }

    void Strip(ShuntaLookKit.MeshBag bag, Material mat, float km, float next, int side, float inner, float outer, float lift)
    {
        Vector3 Edge(float k, float lateral)
        {
            var p = route.PositionAtKm(k); var f = route.TangentAtKm(k);
            f.y = 0f; f.Normalize();
            return p + new Vector3(f.z,0,-f.x) * side * lateral + Vector3.up * lift;
        }
        var a = Edge(km,inner); var b = Edge(next,inner);
        var c = Edge(next,outer); var d = Edge(km,outer);
        if (side > 0)
        {
            bag.Tri(mat,a,b,c,Vector3.up,Vector3.up,Vector3.up);
            bag.Tri(mat,a,c,d,Vector3.up,Vector3.up,Vector3.up);
        }
        else
        {
            bag.Tri(mat,a,c,b,Vector3.up,Vector3.up,Vector3.up);
            bag.Tri(mat,a,d,c,Vector3.up,Vector3.up,Vector3.up);
        }
    }
}


