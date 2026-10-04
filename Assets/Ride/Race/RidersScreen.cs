using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Riders screen ([R]): three tabs over the paused ride.
///   RIDERS  every racer: portrait, insignia, name, level, race, where, distance, reward,
///           record, best time and a Track button that pins them on the minimap
///   RANKS   the 17-insignia ladder with level ranges, who holds each rank, Kuro highlighted
///   GARAGE  the three bike upgrades (8 levels each) bought with MapleCoins
/// Keys: [R]/[Esc] close, [Q]/[E] or Left/Right switch tab, Up/Down select, [Enter] track/buy.
/// Everything is also clickable.
/// </summary>
public sealed class RidersScreen
{
    public enum Tab { Riders = 0, Ranks = 1, Garage = 2 }

    private readonly RectTransform _root, _panel;
    private readonly RectTransform[] _pages = new RectTransform[3];
    private readonly Image[] _tabBg = new Image[3];
    private readonly Text _header, _coins;
    private readonly Image _headerIns, _xpBar;
    private readonly List<RiderRow> _riderRows = new List<RiderRow>();
    private readonly List<Image> _rankRows = new List<Image>();
    private readonly List<Text> _rankHolders = new List<Text>();
    private readonly List<GarageCard> _garage = new List<GarageCard>();
    private readonly Text _garageStats;
    private readonly Func<string> _trackedId;
    private readonly Action<RacerDef> _onTrack;
    private readonly Func<RacerDef, string> _where;
    private int _sel;

    public Tab Current { get; private set; }
    public bool IsOpen => _root.gameObject.activeSelf;

    private sealed class RiderRow
    {
        public RacerDef def;
        public Image bg;
        public Text record, best, reward, track;
    }

    private sealed class GarageCard
    {
        public BikeUpgrade up;
        public Image bg;
        public Text level, price, stat;
        public Image[] pips;
    }

    public RidersScreen(RectTransform layer, Func<string> trackedId, Action<RacerDef> onTrack,
                        Func<RacerDef, string> where, Action onClose)
    {
        _trackedId = trackedId;
        _onTrack = onTrack;
        _where = where;
        _root = HudKit.Rect(layer, "Riders Screen");
        RaceUi.Stretch(_root);
        var dim = HudKit.Panel(_root, "Dim", new Color(0.01f, 0.015f, 0.03f, 0.62f));
        RaceUi.Stretch(dim);
        dim.GetComponent<Image>().raycastTarget = true;
        _panel = RaceLayout.Card(_root, "Panel", 1640f, 900f, RaceMath.Hex(0x3fa2ff), RaceUi.CardGlass);
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);

        // header: Kuro's badge + tabs + close
        _headerIns = RaceLayout.Image(_panel, "Kuro Insignia", RaceArt.InsigniaByIndex(0), 28f, 18f, 64f, 64f);
        _header = RaceLayout.Text(_panel, "Kuro", "", 26, HudKit.Chalk, 100f, 16f, 420f, 36f, style: FontStyle.Bold);
        _xpBar = RaceUi.Bar(_panel, "XP", new Vector2(300f, 10f), new Color(0f, 0f, 0f, 0.5f), RaceMath.Hex(0x3fa2ff));
        RaceLayout.Box((RectTransform)_xpBar.transform.parent, 102f, 58f, 300f, 10f);
        _coins = RaceLayout.Text(_panel, "Coins", "", 22, RaceUi.Coin, 420f, 44f, 260f, 30f, style: FontStyle.Bold);
        string[] names = { "RIDERS", "RANKS", "GARAGE" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = RaceUi.KeyButton(_panel, names[i] + " Tab", i == 0 ? "Q" : (i == 2 ? "E" : ""), names[i],
                                     RaceMath.Hex(0x3fa2ff), () => Select((Tab)idx), 22);
            RaceLayout.Box((RectTransform)b.transform, 720f + i * 250f, 24f, 230f, 50f);
            _tabBg[i] = b.GetComponent<Image>();
        }
        var close = RaceUi.KeyButton(_panel, "Close", "R", "Close", RaceMath.Hex(0x8a8fa3), onClose, 20);
        RaceLayout.Box((RectTransform)close.transform, 1484f, 24f, 136f, 50f);

        for (int i = 0; i < 3; i++)
        {
            _pages[i] = HudKit.Rect(_panel, names[i] + " Page");
            RaceLayout.Box(_pages[i], 24f, 100f, 1592f, 780f);
        }
        BuildRiders(_pages[0]);
        BuildRanks(_pages[1]);
        _garageStats = BuildGarage(_pages[2]);
        _root.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ riders tab

    private static readonly float[] ColX = { 0f, 62f, 108f, 250f, 316f, 520f, 850f, 940f, 1080f, 1180f, 1310f, 1440f };

    private void BuildRiders(RectTransform page)
    {
        string[] heads = { "", "", "RIDER", "LV", "RACE", "WHERE", "DIST", "REWARD", "RECORD", "BEST", "", "" };
        for (int i = 0; i < heads.Length; i++)
            if (heads[i].Length > 0)
                RaceLayout.Text(page, "Head " + heads[i], heads[i], 15, HudKit.ChalkSoft, ColX[i] + 8f, 0f, 200f, 24f, style: FontStyle.Bold);
        var defs = new List<RacerDef>(RaceRoster.All);
        defs.Sort((a, b) => a.Level.CompareTo(b.Level));
        const float rowH = 54f;
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            float y = 28f + i * rowH;
            var bg = HudKit.SoftPanel(page, "Row " + d.Name, new Color(1f, 1f, 1f, i % 2 == 0 ? 0.04f : 0.015f));
            RaceLayout.Box(bg, 0f, y, 1592f, rowH - 4f);
            var p = RaceUi.Portrait(bg, "Portrait", RaceArt.Portrait(d.Name), 46f);
            RaceLayout.Box((RectTransform)p.transform.parent, ColX[0] + 4f, 2f, 46f, 46f);
            RaceLayout.Image(bg, "Insignia", RaceArt.Insignia(d.Level), ColX[1] + 4f, 5f, 40f, 40f);
            RaceLayout.Text(bg, "Name", d.Name, 22, HudKit.Chalk, ColX[2] + 8f, 0f, 140f, 50f, style: FontStyle.Bold);
            RaceLayout.Text(bg, "Level", d.Level.ToString(), 22, HudKit.Chalk, ColX[3] + 8f, 0f, 60f, 50f, style: FontStyle.Bold);
            RaceLayout.Image(bg, "Type Icon", RaceArt.Icon(d.Type), ColX[4] + 4f, 6f, 38f, 38f);
            RaceLayout.Text(bg, "Type", $"<color=#{RaceLayout.Hex(RaceMath.TypeColor(d.Type))}>{RaceMath.DisplayName(d.Type)}</color>",
                            18, HudKit.Chalk, ColX[4] + 46f, 0f, 160f, 50f, style: FontStyle.Bold);
            RaceLayout.Text(bg, "Where", _where(d), 17, HudKit.ChalkSoft, ColX[5] + 8f, 0f, 320f, 50f);
            RaceLayout.Text(bg, "Distance", $"{d.LengthM:0} m", 18, HudKit.Chalk, ColX[6] + 8f, 0f, 90f, 50f);
            var row = new RiderRow { def = d, bg = bg.GetComponent<Image>() };
            row.reward = RaceLayout.Text(bg, "Reward", "", 18, RaceUi.Coin, ColX[7] + 8f, 0f, 140f, 50f, style: FontStyle.Bold);
            row.record = RaceLayout.Text(bg, "Record", "", 18, HudKit.Chalk, ColX[8] + 8f, 0f, 100f, 50f);
            row.best = RaceLayout.Text(bg, "Best", "", 18, HudKit.Chalk, ColX[9] + 8f, 0f, 120f, 50f);
            var btn = RaceUi.KeyButton(bg, "Track", "", "Track", RaceMath.TypeColor(d.Type), () => _onTrack(d), 18);
            RaceLayout.Box((RectTransform)btn.transform, ColX[11], 5f, 140f, 40f);
            row.track = btn.transform.Find("Label").GetComponent<Text>();
            _riderRows.Add(row);
        }
    }

    // ------------------------------------------------------------------ ranks tab

    private void BuildRanks(RectTransform page)
    {
        RaceLayout.Text(page, "Head", "INSIGNIA", 15, HudKit.ChalkSoft, 80f, 0f, 200f, 24f, style: FontStyle.Bold);
        RaceLayout.Text(page, "Head2", "LEVELS", 15, HudKit.ChalkSoft, 420f, 0f, 200f, 24f, style: FontStyle.Bold);
        RaceLayout.Text(page, "Head3", "HELD BY", 15, HudKit.ChalkSoft, 580f, 0f, 200f, 24f, style: FontStyle.Bold);
        const float rowH = 44.5f;
        for (int i = 0; i < RaceMath.InsigniaCount; i++)
        {
            float y = 26f + i * rowH;
            var bg = HudKit.SoftPanel(page, "Rank " + i, new Color(1f, 1f, 1f, i % 2 == 0 ? 0.04f : 0.015f));
            RaceLayout.Box(bg, 0f, y, 1592f, rowH - 3f);
            RaceLayout.Image(bg, "Insignia", RaceArt.InsigniaByIndex(i), 16f, 1f, 40f, 40f);
            RaceLayout.Text(bg, "Name", RaceMath.InsigniaNames[i], 21, HudKit.Chalk, 80f, 0f, 330f, 40f, style: FontStyle.Bold);
            RaceLayout.Text(bg, "Range", RaceMath.InsigniaRange(i), 19, HudKit.ChalkSoft, 420f, 0f, 150f, 40f);
            _rankHolders.Add(RaceLayout.Text(bg, "Holders", "", 19, HudKit.Chalk, 580f, 0f, 1000f, 40f));
            _rankRows.Add(bg.GetComponent<Image>());
        }
    }

    // ------------------------------------------------------------------ garage tab

    private Text BuildGarage(RectTransform page)
    {
        foreach (BikeUpgrade u in Enum.GetValues(typeof(BikeUpgrade)))
        {
            int i = (int)u;
            var card = RaceLayout.Card(page, RiderProgress.UpgradeName(u), 500f, 520f, RaceMath.Hex(0xffc23d), RaceUi.CardGlassSoft);
            RaceLayout.Box(card, i * 546f, 10f, 500f, 520f);
            var gc = new GarageCard { up = u, bg = card.GetComponent<Image>(), pips = new Image[RaceMath.MaxUpgradeLevel] };
            RaceLayout.Text(card, "Name", RiderProgress.UpgradeName(u), 32, HudKit.Chalk, 28f, 26f, 450f, 44f, style: FontStyle.Bold);
            RaceLayout.Text(card, "Effect", RiderProgress.UpgradeEffect(u), 19, HudKit.ChalkSoft, 28f, 74f, 450f, 30f);
            gc.level = RaceLayout.Text(card, "Level", "", 22, HudKit.Chalk, 28f, 124f, 450f, 30f, style: FontStyle.Bold);
            for (int p = 0; p < RaceMath.MaxUpgradeLevel; p++)
            {
                var pip = HudKit.SoftPanel(card, "Pip " + p, Color.white);
                RaceLayout.Box(pip, 28f + p * 55f, 164f, 46f, 20f);
                gc.pips[p] = pip.GetComponent<Image>();
            }
            gc.stat = RaceLayout.Text(card, "Stat", "", 21, HudKit.Chalk, 28f, 214f, 450f, 70f);
            gc.price = RaceLayout.Text(card, "Price", "", 26, RaceUi.Coin, 28f, 310f, 450f, 40f, style: FontStyle.Bold);
            var btn = RaceUi.KeyButton(card, "Buy", "Enter", "Upgrade", RaceMath.Hex(0xffc23d), () => Buy(u), 22);
            RaceLayout.Box((RectTransform)btn.transform, 28f, 440f, 280f, 54f);
            _garage.Add(gc);
        }
        return RaceLayout.Text(page, "Stats", "", 20, HudKit.ChalkSoft, 0f, 560f, 1592f, 90f, TextAnchor.UpperLeft);
    }

    private void Buy(BikeUpgrade u)
    {
        if (RiderProgress.BuyUpgrade(u)) Refresh();
    }

    // ------------------------------------------------------------------ state

    public void Open(Tab tab)
    {
        _root.gameObject.SetActive(true);
        _sel = 0;
        Select(tab);
    }

    public void Close() { _root.gameObject.SetActive(false); }

    public void Select(Tab tab)
    {
        Current = tab;
        _sel = 0;
        for (int i = 0; i < 3; i++)
        {
            _pages[i].gameObject.SetActive(i == (int)tab);
            _tabBg[i].color = i == (int)tab ? new Color(0.16f, 0.36f, 0.66f, 0.95f) : new Color(0.05f, 0.07f, 0.12f, 0.92f);
        }
        Refresh();
    }

    /// <summary>Keyboard handling; call every frame while open. Returns false when it closed itself.</summary>
    public bool HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow)) Select((Tab)(((int)Current + 2) % 3));
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightArrow)) Select((Tab)(((int)Current + 1) % 3));
        int count = Current == Tab.Riders ? _riderRows.Count : (Current == Tab.Garage ? _garage.Count : 0);
        if (count > 0)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || (Current == Tab.Garage && Input.GetKeyDown(KeyCode.LeftBracket)))
            { _sel = (_sel + count - 1) % count; Refresh(); }
            if (Input.GetKeyDown(KeyCode.DownArrow) || (Current == Tab.Garage && Input.GetKeyDown(KeyCode.RightBracket)))
            { _sel = (_sel + 1) % count; Refresh(); }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (Current == Tab.Riders) { _onTrack(_riderRows[_sel].def); Refresh(); }
                else Buy(_garage[_sel].up);
            }
        }
        return IsOpen;
    }

    public void Refresh()
    {
        int lv = RiderProgress.Level;
        _headerIns.sprite = RaceArt.Insignia(lv);
        _header.text = $"KURO  <size=20><color=#c9d3e6>Lv {lv}  ·  {RaceMath.InsigniaName(lv)}</color></size>";
        int need = RiderProgress.XpToNext;
        RaceUi.SetBar(_xpBar, need > 0 ? RiderProgress.Xp / (float)need : 1f);
        _coins.text = RaceUi.Coins(PlayerWardrobe.Coins);

        string tracked = _trackedId();
        for (int i = 0; i < _riderRows.Count; i++)
        {
            var r = _riderRows[i];
            var rec = RiderProgress.RecordFor(r.def.Id);
            bool first = rec == null || !rec.firstWinClaimed;
            r.reward.text = RaceUi.Coins(RaceMath.WinCoins(r.def.Level, r.def.Type, first));
            r.record.text = rec == null ? "0-0" : $"{rec.wins}-{rec.losses}";
            r.best.text = RaceMath.FormatTime(rec != null ? rec.bestTime : -1f);
            bool isTracked = tracked == r.def.Id;
            r.track.text = isTracked ? "Tracking" : "Track";
            bool sel = Current == Tab.Riders && i == _sel;
            r.bg.color = isTracked ? new Color(0.2f, 0.45f, 0.85f, 0.28f)
                       : (sel ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.04f : 0.015f));
        }

        int kIdx = RaceMath.InsigniaIndex(lv);
        for (int i = 0; i < _rankRows.Count; i++)
        {
            var holders = new List<string>();
            if (i == kIdx) holders.Add("<color=#ffc23d><b>KURO (you)</b></color>");
            foreach (var d in RaceRoster.All)
                if (RaceMath.InsigniaIndex(d.Level) == i) holders.Add($"{d.Name} <color=#9aa6bd>Lv {d.Level}</color>");
            _rankHolders[i].text = holders.Count == 0 ? "<color=#6c7489>-</color>" : string.Join("   ", holders);
            _rankRows[i].color = i == kIdx ? new Color(1f, 0.76f, 0.24f, 0.22f)
                               : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.04f : 0.015f);
        }

        var st = RiderProgress.KuroStats;
        for (int i = 0; i < _garage.Count; i++)
        {
            var g = _garage[i];
            int ul = RiderProgress.UpgradeLevel(g.up);
            g.level.text = $"Level {ul} / {RaceMath.MaxUpgradeLevel}";
            for (int p = 0; p < g.pips.Length; p++)
                g.pips[p].color = p < ul ? RaceMath.Hex(0xffc23d) : new Color(1f, 1f, 1f, 0.12f);
            int list = RiderProgress.NextUpgradePrice(g.up);
            int pay = RiderProgress.UpgradePriceToPay(g.up);
            if (list < 0) g.price.text = "<color=#3ddc74>MAXED</color>";
            else if (pay == 0) g.price.text = $"FREE <size=18><color=#9aa6bd>(QA; list {RaceUi.Coins(list)})</color></size>";
            else g.price.text = RaceUi.Coins(list) + (PlayerWardrobe.Coins < list ? " <size=18><color=#ff4757>not enough</color></size>" : "");
            g.stat.text = StatLine(g.up, st);
            g.bg.color = (Current == Tab.Garage && i == _sel) ? new Color(0.12f, 0.16f, 0.26f, 0.9f) : RaceUi.CardGlassSoft;
        }
        _garageStats.text =
            $"Kuro now:  cruise {st.Cruise * 3.6f:0.0} km/h   ·   sprint boost +{st.Boost * 3.6f:0.0} km/h   ·   stamina {st.Stamina:0}\n" +
            "Levels raise all three a little; upgrades raise one a lot. Elite riders need both.";
    }

    private static string StatLine(BikeUpgrade u, RaceMath.Stats s)
    {
        switch (u)
        {
            case BikeUpgrade.CeramicDrivetrain: return $"Sprint boost  {s.Boost:0.00} m/s  <color=#9aa6bd>(+{s.Boost * 3.6f:0.0} km/h)</color>";
            case BikeUpgrade.OnigiriRations: return $"Stamina  {s.Stamina:0}";
            default: return $"Cruise  {s.Cruise:0.00} m/s  <color=#9aa6bd>({s.Cruise * 3.6f:0.0} km/h)</color>";
        }
    }
}
