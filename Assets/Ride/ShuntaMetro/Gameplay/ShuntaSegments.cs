using System;
using System.Collections.Generic;
using UnityEngine;

public enum ShuntaSegmentKind { Climb, Tunnel, Bridge }

[Serializable]
public class ShuntaSegmentDef
{
    public string id = "";
    public string name = "";
    public ShuntaSegmentKind kind;
    public float startKm;
    public float endKm;
    public float LengthKm => endKm - startKm;
}

[Serializable]
public class ShuntaSegmentEntry
{
    public string rider = "";
    public float seconds;
    public bool isPlayer;
}

[Serializable]
public class ShuntaSegmentRecord
{
    public string segmentId = "";
    public List<ShuntaSegmentEntry> entries = new List<ShuntaSegmentEntry>();
}

/// <summary>Builds the timed-segment list from the course JSON: the four climbs, the tunnel zone, the bridge zone.</summary>
public static class ShuntaSegmentTable
{
    /// <summary>Segments sorted by start km (ties: climbs before the tunnel/bridge share of the same span).</summary>
    public static List<ShuntaSegmentDef> Build(ShuntaCourseData course)
    {
        var list = new List<ShuntaSegmentDef>();
        if (course == null) return list;
        if (course.climbs != null)
            foreach (var c in course.climbs)
                list.Add(new ShuntaSegmentDef { id = "climb_" + Slug(c.name), name = c.name, kind = ShuntaSegmentKind.Climb, startKm = c.startKm, endKm = c.endKm });
        if (course.zones != null)
            foreach (var z in course.zones)
            {
                if (z.surface == "tunnel_concrete")
                    list.Add(new ShuntaSegmentDef { id = "tunnel_" + Slug(z.id), name = z.name, kind = ShuntaSegmentKind.Tunnel, startKm = z.startKm, endKm = z.endKm });
                else if (z.surface == "bridge_deck")
                    list.Add(new ShuntaSegmentDef { id = "bridge_" + Slug(z.id), name = z.name + " Sprint", kind = ShuntaSegmentKind.Bridge, startKm = z.startKm, endKm = z.endKm });
            }
        list.Sort((a, b) =>
        {
            int c = a.startKm.CompareTo(b.startKm);
            return c != 0 ? c : ((int)a.kind).CompareTo((int)b.kind);
        });
        return list;
    }

    static string Slug(string s) => (s ?? "").ToLowerInvariant().Replace(' ', '_').Replace('.', '_');
}

/// <summary>
/// Per-segment leaderboards: seeded rival times (deterministic) plus the player's results, kept sorted,
/// serialisable with JsonUtility (<see cref="ToJson"/>/<see cref="FromJson"/>) for PlayerPrefs or a file.
/// </summary>
[Serializable]
public sealed class ShuntaSegmentBoard
{
    public const int MaxEntries = 10;
    public List<ShuntaSegmentRecord> records = new List<ShuntaSegmentRecord>();

    static readonly string[] Rivals =
    {
        "Kuro", "Hana", "Ren", "Sora", "Mika", "Daichi", "Yuki", "Haru", "Aoi", "Takumi"
    };

    /// <summary>Seeds rival times: pace from 44 kph (fastest) down to ~30 kph, scaled per segment kind. Deterministic.</summary>
    public static ShuntaSegmentBoard Seeded(List<ShuntaSegmentDef> segs)
    {
        var b = new ShuntaSegmentBoard();
        foreach (var s in segs)
        {
            var rec = new ShuntaSegmentRecord { segmentId = s.id };
            float kindFactor = s.kind == ShuntaSegmentKind.Climb ? 0.72f : s.kind == ShuntaSegmentKind.Tunnel ? 1.08f : 1f;
            for (int i = 0; i < Rivals.Length; i++)
            {
                float kph = (44f - i * 1.5f) * kindFactor;
                rec.entries.Add(new ShuntaSegmentEntry { rider = Rivals[i], seconds = s.LengthKm / kph * 3600f });
            }
            b.records.Add(rec);
        }
        return b;
    }

    public ShuntaSegmentRecord Get(string segmentId)
    {
        foreach (var r in records) if (r.segmentId == segmentId) return r;
        return null;
    }

    /// <summary>Adds a player result; returns the 1-based rank achieved (0 if it missed the top list or segment unknown).</summary>
    public int Submit(string segmentId, string rider, float seconds)
    {
        var r = Get(segmentId);
        if (r == null) { r = new ShuntaSegmentRecord { segmentId = segmentId }; records.Add(r); }
        var e = new ShuntaSegmentEntry { rider = rider, seconds = seconds, isPlayer = true };
        r.entries.Add(e);
        r.entries.Sort((a, b) => a.seconds.CompareTo(b.seconds));
        int rank = r.entries.IndexOf(e) + 1;
        if (r.entries.Count > MaxEntries) r.entries.RemoveRange(MaxEntries, r.entries.Count - MaxEntries);
        return rank <= MaxEntries ? rank : 0;
    }

    public float? PersonalBest(string segmentId)
    {
        var r = Get(segmentId); float? best = null;
        if (r != null) foreach (var e in r.entries) if (e.isPlayer && (best == null || e.seconds < best)) best = e.seconds;
        return best;
    }

    public string ToJson() => JsonUtility.ToJson(this);
    public static ShuntaSegmentBoard FromJson(string json)
    {
        try { return JsonUtility.FromJson<ShuntaSegmentBoard>(json) ?? new ShuntaSegmentBoard(); }
        catch { return new ShuntaSegmentBoard(); }
    }
}

/// <summary>
/// Detects segment entry/exit from rider km and the running clock. Call <see cref="Tick"/> every frame with the
/// rider's km and elapsed ride seconds; results fire via <see cref="Finished"/>. Segments may overlap
/// (the Rainbow Bridge climb and the bridge sprint run together).
/// </summary>
public sealed class ShuntaSegmentTimer
{
    readonly List<ShuntaSegmentDef> _segs;
    readonly float[] _startTime;
    readonly bool[] _active;
    float _lastKm = float.NegativeInfinity;

    public event Action<ShuntaSegmentDef> Started;
    public event Action<ShuntaSegmentDef, float> Finished;   // def, seconds

    public ShuntaSegmentTimer(List<ShuntaSegmentDef> segs)
    {
        _segs = segs;
        _startTime = new float[segs.Count];
        _active = new bool[segs.Count];
    }

    public bool IsActive(int i) => _active[i];

    public void Tick(float km, float elapsedSeconds)
    {
        for (int i = 0; i < _segs.Count; i++)
        {
            var s = _segs[i];
            if (!_active[i])
            {
                // crossing the start line forward (works even if a frame skips past it)
                if (_lastKm < s.startKm && km >= s.startKm && km < s.endKm)
                { _active[i] = true; _startTime[i] = elapsedSeconds; Started?.Invoke(s); }
            }
            else if (km >= s.endKm)
            {
                _active[i] = false;
                Finished?.Invoke(s, elapsedSeconds - _startTime[i]);
            }
            else if (km < s.startKm) _active[i] = false; // rode backwards out: cancel
        }
        _lastKm = km;
    }
}
