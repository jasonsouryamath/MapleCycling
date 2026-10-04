using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared layout helpers for the race screens (top-left anchored boxes, like the art).</summary>
public static class RaceLayout
{
    public static RectTransform Box(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    public static Text Text(Transform parent, string name, string text, int size, Color color,
                            float x, float y, float w, float h,
                            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal,
                            bool shadow = true)
    {
        var t = HudKit.Label(parent, name, text, size, color, anchor, style);
        Box((RectTransform)t.transform, x, y, w, h);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        if (shadow) HudKit.AddShadow(t);
        return t;
    }

    public static Image Image(Transform parent, string name, Sprite sprite, float x, float y, float w, float h,
                              Color? tint = null)
    {
        var img = RaceUi.Icon(parent, name, sprite, new Vector2(w, h), tint);
        Box(img.rectTransform, x, y, w, h);
        return img;
    }

    public static RectTransform Card(Transform parent, string name, float w, float h, Color accent, Color? glass = null)
    {
        var card = HudKit.SoftPanel(parent, name, glass ?? RaceUi.CardGlass);
        card.sizeDelta = new Vector2(w, h);
        var stripe = HudKit.SoftPanel(card, "Accent", accent);
        stripe.anchorMin = new Vector2(0f, 1f); stripe.anchorMax = new Vector2(1f, 1f);
        stripe.pivot = new Vector2(0.5f, 1f);
        stripe.offsetMin = new Vector2(10f, -6f); stripe.offsetMax = new Vector2(-10f, -2f);
        var edge = HudKit.SoftPanel(card, "Edge", HudKit.WithAlpha(accent, 0.45f), frame: true);
        RaceUi.Stretch(edge);
        return card;
    }

    public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}

// ======================================================================== E prompt card

/// <summary>The "E  Race" card beside a racer: who they are, the race, the reward and the odds.</summary>
public sealed class RacePromptCard
{
    public const float W = 440f, H = 250f;
    private readonly RectTransform _card;
    private readonly RawImage _portrait;
    private readonly Image _insignia, _typeIcon, _oddsChip, _stripe;
    private readonly Text _name, _rank, _type, _reward, _odds, _record;
    private string _shownId;

    public bool Visible => _card.gameObject.activeSelf;
    public RectTransform Rect => _card;

    public RacePromptCard(RectTransform layer, Action onRace)
    {
        _card = RaceLayout.Card(layer, "Race Prompt", W, H, Color.white, new Color(0.03f, 0.04f, 0.07f, 0.94f));
        _stripe = _card.Find("Accent").GetComponent<Image>();
        _card.anchorMin = _card.anchorMax = Vector2.zero;
        _card.pivot = new Vector2(0f, 0.5f);
        _portrait = RaceUi.Portrait(_card, "Portrait", null, 100f);
        RaceLayout.Box((RectTransform)_portrait.transform.parent, 14f, 16f, 100f, 100f);
        _insignia = RaceLayout.Image(_card, "Insignia", RaceArt.InsigniaByIndex(0), 126f, 16f, 38f, 38f);
        _name = RaceLayout.Text(_card, "Name", "", 28, HudKit.Chalk, 170f, 14f, 250f, 40f, style: FontStyle.Bold);
        _rank = RaceLayout.Text(_card, "Rank", "", 17, HudKit.ChalkSoft, 128f, 56f, 300f, 24f);
        _typeIcon = RaceLayout.Image(_card, "Type Icon", RaceArt.Icon(RaceType.Standard), 124f, 82f, 34f, 34f);
        _type = RaceLayout.Text(_card, "Type", "", 20, HudKit.Chalk, 162f, 84f, 270f, 30f, style: FontStyle.Bold);
        _reward = RaceLayout.Text(_card, "Reward", "", 19, RaceUi.Coin, 16f, 124f, 410f, 28f, style: FontStyle.Bold);
        var chip = HudKit.SoftPanel(_card, "Odds", Color.white);
        RaceLayout.Box(chip, 16f, 156f, 170f, 30f);
        _oddsChip = chip.GetComponent<Image>();
        _odds = HudKit.Label(chip, "Text", "", 18, HudKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        RaceUi.Stretch((RectTransform)_odds.transform);
        _record = RaceLayout.Text(_card, "Record", "", 16, HudKit.ChalkSoft, 196f, 156f, 230f, 30f, TextAnchor.MiddleRight);
        var btn = RaceUi.KeyButton(_card, "Race Button", "E", "Race", Color.white, onRace, 24);
        RaceLayout.Box((RectTransform)btn.transform, 16f, 196f, 170f, 44f);
        var hint = RaceLayout.Text(_card, "Hint", "or click", 15, HudKit.ChalkSoft, 196f, 196f, 120f, 44f);
        hint.color = HudKit.WithAlpha(HudKit.ChalkSoft, 0.7f);
        _card.gameObject.SetActive(false);
    }

    /// <summary>Card box in canvas reference px (origin bottom-left), for the play test.</summary>
    public Rect ScreenRect => new Rect(_card.anchoredPosition.x, _card.anchoredPosition.y - H * 0.5f, W, H);

    /// <summary>Shows the card beside a rider whose on-screen box is <paramref name="rider"/>
    /// (canvas reference px, origin bottom-left) and never over it: to the rider's right if
    /// that fits, else flipped to their left, else above or below them.</summary>
    public void Show(RacerDef def, Rect rider)
    {
        if (_shownId != def.Id) Fill(def);
        if (!_card.gameObject.activeSelf) _card.gameObject.SetActive(true);
        const float gap = 24f;
        // keep it on screen at the 1920x1080 reference, and clear of the bottom-left ride card
        // and rank badge (they end ~340 px up) and the top-row cards
        float yMin = H * 0.5f + 350f, yMax = 1080f - H * 0.5f - 130f;
        float y = Mathf.Clamp(rider.center.y + rider.height * 0.15f, yMin, yMax);
        float x = PickX(rider, y, gap);
        if (Overlaps(x, y, rider))
        {
            // neither side fits at this height (a very wide, close rider): go above or below them
            float above = rider.yMax + gap + H * 0.5f, below = rider.yMin - gap - H * 0.5f;
            float yTry = above <= yMax ? above : (below >= yMin ? below : y);
            if (!Overlaps(PickX(rider, yTry, gap), yTry, rider)) { y = yTry; x = PickX(rider, y, gap); }
            else
            {
                // last resort: the side with the least overlap
                float xr = Mathf.Clamp(rider.xMax + gap, 16f, MaxX(y)), xl = Mathf.Clamp(rider.xMin - gap - W, 16f, MaxX(y));
                x = OverlapArea(xr, y, rider) <= OverlapArea(xl, y, rider) ? xr : xl;
            }
        }
        _card.anchoredPosition = new Vector2(x, y);
    }

    // the right-hand 330 px only has to stay clear where the card would reach the top-right
    // World Map / HR cards; lower down it may go further right
    private static float MaxX(float y) => y + H * 0.5f < 925f ? 1920f - W - 16f : 1920f - W - 330f;

    private static float PickX(Rect rider, float y, float gap)
    {
        float maxX = MaxX(y);
        float right = rider.xMax + gap, left = rider.xMin - gap - W;
        if (right <= maxX) return Mathf.Max(16f, right);
        if (left >= 16f) return Mathf.Min(maxX, left);
        return Mathf.Clamp(right, 16f, maxX);
    }

    private static bool Overlaps(float x, float y, Rect r) => OverlapArea(x, y, r) > 1f;

    private static float OverlapArea(float x, float y, Rect r)
    {
        float ox = Mathf.Min(x + W, r.xMax) - Mathf.Max(x, r.xMin);
        float oy = Mathf.Min(y + H * 0.5f, r.yMax) - Mathf.Max(y - H * 0.5f, r.yMin);
        return ox > 0f && oy > 0f ? ox * oy : 0f;
    }

    public void Refresh() { _shownId = null; }

    private void Fill(RacerDef def)
    {
        _shownId = def.Id;
        var color = RaceMath.TypeColor(def.Type);
        _stripe.color = color;
        _card.Find("Edge").GetComponent<Image>().color = HudKit.WithAlpha(color, 0.45f);
        _portrait.texture = RaceArt.Portrait(def.Name);
        _insignia.sprite = RaceArt.Insignia(def.Level);
        _name.text = def.Name;
        _rank.text = $"Lv {def.Level}  ·  {RaceMath.InsigniaName(def.Level)}";
        _typeIcon.sprite = RaceArt.Icon(def.Type);
        _type.text = $"<color=#{RaceLayout.Hex(color)}>{RaceMath.DisplayName(def.Type)}</color>  ·  {def.LengthM:0} m";
        var rec = RiderProgress.RecordFor(def.Id);
        bool first = rec == null || !rec.firstWinClaimed;
        int coins = RaceMath.WinCoins(def.Level, def.Type, first);
        _reward.text = $"Win  +{RaceUi.Coins(coins)}" + (first ? "   <size=15><color=#c9d3e6>first win x1.5</color></size>" : "");
        float gap = RaceMath.EffectiveNpcLevel(def.Level, def.Type) - RiderProgress.EffectiveLevel;
        _odds.text = RaceMath.OddsLabel(gap);
        _oddsChip.color = RaceMath.OddsColor(gap);
        _record.text = rec == null ? "First meeting"
            : $"Record {rec.wins}-{rec.losses}  ·  Best {RaceMath.FormatTime(rec.bestTime)}";
    }

    public void Hide()
    {
        if (_card.gameObject.activeSelf) _card.gameObject.SetActive(false);
    }
}

// ======================================================================== player rank badge

/// <summary>Kuro's own card on the ride HUD: insignia, level, XP to next and MapleCoins.</summary>
public sealed class PlayerRankBadge
{
    private readonly RectTransform _card;
    private readonly Image _insignia, _xp;
    private readonly Text _name, _xpText, _coins;

    public RectTransform Rect => _card;

    public PlayerRankBadge(RectTransform layer)
    {
        _card = HudKit.SoftPanel(layer, "Kuro Rank Badge", RaceUi.CardGlassSoft);
        HudKit.Corner(_card, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 254f), new Vector2(300f, 76f));
        _insignia = RaceLayout.Image(_card, "Insignia", RaceArt.InsigniaByIndex(0), 8f, 8f, 60f, 60f);
        _name = RaceLayout.Text(_card, "Name", "", 20, HudKit.Chalk, 76f, 6f, 216f, 26f, style: FontStyle.Bold);
        _xp = RaceUi.Bar(_card, "XP", new Vector2(214f, 10f), new Color(0f, 0f, 0f, 0.5f), RaceMath.Hex(0x3fa2ff));
        RaceLayout.Box((RectTransform)_xp.transform.parent, 76f, 36f, 214f, 10f);
        _xpText = RaceLayout.Text(_card, "XP Text", "", 14, HudKit.ChalkSoft, 76f, 48f, 110f, 22f);
        _coins = RaceLayout.Text(_card, "Coins", "", 15, RaceUi.Coin, 180f, 48f, 110f, 22f, TextAnchor.MiddleRight, FontStyle.Bold);
        Refresh();
    }

    public void Refresh()
    {
        int lv = RiderProgress.Level;
        _insignia.sprite = RaceArt.Insignia(lv);
        _name.text = $"KURO  <color=#c9d3e6><size=16>Lv {lv}</size></color>";
        int need = RiderProgress.XpToNext;
        RaceUi.SetBar(_xp, need > 0 ? RiderProgress.Xp / (float)need : 1f);
        _xpText.text = need > 0 ? $"{RiderProgress.Xp}/{need} XP" : "MAX LEVEL";
        _coins.text = RaceUi.Coins(PlayerWardrobe.Coins);
    }

    public void SetVisible(bool on)
    {
        if (_card.gameObject.activeSelf != on) _card.gameObject.SetActive(on);
    }
}

// ======================================================================== in-race HUD

/// <summary>Progress bar with both riders, metres to go, gap, timer, speed, stamina and state chips.</summary>
public sealed class RaceHudView
{
    private readonly RectTransform _top, _bottom, _track, _kuroDot, _rivalDot;
    private readonly Image _progress, _stamina;
    private readonly Text _title, _toGo, _gap, _timer, _speed, _staminaText, _controls;
    private readonly RectTransform[] _chips = new RectTransform[3];
    private const float TrackW = 820f;

    public RaceHudView(RectTransform layer)
    {
        _top = HudKit.SoftPanel(layer, "Race Top", RaceUi.CardGlass);
        HudKit.Corner(_top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(900f, 132f));
        _title = RaceLayout.Text(_top, "Title", "", 20, HudKit.Chalk, 0f, 8f, 900f, 28f, TextAnchor.MiddleCenter, FontStyle.Bold);
        var trackBed = HudKit.SoftPanel(_top, "Track", new Color(0f, 0f, 0f, 0.55f));
        RaceLayout.Box(trackBed, 40f, 48f, TrackW, 14f);
        _track = trackBed;
        _progress = HudKit.SoftPanel(trackBed, "Progress", HudKit.WithAlpha(HudKit.Chalk, 0.35f)).GetComponent<Image>();
        var pr = _progress.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f); pr.anchorMax = new Vector2(0f, 1f); pr.pivot = new Vector2(0f, 0.5f);
        pr.offsetMin = new Vector2(0f, 0f); pr.offsetMax = new Vector2(0f, 0f);
        RaceLayout.Image(_top, "Finish", HudSprites.FinishFlag, 40f + TrackW + 4f, 40f, 30f, 30f);
        _rivalDot = Dot(trackBed, "Rival Dot", Color.white, 24f);
        _kuroDot = Dot(trackBed, "Kuro Dot", HudKit.Chalk, 28f);
        _toGo = RaceLayout.Text(_top, "To Go", "", 24, HudKit.Chalk, 40f, 76f, 260f, 44f, style: FontStyle.Bold);
        _gap = RaceLayout.Text(_top, "Gap", "", 24, HudKit.Chalk, 300f, 76f, 300f, 44f, TextAnchor.MiddleCenter, FontStyle.Bold);
        _timer = RaceLayout.Text(_top, "Timer", "", 26, HudKit.Chalk, 600f, 76f, 260f, 44f, TextAnchor.MiddleRight, FontStyle.Bold);
        HudKit.AddOutline(_timer);

        _bottom = HudKit.SoftPanel(layer, "Race Bottom", RaceUi.CardGlass);
        HudKit.Corner(_bottom, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(640f, 150f));
        _speed = RaceLayout.Text(_bottom, "Speed", "", 44, HudKit.Chalk, 20f, 10f, 260f, 56f, style: FontStyle.Bold);
        HudKit.AddOutline(_speed);
        _staminaText = RaceLayout.Text(_bottom, "Stamina Label", "STAMINA", 15, HudKit.ChalkSoft, 290f, 12f, 330f, 20f, style: FontStyle.Bold);
        _stamina = RaceUi.Bar(_bottom, "Stamina", new Vector2(330f, 22f), new Color(0f, 0f, 0f, 0.55f), RaceMath.Hex(0x3ddc74));
        RaceLayout.Box((RectTransform)_stamina.transform.parent, 290f, 36f, 330f, 22f);
        string[] names = { "DRAFTING", "SPRINTING", "BONKED" };
        Color[] cols = { RaceMath.Hex(0x3fa2ff), RaceMath.Hex(0xff4757), RaceMath.Hex(0x8a8fa3) };
        for (int i = 0; i < 3; i++)
        {
            _chips[i] = HudKit.SoftPanel(_bottom, names[i], cols[i]);
            RaceLayout.Box(_chips[i], 20f + i * 150f, 74f, 140f, 30f);
            var t = HudKit.Label(_chips[i], "Text", names[i], 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            RaceUi.Stretch((RectTransform)t.transform);
            HudKit.AddShadow(t);
        }
        _controls = RaceLayout.Text(_bottom, "Controls", "", 15, HudKit.ChalkSoft, 20f, 112f, 600f, 28f);
        SetVisible(false);
    }

    private static RectTransform Dot(RectTransform parent, string name, Color c, float size)
    {
        var rim = HudKit.Panel(parent, name, new Color(0.02f, 0.02f, 0.04f, 0.95f));
        rim.GetComponent<Image>().sprite = HudSprites.Disc;
        rim.anchorMin = rim.anchorMax = new Vector2(0f, 0.5f);
        rim.sizeDelta = new Vector2(size, size);
        var fill = HudKit.Panel(rim, "Fill", c);
        fill.GetComponent<Image>().sprite = HudSprites.Disc;
        fill.anchorMin = fill.anchorMax = new Vector2(0.5f, 0.5f);
        fill.sizeDelta = new Vector2(size - 6f, size - 6f);
        return rim;
    }

    public void Setup(RacerDef def, bool trainer)
    {
        var col = RaceMath.TypeColor(def.Type);
        _title.text = $"KURO  vs  {def.Name.ToUpperInvariant()}   <color=#{RaceLayout.Hex(col)}>{RaceMath.DisplayName(def.Type).ToUpperInvariant()} · {def.LengthM:0} m</color>";
        _rivalDot.Find("Fill").GetComponent<Image>().color = col;
        _controls.text = trainer
            ? "Trainer: pedal > 50% FTP, sprint > 120% FTP   ·   [A/D] line   [Esc] forfeit"
            : "[W] pedal   [Shift] sprint   [A/D] line   [Esc] forfeit";
    }

    public void SetVisible(bool on)
    {
        _top.gameObject.SetActive(on);
        _bottom.gameObject.SetActive(on);
    }

    public void Tick(RaceRider kuro, RaceRider rival, float length, float raceTime, string rivalName, bool timeTrial)
    {
        float kx = Mathf.Clamp01(kuro.x / length), rx = Mathf.Clamp01(rival.x / length);
        _kuroDot.anchoredPosition = new Vector2(kx * TrackW, 0f);
        _rivalDot.anchoredPosition = new Vector2(rx * TrackW, 0f);
        _progress.rectTransform.sizeDelta = new Vector2(kx * TrackW, 0f);
        float toGo = Mathf.Max(0f, length - kuro.x);
        _toGo.text = kuro.Finished ? "FINISHED" : $"{toGo:0} m <size=17>to go</size>";
        float gap = kuro.x - rival.x;
        string who = timeTrial ? $"{rivalName}'s ghost" : rivalName;
        if (Mathf.Abs(gap) < 1f) { _gap.text = "Level"; _gap.color = HudKit.Chalk; }
        else if (gap > 0f) { _gap.text = $"Ahead by {gap:0} m"; _gap.color = RaceUi.Win; }
        else { _gap.text = $"Behind by {-gap:0} m"; _gap.color = HudKit.Ember; }
        _timer.text = RaceMath.FormatTime(kuro.Finished ? kuro.finishTime : raceTime);
        _speed.text = $"{kuro.v * 3.6f:0.0}<size=20> km/h</size>";
        RaceUi.SetBar(_stamina, kuro.StaminaFraction);
        _stamina.color = kuro.Bonked ? RaceMath.Hex(0x8a8fa3)
            : Color.Lerp(RaceMath.Hex(0xff4757), RaceMath.Hex(0x3ddc74), Mathf.InverseLerp(0.15f, 0.6f, kuro.StaminaFraction));
        _staminaText.text = $"STAMINA  {kuro.stamina:0}/{kuro.stats.Stamina:0}";
        Chip(0, kuro.drafting);
        Chip(1, kuro.sprinting);
        Chip(2, kuro.Bonked);
    }

    private void Chip(int i, bool on)
    {
        var img = _chips[i].GetComponent<Image>();
        var c = img.color;
        c.a = on ? 1f : 0.16f;
        img.color = c;
        var t = _chips[i].GetComponentInChildren<Text>();
        t.color = new Color(1f, 1f, 1f, on ? 1f : 0.35f);
    }
}

// ======================================================================== intro + countdown

public sealed class RaceIntroView
{
    private readonly RectTransform _root, _card;
    private readonly RawImage _kuroPortrait, _npcPortrait;
    private readonly Image _kuroIns, _npcIns, _typeIcon;
    private readonly Text _kuroName, _npcName, _type, _vs;
    private readonly Text _count;

    public RaceIntroView(RectTransform layer)
    {
        _root = HudKit.Rect(layer, "Race Intro");
        RaceUi.Stretch(_root);
        _card = RaceLayout.Card(_root, "Card", 1000f, 330f, Color.white);
        _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
        _card.anchoredPosition = new Vector2(0f, 60f);
        _kuroPortrait = RaceUi.Portrait(_card, "Kuro Portrait", RaceArt.Portrait("Kuro"), 170f);
        RaceLayout.Box((RectTransform)_kuroPortrait.transform.parent, 50f, 36f, 170f, 170f);
        _npcPortrait = RaceUi.Portrait(_card, "NPC Portrait", null, 170f);
        RaceLayout.Box((RectTransform)_npcPortrait.transform.parent, 780f, 36f, 170f, 170f);
        _kuroIns = RaceLayout.Image(_card, "Kuro Insignia", RaceArt.InsigniaByIndex(0), 40f, 216f, 64f, 64f);
        _kuroName = RaceLayout.Text(_card, "Kuro Name", "", 30, HudKit.Chalk, 110f, 216f, 260f, 64f, style: FontStyle.Bold);
        _npcIns = RaceLayout.Image(_card, "NPC Insignia", RaceArt.InsigniaByIndex(0), 896f, 216f, 64f, 64f);
        _npcName = RaceLayout.Text(_card, "NPC Name", "", 30, HudKit.Chalk, 630f, 216f, 260f, 64f, TextAnchor.MiddleRight, FontStyle.Bold);
        _vs = RaceLayout.Text(_card, "VS", "VS", 110, HudKit.Chalk, 300f, 40f, 400f, 130f, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_vs, 0.9f, 3f);
        _typeIcon = RaceLayout.Image(_card, "Type Icon", RaceArt.Icon(RaceType.Standard), 440f, 172f, 120f, 120f);
        _type = RaceLayout.Text(_card, "Type", "", 22, HudKit.Chalk, 250f, 290f, 500f, 32f, TextAnchor.MiddleCenter, FontStyle.Bold);
        _count = HudKit.Label(_root, "Countdown", "", 300, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
        RaceUi.Stretch((RectTransform)_count.transform);
        HudKit.AddOutline(_count, 0.95f, 5f);
        _root.gameObject.SetActive(false);
    }

    public void ShowIntro(RacerDef def)
    {
        _root.gameObject.SetActive(true);
        _card.gameObject.SetActive(true);
        _count.gameObject.SetActive(false);
        var col = RaceMath.TypeColor(def.Type);
        _card.Find("Accent").GetComponent<Image>().color = col;
        _card.Find("Edge").GetComponent<Image>().color = HudKit.WithAlpha(col, 0.6f);
        _npcPortrait.texture = RaceArt.Portrait(def.Name);
        _kuroIns.sprite = RaceArt.Insignia(RiderProgress.Level);
        _npcIns.sprite = RaceArt.Insignia(def.Level);
        _kuroName.text = $"KURO <size=20><color=#c9d3e6>Lv {RiderProgress.Level}</color></size>";
        _npcName.text = $"<size=20><color=#c9d3e6>Lv {def.Level}</color></size> {def.Name.ToUpperInvariant()}";
        _vs.color = col;
        _typeIcon.sprite = RaceArt.Icon(def.Type);
        _type.text = $"{RaceMath.DisplayName(def.Type).ToUpperInvariant()}  ·  {def.LengthM:0} m";
        _card.localScale = Vector3.one;
    }

    /// <summary>Animates the card in (0..1).</summary>
    public void AnimateIntro(float t)
    {
        float s = t < 0.15f ? Mathf.Lerp(0.85f, 1.04f, t / 0.15f) : Mathf.Lerp(1.04f, 1f, Mathf.Clamp01((t - 0.15f) / 0.2f));
        _card.localScale = Vector3.one * s;
    }

    public void ShowCount(string text, Color color, float t)
    {
        _root.gameObject.SetActive(true);
        _card.gameObject.SetActive(false);
        _count.gameObject.SetActive(true);
        _count.text = text;
        float pop = 1f + 0.35f * Mathf.Exp(-t * 9f);
        _count.transform.localScale = Vector3.one * pop;
        _count.color = HudKit.WithAlpha(color, Mathf.Clamp01(1.4f - t));
    }

    public void Hide() { _root.gameObject.SetActive(false); }
}

// ======================================================================== results

public struct RaceResult
{
    public RacerDef def;
    public bool won, forfeit, firstWin;
    public float kuroTime, npcTime;
    public int coins, xp;
    public RiderProgress.XpResult level;
}

public sealed class RaceResultsView
{
    private readonly RectTransform _root, _card, _celebrate;
    private readonly Text _banner, _margin, _kuroTime, _npcTime, _coins, _xp, _level, _quip, _record, _celebrateText;
    private readonly Image _xpBar, _celebrateIcon, _celebrateGlow, _celebrateDim, _kuroIns, _npcIns;
    private readonly RawImage _npcPortrait, _kuroPortrait;
    private float _shownAt;
    private bool _celebrating;

    public bool Visible => _root.gameObject.activeSelf;

    public RaceResultsView(RectTransform layer, Action onContinue)
    {
        _root = HudKit.Rect(layer, "Race Results");
        RaceUi.Stretch(_root);
        var dim = HudKit.Panel(_root, "Dim", new Color(0f, 0f, 0f, 0.45f));
        RaceUi.Stretch(dim);
        _card = RaceLayout.Card(_root, "Card", 900f, 640f, Color.white);
        _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
        _banner = RaceLayout.Text(_card, "Banner", "", 72, HudKit.Chalk, 0f, 18f, 900f, 90f, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_banner, 0.9f, 2.5f);
        _margin = RaceLayout.Text(_card, "Margin", "", 24, HudKit.ChalkSoft, 0f, 106f, 900f, 34f, TextAnchor.MiddleCenter);

        _kuroPortrait = RaceUi.Portrait(_card, "Kuro Portrait", RaceArt.Portrait("Kuro"), 96f);
        RaceLayout.Box((RectTransform)_kuroPortrait.transform.parent, 70f, 160f, 96f, 96f);
        _kuroIns = RaceLayout.Image(_card, "Kuro Insignia", RaceArt.InsigniaByIndex(0), 176f, 160f, 40f, 40f);
        RaceLayout.Text(_card, "Kuro Label", "KURO", 22, HudKit.Chalk, 222f, 160f, 180f, 40f, style: FontStyle.Bold);
        _kuroTime = RaceLayout.Text(_card, "Kuro Time", "", 34, HudKit.Chalk, 176f, 206f, 250f, 50f, style: FontStyle.Bold);
        _npcPortrait = RaceUi.Portrait(_card, "NPC Portrait", null, 96f);
        RaceLayout.Box((RectTransform)_npcPortrait.transform.parent, 734f, 160f, 96f, 96f);
        _npcIns = RaceLayout.Image(_card, "NPC Insignia", RaceArt.InsigniaByIndex(0), 684f, 160f, 40f, 40f);
        _npcTime = RaceLayout.Text(_card, "NPC Time", "", 34, HudKit.Chalk, 474f, 206f, 250f, 50f, TextAnchor.MiddleRight, FontStyle.Bold);

        _coins = RaceLayout.Text(_card, "Coins", "", 30, RaceUi.Coin, 70f, 286f, 360f, 44f, style: FontStyle.Bold);
        _xp = RaceLayout.Text(_card, "XP", "", 30, RaceMath.Hex(0x7cc4ff), 470f, 286f, 360f, 44f, TextAnchor.MiddleRight, FontStyle.Bold);
        _xpBar = RaceUi.Bar(_card, "XP Bar", new Vector2(760f, 16f), new Color(0f, 0f, 0f, 0.55f), RaceMath.Hex(0x3fa2ff));
        RaceLayout.Box((RectTransform)_xpBar.transform.parent, 70f, 340f, 760f, 16f);
        _level = RaceLayout.Text(_card, "Level", "", 20, HudKit.ChalkSoft, 70f, 360f, 760f, 30f, TextAnchor.MiddleCenter);

        _quip = RaceLayout.Text(_card, "Quip", "", 24, HudKit.Chalk, 110f, 404f, 680f, 90f, TextAnchor.MiddleCenter, FontStyle.Italic);
        _record = RaceLayout.Text(_card, "Record", "", 18, HudKit.ChalkSoft, 0f, 500f, 900f, 28f, TextAnchor.MiddleCenter);
        var btn = RaceUi.KeyButton(_card, "Continue", "E", "Continue", Color.white, onContinue, 24);
        RaceLayout.Box((RectTransform)btn.transform, 340f, 560f, 220f, 50f);

        // "New insignia" celebration
        _celebrate = HudKit.Rect(_root, "New Insignia");
        _celebrate.anchorMin = _celebrate.anchorMax = new Vector2(0.5f, 0.5f);
        _celebrate.sizeDelta = new Vector2(600f, 600f);
        // its own backdrop, so the celebration reads cleanly over the card and then reveals it
        _celebrateDim = HudKit.Panel(_celebrate, "Dim", new Color(0.01f, 0.015f, 0.03f, 0.85f)).GetComponent<Image>();
        var dimRt = _celebrateDim.rectTransform;
        dimRt.anchorMin = dimRt.anchorMax = new Vector2(0.5f, 0.5f);
        dimRt.sizeDelta = new Vector2(4000f, 3000f);
        _celebrateGlow = RaceUi.Icon(_celebrate, "Glow", RaceArt.Glow, new Vector2(620f, 620f), new Color(1f, 0.85f, 0.4f, 0.8f));
        _celebrateIcon = RaceUi.Icon(_celebrate, "Icon", RaceArt.InsigniaByIndex(0), new Vector2(300f, 300f));
        _celebrateText = HudKit.Label(_celebrate, "Text", "", 40, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_celebrateText, 0.95f, 2.5f);
        ((RectTransform)_celebrateText.transform).anchoredPosition = new Vector2(0f, -210f);
        _root.gameObject.SetActive(false);
    }

    public void Show(RaceResult r)
    {
        _root.gameObject.SetActive(true);
        _shownAt = Time.unscaledTime;
        var def = r.def;
        var col = RaceMath.TypeColor(def.Type);
        _card.Find("Accent").GetComponent<Image>().color = r.won ? RaceUi.Win : col;
        _card.Find("Edge").GetComponent<Image>().color = HudKit.WithAlpha(r.won ? RaceUi.Win : col, 0.55f);
        _banner.text = r.forfeit ? "FORFEIT" : (r.won ? "VICTORY!" : "DEFEAT");
        _banner.color = r.won ? RaceUi.Win : (r.forfeit ? RaceMath.Hex(0x8a8fa3) : RaceUi.Loss);
        float margin = Mathf.Abs(r.npcTime - r.kuroTime);
        _margin.text = r.forfeit ? $"{def.Name} wins. A forfeit counts as a loss."
            : (r.won ? $"Kuro beats {def.Name} by {margin:0.00} s" : $"{def.Name} wins by {margin:0.00} s");
        _kuroIns.sprite = RaceArt.Insignia(r.level.levelBefore);
        _kuroTime.text = r.forfeit ? "DNF" : RaceMath.FormatTime(r.kuroTime);
        _kuroTime.color = r.won ? RaceUi.Win : HudKit.Chalk;
        _npcPortrait.texture = RaceArt.Portrait(def.Name);
        _npcIns.sprite = RaceArt.Insignia(def.Level);
        _npcTime.text = (def.Type == RaceType.TimeTrial ? "<size=18>ghost </size>" : "") + RaceMath.FormatTime(r.npcTime);
        _npcTime.color = !r.won ? RaceUi.Loss : HudKit.Chalk;
        _coins.text = r.coins > 0 ? $"+{RaceUi.Coins(r.coins)}" + (r.firstWin ? " <size=18>(first win x1.5)</size>" : "")
                                  : "<color=#8a8fa3>+0 MC</color>";
        _xp.text = $"+{r.xp} XP";
        int need = RiderProgress.XpToNext;
        RaceUi.SetBar(_xpBar, need > 0 ? RiderProgress.Xp / (float)need : 1f);
        _level.text = r.level.LeveledUp
            ? $"<color=#ffc23d><b>LEVEL UP!</b></color>  Lv {r.level.levelBefore} -> Lv {r.level.levelAfter}"
            : (need > 0 ? $"Lv {RiderProgress.Level}  ·  {RiderProgress.Xp}/{need} XP" : "Lv 60  ·  MAX");
        _quip.text = $"{def.Name}: \"{(r.won ? def.LossQuip : def.WinQuip)}\"";
        var rec = RiderProgress.RecordFor(def.Id);
        _record.text = rec == null ? "" : $"Record vs {def.Name}: {rec.wins}-{rec.losses}   ·   Best {RaceMath.FormatTime(rec.bestTime)}";
        _celebrating = r.level.NewInsignia;
        _celebrate.gameObject.SetActive(_celebrating);
        if (_celebrating)
        {
            _celebrateIcon.sprite = RaceArt.Insignia(r.level.levelAfter);
            _celebrateText.text = $"NEW INSIGNIA\n<size=30>{RaceMath.InsigniaName(r.level.levelAfter)}</size>";
        }
    }

    public void Tick()
    {
        if (!_celebrating) return;
        float t = Time.unscaledTime - _shownAt;
        // the celebration plays over the card for 2.6 s, then tucks away
        float a = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((2.6f - t) / 0.4f);
        _celebrate.gameObject.SetActive(a > 0.01f);
        _celebrateIcon.rectTransform.localScale = Vector3.one * (0.6f + 0.4f * Mathf.Clamp01(t / 0.35f) + 0.04f * Mathf.Sin(t * 6f));
        _celebrateGlow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 30f);
        _celebrateGlow.color = new Color(1f, 0.85f, 0.4f, 0.8f * a);
        _celebrateDim.color = new Color(0.01f, 0.015f, 0.03f, 0.85f * a);
        _celebrateIcon.color = new Color(1f, 1f, 1f, a);
        _celebrateText.color = HudKit.WithAlpha(HudKit.Chalk, a);
        if (a <= 0f && t > 1f) _celebrating = false;
    }

    public void Hide() { _root.gameObject.SetActive(false); }
}
