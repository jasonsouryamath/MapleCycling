using UnityEngine;

/// <summary>Physics-facing modifiers at one point of the Shunta Metro course.</summary>
public struct ShuntaModifiers
{
    /// <summary>Corner/traction grip multiplier (1 = dry). Wet zones are lower.</summary>
    public float gripMultiplier;
    /// <summary>Draft strength multiplier (tunnel 1.35). Scale the draft saving, not the factor itself.</summary>
    public float draftMultiplier;
    /// <summary>0..1 how tightly NPC packs bunch up (0.5 = neutral). Tunnels/underpasses are tighter.</summary>
    public float packTightness;

    public static ShuntaModifiers Neutral => new ShuntaModifiers { gripMultiplier = 1f, draftMultiplier = 1f, packTightness = 0.5f };
}

/// <summary>
/// Per-km grip / draft / pack-tightness for Shunta Metro, read from the zone table in the course JSON.
/// Values are blended with a smoothstep across each zone boundary (<see cref="BlendMetres"/> total width,
/// centred on the boundary), so <see cref="Sample"/> is continuous: no jumps when the rider crosses a zone.
/// Pure data/logic, no scene dependency.
/// </summary>
public sealed class ShuntaZoneModifiers
{
    /// <summary>Total transition width centred on each zone boundary, metres.</summary>
    public float BlendMetres = 300f;

    readonly float[] _end;        // zone endKm
    readonly float[] _grip;
    readonly float[] _draft;
    readonly float[] _pack;

    public int ZoneCount => _end.Length;

    public ShuntaZoneModifiers(ShuntaCourseData course, float blendMetres = 300f)
    {
        BlendMetres = Mathf.Max(0f, blendMetres);
        var z = course != null ? course.zones : null;
        int n = z != null ? z.Length : 0;
        _end = new float[n]; _grip = new float[n]; _draft = new float[n]; _pack = new float[n];
        for (int i = 0; i < n; i++)
        {
            _end[i] = z[i].endKm;
            _grip[i] = z[i].grip <= 0f ? 1f : z[i].grip;
            _draft[i] = z[i].draftMultiplier <= 0f ? 1f : z[i].draftMultiplier;
            _pack[i] = PackFromDraft(_draft[i]);
        }
    }

    /// <summary>Loads the course from Resources (null-safe: returns neutral modifiers if missing).</summary>
    public static ShuntaZoneModifiers FromResources() => new ShuntaZoneModifiers(ShuntaCourseData.Load());

    /// <summary>Pack tightness derived from the zone's draft multiplier: 1.0 -> 0.5, 1.35 -> ~1, 0.9 -> ~0.35.</summary>
    public static float PackFromDraft(float draft) => Mathf.Clamp01(0.5f + (draft - 1f) * 1.45f);

    public ShuntaModifiers Sample(float km)
    {
        int n = _end.Length;
        if (n == 0) return ShuntaModifiers.Neutral;
        float half = BlendMetres * 0.0005f; // half-width in km

        int zi = n - 1;
        for (int i = 0; i < n; i++) if (km <= _end[i]) { zi = i; break; }

        float g = _grip[zi], d = _draft[zi], p = _pack[zi];

        // Blend toward the NEXT zone near this zone's end, and from the PREVIOUS zone near its start.
        // (Zones are >> BlendMetres long, so at most one boundary is active at a time.)
        if (zi < n - 1 && half > 0f)
        {
            float b = _end[zi];
            if (km > b - half) Blend(ref g, ref d, ref p, zi, zi + 1, (km - (b - half)) / (2f * half));
        }
        if (zi > 0 && half > 0f)
        {
            float b = _end[zi - 1];
            if (km < b + half) Blend(ref g, ref d, ref p, zi - 1, zi, (km - (b - half)) / (2f * half));
        }
        return new ShuntaModifiers { gripMultiplier = g, draftMultiplier = d, packTightness = p };
    }

    void Blend(ref float g, ref float d, ref float p, int a, int b, float t)
    {
        t = Mathf.Clamp01(t); t = t * t * (3f - 2f * t);
        g = Mathf.Lerp(_grip[a], _grip[b], t);
        d = Mathf.Lerp(_draft[a], _draft[b], t);
        p = Mathf.Lerp(_pack[a], _pack[b], t);
    }

    /// <summary>
    /// Convenience for the draft hook: the drag multiplier to assign to <c>CyclingPhysics.dragMultiplier</c>.
    /// <paramref name="draftFactor01"/> is the traffic draft factor (0..1) and
    /// <paramref name="baseSaving"/> the region's existing max drag saving (e.g. draftDragSaving).
    /// </summary>
    public static float DragMultiplier(float draftFactor01, float baseSaving, ShuntaModifiers m)
        => 1f - Mathf.Clamp01(baseSaving * m.draftMultiplier) * Mathf.Clamp01(draftFactor01);
}
