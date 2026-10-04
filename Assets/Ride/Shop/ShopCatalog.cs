using System.Collections.Generic;
using UnityEngine;

public enum ShopCategory { Jersey, Bibs, Helmet, Wheels, BikeComputer, Frame }

/// <summary>One purchasable item. Colours are sRGB hex; apparel recolours Kuro's kit.</summary>
public sealed class ShopItem
{
    public string Id, Brand, Name, Blurb;
    public ShopCategory Category;
    public int Price;               // MapleCoins
    public Color Primary = Color.white, Trim = Color.black;
    public float CdaScale = 1f;     // wheels / frames: aero effect on CyclingPhysics.cdA
    public int ComputerTier;        // bike computers: 1 = Pro HUD, 2 = Elite HUD
    /// <summary>
    /// Resources path of the item's authored asset (2026-09-26): a textured garment design atlas
    /// (Shop/Kits/Kit_*, painted by design_assets/3d/kuro/make_shop_kits.py) for jerseys and bibs,
    /// or a frameset prefab (Shop/Frames/FrameKit_*, built by ShopFrameKitBuilder) for frames.
    /// Empty = the original two-colour recolour.
    /// </summary>
    public string Asset;
    public bool Textured => !string.IsNullOrEmpty(Asset);
}

/// <summary>
/// The Maple Row catalogue. Brands are FICTIONAL houses in the spirit of real premium labels
/// (no real names/logos): ARDENT (minimal heritage), HALCYON (bold graphic), ALPENTEK (Swiss
/// technical), CARBONFORGE (wheels), APEX Instruments (computers), AEROLITE (helmets).
/// </summary>
public static class ShopCatalog
{
    public static readonly string[] Brands =
        { "ARDENT", "HALCYON", "ALPENTEK", "CARBONFORGE", "APEX", "AEROLITE" };

    public static readonly Dictionary<string, string> BrandTagline = new Dictionary<string, string>
    {
        { "ARDENT", "Cycle Club  .  quiet luxury" },
        { "HALCYON", "Loud kits for fast mornings" },
        { "ALPENTEK", "Swiss-engineered chamois science" },
        { "CARBONFORGE", "Superbike frame atelier  .  10 flagship framesets  .  hand-laid carbon" },
        { "APEX", "Instruments  .  know every watt" },
        { "AEROLITE", "Helmets  .  lighter than the air they cut" },
    };

    /// <summary>
    /// Store identity for the shop screen (2026-09-25 refinement): Accent colours the store title,
    /// the selected card, the action button and the fitting-room plinth glow; Second is the thin
    /// brand bar under the title. Picked to match each facade in MapleRowBoutiques.cs:
    /// ARDENT charcoal + rose, HALCYON teal/coral, ALPENTEK white/steel + red, CARBONFORGE black
    /// carbon + gold band, APEX clean tech glow, AEROLITE helmets on lit (cool white) plinths.
    /// All accents are light enough to read as text on the near-black counter.
    /// </summary>
    public static readonly Dictionary<string, Color> BrandAccent = new Dictionary<string, Color>
    {
        { "ARDENT", Hex("#E86A8E") }, { "HALCYON", Hex("#2EC4C0") }, { "ALPENTEK", Hex("#C8CDD3") },
        { "CARBONFORGE", Hex("#C9A227") }, { "APEX", Hex("#5CF2C8") }, { "AEROLITE", Hex("#7FB8FF") },
    };

    public static readonly Dictionary<string, Color> BrandSecond = new Dictionary<string, Color>
    {
        { "ARDENT", Hex("#F2EFE8") }, { "HALCYON", Hex("#FF6F59") }, { "ALPENTEK", Hex("#D4002A") },
        { "CARBONFORGE", Hex("#8A8C90") }, { "APEX", Hex("#2F9BFF") }, { "AEROLITE", Hex("#F4F4F4") },
    };

    /// <summary>The shop's item list shows at most this many cards at once (3 x 96 px + gaps);
    /// stores with more items page through them as the selection moves (MapleRowShop.LayoutCards).</summary>
    public const int MaxItemsPerBrand = 3;

    private static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

    private static ShopItem A(string id, string brand, ShopCategory cat, string name, int price,
                              string primary, string trim, string blurb) =>
        new ShopItem { Id = id, Brand = brand, Category = cat, Name = name, Price = price,
                       Primary = Hex(primary), Trim = Hex(trim), Blurb = blurb };

    /// <summary>Photoreal textured garment (design atlas Resources/Shop/Kits/Kit_&lt;id&gt;).</summary>
    private static ShopItem K(string id, string brand, ShopCategory cat, string name, int price,
                              string primary, string trim, string blurb)
    {
        var it = A(id, brand, cat, name, price, primary, trim, blurb);
        it.Asset = "Shop/Kits/Kit_" + id;
        return it;
    }

    /// <summary>Frameset (prefab Resources/Shop/Frames/FrameKit_&lt;frame&gt;). The designs are
    /// fictional houses whose frames read like today's flagship road frames (no real marks).</summary>
    private static ShopItem F(string frame, string name, int price, string primary, string trim, float cda, string blurb)
    {
        var it = A("frame_" + frame, "CARBONFORGE", ShopCategory.Frame, name, price, primary, trim, blurb);
        it.Asset = "Shop/Frames/FrameKit_" + frame;
        it.CdaScale = cda;
        return it;
    }

    public static readonly List<ShopItem> Items = new List<ShopItem>
    {
        A("ardent_jersey_classic", "ARDENT", ShopCategory.Jersey, "Classic Club Jersey", 1400, "#2B2D33", "#E86A8E", "Charcoal merino-blend with the rose chest band."),
        A("ardent_jersey_pro", "ARDENT", ShopCategory.Jersey, "Pro Team Aero Jersey", 1900, "#F2EFE8", "#1E2A44", "Race-cut, off-white with a navy band."),
        A("ardent_bibs_classic", "ARDENT", ShopCategory.Bibs, "Classic Bib Shorts", 1300, "#1B1C20", "#E86A8E", "Black bibs, rose hem gripper."),
        A("halcyon_jersey_tide", "HALCYON", ShopCategory.Jersey, "Tide Graphic Jersey", 1600, "#1FA3A0", "#FF6F59", "Teal with a coral stripe - impossible to miss."),
        A("halcyon_jersey_sunset", "HALCYON", ShopCategory.Jersey, "Sunset Lab Jersey", 1700, "#FF8A3D", "#3A1C5C", "Orange-to-violet energy."),
        A("halcyon_bibs_team", "HALCYON", ShopCategory.Bibs, "Team Bibs", 1250, "#16334A", "#1FA3A0", "Deep navy, teal hem."),
        A("alpentek_bibs_equipe", "ALPENTEK", ShopCategory.Bibs, "Equipe RS Bibs", 2200, "#0F1012", "#C8CDD3", "The chamois everyone talks about."),
        A("alpentek_jersey_mille", "ALPENTEK", ShopCategory.Jersey, "Mille GT Jersey", 1800, "#E9EEF2", "#D4002A", "Clinical white, red accent."),
        A("alpentek_jersey_equipe", "ALPENTEK", ShopCategory.Jersey, "Equipe RS Jersey", 2000, "#5B6168", "#E9EEF2", "Steel-grey race cut to match the RS bibs."),
        A("aerolite_helmet_road", "AEROLITE", ShopCategory.Helmet, "Brezza Road Helmet", 1500, "#2E3A46", "#FFB400", "Slate shell, amber vents. The everyday lid."),
        A("aerolite_helmet_aero", "AEROLITE", ShopCategory.Helmet, "Vento Aero Helmet", 2400, "#101114", "#FF3B30", "Wind-tunnel shell, matte black."),
        A("aerolite_helmet_climb", "AEROLITE", ShopCategory.Helmet, "Piuma Climbing Helmet", 2100, "#CDD7DF", "#2167AE", "215 g of pearl grey and blue."),
        new ShopItem { Id = "carbonforge_30", Brand = "CARBONFORGE", Category = ShopCategory.Wheels, Name = "CF30 Climbing Wheelset", Price = 2600,
                       Primary = Hex("#1A1A1C"), Trim = Hex("#B0B4BA"), CdaScale = 0.99f, Blurb = "Shallow 30 mm climbers. About 1% less drag." },
        new ShopItem { Id = "carbonforge_45", Brand = "CARBONFORGE", Category = ShopCategory.Wheels, Name = "CF45 Carbon Wheelset", Price = 3800,
                       Primary = Hex("#1A1A1C"), Trim = Hex("#C9A227"), CdaScale = 0.985f, Blurb = "45 mm all-rounders. About 1.5% less drag." },
        new ShopItem { Id = "carbonforge_65", Brand = "CARBONFORGE", Category = ShopCategory.Wheels, Name = "CF65 Aero Wheelset", Price = 5200,
                       Primary = Hex("#0B0B0C"), Trim = Hex("#E5E5E5"), CdaScale = 0.97f, Blurb = "65 mm deep section. About 3% less drag, loves the flats." },
        new ShopItem { Id = "apex_pro", Brand = "APEX", Category = ShopCategory.BikeComputer, Name = "APEX Pro", Price = 2600,
                       ComputerTier = 1, Blurb = "Adds W/kg and a live power-zone bar to your HUD." },
        new ShopItem { Id = "apex_elite", Brand = "APEX", Category = ShopCategory.BikeComputer, Name = "APEX Elite", Price = 4200,
                       ComputerTier = 2, Blurb = "Pro, plus average power and a climb readout." },

        // ---- 2026-09-26: photoreal garments, painted in body space (panels, seams, zips, pockets,
        // laser-cut cuffs, silicone grippers, reflective tabs, knit texture, printed wordmarks)
        K("ardent_jersey_atelier", "ARDENT", ShopCategory.Jersey, "Atelier Pro Jersey", 2600, "#2A2C31", "#E0708F",
          "Charcoal race cut with the rose chest band, the club armband on the left sleeve and bonded cuffs."),
        K("ardent_jersey_brevet", "ARDENT", ShopCategory.Jersey, "Brevet Hoop Jersey", 2300, "#1E2A44", "#EEEBE3",
          "Navy with an off-white chest hoop and rose pinstripes. Three pockets and a reflective tab."),
        K("ardent_bibs_atelier", "ARDENT", ShopCategory.Bibs, "Atelier Pro Bibs", 2500, "#131417", "#E0708F",
          "Black Lycra, charcoal side panels, rose leg grippers with the wordmark and a reflective tab."),
        K("halcyon_jersey_contour", "HALCYON", ShopCategory.Jersey, "Contour Pro Jersey", 2400, "#0F2436", "#1FA3A0",
          "Teal topographic contour lines on deep navy, coral collar and cuffs."),
        K("halcyon_jersey_prism", "HALCYON", ShopCategory.Jersey, "Prism Fade Jersey", 2200, "#FF8A3D", "#3A1C5C",
          "A diagonal orange-to-violet fade with a halftone dot transition."),
        K("halcyon_bibs_contour", "HALCYON", ShopCategory.Bibs, "Contour Pro Bibs", 2300, "#101C29", "#FF6F59",
          "Navy bibs with contour-line side panels and coral grippers."),
        K("alpentek_jersey_rsr", "ALPENTEK", ShopCategory.Jersey, "Equipe RSR Aero Jersey", 2900, "#EEF1F3", "#D4002A",
          "Clinical white, perforated grey side panels, red flashes and black laser-cut sleeves."),
        K("alpentek_jersey_stealth", "ALPENTEK", ShopCategory.Jersey, "Mille Stealth Jersey", 2500, "#141518", "#D4002A",
          "Black with a tonal pinstripe and a red collar tab."),
        K("alpentek_bibs_rsr", "ALPENTEK", ShopCategory.Bibs, "Equipe RSR Aero Bibs", 3100, "#0E0F11", "#5B6168",
          "Satin grey side panels, a red hem stripe and wordmark grippers."),

        // ---- 2026-09-26: framesets. Equipping one replaces Kuro's frame and fork on his bike.
        F("corvetto_maestro", "Corvetto Maestro Lugged", 9800, "#F1EEE7", "#A51F26", 1.0f,
          "Italian lugged carbon: star-fluted tubes, pearl white with red lugs, a black pinline and a straight crowned fork."),
        F("serpentina_f", "Serpentina F Asimmetrica", 12500, "#0B0B0D", "#9A0F1D", 0.99f,
          "Asymmetric race frame with a wavy fork and stays, a BB keel and a red fade that burns out behind the head tube."),
        F("featherline_sl", "Featherline SL", 8900, "#8E1C1E", "#0A0A0B", 0.995f,
          "Slim truncated-aero tubes, dropped seatstays and a candy-red fade, with a forged-carbon patch on the top tube."),
        F("windkanal_aero", "Windkanal Aero CF", 10900, "#2B2C30", "#E4E6EA", 0.98f,
          "Deep Kamm-tail tubes, a wide-stance fork, sparkle-stealth paint, chrome lettering and raw-carbon chainstays."),
        F("portal_aero", "Portal Aero", 11800, "#D2EE00", "#0C0C0E", 0.982f,
          "Deep aero tubes and an open portal through the seat cluster. Neon yellow fading to black."),
        F("cielo_leggero", "Cielo Leggero", 9400, "#74C6B3", "#141416", 1.0f,
          "Featherweight climber in celeste, with a nose-cone head tube and an aero-sled down tube."),
        F("monarch_r1", "Monarch R1", 13200, "#D8D1C5", "#6E1534", 0.985f,
          "Grand-tour all-rounder with a winged head tube, hourglass down tube and dropped wishbone stays in champagne pearl."),
        F("velocita_omnia", "Velocita Omnia", 12800, "#21463F", "#D6A84A", 0.982f,
          "Italian aero-climber with a razor top tube, broad fork shoulders and emerald-to-gold lacquer over visible carbon."),
        F("nocturne_x", "Nocturne X", 14500, "#171A22", "#8A5CFF", 0.975f,
          "Full-send aero superbike: deep blade tubes, split crown and midnight flip paint with ultraviolet edge flashes."),
        F("hakone_zenith", "Hakone Zenith", 11900, "#F4F1EA", "#D3222A", 0.99f,
          "Japanese mountain superbike with a compact diamond, high-tension stays and porcelain white lacquer cut by a rising-sun stripe."),
    };

    public static ShopItem Get(string id) => Items.Find(i => i.Id == id);
}
