using System;
using UnityEngine;

/// <summary>
/// The world-map regions of MapleRide, and where each one's pin sits on the world-map art
/// (the v2 Blender relief render at <c>Assets/Resources/UI/MapleRideWorldMap_v2.png</c>; the
/// painted v1 <c>MapleRideWorldMap.png</c> is kept for reference). The geography itself lives
/// in <c>Assets/Resources/World/world_layout.json</c> - see docs/WORLD_GEOGRAPHY.md.
///
/// This is the table the World Map overlay draws and the one <see cref="RegionDirector"/> fast
/// travels through. Regions with a <see cref="Region.BuiltCourseId"/> are rideable *now*; every
/// other pin is drawn locked/greyed so the map reads as the whole world the design describes
/// rather than as a two-item menu.
///
/// EVERYTHING HERE IS PROVISIONAL except the built regions' names and taglines, which are taken
/// from the shipped art: "SAKURA PASS - Petals on the Climb", "SHIOSAI COAST - Ride the Breeze"
/// and "MAPLE CITY - The Heart".
/// </summary>
public static class RegionCatalog
{
    public const string SakuraPass = "sakura_pass";
    public const string ShiosaiCoast = "shiosai_coast";
    public const string MapleCity = "maple_city";
    public const string AzoraHighlands = "azora_highlands";
    public const string TakaMountains = "taka_mountains";
    public const string FujiRidge = "fuji_ridge";
    public const string ShuntaMetro = "shunta_metro";

    /// <summary>
    /// Minato Coast keeps the id the locked world-map pin already used ("minato_port") so the
    /// promotion to a built region does not orphan anything that referenced it.
    /// </summary>
    public const string MinatoCoast = "minato_port";

    /// <summary>Resources path of the world-map art the overlay draws.</summary>
    public const string MapTextureResource = "UI/MapleRideWorldMap_v2";

    /// <summary>
    /// Resources path (TextAsset) of the v2 relief map's projection: exact per-region pin
    /// fractions written by tools/blender/worldmap/build_world_relief.py from the same camera
    /// that rendered the art. When its "image" matches <see cref="MapTextureResource"/> it
    /// overrides the hard-coded <see cref="Region.Pin"/>, so a re-render never strands a pin.
    /// </summary>
    public const string MapProjectionResource = "World/worldmap_v2_projection";

    /// <summary>Resources path (TextAsset) of the canonical world geography (docs/WORLD_GEOGRAPHY.md).</summary>
    public const string WorldLayoutResource = "World/world_layout";

    [Serializable]
    public class Region
    {
        public string Id;
        public string DisplayName;
        public string Tagline;
        /// <summary>Blurb under the region name on the map art (elevation motif).</summary>
        public string Subtitle;
        /// <summary>Pin position as a fraction of the map image (0,0 = bottom left).</summary>
        public Vector2 Pin;
        /// <summary>Course the region drops the rider onto, or null when it is not built yet.</summary>
        public string BuiltCourseId;
        /// <summary>Exact name of the scene object holding this region's environment.</summary>
        public string EnvironmentRoot;

        public bool Unlocked => !string.IsNullOrEmpty(BuiltCourseId);
    }

    // Pin fractions are the v2 relief map's (MapleRideWorldMap_v2.png, World redraw M1-M4) as
    // projected from world_layout.json by the render camera. They are only the FALLBACK: at
    // runtime PinFor() reads the same numbers from the projection JSON the render wrote.
    public static readonly Region[] Regions =
    {
        new Region
        {
            Id = SakuraPass, DisplayName = "Sakura Pass", Tagline = "Petals on the Climb",
            Subtitle = "1,320 m", Pin = new Vector2(0.839f, 0.570f),
            BuiltCourseId = "sakura_circuit",
            EnvironmentRoot = "Sakura Pass Environment",
        },
        new Region
        {
            Id = ShiosaiCoast, DisplayName = "Shiosai Coast", Tagline = "Ride the Breeze",
            Subtitle = "1,380 m", Pin = new Vector2(0.194f, 0.345f),
            BuiltCourseId = "shiosai_breeze",
            EnvironmentRoot = "Shiosai Coast Environment",
        },

        // Maple City - "The Heart", the world's only urban region and its social/sprint hub.
        // Subtitle is the ELEVATION motif the other built rows use; the city is the low-altitude
        // anchor of the map (valley floor ~40 m, Old Town terrace crest ~90 m), which is exactly
        // the contrast the design doc asks for against the high, remote regions.
        new Region
        {
            Id = MapleCity, DisplayName = "Maple City", Tagline = "The Heart",
            Subtitle = "40 m", Pin = new Vector2(0.570f, 0.547f),
            BuiltCourseId = "maple_city_crit",
            EnvironmentRoot = "Maple City Environment",
        },

        // Azora Highlands - "Earn the View", the world's endurance/tempo showcase and its first
        // genuine POINT-TO-POINT: 24 km ridden once, valley floor to the 1,980 m col and down
        // off the far shoulder, with no lap and no return leg. Subtitle is the col elevation,
        // matching the other rows' elevation motif.
        new Region
        {
            Id = AzoraHighlands, DisplayName = "Azora Highlands", Tagline = "Earn the View",
            Subtitle = "1,980 m", Pin = new Vector2(0.339f, 0.640f),
            BuiltCourseId = "azora_ascent",
            EnvironmentRoot = "Azora Highlands Environment",
        },

        // Taka Mountains - "The High Road", the roof of the world and the game's hardest climb:
        // 22 km point-to-point, 1,400 m trailhead to a 2,842 m glacial-lake summit and down the
        // far switchbacks to 2,200 m. 1,442 m of climbing, six sustained kilometres at 11-12%,
        // and the only region above the treeline. It also HOSTS A LEGENDARY: Hyoga, "The North
        // Wall". Subtitle is the summit elevation, matching the other rows' elevation motif.
        new Region
        {
            Id = TakaMountains, DisplayName = "Taka Mountains", Tagline = "The High Road",
            Subtitle = "2,842 m", Pin = new Vector2(0.263f, 0.761f),
            BuiltCourseId = "taka_high_road",
            EnvironmentRoot = "Taka Mountains Environment",
        },

        // Fuji Ridge - "Clouds Above", the sacred one. 13.0 km of strictly MONOTONE climbing
        // from a 776 m cedar base to the 1,776 m Summit Torii Shrine: exactly 1,000 m of gain at
        // a steady 6-8% with a 10% sting at the top, and no descent at all (the doc makes the
        // descent a separate free-ride). The region's hero beat is the Cloudbreak at ~1,200 m,
        // where the road climbs out of a sea of clouds - which is why its finish line is a shrine
        // rather than a timing arch. It also HOSTS A LEGENDARY: Akatsuki, "The Dawn Pilgrim".
        new Region
        {
            Id = FujiRidge, DisplayName = "Fuji Ridge", Tagline = "Clouds Above",
            Subtitle = "1,776 m", Pin = new Vector2(0.538f, 0.765f),
            BuiltCourseId = "fuji_clouds_above",
            EnvironmentRoot = "Fuji Ridge Environment",
        },

        // ---- Minato Coast: the 19.01 km point-to-point across the bay ------------------
        // Promoted from the locked "minato_port" pin rather than added as a new row, so the
        // world map keeps the exact pin position it was authored with and no save data that
        // referenced the old id is orphaned. Five chapters: port city departure, bridge
        // approach, open-ocean crossing, far-shore landfall, inland mountain continuation.
        new Region
        {
            Id = MinatoCoast, DisplayName = "Minato Port", Tagline = "Beyond the Horizon",
            Subtitle = "19.0 km", Pin = new Vector2(0.411f, 0.243f),
            BuiltCourseId = "minato_crossing",
            EnvironmentRoot = "Minato Coast Environment",
        },

        // ---- Nagisa Bay (B5, copilot): tropical resort beach town on the harbour peninsula ----
        // PROVISIONAL pin inside the new_map.png red circle; M3 owns final pin placement.
        new Region
        {
            Id = "nagisa_bay", DisplayName = "Nagisa Bay", Tagline = "Where the City Meets the Sea",
            Subtitle = "16.2 km", Pin = new Vector2(0.538f, 0.399f),
            BuiltCourseId = "nagisa_bay_loop",
            EnvironmentRoot = "Nagisa Bay Environment",
        },

        // ---- Shunta Metro (Claude, 2026-10-02): neon Tokyo, 28.4 km point-to-point -----------
        // Course data + route builder: Assets/Ride/ShuntaMetro, scene Assets/Scenes/ShuntaMetro.unity.
        // PROVISIONAL pin, and drawn LOCKED (no BuiltCourseId) until the course is added to the
        // RouteGraph / streaming manifest; setting BuiltCourseId earlier would unlock a pin that
        // cannot start a ride.
        new Region
        {
            Id = ShuntaMetro, DisplayName = "Shunta Metro", Tagline = "Neon After Dark",
            Subtitle = "28.4 km", Pin = new Vector2(0.628f, 0.318f),
            BuiltCourseId = "shunta_metro",   // shunta-integration: unlocked after ShuntaMetroIntegration.SelfTest PASS
            EnvironmentRoot = "Shunta Metro Environment",
        },

        // ---- not built yet: drawn locked so the world reads whole ----------------------
        new Region { Id = "northern_wilds", DisplayName = "Northern Wilds", Tagline = "Untamed",
                     Pin = new Vector2(0.763f, 0.823f) },
        new Region { Id = "kage_forest", DisplayName = "Kage Forest", Tagline = "Shadows in the Trees",
                     Pin = new Vector2(0.898f, 0.686f) },
        new Region { Id = "kitsune_fields", DisplayName = "Kitsune Fields", Tagline = "Speed Lives Here",
                     Pin = new Vector2(0.909f, 0.364f) },
        new Region { Id = "bamboo_valley", DisplayName = "Bamboo Valley", Tagline = "Find Your Flow",
                     Pin = new Vector2(0.704f, 0.492f) },
        new Region { Id = "tsuki_islands", DisplayName = "Tsuki Islands", Tagline = "Hidden Routes",
                     Pin = new Vector2(0.801f, 0.175f) },
        new Region { Id = "sunset_canyon", DisplayName = "Sunset Canyon", Tagline = "Golden Miles",
                     Pin = new Vector2(0.124f, 0.552f) },

        // ---- World redraw M3 (2026-09-27): regions the vast v2 geography adds ----------
        // Each sits on a real feature of world_layout.json (docs/WORLD_GEOGRAPHY.md).
        new Region { Id = "kaze_cape", DisplayName = "Kaze Cape", Tagline = "Where the Wind Turns",
                     Pin = new Vector2(0.043f, 0.378f) },
        new Region { Id = "mizuumi_lakes", DisplayName = "Mizuumi Lakes", Tagline = "Mirror Waters",
                     Pin = new Vector2(0.376f, 0.848f) },
        new Region { Id = "hinode_caldera", DisplayName = "Hinode Caldera", Tagline = "First Light",
                     Pin = new Vector2(0.968f, 0.574f) },
        new Region { Id = "chaen_terraces", DisplayName = "Chaen Terraces", Tagline = "Steps of Green",
                     Pin = new Vector2(0.785f, 0.415f) },
        new Region { Id = "hotaru_delta", DisplayName = "Hotaru Delta", Tagline = "Lanterns on the Water",
                     Pin = new Vector2(0.304f, 0.500f) },
        new Region { Id = "kumo_isle", DisplayName = "Kumo Isle", Tagline = "Island in the Mist",
                     Pin = new Vector2(0.161f, 0.163f) },
    };

    // ------------------------------------------------------------------ v2 projection pins

    [Serializable] private class ProjectionPin { public string id; public float ground_m; public float u; public float v; }
    [Serializable] private class Projection { public string image; public int width; public int height; public ProjectionPin[] regions; }

    private static System.Collections.Generic.Dictionary<string, Vector2> _projected;

    /// <summary>
    /// Where <paramref name="region"/>'s pin sits on the CURRENT map art: the projection JSON's
    /// exact fraction when it was rendered for <see cref="MapTextureResource"/>, else the
    /// hard-coded <see cref="Region.Pin"/>.
    /// </summary>
    public static Vector2 PinFor(Region region)
    {
        if (region == null) return new Vector2(0.5f, 0.5f);
        if (_projected == null) LoadProjection();
        return _projected.TryGetValue(region.Id, out var p) ? p : region.Pin;
    }

    /// <summary>Drops the cached projection so the next <see cref="PinFor"/> re-reads it (editor tooling).</summary>
    public static void ReloadProjection() => _projected = null;

    private static void LoadProjection()
    {
        _projected = new System.Collections.Generic.Dictionary<string, Vector2>();
        var ta = Resources.Load<TextAsset>(MapProjectionResource);
        if (ta == null) return;
        Projection proj;
        try { proj = JsonUtility.FromJson<Projection>(ta.text); }
        catch (Exception e) { Debug.LogWarning($"[worldmap] bad {MapProjectionResource}: {e.Message}"); return; }
        if (proj == null || proj.regions == null || proj.image != MapTextureResource) return;
        foreach (var r in proj.regions)
            if (!string.IsNullOrEmpty(r.id)) _projected[r.id] = new Vector2(r.u, r.v);
    }

    public static Region Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var r in Regions)
            if (r.Id == id) return r;
        return null;
    }

    /// <summary>Region ids that have a built, rideable course.</summary>
    public static string[] UnlockedIds()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (var r in Regions)
            if (r.Unlocked) list.Add(r.Id);
        return list.ToArray();
    }
}
