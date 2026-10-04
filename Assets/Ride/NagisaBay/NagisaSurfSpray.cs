using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NB1 - spray and mist where Nagisa Bay waves break: wind-blown spray off feathering crests anywhere near the camera
/// (found by sampling <see cref="NagisaSurf.BreakAt"/>), and big bursts when a wave hits the rocks / breakwater
/// (the "burst points" baked by the Surf stage). Particles are drawn with the kit's MapleRide/HDRP/Snowflake soft-disc
/// shader, so they take sun / ambient / fog like the rest of the region. Cheap: a few dozen field samples per frame
/// and one pooled ParticleSystem.
/// </summary>
[DisallowMultipleComponent]
public sealed class NagisaSurfSpray : MonoBehaviour
{
    public Material sprayMaterial;
    public Vector3[] burstPoints = new Vector3[0];     // rock / breakwater contact points (world, y = sea level)
    public float activeRadius = 170f;
    public int samplesPerFrame = 48;
    public int maxParticles = 2500;

    private ParticleSystem _ps;
    private float[] _burstCooldown;
    private readonly System.Random _rng = new System.Random(8841);

    private void Awake()
    {
        var go = new GameObject("NagisaSurf Spray Particles");
        go.transform.SetParent(transform, false);
        _ps = go.AddComponent<ParticleSystem>();
        var main = _ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1.6f; main.startSpeed = 0f; main.startSize = 1f;
        main.maxParticles = maxParticles;
        main.gravityModifier = 0.12f;
        var em = _ps.emission; em.enabled = false;
        var sh = _ps.shape; sh.enabled = false;
        var col = _ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = _ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.5f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        if (sprayMaterial == null) { var s = Shader.Find("MapleRide/HDRP/Snowflake"); if (s != null) sprayMaterial = new Material(s); }
        r.sharedMaterial = sprayMaterial;
        _ps.Play();
        _burstCooldown = new float[burstPoints != null ? burstPoints.Length : 0];
    }

    private float R() => (float)_rng.NextDouble();

    private void Emit(Vector3 pos, Vector3 vel, float size, float life)
    {
        var p = new ParticleSystem.EmitParams { position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = new Color(1f, 1f, 1f, 0.8f) };
        _ps.Emit(p, 1);
    }

    private void Update()
    {
        if (!NagisaSurf.HasField) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        float t = NagisaSurf.Now;

        // wind-blown spray off feathering crests near the camera
        for (int i = 0; i < samplesPerFrame; i++)
        {
            float ang = R() * Mathf.PI * 2f, rad = Mathf.Sqrt(R()) * activeRadius;
            float x = c.x + Mathf.Cos(ang) * rad, z = c.z + Mathf.Sin(ang) * rad;
            float off = NagisaSurf.OffshoreDistance(x, z);
            if (off < 2f || off > 150f) continue;
            float lip = NagisaSurf.BreakAt(x, z, t, out _);
            if (lip < 0.55f || R() > lip) continue;
            float y = NagisaSurf.HeightAt(x, z, t);
            Vector2 wind = -NagisaSurf.ShoreDir(x, z);   // spray is blown off the lip, offshore
            int n = 1 + (int)(R() * 3f);
            for (int k = 0; k < n; k++)
            {
                var v = new Vector3(wind.x * (1.5f + R() * 2.5f), 0.6f + R() * 1.6f, wind.y * (1.5f + R() * 2.5f));
                Emit(new Vector3(x + (R() - 0.5f) * 2f, y + 0.2f, z + (R() - 0.5f) * 2f), v, 0.5f + R() * 1.1f, 1.0f + R() * 1.4f);
            }
        }

        // big bursts where a wave hits a rock / breakwater / jetty
        for (int b = 0; b < burstPoints.Length; b++)
        {
            Vector3 bp = burstPoints[b];
            if ((bp - c).sqrMagnitude > 260f * 260f) continue;
            if (_burstCooldown[b] > t) continue;
            // sample the water a few metres seaward of the contact point, where the wave arrives from
            Vector2 sea = -NagisaSurf.ShoreDir(bp.x, bp.z);
            float lip = NagisaSurf.BreakAt(bp.x + sea.x * 5f, bp.z + sea.y * 5f, t, out _);
            float tr = NagisaSurf.Sample(bp.x + sea.x * 5f, bp.z + sea.y * 5f, t).whitewater;
            float strength = Mathf.Max(lip, tr * 0.9f);
            if (strength < 0.45f) continue;
            _burstCooldown[b] = t + 3.5f + R() * 3f;
            int n = 18 + (int)(R() * 22f);
            for (int k = 0; k < n; k++)
            {
                float up = 3f + R() * 7f * strength;
                var v = new Vector3((R() - 0.5f) * 3f - sea.x * 1.5f, up, (R() - 0.5f) * 3f - sea.y * 1.5f);
                Emit(bp + new Vector3((R() - 0.5f) * 3f, 0.3f, (R() - 0.5f) * 3f), v, 0.9f + R() * 1.8f, 1.6f + R() * 1.6f);
            }
        }
    }
}
