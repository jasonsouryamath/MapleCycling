using System.Text;

/// <summary>
/// One place that turns route data names into the names the rider reads, so the banner, the
/// route bar, the next-checkpoint line and the map pin can never disagree again.
///
/// WHY THIS EXISTS. Minato showed three names for one place: the route bar said
/// "MINATO COAST" (course name minus its tagline), the banner said "MINATO COAST CROSSING" (the
/// route-graph SEGMENT name, which on a single-segment course is just an internal name for the
/// whole route), and checkpoints came through as raw ids ("MC_PORT_CITY_DEPARTURE").
/// </summary>
public static class RouteNames
{
    /// <summary>"Minato Coast - Beyond the Horizon" -> "Minato Coast": the place, not the tagline.</summary>
    public static string Place(string courseDisplayName)
    {
        if (string.IsNullOrEmpty(courseDisplayName)) return "";
        int cut = courseDisplayName.IndexOf(" - ", System.StringComparison.Ordinal);
        return (cut > 0 ? courseDisplayName.Substring(0, cut) : courseDisplayName).Trim();
    }

    private static RouteCourse _singleFor;
    private static bool _single;

    /// <summary>
    /// The section label for a segment. A course that runs on ONE segment has no sections, so
    /// the segment's graph name is replaced with the course's place name.
    /// </summary>
    public static string Section(RouteCourse course, string segmentName)
    {
        if (course == null) return segmentName ?? "";
        if (!ReferenceEquals(course, _singleFor))
        {
            _singleFor = course;
            _single = true;
            var seg = course.SegmentOf;
            for (int i = 1; i < seg.Length; i++)
                if (seg[i] != seg[0]) { _single = false; break; }
        }
        return _single || string.IsNullOrEmpty(segmentName) ? Place(course.DisplayName) : segmentName;
    }

    /// <summary>
    /// "MC_PORT_CITY_DEPARTURE" -> "Port City Departure". Authored display names (anything
    /// with lower-case letters or no underscore) pass through untouched.
    /// </summary>
    public static string Checkpoint(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('_') < 0) return raw ?? "";
        bool anyLower = false;
        foreach (char c in raw) if (char.IsLower(c)) { anyLower = true; break; }
        if (anyLower) return MixedId(raw);

        var parts = raw.Split(new[] { '_' }, System.StringSplitOptions.RemoveEmptyEntries);
        int start = parts.Length > 2 && parts[0].Length <= 3 ? 1 : 0;   // "MC_" region prefix
        var sb = new StringBuilder(raw.Length);
        for (int i = start; i < parts.Length; i++)
        {
            string w = parts[i].ToLowerInvariant();
            bool minor = i > start && (w == "the" || w == "and" || w == "of" || w == "to" || w == "a");
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(minor ? w : char.ToUpperInvariant(w[0]) + w.Substring(1));
        }
        return sb.ToString();
    }

    /// <summary>
    /// "SC_KM_060_GatewaySummit" -> "Gateway Summit": drop the all-caps / numeric id tokens
    /// before the first word (region code, KM marker, distance), keep later ones ("Hairpin 3"),
    /// and split the CamelCase words.
    /// </summary>
    private static string MixedId(string raw)
    {
        var parts = raw.Split(new[] { '_' }, System.StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(raw.Length + 8);
        foreach (var p in parts)
        {
            bool idToken = true;
            foreach (char c in p) if (char.IsLower(c)) { idToken = false; break; }
            if (idToken && sb.Length == 0) continue;          // leading id tokens only
            if (idToken) { sb.Append(' ').Append(p); continue; } // keep a trailing "3"
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                bool wordStart = i > 0 && char.IsUpper(c) && (char.IsLower(p[i - 1]) ||
                                 (i + 1 < p.Length && char.IsLower(p[i + 1]) && char.IsUpper(p[i - 1])));
                if (sb.Length > 0 && (i == 0 || wordStart)) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
        }
        return sb.Length > 0 ? sb.ToString() : raw;
    }
}
